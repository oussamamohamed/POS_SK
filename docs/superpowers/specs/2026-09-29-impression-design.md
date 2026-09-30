# Impression des tickets (sous-projet C) — design

Date : 2026-09-29
Statut : validé en brainstorming, en attente de relecture
Prérequis : sous-projet A (multilangue), fusionné. Décisions héritées : section « Impression » de
`docs/superpowers/specs/2026-09-27-multilangue-design.md`.

## Contexte et objectif

Aujourd'hui aucun ticket n'est imprimé. Le paiement comptoir renvoie seulement les indicateurs
`printPickupVoucher` / `printFiscalReceipt`, que les deux clients ignorent ; le seul envoi réel vers une
imprimante est l'impression de test (`PrinterConfigurationService.SendTestPrintAsync`, texte brut en TCP
9100). Le sous-projet A fournit déjà le contenu localisé des tickets (`TicketDocument`,
`TicketDocumentBuilder.PickupCoupon` / `FiscalReceipt`) et le réglage `RestaurantSettings.ReceiptLanguage`.

Objectif : imprimer réellement, de façon fiable, les documents utiles au service, dans la bonne langue
(arabe compris), sur la bonne imprimante, sans jamais bloquer une vente ni perdre un bon cuisine.

Architecture de déploiement : **un seul serveur .NET par restaurant**, multiplateforme (macOS, Windows,
Linux), sur le réseau local des imprimantes ; les iPad et navigateurs sont des clients appairés.

## Décisions

| Sujet | Décision |
|---|---|
| Documents | Bon de retrait (chaque vente comptoir), ticket de caisse (sur demande, loi AGEC), bons de commande cuisine (à l'envoi en cuisine) |
| Hors périmètre | Réimpression manuelle depuis l'historique, rapports X/Z imprimés, devise (sous-projet B), libellé cuisine stocké affiché sur l'écran KDS |
| Déclenchement | Automatique côté serveur, dans le flux de paiement / d'envoi en cuisine |
| Fiabilité | File d'impression persistée en base (`PrintJobs`) + service d'arrière-plan avec relance |
| Imprimante des tickets client | Par caisse (`Device.ReceiptPrinterId`), repli sur l'imprimante du poste `RECEIPT` |
| Imprimante des bons cuisine | Par poste de préparation : article → famille → `HOT_KITCHEN` ; chaque imprimante active du poste reçoit une copie |
| Langue des bons cuisine | Réglage propre `KitchenTicketLanguage`, par défaut = `ReceiptLanguage` |
| Rendu | Image monochrome côté serveur (SkiaSharp + Topten.RichTextKit), polices Noto embarquées, ESC/POS `GS v 0` par bandes |
| Tiroir-caisse | Ouvert si paiement avec espèces et imprimante de caisse `OpenCashDrawerOnReceipt` |
| Paiement à table | Ticket de caisse imprimé sur demande (`requestReceiptPrint`) |

## Données et configuration

### Table `PrintJobs`

Entité `PrintJob` (Domain) et `CREATE TABLE IF NOT EXISTS PrintJobs` dans le bloc de schéma de
`Program.cs` :

| Champ | Type | Rôle |
|---|---|---|
| `Id` | Guid (UUIDv7) | Clé ; l'ordre d'insertion donne l'ordre d'impression |
| `PrinterId` | Guid | Imprimante cible |
| `Kind` | enum `PickupVoucher` / `Receipt` / `KitchenTicket` | Type de document |
| `DocumentJson` | texte | `TicketDocument` sérialisé |
| `OpenCashDrawer` | bool | Impulsion tiroir après impression |
| `Status` | enum `Pending` / `Sent` / `Failed` / `Cancelled` | État |
| `Attempts` | int | Nombre d'essais |
| `NextAttemptAtUtc` | DateTimeOffset | Prochain essai |
| `DeadlineAtUtc` | DateTimeOffset | Création (ou relance) + 30 min ; au-delà → `Failed` |
| `LastError` | texte nullable | Dernière erreur (réseau, refus) |
| `CreatedAtUtc`, `SentAtUtc` | DateTimeOffset | Horodatage |

Index `(Status, NextAttemptAtUtc)`. `TicketDocument` et ses lignes doivent être sérialisables en JSON avec un
discriminant de type (`TicketText` / `TicketColumns` / `TicketSeparator`).

### Réglages et champs ajoutés

- `RestaurantSettings.KitchenTicketLanguage` (`en`/`fr`/`ar`) : initialisé à la valeur de `ReceiptLanguage`
  pour une ligne existante ; exposé par `GET/PUT /api/settings` (`kitchenTicketLanguage`, même validation).
  Colonne ajoutée par `ALTER TABLE` idempotent.
- `Category.PreparationStationId` (nullable) : poste par défaut des articles de la famille ; exposé et
  modifiable par les endpoints catalogue existants.
- `Device.ReceiptPrinterId` (Guid nullable) : imprimante de tickets de la caisse ; modifiable depuis
  Gestion → Appareils (endpoint appareils existant).
- `PaymentSettlementRequest.RequestReceiptPrint` (bool, défaut `false`) pour `POST /api/checkout/pay`.

### Postes de préparation

Liste fixe partagée : `HOT_KITCHEN`, `COLD`, `GRILL`, `DESSERT`, `BAR`, plus `RECEIPT` (imprimantes de
caisse). Résolution du poste d'une ligne : `OrderItem.PreparationStationId` → `Product.PreparationStationId`
→ `Category.PreparationStationId` → `HOT_KITCHEN`. L'heuristique actuelle par nom de catégorie
(`STATION-HOT` / `STATION-BAR` / `STATION-PASTRY` dans `KitchenRoutingService`) est supprimée. Les postes
personnalisables sont hors périmètre.

## Déroulement

### Déclencheurs (serveur)

La mise en file se fait après le succès de l'opération métier ; elle ne peut jamais la faire échouer.

- **Paiement comptoir** (`POST /api/orders/counter/checkout`) : un seul job sur l'imprimante de la caisse —
  `FiscalReceipt(...)` (ticket de caisse + bon de retrait) si `RequestFiscalReceiptPrint`, sinon
  `PickupCoupon(...)`. `OpenCashDrawer` = paiement contenant des espèces ET imprimante
  `OpenCashDrawerOnReceipt`.
- **Paiement à table** (`POST /api/checkout/pay`) : si `RequestReceiptPrint` et qu'un reçu fiscal est émis
  (commande entièrement réglée), job `Receipt` sur l'imprimante de la caisse ; même règle de tiroir.
- **Envoi en cuisine** (dispatch, suite, réclame) : pour chaque `KitchenTicket` créé, un job
  `KitchenTicket` par imprimante active rattachée à son poste.
- Aucune imprimante résolue : rien n'est mis en file ; avertissement journalisé ; la réponse du paiement
  porte `printQueued: false`. Les réponses de paiement gagnent `printQueued` (bool) ; les indicateurs
  existants `printPickupVoucher` / `printFiscalReceipt` restent inchangés.

### Bon cuisine

`TicketDocumentBuilder.KitchenTicket(KitchenTicket ticket, Order order, string language)` :

- En-tête en grand : numéro de table, ou « À emporter » + numéro de retrait + bipeur, construit depuis
  `Order` (pas depuis le libellé stocké dans `KitchenTicket.TableNumber`) ; serveur ; couverts ; heure.
- Lignes : quantité + nom de l'article en grand, options, commentaire cuisine, service
  (Direct/Suite/Dessert) traduit.
- Libellés via `Texts.Get` (clés `kitchen_ticket.*` dans les trois `.resx`) dans la langue
  `KitchenTicketLanguage`.

### Service d'impression (`PrintWorker`, `BackgroundService`)

- Réveillé par un signal à chaque mise en file et par une scrutation toutes les 5 s.
- Par imprimante : jobs `Pending` échus traités un par un dans l'ordre d'`Id` (FIFO), une connexion à la
  fois ; imprimantes différentes en parallèle. Un job en tête qui échoue bloque les suivants de la même
  imprimante (ordre préservé).
- Échec : `Attempts++`, prochain essai à +5 s, +15 s, +30 s puis toutes les 60 s ; au-delà de
  `DeadlineAtUtc` → `Failed`.
- Au démarrage, les jobs `Pending` en base sont repris.
- Imprimante désactivée ou supprimée : ses jobs `Pending` passent `Cancelled` (`LastError` = raison).
- Purge quotidienne des jobs `Sent` et `Cancelled` de plus de 7 jours ; les `Failed` sont conservés.
- État d'une imprimante (en mémoire) : « en ligne » si son dernier envoi a réussi, « hors ligne depuis T »
  s'il a échoué, « inconnu » tant qu'aucun envoi n'a eu lieu depuis le démarrage.
- Événement SignalR `OnPrinterStatusChanged(printerId, printerName, isOnline, pendingCount)` sur le hub
  `/hubs/pos` à chaque transition en ligne ↔ hors ligne et à chaque job passé `Failed`.

### Rendu et envoi

- `EscPosRasterRenderer` : dessine un `TicketDocument` en image 1 bit, largeur 576 points (80 mm) ou
  384 points (58 mm) selon `PaperWidthMm`, direction RTL si `RightToLeft`. Mise en forme bidirectionnelle
  et liaison des lettres par Topten.RichTextKit (HarfBuzz). Polices embarquées : Noto Sans et Noto Sans
  Arabic (licence OFL). Colonnes : libellé côté début, valeur côté fin (inversées en RTL), montants
  toujours en chiffres occidentaux.
- `EscPosSender` : `ESC @`, image en bandes de 256 lignes (`GS v 0`), `ESC p 0 25 250` si tiroir,
  `GS V 66 0` (coupe). Connexion 3 s, écriture 10 s.
- L'impression de test (`SendTestPrintAsync`) passe par le même rendu (texte localisé, accents et arabe
  corrects).
- Dépendances : `SkiaSharp`, `Topten.RichTextKit`, `SkiaSharp.NativeAssets.Linux.NoDependencies`,
  `HarfBuzzSharp.NativeAssets.Linux` (macOS et Windows couverts par les paquets de base).

## Clients (web et iPad)

- Paiement à table : case « Imprimer le ticket de caisse » (`requestReceiptPrint`).
- Gestion → Catalogue : sélecteur « Poste de préparation » sur les familles (vide = cuisine chaude) ; celui
  des articles existe déjà (vide = poste de la famille).
- Gestion → Appareils : sélecteur « Imprimante de ticket » par caisse (vide = imprimante du poste `RECEIPT`).
- Gestion → Réglages : « Langue des bons cuisine » à côté de « Langue des tickets ».
- Gestion → Imprimantes : état par imprimante (en ligne / hors ligne depuis HH:MM / n en attente), liste des
  jobs `Failed` avec « Relancer », jobs `Pending` avec « Annuler ».
- Notification en direct sur `OnPrinterStatusChanged` (« Imprimante Cuisine chaude hors ligne — 3 bons en
  attente », puis « rétablie »).
- Nouvelles chaînes dans les trois langues (web `i18n/*.json`, PosKit `.strings`, app `.xcstrings`) ;
  fixtures de contrat iOS mises à jour pour les nouveaux champs.

## API

| Méthode | Route | Accès | Rôle |
|---|---|---|---|
| GET | `/api/printers/{id}/jobs?status=` | manager/admin | Jobs d'une imprimante (par défaut `Pending` + `Failed`) |
| POST | `/api/print-jobs/{id}/retry` | manager/admin | `Failed` → `Pending`, nouvelle échéance +30 min |
| POST | `/api/print-jobs/{id}/cancel` | manager/admin | `Pending`/`Failed` → `Cancelled` |
| GET | `/api/printers/status` | authentifié | État courant de chaque imprimante (en ligne, depuis quand, en attente) |

Erreurs traduites via `Texts.T` (`errors.print_job_*`).

## Contraintes

- NF525 inchangé : l'impression lit le reçu déjà signé ; aucune écriture dans la chaîne fiscale.
- Montants en centimes jusqu'au rendu ; affichage `0.00` en culture invariante (devise : sous-projet B).
- Les tickets restent des `TicketDocument` ; aucune logique d'impression ne connaît les entités métier
  hormis les constructeurs de documents.
- `TreatWarningsAsErrors` : les nouveaux paquets ne doivent introduire aucun avertissement.

## Tests

- **Rendu** : largeur 576/384, hauteur > 0, un texte arabe produit des pixels noirs, découpage exact en
  bandes de 256 lignes (dernière bande partielle), document RTL rendu sans exception.
- **Envoi** : faux serveur TCP de test vérifiant la séquence d'octets (init, bandes, tiroir, coupe) et le
  comportement sur connexion refusée / délai dépassé.
- **Worker** : FIFO par imprimante, blocage de la file d'une imprimante par son job en échec, délais de
  relance, passage `Failed` à l'échéance, reprise des `Pending` au démarrage, annulation à la
  désactivation, purge.
- **Déclencheurs** : comptoir (bon de retrait toujours, ticket combiné sur demande, tiroir seulement avec
  espèces), table (ticket seulement si demandé et reçu émis), cuisine (résolution article → famille →
  `HOT_KITCHEN`, une copie par imprimante du poste, aucune imprimante → rien en file), `printQueued`.
- **Bon cuisine** : contenu (table ou à emporter + retrait + bipeur, options, commentaire, service) dans les
  trois langues.
- **API** : retry/cancel/jobs/status avec droits d'accès ; `kitchenTicketLanguage` ; `Category.PreparationStationId` ;
  `Device.ReceiptPrinterId`.
- **Clients** : décodage des nouveaux champs (fixtures), sélecteurs, case ticket à table (Playwright,
  XCUITest), notification `OnPrinterStatusChanged`.
- **Manuel** : procédure documentée d'essai sur une imprimante d'entrée de gamme (vitesse, arabe, bandes)
  dans `docs/impression.md` ; si trop lente, le repli prévu par la spec A (mode texte pour `en`/`fr`) sera
  un sous-projet ultérieur.

## Ordre de livraison

1. Données : `PrintJobs`, champs `Category`, `Device`, `RestaurantSettings`, `PaymentSettlementRequest`.
2. Rendu (`EscPosRasterRenderer`) + envoi (`EscPosSender`) + impression de test migrée.
3. Worker + endpoints jobs/état + SignalR.
4. Déclencheurs comptoir / table / cuisine + bon cuisine + résolution des postes.
5. Client web (sélecteurs, case, état imprimantes, notification).
6. iPad (PosKit + app : mêmes éléments).
7. Documentation `docs/impression.md` + essai manuel.
