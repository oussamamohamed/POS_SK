# Lot 4c — Imprimantes Star et Epson — Plan d'implémentation

> **Pour les agents d'exécution :** SOUS-SKILL REQUIS : superpowers:subagent-driven-development (recommandé) ou superpowers:executing-plans. Les étapes utilisent des cases `- [ ]`.

**Objectif :** le mode autonome imprime aussi sur les imprimantes Star (SDK StarIO10) et Epson (SDK ePOS2), en réseau, Bluetooth ou USB, derrière le protocole `PrinterTransport` du lot 4b, et sait rechercher ces imprimantes. Les octets envoyés restent ceux de `TicketPayload.build` (ESC/POS) : aucun changement de rendu.

**Architecture :** nouveau paquet SPM `ios/Packages/PosPrinters`, dépendant de `PosKit`. Il contient :
- une cible cœur `PosPrinters`, sans SDK, compilée sur macOS et iOS et testée par `swift test`. Elle porte l'adresse du périphérique (`deviceIdentifier`), un protocole de session (`PrinterSession`), le transport générique `SessionPrinterTransport` (ouverture, envoi, fermeture, délais, traduction d'erreurs) et la recherche (`PrinterDiscovery`) ;
- deux adaptateurs minces, compilés seulement pour iOS (`#if os(iOS)`, dépendances conditionnelles) : `PosPrintersStar` (StarIO10 par SPM) et `PosPrintersEpson` (xcframework Epson locale, présente seulement si l'utilisateur l'a installée).
`PosKit` reste sans dépendance externe. L'app branche `RoutingPrinterTransport(lan:star:epson:)` avec les adaptateurs disponibles.

**Tech Stack :** Swift 6, Swift Testing, SPM, StarXpand SDK iOS (`https://github.com/star-micronics/StarXpand-SDK-iOS`, produit `StarIO10`, iOS 15+, binaire iOS seulement), Epson ePOS SDK for iOS (`libepos2.xcframework`, téléchargement manuel chez Epson, licence interdisant a priori la redistribution), XcodeGen.

**Spec :** `docs/superpowers/specs/2026-10-22-ipad-standalone-4-impression-design.md` (lot 4c, décision « SDK » : si Epson bloque, 4c livre Star et laisse Epson derrière un protocole prêt).

## Global Constraints

- Branche : `feat/ipad-standalone-4c-star-epson`, créée depuis `main` (`e3410d9`). Pousser à la fin de chaque tâche.
- `PosKit` reste sans dépendance externe ; les SDK ne sont importés que dans `PosPrintersStar` et `PosPrintersEpson`, sous `#if os(iOS)` et `#if canImport(...)`.
- `swift test` dans `ios/Packages/PosKit` ET dans `ios/Packages/PosPrinters` doit passer sur macOS sans SDK Epson installé.
- StarXpand épinglé en version exacte `2.14.1` (`exact: "2.14.1"`).
- SDK Epson : jamais commité. Il est installé par `ios/scripts/install_epson_sdk.sh <zip téléchargé>` dans `ios/Packages/PosPrinters/Vendor/` (git-ignoré). Sans lui, tout compile et `epson` reste `unsupported` (comportement 4b).
- Délais identiques au LAN : ouverture/connexion 3 s, envoi 10 s.
- Format de `Printer.deviceIdentifier` (colonne `DeviceIdentifier`, lot 4b) :
  - Star : `<interface>:<identifiant>` avec `interface` ∈ `lan`, `bluetooth`, `bluetoothLE`, `usb` (ex. `bluetooth:00:11:62:AA:BB:CC`, `lan:192.168.1.50`). `nil` ou vide → `lan:<ipAddress>`.
  - Epson : `<série>;<cible>` où `cible` est la chaîne native du SDK (`TCP:192.168.1.60`, `BT:00:01:90:AA:BB:CC`, `USB:000000000000000000`) et `série` un nom de `Epos2PrinterSeries` sans préfixe (`TM_M30`, `TM_T88`…). Série absente → `TM_T88`. `nil` ou vide → `TM_T88;TCP:<ipAddress>`.
- Messages d'erreur en français (ils vont dans `PrintJobs.LastError`), via `PrinterTransportError.connection(String)` sauf délais (`connectTimeout`, `writeTimeout`).
- Aucune chaîne d'interface (lot 4d). Mettre à jour `docs/plans/ipad-standalone-4c-progress.md` après chaque tâche (statut, compteurs PosKit / PosPrinters / .NET, constats).
- Tests à chaque tâche : `cd ios/Packages/PosKit && swift test`, `cd ios/Packages/PosPrinters && swift test`, et (dès la tâche 2) la compilation iOS du paquet : `cd ios/Packages/PosPrinters && xcodebuild -scheme PosPrinters-Package -destination 'generic/platform=iOS Simulator' build`. `dotnet test RestaurantPos.slnx` une fois en fin de lot (aucun changement .NET attendu).

## Review Focus

1. Imprimante Star configurée en StarPRNT (pas en émulation ESC/POS) : les octets ESC/POS sortent en charabia. Le transport ne peut pas le détecter ; le guide d'essai papier (tâche 5) impose de vérifier l'émulation. Tâche 5.
2. Délai dépassé dans le SDK (connexion ou impression) : doit donner `connectTimeout` / `writeTimeout`, jamais un blocage du moteur. Tâches 1, 2 et 3 (sessions factices qui ne répondent jamais).
3. Fermeture toujours appelée, même après un échec d'envoi (sinon la connexion Bluetooth reste prise et le job suivant échoue). Tâche 1.
4. `deviceIdentifier` mal formé ou vide : erreur claire (`connection("Identifiant d'imprimante invalide : …")`), pas de crash. Tâche 1.
5. Build sans SDK Epson : l'app compile et imprime en LAN et Star ; une imprimante Epson donne « Transport non pris en charge : epson ». Tâches 3 et 4.

## File Structure

- Créer `ios/Packages/PosPrinters/Package.swift`, `Sources/PosPrinters/` (`PrinterAddress.swift`, `PrinterSession.swift`, `SessionPrinterTransport.swift`, `PrinterDiscovery.swift`, `EpsonCodes.swift`), `Sources/PosPrintersStar/` (`StarSession.swift`, `StarDiscovery.swift`), `Sources/PosPrintersEpson/` (`EpsonSession.swift`, `EpsonDiscovery.swift`), `Tests/PosPrintersTests/`.
- Créer `ios/scripts/install_epson_sdk.sh` ; modifier `.gitignore`, `ios/project.yml`, `ios/RestaurantPOS/App/RestaurantPOSApp.swift`, `ios/scripts/test.sh` (ajout de `swift test` de PosPrinters au mode `unit`), `CLAUDE.md`.
- Créer `docs/impression-essai-papier.md` (guide d'essai sur matériel).

---

### Task 1: Paquet `PosPrinters`, adresse, session et transport générique

**Files:**
- Create: `ios/Packages/PosPrinters/Package.swift`, `Sources/PosPrinters/PrinterAddress.swift`, `PrinterSession.swift`, `SessionPrinterTransport.swift`
- Test: `Tests/PosPrintersTests/PrinterAddressTests.swift`, `SessionPrinterTransportTests.swift`
- Modify: `ios/scripts/test.sh` (mode `unit` : lancer aussi `swift test` dans `Packages/PosPrinters`)

**Interfaces:**
- Consumes : `PosKit.PrinterTransport`, `PrinterTransportError`, `Printer` (`transport`, `deviceIdentifier`, `ipAddress`).
- Produces :

```swift
public enum StarInterface: String, Sendable { case lan, bluetooth, bluetoothLE, usb }
public enum PrinterAddress: Equatable, Sendable {
    case star(interface: StarInterface, identifier: String)
    case epson(series: String, target: String)
    /// Lit `printer.deviceIdentifier` selon `printer.transport` ; nil/vide → réseau sur `ipAddress`. Lève `PrinterTransportError.connection("Identifiant d'imprimante invalide : …")`.
    public static func parse(_ printer: Printer) throws -> PrinterAddress
    /// Chaîne à stocker dans `deviceIdentifier` (inverse de `parse`).
    public var deviceIdentifier: String { get }
}

/// Une connexion à une imprimante, ouverte pour un envoi puis fermée.
public protocol PrinterSession: Sendable {
    func open(timeout: TimeInterval) async throws
    func send(_ payload: [UInt8], timeout: TimeInterval) async throws
    func close() async
}

public enum PrinterSessionFailure: Error, Equatable, Sendable { case timeout, failed(String) }

/// Transport commun Star/Epson : parse l'adresse, crée la session, open → send → close (close toujours), traduit les erreurs.
public struct SessionPrinterTransport: PrinterTransport {
    public init(connectTimeout: TimeInterval = 3, writeTimeout: TimeInterval = 10,
                makeSession: @escaping @Sendable (PrinterAddress) throws -> any PrinterSession)
}
```

Traduction : `PrinterSessionFailure.timeout` pendant `open` → `connectTimeout` ; pendant `send` → `writeTimeout` ; `failed(m)` → `connection(m)` ; toute autre erreur → `connection(error.localizedDescription)`. Les délais sont aussi garantis par le transport lui-même (course entre l'appel de session et une tâche de délai, `withThrowingTaskGroup`) : une session qui ne répond jamais donne le délai, `close()` est appelée, et la fonction rend la main.

- [ ] **Step 1 : manifeste.** `swift-tools-version: 6.0`, plateformes `.iOS(.v17), .macOS(.v14)`, dépendances : `PosKit` (`path: "../PosKit"`) et StarXpand (`.package(url: "https://github.com/star-micronics/StarXpand-SDK-iOS", exact: "2.14.1")`). Cibles : `PosPrinters` (dépend de `PosKit`), `PosPrintersStar` (dépend de `PosPrinters` et de `.product(name: "StarIO10", package: "StarXpand-SDK-iOS", condition: .when(platforms: [.iOS]))`), `PosPrintersEpson` (dépend de `PosPrinters` ; la cible binaire Epson est ajoutée à la tâche 3), test `PosPrintersTests` (dépend de `PosPrinters` seulement). Produits : `PosPrinters`, `PosPrintersStar`, `PosPrintersEpson`. Les deux cibles d'adaptateur contiennent pour l'instant un fichier vide sous `#if os(iOS)`. Vérifier que `swift test` sur macOS résout le paquet Star sans essayer de le lier ; vérifier aussi le nom d'identité du paquet Star tel que SPM le voit (le manifeste Star s'appelle `StarIO10`, l'URL `StarXpand-SDK-iOS`) et noter dans le fichier de suivi la forme retenue.
- [ ] **Step 2 : tests (RED).** `PrinterAddressTests` : Star (`bluetooth:00:11:62:AA:BB:CC` garde les `:` de l'identifiant, `bluetoothLE:…`, `usb:…`, `lan:…`, `nil` → `lan:<ip>`, vide → `lan:<ip>`, interface inconnue → erreur), Epson (`TM_M30;TCP:192.168.1.60`, cible seule → série `TM_T88`, `nil` → `TM_T88;TCP:<ip>`, cible sans préfixe connu `TCP:`/`BT:`/`USB:` → erreur), aller-retour `parse` ↔ `deviceIdentifier`, `transport == .lan` → erreur (ce transport ne sert pas au LAN). `SessionPrinterTransportTests` avec une session factice (`actor` dans les tests) : succès → `open`, `send` (octets identiques), `close` dans cet ordre ; échec d'`open` → `close` appelée quand même, erreur `connection` ; `send` échoue → `close` appelée ; session qui ne répond jamais à `open` (délai 0,2 s) → `connectTimeout` en moins de 2 s et `close` appelée ; idem `send` → `writeTimeout` ; `PrinterSessionFailure.timeout` renvoyé par la session → bon délai ; `makeSession` qui lève → erreur propagée sans appel de session ; adresse invalide → erreur claire, pas de session créée.
- [ ] **Step 3 : implémenter.** Puis `swift test` (PosKit et PosPrinters), `./scripts/test.sh unit` depuis `ios/`, commit `feat(ios): paquet PosPrinters, adresse et transport générique par session (4c)`.

---

### Task 2: Adaptateur Star (StarIO10) et recherche

**Files:**
- Create: `Sources/PosPrintersStar/StarSession.swift`, `StarDiscovery.swift` ; `Sources/PosPrinters/PrinterDiscovery.swift`
- Test: `Tests/PosPrintersTests/PrinterDiscoveryTests.swift` (logique commune, sans SDK)

**Interfaces:**
- Produces :

```swift
// PosPrinters (cœur)
public struct DiscoveredPrinter: Equatable, Sendable {
    public let transport: PrinterTransportKind      // .star ou .epson
    public let name: String                          // modèle lu par le SDK, ex. « TSP143IV »
    public let deviceIdentifier: String              // au format de PrinterAddress
    public let ipAddress: String?                    // pour une imprimante réseau
}
public protocol PrinterDiscovery: Sendable {
    /// Recherche pendant `timeout` ; résultats sans doublon (même deviceIdentifier), dans l'ordre d'arrivée.
    func discover(timeout: TimeInterval) async throws -> [DiscoveredPrinter]
}
/// Fusionne plusieurs recherches ; l'échec d'une source n'empêche pas les autres (elle est ignorée et journalisée).
public struct CombinedPrinterDiscovery: PrinterDiscovery { public init(_ sources: [any PrinterDiscovery]) }

// PosPrintersStar (iOS)
public enum StarPrinters {
    public static func transport() -> SessionPrinterTransport      // makeSession → StarSession
    public static func discovery(interfaces: [StarInterface] = [.lan, .bluetooth, .bluetoothLE, .usb]) -> any PrinterDiscovery
}
```

`StarSession` : `StarPrinter(StarConnectionSettings(interfaceType:identifier:))`, `openTimeout`/`printTimeout` en millisecondes depuis les délais reçus, `open()`, `print(raw: Data(payload))`, `close()`. Erreurs StarIO10 : délai → `PrinterSessionFailure.timeout` ; les autres → `failed(message français)` avec au minimum : introuvable « Imprimante introuvable », occupée « Imprimante occupée par un autre appareil », non imprimé « Impression non effectuée (couvercle ouvert ou papier absent ?) », sinon « Erreur Star : <description du SDK> ». `StarPrinter` est une classe non `Sendable` : l'envelopper dans un `actor` (un seul appelant à la fois, déjà garanti par le moteur 4b qui sérialise par imprimante). `StarDiscovery` : gestionnaire de recherche de StarIO10 pour les interfaces demandées, durée en ms, délégué converti en `async` par continuation (reprise unique, aussi en cas d'erreur de démarrage), modèle → `name`, `lan` → `ipAddress` = identifiant.

- [ ] **Step 1 : tests (RED)** de `CombinedPrinterDiscovery` (fusion, dédoublonnage par `deviceIdentifier`, source en échec ignorée, ordre d'arrivée) avec des sources factices. L'adaptateur Star ne se teste pas sans matériel : il reste mince (aucune logique hors appels SDK et traduction d'erreurs).
- [ ] **Step 2 : implémenter**, puis compiler pour iOS (`xcodebuild -scheme PosPrinters-Package -destination 'generic/platform=iOS Simulator' build` depuis `ios/Packages/PosPrinters`). Vérifier les noms exacts de l'API StarIO10 2.14.1 (types d'erreur, gestionnaire de recherche, délégué) dans l'interface Swift du xcframework résolu (`.swiftinterface`), pas de mémoire.
- [ ] **Step 3 : suites + commit** `feat(ios): imprimantes Star (StarIO10) et recherche (4c)`.

---

### Task 3: Adaptateur Epson (ePOS2), installation du SDK, recherche

**Files:**
- Create: `ios/scripts/install_epson_sdk.sh`, `Sources/PosPrintersEpson/EpsonSession.swift`, `EpsonDiscovery.swift`, `Sources/PosPrinters/EpsonCodes.swift` (traduction pure des codes, testable)
- Modify: `ios/Packages/PosPrinters/Package.swift` (cible binaire conditionnelle), `.gitignore` (`ios/Packages/PosPrinters/Vendor/`)
- Test: `Tests/PosPrintersTests/EpsonCodesTests.swift`, test du script

**Interfaces:**
- Produces :

```swift
// PosPrintersEpson (iOS, et seulement si le SDK est installé)
public enum EpsonPrinters {
    public static var isAvailable: Bool { get }                    // true si compilé avec le SDK
    public static func transport() -> SessionPrinterTransport?     // nil sans SDK
    public static func discovery() -> (any PrinterDiscovery)?      // nil sans SDK
}
```

Règles :
- **Installation.** `install_epson_sdk.sh <chemin du zip ou du dossier Epson>` : copie `libepos2.xcframework` dans `ios/Packages/PosPrinters/Vendor/`, vérifie la présence des en-têtes (`ePOS2.h`), et, si le xcframework n'a pas de `module.modulemap`, en ajoute un (`module libepos2 { header "ePOS2.h" export * }`) dans le dossier `Headers` de chaque tranche. Le script affiche la version trouvée et rappelle que le SDK ne doit pas être commité. Idempotent.
- **Manifeste.** `Package.swift` teste `FileManager.default.fileExists(atPath:)` sur `Vendor/libepos2.xcframework` (chemin calculé depuis `#filePath`) ; s'il existe, il ajoute `.binaryTarget(name: "libepos2", path: "Vendor/libepos2.xcframework")` et la dépendance `.target(name: "libepos2", condition: .when(platforms: [.iOS]))` à `PosPrintersEpson`, avec les `linkerSettings` exigés par le guide d'intégration Epson du zip (a priori `ExternalAccessory`, `CoreBluetooth`, `xml2`). Sans le SDK, `PosPrintersEpson` compile avec `isAvailable == false`.
- **Session.** `Epos2Printer(printerSeries:lang:)` (série lue de l'adresse ; langue ANK), `connect(target, timeout:)` (ms), `addCommand(Data(payload))`, `sendData(timeout)`, résultat via le délégué de réception converti en `async` (continuation à reprise unique), puis `clearCommandBuffer()` et `disconnect()` dans `close()`. Les objets Epson ne sont pas thread-safe : tout passe par un `actor` par session. Codes de retour et codes de réception → `PrinterSessionFailure` par `EpsonCodes` (cœur, testable sans SDK car il prend des entiers) : délai → `.timeout` ; connexion ou introuvable → « Imprimante introuvable » ; occupée → « Imprimante occupée par un autre appareil » ; couvercle → « Couvercle ouvert » ; papier → « Plus de papier » ; autre → « Erreur Epson (code N) ». Les valeurs numériques des constantes sont relevées dans `ePOS2.h` et recopiées dans `EpsonCodes` avec la version du SDK en commentaire ; un test sous `#if canImport(libepos2)` vérifie qu'elles égalent les constantes du SDK.
- **Recherche.** `Epos2Discovery.start(_:delegate:)` (réseau, Bluetooth, USB ; imprimantes seulement), arrêt après le délai par `Epos2Discovery.stop()` (réessayer tant qu'il renvoie « en cours »), `target` → cible, `deviceName` → `name` et série (table `EpsonCodes.series(forDeviceName:)` : TM-m30, TM-m30II, TM-m30III, TM-m50, TM-T20, TM-T82, TM-T88 ; inconnu → `TM_T88`), `ipAddress`.
- **Plan B (spec).** Si le SDK n'est pas installé au moment de cette tâche, l'implémenteur écrit et teste tout ce qui n'exige pas le SDK (script sur un faux xcframework dans un dossier temporaire, manifeste conditionnel, `EpsonCodes` avec les valeurs du SDK 2.32.0 relevées dans la documentation Epson publique et marquées « à vérifier », branche « sans SDK »), écrit l'adaptateur réel derrière `#if canImport(libepos2)`, et consigne dans le fichier de suivi qu'il n'est pas compilé tant que `install_epson_sdk.sh` n'a pas été lancé.

- [ ] **Step 1 : tests (RED)** `EpsonCodesTests` (chaque code → bonne erreur, série par nom d'appareil) ; test du script d'installation sur un faux xcframework (deux tranches, sans modulemap → modulemap ajouté ; relance idempotente ; zip sans `libepos2.xcframework` → code de sortie non nul et message). Le test du script est un test shell lancé par `swift test` (via `Process`) ou un script `ios/scripts/test_install_epson_sdk.sh` appelé par `./scripts/test.sh unit` : au choix, le plus simple.
- [ ] **Step 2 : implémenter.** Vérifier `swift test` (macOS, sans SDK), le build iOS du paquet sans SDK, et — si le SDK est installé — le build iOS avec SDK.
- [ ] **Step 3 : suites + commit** `feat(ios): imprimantes Epson (ePOS2, SDK local optionnel) et recherche (4c)`.

---

### Task 4: Branchement dans l'app

**Files:**
- Modify: `ios/project.yml`, `ios/RestaurantPOS/App/RestaurantPOSApp.swift`

Règles :
- `project.yml` : paquet local `PosPrinters` (`path: Packages/PosPrinters`), dépendances de l'app `PosPrinters`, `PosPrintersStar`, `PosPrintersEpson`. Clés `Info.plist` : `NSBluetoothAlwaysUsageDescription` (« La caisse utilise le Bluetooth pour communiquer avec les imprimantes de tickets. »), `UISupportedExternalAccessoryProtocols` = [`jp.star-m.starpro`, `com.epson.escpos`] (Bluetooth classique et USB/Lightning ; exige l'enregistrement MFi auprès de Star et d'Epson avant une publication sur l'App Store : à consigner dans `CLAUDE.md`, sans effet sur les builds de développement). `NSLocalNetworkUsageDescription` existe déjà.
- `RestaurantPOSApp.openLocal` : hors tests UI, `transport: RoutingPrinterTransport(lan: LANPrinterTransport(), star: StarPrinters.transport(), epson: EpsonPrinters.transport())` ; en test UI, `FakePrinterTransport()` inchangé. Rien dans l'interface (lot 4d) ; la recherche combinée `CombinedPrinterDiscovery([StarPrinters.discovery()] + [EpsonPrinters.discovery()].compactMap { $0 })` est construite au même endroit et rangée là où le lot 4d la prendra (un point d'injection existant de l'app ; n'en créer un que s'il n'en existe aucun, et le dire dans le rapport).
- [ ] **Step 1 : implémenter**, `cd ios && xcodegen generate`, build de l'app : `xcodebuild -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'generic/platform=iOS Simulator' -derivedDataPath build/DerivedData build` (sans SDK Epson ; et avec, s'il est installé).
- [ ] **Step 2 : suites** (`./scripts/test.sh unit`, `swift test` PosPrinters) **+ commit** `feat(ios): imprimantes Star et Epson branchées dans l'app (4c)`.

---

### Task 5: Guide d'essai papier, documentation, vérification finale

**Files:**
- Create: `docs/impression-essai-papier.md`
- Modify: `CLAUDE.md`, `docs/plans/ipad-standalone-4c-progress.md`

- [ ] **Step 1 : guide d'essai papier** (pour l'utilisateur, en français, sur son matériel) : préparation (Star : émulation ESC/POS activée par Star Quick Setup Utility, sinon le ticket sort illisible ; Epson : SDK installé par le script ; Bluetooth appairé dans Réglages iOS, ou BLE), configuration d'une imprimante (en attendant l'écran 4d : la manière la plus simple de poser `transport` et `deviceIdentifier` aux formats de la contrainte globale, à déterminer en lisant le code de l'écran Gestion et du dépôt), puis la liste à cocher par marque et par liaison : page de test, ticket de vente comptoir avec espèces (tiroir), ticket en arabe (raster, RTL), bon cuisine, rapport X, duplicata, imprimante éteinte pendant une vente puis rallumée (le ticket sort à la reprise, dans l'ordre). Pour chaque cas : résultat attendu et où lire l'erreur (`PrintJobs.LastError`).
- [ ] **Step 2 : `CLAUDE.md`** : paragraphe « Impression, lot 4c » : paquet `PosPrinters` (cœur testable sans SDK, adaptateurs iOS), StarXpand 2.14.1, SDK Epson local non commité et `install_epson_sdk.sh`, formats de `deviceIdentifier`, délais, émulation ESC/POS requise sur Star, clés `Info.plist` et MFi, commande de test du paquet, et la règle : le rendu reste celui de `TicketPayload` (aucune commande propre aux SDK).
- [ ] **Step 3 : vérifications finales** : `dotnet test RestaurantPos.slnx`, `swift test` (PosKit et PosPrinters), `./scripts/test.sh unit`, build simulateur de l'app. Noter les compteurs et l'état « SDK Epson installé ou non » dans le fichier de suivi.
- [ ] **Step 4 : commit** `docs: lot 4c fait (imprimantes Star et Epson)`, push.
