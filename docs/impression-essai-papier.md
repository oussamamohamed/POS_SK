# Essai papier des imprimantes (iPad autonome, lot 4c)

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

### Ce que l'app permet aujourd'hui (constaté dans le code)

- Écran **Gestion → Imprimantes** (`PrintersAdminView` / `PrinterEditorSheet`) : nom, adresse IP (IPv4 obligatoire, même pour `epson`), port, largeur de papier (58/80 mm), tiroir à l'encaissement, mode texte, stations. Boutons **Test** (page de test) et **Impressions** (jobs en attente ou en échec, avec l'erreur, **Relancer** / **Annuler**).
- **Aucun champ `transport` ni `deviceIdentifier`** dans cet écran : il arrive avec le lot 4d (écran de configuration et recherche réseau, `AppEnvironment.printerDiscovery` est prêt mais inutilisé). `LocalPosAPI.savePrinter` accepte bien ces deux champs (struct `Printer`), mais rien dans l'UI ne les pose. Sans action de votre part, toute imprimante créée est en `lan` (réseau brut, IP:port).
- Éditer une imprimante dans l'écran existant **conserve** `transport` et `deviceIdentifier` déjà posés.

### TM-T88VI et TM-T88III en LAN brut (aucun contournement)

1. Gestion → Imprimantes → nouvelle imprimante : nom, IP de l'imprimante, port 9100, papier 80 mm.
2. Station : cocher `RECEIPT` pour les tickets de vente (l'imprimante de ticket est la première imprimante `RECEIPT` active par nom). Cocher aussi la station cuisine voulue pour le bon cuisine (`HOT_KITCHEN` par défaut dans le formulaire).
3. « Tiroir à l'encaissement » activé pour le test du tiroir.

### TM-T88VI en ePOS2 (contournement, en attendant le lot 4d)

Créer l'imprimante comme ci-dessus (IP, port 9100), puis poser les deux colonnes directement dans la base SQLite de l'app. Le fichier est `Application Support/RestaurantPOS/pos-local.sqlite` dans le conteneur de l'app (bundle id `com.restaurantpos.ipad`).

- **Simulateur** (utile pour préparer la requête, sans imprimante) :
  ```bash
  DB="$(xcrun simctl get_app_container booted com.restaurantpos.ipad data)/Library/Application Support/RestaurantPOS/pos-local.sqlite"
  xcrun simctl terminate booted com.restaurantpos.ipad
  sqlite3 "$DB" "SELECT Id, Name, TransportKind FROM PrinterConfigurations;"
  # Adapter le nom dans WHERE au nom réel de l'imprimante affiché ci-dessus.
  sqlite3 "$DB" "UPDATE PrinterConfigurations SET TransportKind='epson', DeviceIdentifier='TM_T88;TCP:192.168.1.60' WHERE Name='Ticket';"
  ```
- **iPad réel** : le conteneur de l'app n'est pas accessible sans outil.

  > **ATTENTION : « Replace Container… » remplace TOUT le conteneur de données de l'app, base fiscale et `<base>-archives/` comprises.** Toute vente faite entre le téléchargement et le remplacement est **perdue silencieusement** (les reçus fiscaux signés disparaissent : non-conformité NF525). À faire uniquement sur un iPad **sans vente réelle**, ou après un export `.posbak` et sans aucune vente jusqu'au remplacement. Exige un build signé en développement (pas TestFlight ni App Store). Même précaution avant toute modification de la base sur un iPad réel.

  Voies possibles, aucune n'a été essayée ici : Xcode → Window → Devices and Simulators → l'app → « Download Container… » puis modifier le fichier du paquet, puis le remettre avec « Replace Container… » (l'app doit être fermée) ; ou attendre le lot 4d. Si aucune ne convient, **cet essai ePOS2 attend le lot 4d**.

Valeurs valides de `TransportKind` : `lan`, `star`, `epson`. L'IP du `deviceIdentifier` doit être celle de l'imprimante. Formats complets :

- Epson : `<série>;<cible>`, cible `TCP:<ip>`, `BT:<adresse>` ou `USB:<id>` ; vide = `TM_T88;TCP:<ipAddress>`. Une valeur sans `;` est lue comme la cible seule avec la série `TM_T88`.
- Star : `<interface>:<identifiant>`, interface `lan`, `bluetooth`, `bluetoothLE` ou `usb` (ex. `bluetooth:00:11:62:AA:BB:CC`) ; vide = `lan:<ipAddress>`.
- Format invalide : l'impression échoue avec « Identifiant d'imprimante invalide : … ».

Avant tout `UPDATE`, lister les imprimantes : `SELECT Id, Name, TransportKind FROM PrinterConfigurations;`. Le nom dans `WHERE Name='Ticket'` est un exemple : l'adapter au nom réel de l'imprimante.

Revenir en LAN brut : `UPDATE PrinterConfigurations SET TransportKind='lan', DeviceIdentifier=NULL WHERE Name='Ticket';`

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
