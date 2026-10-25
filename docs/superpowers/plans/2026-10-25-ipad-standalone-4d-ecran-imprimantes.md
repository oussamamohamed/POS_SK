# Lot 4d — Écran Imprimantes du mode autonome — Plan d'implémentation

> **Pour les agents d'exécution :** SOUS-SKILL REQUIS : superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans. Les étapes utilisent des cases `- [ ]`.

**Objectif :** compléter l'écran Gestion → Imprimantes du mode autonome : choix du transport (`lan` / `star` / `epson`), identifiant du périphérique, recherche d'imprimantes Star et Epson, état en ligne / hors ligne rafraîchi, jobs en attente et en échec avec relance et annulation (déjà présents), impression de test ; chaînes FR / EN / AR ; XCUITest en `-UITestLocal`. Il remplace l'`UPDATE` SQL du guide d'essai papier.

**Architecture :** les écrans et le `PrinterStore` existent déjà (`AdminScreen.swift`, `AdminStores.swift`) ; on les étend, on ne les remplace pas. Le transport et l'identifiant ne s'affichent qu'en mode autonome (le serveur .NET ne les connaît pas). La recherche vit dans `PosPrinters` (qui voit `PosKit` ; `PosKit` ne voit pas les SDK) : un `PrinterDiscoveryStore` `@Observable`, branché sur `AppEnvironment.printerDiscovery` (lot 4c). Les statuts sont interrogés toutes les 5 s tant que l'écran est ouvert (pas de SignalR en autonome).

**Tech Stack :** Swift 6, SwiftUI, Swift Testing (PosKit, PosPrinters), XCUITest, String Catalog `Localizable.xcstrings` (app) et `Localizable.strings` fr/en/ar (PosKit, via `L10n`).

**Spec :** `docs/superpowers/specs/2026-10-22-ipad-standalone-4-impression-design.md` (§ 4d). Reporté du 4c : `docs/plans/ipad-standalone-4c-progress.md`, section « Reporté au lot 4d ».

## Global Constraints

- Branche : `feat/ipad-standalone-4d-ecran-imprimantes`, créée depuis `main` (`667f8d0`). Pousser à la fin de chaque tâche. Fusion dans `main` seulement sur demande de l'utilisateur.
- `PosKit` reste sans dépendance externe ; `PosKit` n'importe jamais `PosPrinters`. Les vues SwiftUI ne parlent qu'aux stores.
- Éviter les noms qui collisionnent avec SwiftUI / Foundation ; découper les gros `body` SwiftUI (délai du vérificateur de types).
- Chaînes d'interface : jeux de clés **identiques** FR / EN / AR (`LocalizationTests` du PosKit vérifie la parité des `.strings` ; le catalogue de l'app est vérifié par la tâche 3 : chaque clé nouvelle a `fr`, `en`, `ar`).
- Les messages d'erreur du moteur (`PrintJobs.LastError`, résultats de `testPrinter`) restent en français comme aux lots 4b et 4c.
- Tests UI : jamais le vrai transport ni le vrai SDK. `-UITestLocal` injecte `FakePrinterTransport` (déjà) et, nouveau, une `FakePrinterDiscovery`.
- Ne jamais builder avec `CODE_SIGNING_ALLOWED=NO` pour tester appairage ou découverte réseau (le build simulateur reste autorisé pour l'UI).
- Pas de `swift package purge-cache` (global). Cache de manifeste SwiftPM périmé : `env POS_X=$RANDOM xcodebuild …`.
- Mettre à jour `docs/plans/ipad-standalone-4d-progress.md` après chaque tâche (statut, compteurs PosKit / PosPrinters / UI, constats). Suites à chaque tâche : `cd ios/Packages/PosKit && swift test`, `cd ios/Packages/PosPrinters && swift test`, `cd ios && ./scripts/test.sh unit`.

## Review Focus

1. **Imprimante Bluetooth/USB sans adresse IP** : aujourd'hui `PrinterStore.save` exige une IPv4 valide et `LocalPosAPI.savePrinter` refuse une adresse vide (400). Une Star Bluetooth ou une Epson USB doit s'enregistrer sans IP. Tâche 1.
2. **Mode serveur** : le sélecteur de transport et le champ identifiant n'apparaissent pas ; le corps envoyé au .NET reste inchangé (déjà garanti par l'`encode` de `Printer`). Tâche 3 (cas (f) de la tâche 4).
3. **Recherche** : une seconde recherche annule la première ; zéro résultat affiche un message, pas un écran vide ; fermer la feuille annule la recherche (pas de continuation orpheline). Tâches 2 et 3.
4. **Identifiant mal formé** saisi à la main (ex. `bluetooth:` sans valeur, cible Epson sans préfixe `TCP:`/`BT:`/`USB:`) : refus clair à l'enregistrement, pas de job qui échoue plus tard. Tâche 1.
5. **Rafraîchissement des statuts** : la boucle s'arrête quand l'écran disparaît (aucune tâche en arrière-plan) et n'empile pas de requêtes. Tâche 3.

---

## File Structure

- Modifier `Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Printers.swift` (validation par transport, message de test), `Stores/AdminStores.swift` (`PrinterStore`), `Testing/InMemoryPosAPI.swift` (parité), `Resources/{fr,en,ar}.lproj/Localizable.strings`.
- Créer `Packages/PosPrinters/Sources/PosPrinters/PrinterDiscoveryStore.swift`, `FakePrinterDiscovery.swift` ; modifier `PrinterDiscovery.swift` (doc), `PosPrintersStar/StarDiscovery.swift` (IP LAN), `PosPrintersEpson/EpsonDiscovery.swift` (délégué).
- Modifier `RestaurantPOS/Features/Admin/AdminScreen.swift` (éditeur, recherche, statuts), éventuellement extraire `PrinterSearchSheet.swift` dans le même dossier ; `RestaurantPOS/App/RestaurantPOSApp.swift` (découverte factice en test UI) ; `RestaurantPOS/Resources/Localizable.xcstrings`.
- Tests : `Packages/PosKit/Tests/PosKitTests/Local/LocalPrinterTransportAdminTests.swift`, `PrinterStoreTests.swift`, `Packages/PosPrinters/Tests/PosPrintersTests/PrinterDiscoveryStoreTests.swift`, `RestaurantPOSUITests/PrinterAdminUITests.swift`.

---

### Task 1: Validation par transport (PosKit)

**Files:**
- Modify: `LocalPosAPI+Printers.swift`, `AdminStores.swift` (`PrinterStore`), `InMemoryPosAPI.swift`
- Test: `Tests/PosKitTests/Local/LocalPrinterTransportAdminTests.swift`, `Tests/PosKitTests/PrinterStoreTests.swift` (compléter s'il existe, sinon créer)

**Interfaces:**
- Produces : `PrinterStore.validate(_ printer: Printer) -> String?` (clé `L10n` du refus, `nil` si valide) ; `public static func isValidDeviceIdentifier(_ id: String?, transport: PrinterTransportKind) -> Bool` dans `PrinterStore`. `PosKit` n'importe pas `PosPrinters` : réécrire la grammaire de `PrinterAddress.parse` en une dizaine de lignes dans `PosKit`, avec un test qui reprend les mêmes cas que `PrinterAddressTests`.
- Règles : `lan` → nom non vide et IPv4 valide (inchangé) ; `star` / `epson` → nom non vide et identifiant valide (Star : `<lan|bluetooth|bluetoothLE|usb>:<non vide>` ; Epson : `[<série>;]` puis `TCP:`, `BT:` ou `USB:` suivi d'un texte non vide) ; l'IP est facultative et jamais obligatoire. `LocalPosAPI.savePrinter` applique la même règle côté acteur (400 « Identifiant d'imprimante invalide : … » ; adresse vide acceptée si `transport != .lan`). `testPrinter` : le message de succès ou d'échec cite `ip:port` pour `lan`, sinon le nom et l'identifiant du périphérique.

- [ ] **Step 1 : tests (RED).** `LocalPrinterTransportAdminTests` : enregistrer une imprimante `star` + `bluetooth:00:11:62:AA:BB:CC` sans IP → relue avec les mêmes champs ; `epson` + `TM_T88;TCP:192.168.1.60` ; `lan` sans IP → 400 ; `star` + `bluetooth:` → 400 ; `epson` + `XYZ:1` → 400 ; mise à jour d'une imprimante `lan` vers `star` conserve l'id. `PrinterStoreTests` : `validate` renvoie la bonne clé pour chaque cas ; `InMemoryPosAPI` accepte et refuse les mêmes cas ; message de `testPrinter` d'une imprimante non LAN sans IP (aucun « :9100 »).
- [ ] **Step 2 : implémenter** (`validate`, grammaire, garde dans `savePrinter` local et en mémoire, message de test). Clé `L10n` FR / EN / AR : `admin.device_identifier_invalid`.
- [ ] **Step 3 : suites + commit** `feat(ios): validation des imprimantes par transport, sans IP pour Bluetooth/USB (4d)`.

---

### Task 2: Recherche d'imprimantes (store, report du 4c)

**Files:**
- Create: `PrinterDiscoveryStore.swift`, `FakePrinterDiscovery.swift` (dans `Sources/PosPrinters/`)
- Modify: `PrinterDiscovery.swift` (doc), `PosPrintersStar/StarDiscovery.swift`, `PosPrintersEpson/EpsonDiscovery.swift`
- Test: `Tests/PosPrintersTests/PrinterDiscoveryStoreTests.swift`

**Interfaces:**
- Consumes : `PrinterDiscovery`, `DiscoveredPrinter`, `PosKit.Printer`.
- Produces :

```swift
@MainActor @Observable
public final class PrinterDiscoveryStore {
    public enum Phase: Equatable, Sendable { case idle, searching, done }
    public private(set) var phase: Phase
    public private(set) var results: [DiscoveredPrinter]
    public init(discovery: any PrinterDiscovery, timeout: TimeInterval = 10)
    /// Lance (ou relance : annule la précédente). Une source en échec est déjà absorbée par `CombinedPrinterDiscovery`.
    public func search() async
    public func cancel()                                    // phase → idle, résultats conservés
    /// Brouillon pour l'éditeur : nom = modèle, ip = `ipAddress ?? ""`, transport, identifiant, papier 80 mm, poste RECEIPT.
    public func draft(from found: DiscoveredPrinter) -> Printer
}
public struct FakePrinterDiscovery: PrinterDiscovery { public init(_ found: [DiscoveredPrinter], delay: TimeInterval = 0) }
```

- Report du 4c à solder ici : `StarDiscovery` renseigne `ipAddress` avec `printer.information?.detail.lan.ipAddress` pour l'interface `lan` (nom d'API à vérifier dans le `.swiftinterface` de StarIO10 2.14.1, pas de mémoire ; repli sur `identifier` si l'IP est absente) ; `EpsonDiscovery.Delegate` reste référencé jusqu'à ce que `stop()` réussisse ou que les 20 essais soient épuisés, et l'échec est journalisé ; le commentaire de `PrinterDiscovery` dit la vérité (dédoublonnage porté par `CombinedPrinterDiscovery`, résultats dans l'ordre des sources). Entitlement `com.apple.developer.networking.multicast` : **non ajouté** (il exige l'accord d'Apple) ; noter dans `CLAUDE.md` (tâche 4) que la recherche réseau peut ne rien trouver sans lui, que la saisie manuelle de l'IP reste le chemin garanti, et que la recherche Bluetooth / USB n'en a pas besoin.

- [ ] **Step 1 : tests (RED).** `PrinterDiscoveryStoreTests` avec `FakePrinterDiscovery` : `search()` passe `searching` → `done` avec les résultats ; zéro résultat → `done`, `results` vide ; deux `search()` successifs : seule la dernière compte (délai de 0,2 s pour la première) ; `cancel()` pendant la recherche → `idle`, aucune mise à jour tardive ; `draft(from:)` pour une Star Bluetooth (ip vide, identifiant conservé) et une Epson réseau (ip remplie).
- [ ] **Step 2 : implémenter** (annulation par `Task` stockée). Vérifier la compilation iOS du paquet : `xcodebuild -scheme PosPrinters-Package -destination 'generic/platform=iOS Simulator' build` (SDK Epson présent dans `Vendor/`).
- [ ] **Step 3 : suites + commit** `feat(ios): store de recherche d'imprimantes, IP LAN Star, délégué Epson (4d)`.

---

### Task 3: Écran Imprimantes (éditeur, recherche, statuts, chaînes)

**Files:**
- Modify: `RestaurantPOS/Features/Admin/AdminScreen.swift` (`PrintersAdminView`, `PrinterEditorSheet`, `statusText`), `RestaurantPOS/App/RestaurantPOSApp.swift`, `RestaurantPOS/Resources/Localizable.xcstrings`, `Packages/PosKit/Sources/PosKit/Resources/{fr,en,ar}.lproj/Localizable.strings`
- Create (si `AdminScreen.swift` grossit trop) : `RestaurantPOS/Features/Admin/PrinterSearchSheet.swift`
- Test: `Tests/PosKitTests/LocalizationTests.swift` (parité des clés nouvelles du PosKit)

**Interfaces:**
- Consumes : `PrinterStore.validate`, `PrinterDiscoveryStore`, `AppEnvironment.printerDiscovery`, et la façon dont `AdminScreen` sait déjà qu'on est en autonome (lire le code).
- Produces : identifiants d'accessibilité `printer.transport` (picker), `printer.deviceId` (champ), `printer.search` (bouton), `printer.search.result.<n>`, `printer.search.empty`, plus les existants `printer.add`, `printer.name`, `printer.save`, `printer.test.<nom>`, `printer.status`.

Règles :
- **Éditeur** (autonome seulement) : `Picker` transport (LAN / Star / Epson, libellés localisés) ; si `lan` : champs IP et port actuels ; sinon : champ identifiant (clavier ASCII, exemple selon le transport : `bluetooth:00:11:62:AA:BB:CC` / `TM_T88;TCP:192.168.1.60`), champ IP facultatif, bouton « Rechercher » qui ouvre la feuille ; choisir un résultat remplit le brouillon (`draft(from:)`, le nom n'est écrasé que s'il est vide). Papier, tiroir, mode texte, postes : inchangés. Enregistrer passe par `PrinterStore.validate`.
- **Feuille de recherche** : `ProgressView` pendant `searching`, liste (nom, transport, identifiant), message « Aucune imprimante trouvée — saisissez l'adresse à la main » si `done` est vide, bouton « Relancer » ; `search()` à l'ouverture ; fermer la feuille appelle `cancel()`.
- **Ligne d'imprimante** : sous-titre `ip:port · papier · postes` pour `lan`, sinon `transport · identifiant · papier · postes`. Boucle de rafraîchissement de `statuses` toutes les 5 s par `.task` sur `PrintersAdminView` (annulée avec la vue, une requête à la fois) ; le rafraîchissement après test, relance et annulation existe déjà (`act`).
- **Chaînes** : toutes les clés neuves en FR / EN / AR dans le catalogue de l'app et, pour celles que lit un store, dans `Localizable.strings` : libellés de transport, « Rechercher », « Recherche en cours… », « Aucune imprimante trouvée… », « Relancer », identifiant, exemples.
- **Découverte injectée** : `RestaurantPOSApp` fournit un `PrinterDiscoveryStore(discovery: printerDiscovery)` à l'écran ; en test UI (`-UITestMode`, `-UITestLocal*`), `FakePrinterDiscovery([DiscoveredPrinter(transport: .epson, name: "TM-T88VI", deviceIdentifier: "TM_T88;TCP:192.168.1.60", ipAddress: "192.168.1.60"), DiscoveredPrinter(transport: .star, name: "TSP143IV", deviceIdentifier: "bluetooth:00:11:62:AA:BB:CC", ipAddress: nil)])`.

- [ ] **Step 1 : implémenter** l'éditeur, la feuille de recherche, la boucle de statuts et les chaînes ; `cd ios && xcodegen generate` ; build simulateur de l'app (commande du rapport de la tâche 4 du lot 4c). Découper les `body` longs.
- [ ] **Step 2 : parité des chaînes.** Étendre `LocalizationTests` : chaque clé neuve de `Localizable.strings` existe en fr, en et ar. Contrôler les clés neuves du catalogue de l'app par un script `python3 -I` (fr, en, ar présents) et consigner le résultat dans le rapport.
- [ ] **Step 3 : suites + commit** `feat(ios): écran Imprimantes complété (transport, recherche, statuts) (4d)`.

---

### Task 4: XCUITest, documentation, vérification finale

**Files:**
- Create: `RestaurantPOSUITests/PrinterAdminUITests.swift`
- Modify: `docs/impression-essai-papier.md`, `CLAUDE.md`, `docs/plans/ipad-standalone-4d-progress.md`

- [ ] **Step 1 : XCUITest** (`launchStandalone()`, puis section Gestion → `admin.printers`). Cas : (a) ajouter une imprimante : `printer.add` → transport Epson → `printer.search` → résultat « TM-T88VI » → identifiant rempli → enregistrer → la ligne apparaît ; (b) le bouton de test de cette ligne → toast « Test d'impression réussi » (transport factice) ; (c) une Star Bluetooth sans IP s'enregistre ; (d) un identifiant invalide → toast d'avertissement et la feuille reste ouverte ; (e) le parcours (a) en `language: "en"` et en `"ar"` (éléments trouvés par identifiant d'accessibilité, un libellé visible par langue vérifié) ; (f) en mode serveur (`launch(section: "admin")`), `printer.transport` n'existe pas. Lancer ces tests seulement : `xcodebuild test -only-testing:RestaurantPOSUITests/PrinterAdminUITests`.
- [ ] **Step 2 : documentation.** Guide d'essai papier : remplacer l'`UPDATE` SQL et l'échange de conteneur par la saisie dans l'écran (garder en annexe courte l'ancienne méthode avec son avertissement fiscal) ; `CLAUDE.md` : paragraphe « Impression, lot 4d » (écran, store de recherche, boucle de statuts, tests UI, entitlement multicast non ajouté, et la règle de suivi .NET / Swift de la spec : toute modification de `TicketDocumentBuilder`, `EscPosTextRenderer`, `PrintDispatcher`, `PrintQueueProcessor` ou de l'entité `PrintJob` côté .NET impose de régénérer `ticket_document_reference.json` et de reporter le changement dans `Local/`) ; fichier de suivi.
- [ ] **Step 3 : vérifications finales** : `dotnet test RestaurantPos.slnx` (flake connu entre minuit et 2 h, heure de Paris), `swift test` PosKit et PosPrinters, `./scripts/test.sh unit`, build de l'app, `PrinterAdminUITests` et `PrintingUITests`. Noter les compteurs dans le fichier de suivi.
- [ ] **Step 4 : commit** `docs: lot 4d fait (écran Imprimantes)`, push.

---

## Décisions à confirmer

- **Page de test directe.** `testPrinter` envoie la page sans passer par la file (comme `SendTestPrintAsync` du .NET) : il n'y a donc pas de job « envoyé » à observer. La preuve de la spec (« voir le job passer à envoyé ») devient : toast de réussite du test, la file étant déjà couverte en 4b. Si vous voulez que le test passe par la file, c'est un écart au .NET à décider avant la tâche 3.
- **Entitlement multicast** non demandé à Apple dans ce lot.
