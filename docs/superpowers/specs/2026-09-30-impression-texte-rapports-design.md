# Impression en mode texte, rapports X/Z imprimés, pourboire à table — design

Date : 2026-09-30
Statut : validé en brainstorming, en attente de relecture
Prérequis : sous-projet C (impression), fusionné (PR #3). Référence : `docs/superpowers/specs/2026-09-29-impression-design.md`.

## Contexte et objectif

Tous les tickets sortent aujourd'hui en image 1 bit (`EscPosRasterRenderer`), ce qui peut être lent sur une
imprimante d'entrée de gamme. La spec multilangue prévoyait un repli en texte ESC/POS pour `en`/`fr`. Les
rapports X et la clôture Z ne sont qu'affichés, jamais imprimés. Les pourboires ne sont saisis qu'au comptoir
et ne sont attribués à aucun serveur.

Objectifs :

1. Pouvoir imprimer en texte ESC/POS natif, imprimante par imprimante, les documents en `en`/`fr`.
2. Imprimer le rapport X et la clôture Z, avec la liste des articles vendus (triés par famille puis nom) et les
   pourboires par serveur.
3. Saisir un pourboire au paiement à table, attribué au serveur de la table.
4. Fiabiliser la clôture Z : tout encaisser avant la Z, aucune annulation d'une période clôturée.

## Décisions

| Sujet | Décision |
|---|---|
| Activation du texte | Réglage par imprimante `TextMode` (défaut `false`) ; l'arabe (document RTL) reste toujours en image |
| Encodage texte | Page de code PC858 (`ESC t 19`) ; caractère non encodable → `?` |
| Rapports imprimés | X (sur demande), Z (automatique à la clôture + réimpression de la dernière) |
| Imprimante des rapports | Imprimante de caisse du terminal (règle existante : `Device.ReceiptPrinterId` sinon poste `RECEIPT`) |
| Langue des rapports | `ReceiptLanguage` |
| Articles dans les rapports | Par famille puis nom ; « Qté x Nom » + TTC ; sous-total TTC par famille ; offerts à 0.00 ; sans famille → « Autres » en dernier |
| Pourboires dans les rapports | Total par serveur (trié par nom) + total général |
| Pourboire à table | Saisi à l'encaissement, **seulement sur le paiement qui solde la commande** |
| Attribution du pourboire | Table : `Order.OperatorId` (serveur qui a ouvert la table) ; comptoir : opérateur connecté à l'encaissement si la commande n'a pas de serveur |
| Écran des rapports | Inchangé : articles et pourboires n'apparaissent que sur le ticket imprimé |
| Clôture Z | Refusée tant qu'une commande est en cours dans le restaurant : il faut tout encaisser avant la Z |
| Annulation après Z | Interdite : un reçu d'une période clôturée ne peut plus être annulé |

## Mode texte

### Données

- `PrinterConfiguration.TextMode` (bool, défaut `false`) ; `ALTER TABLE PrinterConfigurations ADD COLUMN
  TextMode INTEGER NOT NULL DEFAULT 0` idempotent dans `Program.cs`.
- `PrinterRegistrationRequest` et `UpdatePrinterRequest` gagnent `bool? TextMode` : absent à la création →
  `false` ; absent en modification → valeur existante conservée (clients non mis à jour).
- `GET /api/printers` expose `textMode`.

### Rendu (`EscPosTextRenderer`)

`Render(TicketDocument doc, int paperWidthMm, bool openCashDrawer) : byte[]` produit la séquence complète :

- `ESC @`, `ESC t 19` (PC858).
- Largeur : 48 colonnes (80 mm), 32 colonnes (58 mm) ; une ligne en grande taille compte double largeur
  (24 / 16 colonnes).
- `TicketText` : `ESC a` (0 gauche, 1 centre, 2 droite selon `TicketAlign`), `ESC E 1/0` pour le gras,
  `GS ! 0x11/0x00` pour la grande taille ; le texte trop long est coupé aux espaces (mot plus long que la
  ligne : coupé net).
- `TicketColumns` : libellé à gauche, valeur à droite, espaces entre les deux. Si les deux ne tiennent pas :
  libellé seul (coupé aux espaces), puis valeur alignée à droite sur la ligne suivante.
- `TicketSeparator` : ligne de `-` sur toute la largeur ; `Cut: true` → `GS V 66 0` (avance et coupe) à la
  place de la ligne.
- Fin : impulsion tiroir `ESC p 0 25 250` si demandée, puis `GS V 66 0` — même ordre que le rendu image.
- Encodage : `Encoding.GetEncoding(858)` via `CodePagesEncodingProvider` (enregistré une fois), repli de
  remplacement `?`. Aucune exception sur un caractère inconnu.

### Choix du rendu

`EscPosPrinterTransport.SendAsync` : texte si `printer.TextMode && !document.RightToLeft`, sinon image
(inchangé). L'impression de test suit la même règle, ce qui permet de vérifier les accents depuis Gestion →
Imprimantes.

## Rapports X et Z imprimés

### Données du ticket (`ReportPrintDataService`)

Calcul à partir de la période du rapport (X : période en cours de `GenerateXReportAsync` ; Z :
`PeriodStartUtc`–`PeriodEndUtc` de la clôture) et du même périmètre de terminaux que le rapport X
(terminal principal : `TerminalId`, `POS_MAIN_TERM`, `POS01`).

- **Commandes retenues** : ayant au moins un reçu non annulé (`IsVoid == false`, montant positif) dans la
  période, et statut différent de `Cancelled`. Une période clôturée ne pouvant plus recevoir d'annulation
  (voir « Règles de clôture »), une réimpression de Z redonne la même liste.
- **Articles** : lignes des commandes retenues, regroupées par (famille, nom d'article).
  - Famille : `Product.CategoryId` → `Category.Name` ; article ou famille introuvable → groupe « Autres »
    (clé `report.other_category`), placé en dernier.
  - Tri : familles par nom puis articles par nom, comparaison `fr` insensible à la casse.
  - Quantité : somme des quantités ; TTC : somme de `CalculateTotalTtc()` (articles offerts → 0.00).
  - Sous-total TTC par famille.
- **Pourboires** : `Order.TipAmount` des commandes retenues, regroupé par `Order.OperatorId` ; nom de
  l'opérateur, sinon « Inconnu » (`report.unknown_server`) ; tri par nom ; total général. Serveurs sans
  pourboire omis ; section omise si le total est nul.
- Ces montants sont informatifs : ils ne modifient ni les totaux fiscaux, ni la clôture Z signée, ni son hash.

### Documents

`TicketDocumentBuilder.XReport(FiscalSummaryDto summary, ReportPrintData data, string language, DateTimeOffset nowUtc)`
et `ZClosure(DailyFiscalClosureDto closure, ReportPrintData data, string language)` :

1. Titre en grand : « RAPPORT X » / « CLÔTURE Z n° {sequence} ».
2. Terminal ; période (X) ou date de clôture + gérant (Z) ; date d'impression (X).
3. Nombre de tickets, total TTC, total HT.
4. TVA par taux.
5. Total par moyen de paiement (libellés traduits existants).
6. Grand total perpétuel.
7. « Articles vendus » : par famille (titre en gras), lignes, sous-total.
8. « Pourboires par serveur » : lignes, total.
9. Signature (Z uniquement).

Clés `report.*` dans les trois `.resx`.

### File et déclencheurs

- `PrintJobKind.Report = 3`.
- `PrintDispatcher.QueueReportAsync(string terminalId, TicketDocument document, CancellationToken)` :
  imprimante de caisse du terminal, jamais d'exception, renvoie `printQueued`.
- `POST /api/fiscal/z-closure` : après une clôture réussie, mise en file du ticket Z ; la réponse gagne
  `printQueued`.
- `POST /api/fiscal/x-report/print?terminalId=` : calcule le rapport X à l'instant et l'imprime →
  `{ printQueued }`.
- `POST /api/fiscal/latest-closure/print?terminalId=` : réimprime la dernière clôture Z → `{ printQueued }` ;
  404 si aucune clôture.
- Accès : manager/admin (politique du groupe `/api/fiscal`). NF525 inchangé : lecture seule de la chaîne.

## Règles de clôture

### Pas de Z avec des commandes en cours

- Avant toute écriture, `POST /api/fiscal/z-closure` recherche les commandes en cours dans tout le restaurant
  (une commande n'est rattachée à aucun terminal) : au moins un article, statut ni `Paid` ni `Cancelled`.
  Cela inclut les commandes partiellement réglées et les paniers comptoir mis en attente ; une table ouverte
  sans article ne bloque pas.
- S'il y en a : 409 `{ code: "open_orders", message: Texts.T("errors.z_closure_open_orders"), openOrders: [...] }`,
  chaque élément portant `tableNumber` (ou libellé du panier en attente) et `remainingTtc` (TTC restant dû,
  décimal). Rien n'est écrit, rien n'est imprimé.
- Le rapport X n'est pas concerné.

### Pas d'annulation après Z

- `POST /api/checkout/void/{receiptId}` refuse, avant toute écriture, un reçu dont `CreatedAtUtc` est
  antérieur ou égal au `PeriodEndUtc` de la dernière clôture Z de son terminal (même règle d'alias du terminal
  principal que `GetLatestZClosureAsync`) : 409 `{ code: "void_after_closure", message:
  Texts.T("errors.void_after_closure") }`.
- La vérification est une méthode du service fiscal, `IsInClosedPeriodAsync(string terminalId,
  DateTimeOffset createdAtUtc)`, appelée par l'endpoint ; la chaîne NF525 et `VoidReceiptAsync` ne changent pas.

## Pourboire à table

- `PaymentSettlementRequest.TipAmount` (decimal, défaut 0 ; négatif → 400).
- Si `TipAmount > 0` : le pourboire n'est accepté que si ce paiement solde la commande (montant encaissé ≥
  solde restant TTC avant pourboire + pourboire). Sinon 400 `errors.tip_only_on_final_payment`, rien n'est
  écrit.
- Accepté : ajouté à `Order.TipAmount` avant `ProcessPaymentTendersAsync` ; le client encaisse
  solde + pourboire. Le reçu d'un paiement qui solde garde le TTC de la commande (ratio 1) : le montant fiscal
  reste hors pourboire, la chaîne NF525 ne change pas de format.
- Comptoir (`/api/orders/counter/checkout`) : si `Order.OperatorId` est vide, il reçoit l'identifiant de
  l'opérateur connecté (claim `NameIdentifier`).

## Clients (web et iPad)

- Gestion → Imprimantes : case « Mode texte (fr/en, plus rapide) » à la création et à la modification.
- Écran fiscal : bouton « Imprimer » sur le rapport X ; bouton « Réimprimer » sur la dernière clôture ;
  avertissement si `printQueued` vaut `false` (y compris après une clôture Z) ; clôture Z refusée → message
  et liste des tables / paniers à encaisser (numéro, TTC restant).
- Annulation d'un reçu refusée après Z → message traduit (`void_after_closure`).
- Paiement à table : champ pourboire (même interface que le comptoir), visible seulement quand le montant à
  encaisser est le solde restant ; le montant envoyé inclut le pourboire.
- Chaînes en/fr/ar (web `i18n/*.json`, PosKit et app `.xcstrings`) ; fixtures iOS (`printers.json`, réponses
  de clôture) mises à jour.

## Contraintes

- Montants en centimes jusqu'au rendu, affichés `0.00` en culture invariante.
- Filtres et tris sur `DateTimeOffset` en mémoire (limite EF Core SQLite).
- `TreatWarningsAsErrors` : aucun avertissement nouveau.
- Aucune logique d'impression ne connaît les entités métier hormis les constructeurs de documents,
  `PrintDispatcher` et `ReportPrintDataService`.

## Tests

- **Rendu texte** : octets pour une ligne centrée en gras, une ligne en grande taille (largeur moitié),
  colonnes complétées en 32 et 48 colonnes, libellé trop long (repli), mot plus long que la ligne, « é » →
  `0x82` en PC858, « € » → `0xD5`, caractère inconnu → `?`, séparateur, coupe, tiroir avant la dernière coupe.
- **Choix du rendu** : texte si `TextMode` et `fr` ; image si `ar` ; image si `TextMode` faux ; impression de
  test idem.
- **API imprimantes** : `textMode` en création, modification, liste ; `PUT` sans le champ conserve la valeur.
- **Données de rapport** : tri famille puis nom, sous-totaux, offerts, « Autres », commande annulée exclue,
  commande hors période exclue, pourboires par serveur et « Inconnu ».
- **Documents** : X et Z en en/fr/ar (titres, sections, signature Z seulement).
- **Déclencheurs** : clôture Z → un job `Report`, `printQueued` vrai ; sans imprimante de caisse → aucun job,
  `printQueued` faux ; routes X et réimpression Z ; serveur (rôle Waiter) refusé ; 404 sans clôture.
- **Règles de clôture** : Z refusée (409, liste) avec une table non soldée, une commande partiellement réglée,
  un panier en attente ; acceptée avec une table ouverte sans article ; rien d'écrit en cas de refus ;
  annulation d'un reçu antérieur à la dernière Z refusée (409), d'un reçu postérieur acceptée ; alias du
  terminal principal.
- **Pourboire** : attribué au serveur de la table ; au comptoir à l'opérateur connecté ; refusé sur un
  paiement partiel (400, rien d'écrit) ; reçu d'un paiement complet avec pourboire = TTC de la commande.
- **Clients** : case mode texte, boutons Imprimer/Réimprimer, champ pourboire à table (Playwright, XCUITest),
  décodage des nouveaux champs (fixtures).
- **Manuel** : impression de test en mode texte (accents), rapport X en 58 et 80 mm, comparaison de vitesse
  texte / image ajoutée à `docs/impression.md`.

## Ordre de livraison

1. Mode texte : champ, API, rendu, choix du rendu.
2. Pourboire à table et attribution comptoir.
3. Données de rapport + documents X/Z.
4. Règles de clôture (Z bloquée par les commandes en cours, pas d'annulation après Z).
5. Déclencheurs X/Z + routes.
6. Client web.
7. iPad (PosKit + app).
8. Documentation `docs/impression.md`.
