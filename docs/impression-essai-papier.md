# Essai papier des imprimantes (iPad autonome, lots 4c et 4d)

Guide pour valider sur le matériel réel l'impression de l'iPad en mode autonome. Les adaptateurs Star et Epson (paquet `ios/Packages/PosPrinters`) n'ont **jamais été testés sur du matériel** : seul le cœur (adresses, délais, session) l'est, par des tests automatiques. Cet essai est donc obligatoire avant tout usage réel. Le rendu raster (arabe) l'est aussi (voir `CLAUDE.md`, lot 4a).

Matériel de l'essai :

| Imprimante | Liaison | `transport` | `deviceIdentifier` |
|---|---|---|---|
| Epson TM-T88VI | ePOS2 en TCP | `epson` | `TM_T88;TCP:<ip>` |
| Epson TM-T88VI | LAN brut, port 9100 | `lan` | vide |
| Epson TM-T88III | LAN brut, port 9100 (carte réseau obligatoire) | `lan` | vide |
| Star (non testée, pas de matériel) | StarXpand | `star` | `<interface>:<identifiant>` |

Le TM-T88III n'est pas dans la liste des modèles du SDK Epson ePOS2 : il ne peut utiliser que le LAN brut. Le TM-T88VI n'a pas de série propre dans le SDK, il est géré comme `TM_T88`.

## 1. Préparation

- [ ] Imprimantes en **émulation ESC/POS** (Epson : c'est le réglage d'usine ; Star : à activer avec Star Quick Setup Utility, sinon le ticket sort illisible).
- [ ] Imprimante et iPad sur le **même réseau**, imprimante en **adresse IP fixe** (ou réservation DHCP). Relever l'IP (auto-test de l'imprimante : maintenir le bouton Feed à la mise sous tension).
- [ ] iPad : Réglages → Confidentialité et sécurité → **Réseau local** : l'app RestaurantPOS est autorisée. Sinon la connexion échoue sans message clair.
- [ ] Build **signé** de l'app (pas de `CODE_SIGNING_ALLOWED=NO`) installé sur l'iPad.
- [ ] Pour ePOS2 (`epson`) : le SDK Epson doit avoir été installé avant la compilation (`ios/scripts/install_epson_sdk.sh <zip ou dossier du SDK 2.37.1>`, puis `cd ios && xcodegen generate`). Sans SDK, l'app compile mais une imprimante `epson` échoue avec « Transport non pris en charge : epson ». Si Xcode répond « does not contain a binary artifact » : voir `CLAUDE.md` (cache de manifeste SwiftPM).
- [ ] Mode autonome configuré (première configuration faite), PIN responsable connu (Gestion et Fiscal exigent un responsable).
- [ ] Papier en place ; pour le test tiroir : tiroir-caisse branché sur la prise RJ11 de l'imprimante.

## 2. Configurer une imprimante

### Saisie dans l'écran (lot 4d)

Gestion → **Imprimantes** → **+** (`PrintersAdminView` / `PrinterEditorSheet`). En mode autonome, le champ **Transport** propose `Réseau (LAN)`, `Star` et `Epson` ; en mode serveur il n'existe pas (IP et port seulement).

- **TM-T88VI / TM-T88III en LAN brut** : transport LAN, nom, IP de l'imprimante, port 9100, papier 80 mm.
- **TM-T88VI en ePOS2** : transport **Epson**, puis soit **Rechercher** (feuille de résultats, toucher « TM-T88VI » remplit identifiant `TM_T88;TCP:<ip>`, IP et nom), soit saisir l'identifiant (`TM_T88;TCP:192.168.1.60`) ou seulement l'IP (identifiant vide = `TM_T88;TCP:<ip>`).
- **Star** : transport **Star**, Rechercher ou identifiant `<interface>:<identifiant>` (ex. `bluetooth:00:11:62:AA:BB:CC`) ; l'IP est facultative (Bluetooth, USB).
- Stations : cocher `RECEIPT` pour les tickets de vente (première imprimante `RECEIPT` active par nom) et la station cuisine voulue. « Tiroir à l'encaissement » pour le test du tiroir.
- Un identifiant invalide est refusé à l'enregistrement (avertissement « Identifiant d'imprimante invalide », la feuille reste ouverte). Éditer une imprimante conserve ses valeurs.
- La recherche **réseau** peut ne rien trouver : l'entitlement `com.apple.developer.networking.multicast` n'est pas demandé à Apple. **La saisie manuelle de l'IP / de l'identifiant est le chemin garanti** ; la recherche Bluetooth / USB n'en a pas besoin.
- Chaque ligne de la liste a un bouton **Test** (page de test envoyée directement, sans job, comme `SendTestPrintAsync` du .NET) et l'état en ligne / hors ligne (rafraîchi toutes les 5 s).

Formats de l'identifiant :

- Epson : `<série>;<cible>`, cible `TCP:<ip>`, `BT:<adresse>` ou `USB:<id>` ; vide = `TM_T88;TCP:<ipAddress>`. Une valeur sans `;` est lue comme la cible seule avec la série `TM_T88`.
- Star : `<interface>:<identifiant>`, interface `lan`, `bluetooth`, `bluetoothLE` ou `usb` ; vide = `lan:<ipAddress>`.
- Revenir en LAN brut : repasser le transport sur `Réseau (LAN)`.

**Vérifié** : par le XCUITest `PrinterAdminUITests` (simulateur, mode autonome, recherche et transport factices) : ajout Epson via Rechercher, ligne affichée, bouton Test (toast de réussite), Star Bluetooth sans IP, identifiant invalide, parcours en anglais et en arabe, absence du champ Transport en mode serveur ; et par lecture du code : formats d'identifiant, repli sur l'IP, champs Star/Epson. **Non vérifié** : recherche réseau et Bluetooth réelle (le test utilise `FakePrinterDiscovery`), ouverture ePOS2 sur appareil, état vide / « Relancer » de la feuille de recherche, boucle de statuts de 5 s, rendu sur papier.

### Où lire une erreur

- Dans l'app : Gestion → Imprimantes → **Impressions** de l'imprimante (les jobs `Failed` et `Pending` avec leur message, boutons Relancer et Annuler). Le bouton **Test** affiche le message d'échec de la page de test.
- Dans la base : `SELECT Status, Attempts, LastError FROM PrintJobs ORDER BY CreatedAtUtc DESC LIMIT 10;` (`Status` : 0 en attente, 1 envoyé, 2 en échec, 3 annulé). Les messages sont en français : connexion refusée ou délai de 3 s, écriture au-delà de 10 s, erreur Epson/Star avec le motif (capot ouvert, plus de papier, imprimante occupée…), « Transport non pris en charge : … », « Identifiant d'imprimante invalide : … ».
- Relances automatiques à 5 / 5 / 15 / 30 / 60 s, abandon (`Failed`) au bout de 30 min.

## 3. Listes de contrôle

Pour chaque imprimante/liaison, cocher chaque cas. En cas d'échec, noter le texte de `PrintJobs.LastError`.

Cas communs (résultat attendu) :

| Cas | Comment | Résultat attendu |
|---|---|---|
| Page de test | Gestion → Imprimantes → **Test** | Page de test imprimée, message « Test d'impression réussi (nom @ ip:port) ». Tiroir ouvert si l'option est active. |
| Vente comptoir, espèces | Comptoir : un article, encaisser en espèces | Ticket lisible (accents, € alignés, TVA, mention fiscale), coupe, **tiroir ouvert**. |
| Ticket en arabe | Gestion → Établissement : réglage « langue des tickets » sur arabe, puis vente comptoir | Ticket en raster, écriture de droite à gauche, lettres jointes, montants lisibles. Revenir à la langue d'origine ensuite. |
| Bon cuisine | Vente avec un article d'une station cuisine, envoi en cuisine | Bon sur l'imprimante de la station ; **pas de tiroir**. |
| Rapport X | Fiscal → **Imprimer le rapport X** | Rapport imprimé, sans tiroir. |
| Duplicata | Fiscal → **Réimprimer un ticket** (numéro d'un ticket) | Ticket marqué « DUPLICATA n°1 » ; un second duplicata donne n°2 ; **pas de tiroir**. |
| Imprimante éteinte pendant une vente | Éteindre l'imprimante, encaisser 2 ventes, rallumer | Les ventes aboutissent sans erreur à l'écran ; Impressions : 2 jobs `Pending` avec une erreur de connexion ; après rallumage (≤ 60 s), les **2 tickets sortent dans l'ordre des ventes** et les jobs passent `Sent`. |

### TM-T88VI, ePOS2 en TCP (`epson`, `TM_T88;TCP:<ip>`)

- [ ] Réglage posé (section 2) et SDK installé dans le build
- [ ] L'app se lance sur l'iPad avec le SDK Epson embarqué (build ePOS2) ; la signature du framework sur appareil n'est pas vérifiée
- [ ] Page de test
- [ ] Vente comptoir espèces + tiroir
- [ ] Ticket en arabe (raster, RTL)
- [ ] Bon cuisine
- [ ] Rapport X
- [ ] Duplicata
- [ ] Imprimante éteinte puis rallumée, tickets dans l'ordre
- [ ] Erreur volontaire (capot ouvert pendant l'envoi) : message Epson lisible dans `LastError`, le job est relancé ensuite

### TM-T88VI, LAN brut 9100 (`lan`)

- [ ] Page de test
- [ ] Vente comptoir espèces + tiroir
- [ ] Ticket en arabe (raster, RTL)
- [ ] Bon cuisine
- [ ] Rapport X
- [ ] Duplicata
- [ ] Imprimante éteinte puis rallumée, tickets dans l'ordre

### TM-T88III, LAN brut 9100 (`lan`, carte réseau)

- [ ] Page de test
- [ ] Vente comptoir espèces + tiroir
- [ ] Ticket en arabe (raster, RTL) 
- [ ] Bon cuisine
- [ ] Rapport X
- [ ] Duplicata
- [ ] Imprimante éteinte puis rallumée, tickets dans l'ordre
- [ ] Ne pas tenter `epson` : modèle hors SDK, hors périmètre

### Star (non testée, pas de matériel)

À faire seulement si un modèle Star est acquis. Émulation ESC/POS activée avec Star Quick Setup Utility ; `transport` `star`, `deviceIdentifier` `<interface>:<identifiant>` ; Bluetooth appairé dans Réglages iOS (ou BLE). Mêmes cas que ci-dessus.

- [ ] Page de test
- [ ] Vente comptoir espèces + tiroir
- [ ] Ticket en arabe
- [ ] Bon cuisine
- [ ] Rapport X
- [ ] Duplicata
- [ ] Imprimante éteinte puis rallumée

## 4. Risque résiduel connu

Si une impression dure plus de ~2 s après un délai (3 s de connexion ou 10 s d'écriture dépassés), la fermeture de la session Epson (`disconnect()`) peut échouer car le SDK est encore occupé : la reprise est bornée à 2 s puis abandonnée. Conséquence possible : l'imprimante reste « occupée » et le job suivant échoue une fois avant de passer. Risque accepté ; à observer lors de l'essai « imprimante éteinte puis rallumée » et à signaler.

## 5. Avant une publication App Store

Enregistrer l'app auprès de **Star et Epson** (programme MFi / accessoires externes). `Info.plist` contient déjà `NSBluetoothAlwaysUsageDescription` et `UISupportedExternalAccessoryProtocols`.

## Annexe : ancienne méthode (UPDATE SQL, échange de conteneur)

Obsolète depuis le lot 4d (l'écran pose `transport` et `deviceIdentifier`). Gardée pour mémoire : sur simulateur, `sqlite3 "$DB" "UPDATE PrinterConfigurations SET TransportKind='epson', DeviceIdentifier='TM_T88;TCP:<ip>' WHERE Name='<nom>';"` (base : `Application Support/RestaurantPOS/pos-local.sqlite` dans le conteneur `com.restaurantpos.ipad`, app fermée).

> **ATTENTION : sur iPad réel, « Download Container… » puis « Replace Container… » (Xcode → Devices and Simulators) remplace TOUT le conteneur de données, base fiscale et `<base>-archives/` comprises.** Toute vente faite entre le téléchargement et le remplacement est **perdue silencieusement** (reçus fiscaux signés disparus : non-conformité NF525). Faire un export `.posbak` d'abord, ne vendre rien entre-temps, et seulement avec un build signé en développement (ni TestFlight ni App Store). À ne jamais faire sur un iPad avec des ventes réelles.
