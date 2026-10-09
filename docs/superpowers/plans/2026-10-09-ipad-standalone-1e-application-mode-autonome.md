# iPad standalone — plan 1e : application SwiftUI en mode autonome (choix du mode, première configuration, tests UI) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** L'application iPad propose « Serveur » ou « Autonome » au premier lancement et, en mode autonome, s'exécute entièrement sur `LocalPosAPI` : première configuration (responsable, établissement), écran de PIN, parcours table complet, bandeau « non fiscal ». C'est la condition de sortie du sous-projet 1 de la spec.

**Architecture:** `AppEnvironment` détient le mode (mémorisé dans `UserDefaults`) et **l'unique instance** de `LocalPosAPI` du processus. En mode autonome, l'`AppModel` est construit sur cet acteur avec une identité de poste fixe `T01` (sans appairage). `RootView` aiguille : erreur d'ouverture, choix du mode, première configuration, appairage (serveur), PIN, coque principale. Les modes de test `-UITestLocal`, `-UITestLocalBlank` et `-UITestModeChoice` utilisent une base SQLite en mémoire.

**Tech Stack:** Swift 6, SwiftUI, XcodeGen, XCUITest, Swift Testing (package `PosKit`). Aucune dépendance nouvelle.

**Spec:** `docs/superpowers/specs/2026-10-06-ipad-standalone-design.md` (sous-projet 1 « Socle local », condition de sortie : « Parcours table complet en mode autonome (tests UI adaptés) »). Suite du plan 1d (`docs/superpowers/plans/2026-10-08-ipad-standalone-1d-imprimantes-happy-hour-durcissement.md`, mergé dans `main`) dont la section « Conditions d'entrée du plan 1e » (`docs/plans/ipad-standalone-1d-progress.md`) est reprise ici. **Ce plan se base sur `main`** (329 tests PosKit).

## Global Constraints

- `PosKit` reste **sans dépendance externe** ; Swift 6 avec `SWIFT_STRICT_CONCURRENCY: complete` ; aucun avertissement de compilation (package et application).
- `project.yml` est la source de vérité du projet Xcode (le `.xcodeproj` n'est pas versionné) : ne pas le modifier, relancer `xcodegen generate` après tout ajout de fichier.
- Les vues ne parlent jamais au réseau ni à la base : uniquement aux stores et à `AppEnvironment`.
- Toute chaîne affichée est une clé du catalogue `ios/RestaurantPOS/Resources/Localizable.xcstrings`, avec **les trois langues** `en`, `fr`, `ar` à l'état `translated` (le test `everyAppCatalogKeyIsTranslated` le vérifie) ; chiffres occidentaux forcés par l'application (ne pas formater de nombres à la main).
- Types et identifiants de vues sans collision avec SwiftUI/Foundation ; corps de vue court (découper les gros corps pour éviter les délais d'inférence).
- Identifiants d'accessibilité en minuscules pointées (`setup.name`, `mode.standalone`…), utilisés par les tests UI.
- Ne jamais construire avec `CODE_SIGNING_ALLOWED=NO` pour tester l'appairage ou la découverte.
- Messages d'erreur renvoyés par `LocalPosAPI` : laissés en français tels quels (ils viennent du modèle, comme ceux du serveur).
- Mode autonome = **non fiscal** (sous-projet 2) : toujours signalé par le bandeau ; la section Fiscal est masquée.

## Décisions de conception

1. **Un seul mode par installation, sans retour arrière dans l'application.** Le choix est mémorisé (`pos.appMode`). Basculer de mode suppose de réinstaller l'application : migrer des ventes d'un mode à l'autre n'a pas de sens sans le sous-projet de synchronisation.
2. **Mise à niveau transparente** : un iPad déjà appairé à un serveur avant ce plan n'a pas de choix mémorisé ; il reste en mode serveur sans rien demander (`AppMode.resolve`).
3. **Une seule instance de l'acteur par fichier** (condition du plan 1d) : `AppEnvironment` ouvre `LocalPosAPI` une fois, la réutilise lors des reconstructions de modèle et ne la recrée jamais.
4. **Production = `LocalPosAPI.openStandalone`** (`seed: .blank`, jamais de comptes de démonstration) ; le défaut `.demo` reste réservé aux tests (condition du plan 1d). Les modes de test `-UITestLocal` (démo) et `-UITestLocalBlank` (vierge) ouvrent une base en mémoire.
5. **Identité du poste** : `DeviceCredentials.standalone` (`T01`) dans un magasin en mémoire ; `TerminalSettings.isPaired` est vrai, donc ni écran d'appairage ni découverte Bonjour.
6. **Première configuration obligatoire** : tant que `needsSetup()` est vrai, rien d'autre que l'écran de configuration n'est accessible. Le PIN est saisi deux fois ; les autres validations (nom, SIRET à 14 chiffres, PIN à 4 chiffres) viennent de `completeFirstRun`.
7. **Section Fiscal masquée en mode autonome** (le fiscal répond 501 jusqu'au sous-projet 2) ; l'écran Réseau n'affiche plus de serveur ni de synchronisation.
8. **Hors de ce plan (sous-projet 2, fiscal)** : export et sauvegarde automatique de la base (le hash des PIN à 4 chiffres impose de chiffrer tout export avant Fichiers/iCloud), planification de la sauvegarde quotidienne, protection de la copie, remplacement des règlements provisoires. Consigné dans les conditions d'entrée du sous-projet 2 (tâche 4).

## Review Focus

Entrées ou pannes que la spec implique mais qu'aucun test nominal n'exerce ; chacune a son test dans la tâche indiquée.

1. **Un iPad déjà appairé avant la mise à jour ne doit pas se voir proposer le choix du mode** (ni perdre son appairage) → `pairedLegacyInstallStaysInServerMode` (tâche 1).
2. **La production n'amorce jamais de comptes de démonstration** (PIN 1234, 9999…) → `openStandaloneNeverSeedsDemoAccounts` (tâche 1).
3. **Une installation autonome vierge ne laisse aucun accès avant la première configuration** (aucun pavé PIN) → `testBlankInstallationRequiresFirstRunBeforeAnyPin` (tâche 4).
4. **Une confirmation de PIN différente ne crée pas de compte** → `testFirstRunRejectsMismatchedPins` (tâche 4).
5. **Le choix « Serveur » mène toujours à l'appairage** → `testChoosingServerModeLeadsToPairing` (tâche 4).

## File Structure

| Fichier | Responsabilité |
|---|---|
| `ios/Packages/PosKit/Sources/PosKit/Stores/AppMode.swift` | Modes `server`/`standalone`, clé de stockage, règle de reprise (`resolve`). |
| `ios/Packages/PosKit/Sources/PosKit/Models/DeviceModels.swift` | (modifié) `DeviceCredentials.standalone`. |
| `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Setup.swift` | (modifié) `needsSetup` aligné sur la garde 409, `openStandalone(path:)`. |
| `ios/RestaurantPOS/App/RestaurantPOSApp.swift` | (réécrit) configuration de lancement, `AppEnvironment` (mode, instance unique, configuration). |
| `ios/RestaurantPOS/App/StandaloneScreens.swift` | Choix du mode, erreur d'ouverture, bandeau « non fiscal ». |
| `ios/RestaurantPOS/App/FirstRunScreen.swift` | Première configuration (responsable, établissement). |
| `ios/RestaurantPOS/App/RootView.swift`, `MainShell.swift` | (modifiés) aiguillage, pied de l'écran PIN, bandeau, rail sans Fiscal. |
| `ios/RestaurantPOS/Features/Admin/AdminScreen.swift` | (modifié) écran Réseau sans serveur en mode autonome. |
| `ios/RestaurantPOS/Resources/Localizable.xcstrings` | (modifié) clés `mode.*`, `startup.*`, `setup.*`. |
| `ios/RestaurantPOSUITests/PosUITestCase.swift` | (modifié) `launchStandalone`. |
| `ios/RestaurantPOSUITests/StandaloneUITests.swift` | Tests UI du mode autonome. |
| `ios/Packages/PosKit/Tests/PosKitTests/AppModeTests.swift`, `Local/LocalStandaloneModeTests.swift` | Tests PosKit de la tâche 1. |
| `docs/plans/ipad-standalone-1e-progress.md` | Suivi d'exécution (exigé par `CLAUDE.md`). |

## Conventions pour toutes les tâches

- Tests PosKit : `cd ios/Packages/PosKit && swift test` (la suite complète fait foi). Base de référence (`main`) : **329 tests**. Totaux attendus : T1 335 · T2 335 · T3 335 · T4 335.
- Build de l'application (depuis `ios/`) :
  ```bash
  xcodegen generate --quiet
  xcodebuild build -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'platform=iOS Simulator,name=iPad Pro 13-inch (M5)' -derivedDataPath build/DerivedData -quiet 2>&1 | tail -20
  ```
  Attendu : aucune sortie d'erreur ni d'avertissement (`error:` / `warning:`).
- Tests UI d'une classe (depuis `ios/`, simulateur déjà démarré ou non) :
  ```bash
  xcodebuild test -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'platform=iOS Simulator,name=iPad Pro 13-inch (M5)' -derivedDataPath build/DerivedData -only-testing:RestaurantPOSUITests/<Classe> 2>&1 | grep -E "Test Case .*(passed|failed)|error:|\*\* TEST"
  ```
- Chaque tâche se termine par : suite PosKit verte, build de l'application sans avertissement, mise à jour de `docs/plans/ipad-standalone-1e-progress.md`, commit.
- Commits au style du dépôt : `feat(ios): …`, en français, avec le trailer de co-signature de la session.
- Ajout de clés de localisation : toujours par le script Python des tâches (il conserve le formatage du fichier : `json.dumps(indent=2, ensure_ascii=False)` suivi d'un saut de ligne, nouvelles clés en fin de liste).

---

### Task 1: Briques PosKit (mode, identité du poste, ouverture de production)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Stores/AppMode.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Models/DeviceModels.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Setup.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/AppModeTests.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalStandaloneModeTests.swift`
- Create: `docs/plans/ipad-standalone-1e-progress.md`

**Interfaces:**
- Consumes (plans 1a-1d) : `LocalPosAPI.init(path:seed:clock:calendar:)`, `LocalPosAPI.standaloneTerminalId`, `needsSetup()`, `completeFirstRun(_:)`, `PairResponse`, `DeviceCredentials`.
- Produces :
  - `public enum AppMode: String, Sendable { case server, standalone; static let storageKey; static func resolve(stored: AppMode?, hasPairedCredentials: Bool) -> AppMode? }`.
  - `DeviceCredentials.standalone` (terminal `T01`).
  - `LocalPosAPI.openStandalone(path: String) throws -> LocalPosAPI` (`seed: .blank`).
  - `LocalPosAPI.needsSetup()` vrai si et seulement si aucun compte n'existe.

- [ ] **Step 0: Relever la base de référence, créer la branche et le fichier de suivi**

Run (depuis la racine du dépôt) :
```bash
git branch --show-current
cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run"
```
Expected: `main` ; `Test run with 329 tests … passed`. Créer ensuite la branche de travail : `git switch -c feat/ipad-standalone-1e`.

Créer `docs/plans/ipad-standalone-1e-progress.md` :
```markdown
# iPad standalone 1e — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-09-ipad-standalone-1e-application-mode-autonome.md`

Base de référence (avant tâche 1) : PosKit 329 · .NET et web inchangés (aucun fichier .NET ni web modifié)

| Tâche | Statut | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|
| 1 Briques PosKit | à faire | | | |
| 2 Environnement, aiguillage, choix du mode | à faire | | | |
| 3 Première configuration | à faire | | | |
| 4 Tests UI et documentation | à faire | | | |
```

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/AppModeTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("AppMode : choix du mode de fonctionnement")
struct AppModeTests {
    @Test func aStoredChoiceAlwaysWins() {
        #expect(AppMode.resolve(stored: .standalone, hasPairedCredentials: true) == .standalone)
        #expect(AppMode.resolve(stored: .server, hasPairedCredentials: false) == .server)
    }

    @Test func pairedLegacyInstallStaysInServerMode() {
        // Un iPad appairé avant l'existence du mode autonome n'a aucun choix mémorisé : il reste en mode serveur, sans question.
        #expect(AppMode.resolve(stored: nil, hasPairedCredentials: true) == .server)
    }

    @Test func freshInstallAsksForAChoice() {
        #expect(AppMode.resolve(stored: nil, hasPairedCredentials: false) == nil)
        #expect(AppMode.server.rawValue == "server" && AppMode.standalone.rawValue == "standalone")
        #expect(AppMode.storageKey == "pos.appMode")
    }
}
```

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalStandaloneModeTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : installation autonome")
struct LocalStandaloneModeTests {
    private static let setup = LocalSetup(
        managerName: "Marie Curie", managerPin: "4321", companyName: "Chez Marie", address: "1 rue de la Paix",
        siret: "12345678901234", vatNumber: ""
    )

    @Test func standaloneCredentialsIdentifyTheFixedTerminal() {
        let credentials = DeviceCredentials.standalone
        #expect(credentials.terminalId == LocalPosAPI.standaloneTerminalId && credentials.terminalId == "T01")
        #expect(!credentials.token.isEmpty && !credentials.name.isEmpty)
    }

    @Test func openStandaloneNeverSeedsDemoAccounts() async throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        do {
            let first = try LocalPosAPI.openStandalone(path: path)
            #expect(try await first.needsSetup())
            for pin in ["1234", "2468", "5678", "9999"] { #expect(try await first.login(pin: pin).success == false) }
            #expect(try await first.staff().isEmpty)
            #expect(try await first.printers().isEmpty)
        }
        // Une réouverture n'amorce toujours rien.
        let reopened = try LocalPosAPI.openStandalone(path: path)
        #expect(try await reopened.needsSetup())
        #expect(try await reopened.staff().isEmpty)
    }

    @Test func needsSetupFollowsTheFirstRunGuard() async throws {
        let blank = try LocalPosAPI.openStandalone(path: ":memory:")
        #expect(try await blank.needsSetup())
        try await blank.completeFirstRun(Self.setup)
        #expect(try await blank.needsSetup() == false)
        await #expect(throws: APIError.server(status: 409, message: "L'installation est déjà configurée.")) { try await blank.completeFirstRun(Self.setup) }

        let demo = try LocalPosAPI(path: ":memory:")
        #expect(try await demo.needsSetup() == false)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter "AppModeTests|LocalStandaloneModeTests" 2>&1 | tail -5`
Expected: erreur de compilation `cannot find 'AppMode' in scope`.

- [ ] **Step 3: Implémenter**

Créer `ios/Packages/PosKit/Sources/PosKit/Stores/AppMode.swift` :
```swift
import Foundation

/// Mode de fonctionnement de l'iPad, choisi une fois au premier lancement.
public enum AppMode: String, Sendable {
    /// Relié à un serveur de caisse (appairage, temps réel).
    case server
    /// Tout vit sur l'iPad (`LocalPosAPI`), sans serveur.
    case standalone

    public static let storageKey = "pos.appMode"

    /// Mode à utiliser au lancement. Le choix mémorisé l'emporte ; sans choix, un iPad déjà appairé (installation antérieure au mode
    /// autonome) reste en mode serveur sans rien demander ; un iPad neuf renvoie `nil` : l'application propose le choix.
    public static func resolve(stored: AppMode?, hasPairedCredentials: Bool) -> AppMode? {
        stored ?? (hasPairedCredentials ? .server : nil)
    }
}
```

Dans `DeviceModels.swift`, remplacer :
```swift
        token: "device-token-demo", terminalId: "T01", name: "iPad démo", role: "Caisse", serverName: "Serveur démo"
    ))
```
par :
```swift
        token: "device-token-demo", terminalId: "T01", name: "iPad démo", role: "Caisse", serverName: "Serveur démo"
    ))

    /// Identité du poste en mode autonome : l'iPad est son propre terminal (`T01`), sans appairage ni serveur.
    public static let standalone = DeviceCredentials(PairResponse(
        deviceId: UUID(uuidString: "00000000-0000-0000-0000-0000000000A1")!,
        token: "standalone", terminalId: LocalPosAPI.standaloneTerminalId, name: "Cet iPad", role: "Autonome", serverName: "Mode autonome"
    ))
```

Dans `LocalPosAPI+Setup.swift`, remplacer :
```swift
    /// `true` tant qu'aucun responsable actif n'existe (base vierge) : l'application doit alors proposer la configuration initiale.
    public func needsSetup() async throws -> Bool {
        try staffRepository.members().allSatisfy { !($0.isActive && $0.role.isManager) }
    }
```
par :
```swift
    /// Ouvre le fichier d'une installation réelle : jamais de comptes ni de données de démonstration (`seed: .blank`).
    /// C'est le seul point d'entrée de la production ; le défaut `.demo` de l'initialiseur est réservé aux tests.
    public static func openStandalone(path: String) throws -> LocalPosAPI {
        try LocalPosAPI(path: path, seed: .blank)
    }

    /// `true` tant qu'aucun compte n'existe (base vierge) : l'application doit alors proposer la configuration initiale.
    /// Même critère que la garde « déjà configurée » (409) de `completeFirstRun`.
    public func needsSetup() async throws -> Bool {
        try staffRepository.members().isEmpty
    }
```

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter "AppModeTests|LocalStandaloneModeTests|LocalSetupTests" 2>&1 | grep -E "error:|✘|Test run"`
Expected: tous les tests passent (les tests existants `LocalSetupTests` restent verts).

- [ ] **Step 5: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"` → Expected: `335 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1e-progress.md` (tâche 1 `fait`, PosKit `335`).

```bash
git add ios/Packages/PosKit docs/plans/ipad-standalone-1e-progress.md
git commit -m "feat(ios-local): mode de fonctionnement, identité du poste autonome et ouverture de production"
```

---

### Task 2: Environnement, aiguillage et choix du mode

**Files:**
- Create: `ios/RestaurantPOS/App/RestaurantPOSApp.swift` (remplace le fichier existant)
- Create: `ios/RestaurantPOS/App/StandaloneScreens.swift`
- Modify: `ios/RestaurantPOS/App/RootView.swift`
- Modify: `ios/RestaurantPOS/App/MainShell.swift`
- Modify: `ios/RestaurantPOS/Features/Admin/AdminScreen.swift`
- Modify: `ios/RestaurantPOS/Resources/Localizable.xcstrings`

**Interfaces:**
- Consumes (tâche 1 et plans 1a-1d) : `AppMode`, `DeviceCredentials.standalone`, `LocalPosAPI` (`openStandalone`, `needsSetup`, `completeFirstRun`), `LocalDatabaseLocation.defaultURL()`, `LocalSetup`, `TerminalSettings`, `InMemoryCredentialStore`, `AppModel`, composants du design system (`ActionButton`, `Theme`).
- Produces :
  - `LaunchConfiguration.usesLocalBackend`, `.localIsBlank`, `.showsModeChoice` (`-UITestLocal`, `-UITestLocalBlank`, `-UITestModeChoice`).
  - `AppEnvironment` : `mode: AppMode?`, `isStandalone`, `setupState` (`.checking/.needed/.done`), `startupError`, `choose(_:)`, `retryStartup()`, `refreshSetup()`, `completeSetup(_:)`.
  - Vues `ModeChoiceScreen`, `StartupErrorScreen`, `StandaloneBanner`.
  - Clés `mode.*`, `startup.*`.

- [ ] **Step 1: Ajouter les clés de localisation**

Run (depuis `ios/`) :
```bash
python3 - <<'EOF'
import json
path = "RestaurantPOS/Resources/Localizable.xcstrings"
catalog = json.load(open(path, encoding="utf-8"))
new = {
    "mode.choice_title": ("How will this iPad be used?", "Comment cet iPad sera-t-il utilisé ?", "كيف سيُستخدم هذا الـ iPad؟"),
    "mode.choice_subtitle": ("This choice is final unless the app is reinstalled.", "Ce choix est définitif tant que l'application n'est pas réinstallée.", "هذا الاختيار نهائي ما لم يُعاد تثبيت التطبيق."),
    "mode.server_title": ("Connected to a server", "Relié à un serveur", "متصل بخادم"),
    "mode.server_detail": ("Pair this iPad with the restaurant's POS server.", "Appairez cet iPad au serveur de caisse du restaurant.", "اقرن هذا الـ iPad بخادم نقطة البيع الخاص بالمطعم."),
    "mode.standalone_title": ("Standalone iPad", "iPad autonome", "iPad مستقل"),
    "mode.standalone_detail": ("Everything stays on this iPad, no server needed.", "Tout reste sur cet iPad, sans serveur.", "كل شيء يبقى على هذا الـ iPad دون الحاجة إلى خادم."),
    "mode.standalone_status": ("Standalone mode", "Mode autonome", "الوضع المستقل"),
    "mode.non_fiscal_banner": ("Standalone mode, not fiscal: do not use for real sales yet.", "Mode autonome, non fiscal : ne pas encaisser en conditions réelles pour l'instant.", "الوضع المستقل، غير ضريبي: لا تستخدمه للبيع الفعلي بعد."),
    "startup.error_title": ("Cannot open the data", "Impossible d'ouvrir les données", "تعذّر فتح البيانات"),
    "startup.retry": ("Try again", "Réessayer", "إعادة المحاولة"),
}
for key, (en, fr, ar) in new.items():
    assert key not in catalog["strings"], key
    catalog["strings"][key] = {"extractionState": "manual", "localizations": {lang: {"stringUnit": {"state": "translated", "value": value}} for lang, value in (("ar", ar), ("en", en), ("fr", fr))}}
open(path, "w", encoding="utf-8").write(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n")
print(len(new), "clés ajoutées")
EOF
```
Expected: `10 clés ajoutées`.

- [ ] **Step 2: Réécrire la configuration de lancement et `AppEnvironment`**

Créer `ios/RestaurantPOS/App/RestaurantPOSApp.swift` (remplace le fichier existant) :
```swift
import SwiftUI
import PosKit

@main
struct RestaurantPOSApp: App {
    @State private var environment = AppEnvironment()
    @AppStorage(AppearancePreference.storageKey) private var appearance = AppearancePreference.dark.rawValue

    var body: some Scene {
        WindowGroup {
            RootView()
                .environment(environment)
                .environment(environment.model)
                .environment(environment.router)
                // I2(b) : chiffres occidentaux même sur un iPad réglé en région arabe (ex. ar_SA, ar_EG
                // dont le système de numérotation par défaut est arabo-indien). Ne force que le système
                // de numérotation : langue et région restent celles de l'appareil, donc la langue de
                // l'interface (résolue via `Bundle.main.preferredLocalizations`) n'est pas affectée.
                .environment(\.locale, Money.latinDigitsLocale(.current))
                .id(environment.generation)
                .tint(Theme.primary)
                .preferredColorScheme((AppearancePreference(rawValue: appearance) ?? .dark).colorScheme)
        }
    }
}

/// Configuration de lancement.
/// - `-UITestMode` : backend en mémoire, réglages éphémères, animations coupées (tests XCUITest).
/// - `-UITestHappyHour` : Happy Hour forcé dès le lancement (mode test).
/// - `POS_SERVER_URL` (variable d'environnement) : force l'adresse du serveur.
/// - `-UITestFailedPrintJob` : une impression en échec sur l'imprimante cuisine (mode test).
/// - `-UITestPin 1234` : déverrouillage automatique (mode test uniquement).
/// - `-UITestSection floor` : écran affiché après connexion (mode test uniquement).
/// - `-UITestPaired` : poste déjà appairé (sinon écran d'appairage, mode test uniquement).
/// - `-UITestLocal` : mode autonome sur une base SQLite en mémoire avec les données de démonstration (mode test uniquement).
/// - `-UITestLocalBlank` : mode autonome sur une base en mémoire vierge, donc première configuration (mode test uniquement).
/// - `-UITestModeChoice` : aucun mode mémorisé, l'écran « Serveur / Autonome » s'affiche (mode test uniquement).
struct LaunchConfiguration {
    let isUITest: Bool
    let forceHappyHour: Bool
    let seedsFailedPrintJob: Bool
    let serverOverride: String?
    let autoPin: String?
    let initialSection: Router.Section?
    let startsPaired: Bool
    let usesLocalBackend: Bool
    let localIsBlank: Bool
    let showsModeChoice: Bool

    static let current: LaunchConfiguration = {
        let args = ProcessInfo.processInfo.arguments
        let defaults = UserDefaults.standard
        let isUITest = args.contains("-UITestMode")
        return LaunchConfiguration(
            isUITest: isUITest,
            forceHappyHour: args.contains("-UITestHappyHour"),
            seedsFailedPrintJob: args.contains("-UITestFailedPrintJob"),
            serverOverride: ProcessInfo.processInfo.environment["POS_SERVER_URL"],
            autoPin: isUITest ? defaults.string(forKey: "UITestPin") : nil,
            initialSection: isUITest ? defaults.string(forKey: "UITestSection").flatMap(Router.Section.init(rawValue:)) : nil,
            startsPaired: isUITest && args.contains("-UITestPaired"),
            usesLocalBackend: isUITest && (args.contains("-UITestLocal") || args.contains("-UITestLocalBlank")),
            localIsBlank: isUITest && args.contains("-UITestLocalBlank"),
            showsModeChoice: isUITest && args.contains("-UITestModeChoice")
        )
    }()
}

/// Construit l'`AppModel` selon le mode de lancement et le reconstruit si le serveur change.
@MainActor @Observable
final class AppEnvironment {
    /// Première configuration d'une installation autonome.
    enum SetupState { case checking, needed, done }

    private(set) var model: AppModel
    let router = Router()
    private(set) var settings: TerminalSettings
    private(set) var generation = 0
    let launch = LaunchConfiguration.current
    /// `nil` tant que l'utilisateur n'a pas choisi (premier lancement, aucun poste appairé).
    private(set) var mode: AppMode?
    private(set) var setupState = SetupState.done
    /// Ouverture de la base locale impossible : l'application affiche l'erreur au lieu de démarrer.
    private(set) var startupError: String?
    @ObservationIgnored private let defaults: UserDefaults
    /// **Une seule instance** de l'acteur pour tout le processus : la recréer ouvrirait une seconde connexion sur le même fichier.
    @ObservationIgnored private var localAPI: LocalPosAPI?

    var isStandalone: Bool { mode == .standalone }

    init() {
        let launch = LaunchConfiguration.current
        let defaults: UserDefaults
        let serverSettings: TerminalSettings
        if launch.isUITest {
            UIView.setAnimationsEnabled(false)
            defaults = UserDefaults(suiteName: "RestaurantPOS.UITests")!
            defaults.removePersistentDomain(forName: "RestaurantPOS.UITests")
            serverSettings = TerminalSettings(defaults: defaults, credentialStore: InMemoryCredentialStore(launch.startsPaired ? .demo : nil))
        } else {
            defaults = .standard
            serverSettings = TerminalSettings(credentialStore: KeychainCredentialStore())
        }
        if let override = launch.serverOverride { serverSettings.serverURL = override }

        let stored = defaults.string(forKey: AppMode.storageKey).flatMap(AppMode.init(rawValue:))
        let mode: AppMode?
        if launch.usesLocalBackend {
            mode = .standalone
        } else if launch.isUITest {
            mode = launch.showsModeChoice ? nil : .server
        } else {
            mode = AppMode.resolve(stored: stored, hasPairedCredentials: serverSettings.isPaired)
        }
        let settings = mode == .standalone ? Self.standaloneSettings(defaults: defaults) : serverSettings
        var api: LocalPosAPI?
        var startupError: String?
        if mode == .standalone {
            do { api = try Self.openLocal(launch: launch) } catch { startupError = Self.describe(error) }
        }

        self.defaults = defaults
        self.mode = mode
        self.settings = settings
        self.localAPI = api
        self.startupError = startupError
        self.setupState = mode == .standalone && startupError == nil ? .checking : .done
        model = Self.makeModel(settings: settings, launch: launch, localAPI: api)
        if let section = launch.initialSection { router.section = section }
        if let pin = launch.autoPin {
            let session = model.session
            Task { for digit in pin { await session.appendDigit(String(digit)) } }
        }
    }

    private static func standaloneSettings(defaults: UserDefaults) -> TerminalSettings {
        TerminalSettings(defaults: defaults, credentialStore: InMemoryCredentialStore(.standalone))
    }

    /// Production : fichier protégé, jamais de données de démonstration. Tests : base en mémoire (démonstration ou vierge).
    private static func openLocal(launch: LaunchConfiguration) throws -> LocalPosAPI {
        if launch.isUITest {
            return try LocalPosAPI(path: ":memory:", seed: launch.usesLocalBackend && !launch.localIsBlank ? .demo : .blank)
        }
        return try LocalPosAPI.openStandalone(path: LocalDatabaseLocation.defaultURL().path)
    }

    private static func describe(_ error: Error) -> String {
        (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
    }

    private static func makeModel(settings: TerminalSettings, launch: LaunchConfiguration, localAPI: LocalPosAPI?) -> AppModel {
        if let localAPI { return AppModel(api: localAPI, settings: settings) }
        if launch.isUITest {
            let api = InMemoryPosAPI()
            if launch.forceHappyHour { Task { await api.forceHappyHour(minutes: 45) } }
            if launch.seedsFailedPrintJob {
                Task { if let kitchen = try? await api.printers().first(where: { $0.name.contains("Cuisine") }) { await api.seedFailedPrintJob(printerId: kitchen.id) } }
            }
            return AppModel(api: api, settings: settings)
        }
        let url = settings.url ?? URL(string: "http://localhost:5080")!
        return AppModel(api: HTTPPosAPI(baseURL: url, deviceToken: settings.credentials?.token), settings: settings, realtimeBaseURL: url)
    }

    /// Mémorise le choix du mode et reconstruit l'état. Le mode autonome ouvre la base (une seule fois) ; une erreur d'ouverture est
    /// affichée par `StartupErrorScreen`.
    func choose(_ newMode: AppMode) {
        defaults.set(newMode.rawValue, forKey: AppMode.storageKey)
        model.stopRealtime()
        mode = newMode
        startupError = nil
        if newMode == .standalone {
            settings = Self.standaloneSettings(defaults: defaults)
            if localAPI == nil {
                do { localAPI = try Self.openLocal(launch: launch) } catch { startupError = Self.describe(error) }
            }
            setupState = startupError == nil ? .checking : .done
        } else {
            setupState = .done
        }
        model = Self.makeModel(settings: settings, launch: launch, localAPI: newMode == .standalone ? localAPI : nil)
        router.section = .order
        generation += 1
    }

    /// Nouvel essai après une erreur d'ouverture de la base.
    func retryStartup() { choose(mode ?? .standalone) }

    /// Mode autonome : la base est-elle vierge ? (première configuration à faire)
    func refreshSetup() async {
        guard isStandalone, let localAPI else { return }
        do { setupState = try await localAPI.needsSetup() ? .needed : .done } catch { startupError = Self.describe(error) }
    }

    /// Crée le premier responsable et l'établissement ; lève l'erreur de validation du modèle pour l'afficher dans le formulaire.
    func completeSetup(_ setup: LocalSetup) async throws {
        guard let localAPI else { return }
        try await localAPI.completeFirstRun(setup)
        setupState = .done
    }

    /// Applique une nouvelle adresse serveur : nouvelle session, nouvel état (mode serveur).
    func reconnect() {
        model.stopRealtime()
        model = Self.makeModel(settings: settings, launch: launch, localAPI: nil)
        router.section = .order
        generation += 1
    }

    /// Appairage réussi. Même serveur (ré-appairage après révocation) : on garde le modèle, donc le brouillon,
    /// et on change seulement le jeton. Nouveau serveur : nouvelle session.
    @discardableResult
    func completePairing(serverURL: URL, response: PairResponse) async -> Bool {
        let serverChanged = serverURL.absoluteString != settings.serverURL
        guard await model.applyPairing(serverURL: serverURL.absoluteString, credentials: DeviceCredentials(response)) else { return false }
        if serverChanged { reconnect() }
        return true
    }

    /// Le serveur a changé d'adresse (DHCP) : on le retrouve par son nom Bonjour. Sans objet en mode autonome.
    func rediscoverServerIfUnreachable() async {
        guard !launch.isUITest, !isStandalone, let name = settings.credentials?.serverName else { return }
        if (try? await model.api.health()) != nil { return }
        guard let url = await ServerBrowser.resolve(name: name), url.absoluteString != settings.serverURL else { return }
        settings.serverURL = url.absoluteString
        reconnect()
    }
}

@MainActor @Observable
final class Router {
    enum Section: String, CaseIterable, Identifiable {
        case order, floor, kitchen, fiscal, admin
        var id: String { rawValue }

        var title: String {
            switch self {
            case .order: String(localized: "common.nav_order")
            case .floor: String(localized: "common.nav_floor")
            case .kitchen: String(localized: "common.nav_kitchen")
            case .fiscal: String(localized: "common.nav_fiscal")
            case .admin: String(localized: "common.nav_admin")
            }
        }

        var systemImage: String {
            switch self {
            case .order: "cart"
            case .floor: "square.grid.3x3.topleft.filled"
            case .kitchen: "flame"
            case .fiscal: "doc.text.magnifyingglass"
            case .admin: "gearshape.2"
            }
        }

        var requiresManager: Bool { self == .fiscal || self == .admin }
    }

    var section: Section = .order
}
```

- [ ] **Step 3: Écrans du mode autonome**

Créer `ios/RestaurantPOS/App/StandaloneScreens.swift` :
```swift
import SwiftUI
import PosKit

/// Premier lancement : « Serveur » ou « Autonome ».
struct ModeChoiceScreen: View {
    @Environment(AppEnvironment.self) private var environment

    var body: some View {
        ZStack {
            Theme.canvas.ignoresSafeArea()
            VStack(spacing: Theme.Space.xxl) {
                Image("BrandMark")
                    .resizable()
                    .scaledToFill()
                    .frame(width: 88, height: 88)
                    .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.lg, style: .continuous))
                    .accessibilityHidden(true)
                VStack(spacing: Theme.Space.s) {
                    Text("mode.choice_title").font(.posTitle).foregroundStyle(Theme.ink)
                    Text("mode.choice_subtitle").font(.posBody).foregroundStyle(Theme.inkMuted)
                }
                HStack(spacing: Theme.Space.l) {
                    ModeCard(title: "mode.server_title", detail: "mode.server_detail", systemImage: "server.rack", identifier: "mode.server") {
                        environment.choose(.server)
                    }
                    ModeCard(title: "mode.standalone_title", detail: "mode.standalone_detail", systemImage: "ipad", identifier: "mode.standalone") {
                        environment.choose(.standalone)
                    }
                }
            }
            .padding(40)
            .frame(maxWidth: 820)
        }
    }
}

private struct ModeCard: View {
    let title: LocalizedStringKey
    let detail: LocalizedStringKey
    let systemImage: String
    let identifier: String
    let action: () -> Void

    var body: some View {
        Button(action: { Haptics.tap(); action() }) {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                Image(systemName: systemImage).font(.system(size: 32, weight: .semibold)).foregroundStyle(Theme.primaryInk)
                Text(title).font(.posHeadline).foregroundStyle(Theme.ink)
                Text(detail).font(.posBody).foregroundStyle(Theme.inkMuted).multilineTextAlignment(.leading)
            }
            .padding(Theme.Space.xl)
            .frame(maxWidth: .infinity, minHeight: 190, alignment: .topLeading)
            .background(RoundedRectangle(cornerRadius: Theme.Radius.lg, style: .continuous).fill(Theme.surface))
            .contentShape(RoundedRectangle(cornerRadius: Theme.Radius.lg, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier(identifier)
    }
}

/// La base locale n'a pas pu être ouverte : on ne démarre pas (jamais de base vide de remplacement).
struct StartupErrorScreen: View {
    @Environment(AppEnvironment.self) private var environment
    let message: String

    var body: some View {
        VStack(spacing: Theme.Space.l) {
            Image(systemName: "exclamationmark.triangle.fill").font(.system(size: 44)).foregroundStyle(Theme.danger)
            Text("startup.error_title").font(.posTitle).foregroundStyle(Theme.ink)
            Text(message).font(.posBody).foregroundStyle(Theme.inkMuted).multilineTextAlignment(.center)
                .accessibilityIdentifier("startup.message")
            ActionButton(title: String(localized: "startup.retry"), systemImage: "arrow.clockwise", kind: .primary) {
                environment.retryStartup()
            }
            .frame(maxWidth: 320)
            .accessibilityIdentifier("startup.retry")
        }
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Theme.canvas.ignoresSafeArea())
    }
}

/// Rappel permanent : le mode autonome n'est pas encore fiscal (sous-projet 2).
struct StandaloneBanner: View {
    var body: some View {
        HStack(spacing: Theme.Space.s) {
            Image(systemName: "exclamationmark.triangle.fill")
            Text("mode.non_fiscal_banner").font(.system(size: 13, weight: .semibold))
            Spacer()
        }
        .foregroundStyle(Theme.warning)
        .padding(.horizontal, Theme.Space.xl)
        .padding(.vertical, Theme.Space.s)
        .background(Theme.warning.opacity(0.14))
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("standalone.banner")
    }
}
```

- [ ] **Step 4: Aiguillage et pied de l'écran PIN**

Dans `RootView.swift`, remplacer :
```swift
struct RootView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        ZStack(alignment: .top) {
            if !model.settings.isPaired {
```
par :
```swift
struct RootView: View {
    @Environment(AppModel.self) private var model
    @Environment(AppEnvironment.self) private var environment

    var body: some View {
        ZStack(alignment: .top) {
            if let error = environment.startupError {
                StartupErrorScreen(message: error)
            } else if environment.mode == nil {
                ModeChoiceScreen()
                    .transition(.opacity)
            } else if environment.isStandalone && environment.setupState == .checking {
                ProgressView().controlSize(.large)
            } else if environment.isStandalone && environment.setupState == .needed {
                FirstRunScreen()
                    .transition(.opacity)
            } else if !model.settings.isPaired {
```

Dans `RootView.swift`, remplacer :
```swift
        .animation(.easeInOut(duration: 0.25), value: model.settings.isPaired)
```
par :
```swift
        .animation(.easeInOut(duration: 0.25), value: model.settings.isPaired)
        .task(id: environment.generation) { await environment.refreshSetup() }
```

Dans `RootView.swift`, remplacer :
```swift
                HStack {
                    Label(environment.launch.isUITest ? String(localized: "login.demo_mode_label") : model.settings.serverURL, systemImage: "server.rack")
                        .font(.footnote)
                        .foregroundStyle(Theme.inkMuted)
                    Button("login.change_server") { showsServerSettings = true }
                        .font(.footnote.weight(.semibold))
                        .accessibilityIdentifier("lock.server")
                }
```
par :
```swift
                HStack {
                    if environment.isStandalone {
                        Label("mode.standalone_status", systemImage: "ipad")
                            .font(.footnote)
                            .foregroundStyle(Theme.inkMuted)
                            .accessibilityIdentifier("lock.standalone")
                    } else {
                        Label(environment.launch.isUITest ? String(localized: "login.demo_mode_label") : model.settings.serverURL, systemImage: "server.rack")
                            .font(.footnote)
                            .foregroundStyle(Theme.inkMuted)
                        Button("login.change_server") { showsServerSettings = true }
                            .font(.footnote.weight(.semibold))
                            .accessibilityIdentifier("lock.server")
                    }
                }
```

- [ ] **Step 5: Bandeau et rail sans Fiscal**

Dans `MainShell.swift`, remplacer :
```swift
struct MainShell: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router
```
par :
```swift
struct MainShell: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router
    @Environment(AppEnvironment.self) private var environment
```

Dans `MainShell.swift`, remplacer :
```swift
                HeaderBar()
                if model.happyHour.isActive {
```
par :
```swift
                HeaderBar()
                if environment.isStandalone { StandaloneBanner() }
                if model.happyHour.isActive {
```

Dans `MainShell.swift`, remplacer :
```swift
struct SidebarRail: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router
```
par :
```swift
struct SidebarRail: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router
    @Environment(AppEnvironment.self) private var environment

    /// Le fiscal n'existe pas encore en mode autonome (sous-projet 2) : sa section est masquée plutôt que de répondre « non disponible ».
    private func isVisible(_ section: Router.Section) -> Bool {
        if environment.isStandalone && section == .fiscal { return false }
        return !section.requiresManager || model.session.isManager
    }
```

Dans `MainShell.swift`, remplacer :
```swift
                if !section.requiresManager || model.session.isManager {
```
par :
```swift
                if isVisible(section) {
```

- [ ] **Step 6: Écran Réseau sans serveur en mode autonome**

Dans `AdminScreen.swift`, remplacer :
```swift
            Section("common.master_server_section") {
                LabeledContent("admin.address_label", value: environment.launch.isUITest
```
par :
```swift
            if environment.isStandalone {
                Section("mode.standalone_status") {
                    Text("mode.standalone_detail").font(.footnote).foregroundStyle(Theme.inkMuted)
                }
            } else {
            Section("common.master_server_section") {
                LabeledContent("admin.address_label", value: environment.launch.isUITest
```

Dans `AdminScreen.swift`, remplacer :
```swift
                    .accessibilityIdentifier("network.forceSync")
            }
```
par :
```swift
                    .accessibilityIdentifier("network.forceSync")
            }
            }
```

- [ ] **Step 7: Version provisoire de l'écran de première configuration, puis vérifier**

`RootView` référence `FirstRunScreen`, que la tâche 3 écrit pour de bon.

Créer `ios/RestaurantPOS/App/FirstRunScreen.swift` (version provisoire, remplacée par la tâche 3) :
```swift
import SwiftUI

struct FirstRunScreen: View {
    var body: some View { EmptyView() }
}
```

Run (depuis `ios/`) la commande de build de l'application (voir « Conventions »).
Expected: aucune sortie `error:` ni `warning:`.

Run : `cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"` → Expected: `335 tests … passed` (le test `everyAppCatalogKeyIsTranslated` valide les 9 nouvelles clés).

Run (non-régression des parcours serveur, depuis `ios/`) : tests UI de `SessionUITests` et `PairingUITests` (voir « Conventions »). Expected: `** TEST SUCCEEDED **`.

- [ ] **Step 8: Suivi, commit**

Mettre à jour `docs/plans/ipad-standalone-1e-progress.md` (tâche 2 `fait`, PosKit `335`).

```bash
git add ios/RestaurantPOS ios/Packages/PosKit docs/plans/ipad-standalone-1e-progress.md
git commit -m "feat(ios): choix Serveur/Autonome, instance unique de la base locale et aiguillage de l'application"
```

---

### Task 3: Première configuration

**Files:**
- Create: `ios/RestaurantPOS/App/FirstRunScreen.swift` (remplace la version provisoire éventuelle)
- Modify: `ios/RestaurantPOS/Resources/Localizable.xcstrings`

**Interfaces:**
- Consumes (tâche 2) : `AppEnvironment.completeSetup(_:)`, `LocalSetup`, `SessionStore.pinLength`, `Theme`.
- Produces : `FirstRunScreen` ; identifiants `setup.name`, `setup.pin`, `setup.pinConfirm`, `setup.company`, `setup.address`, `setup.siret`, `setup.vat`, `setup.submit`, `setup.error` ; clés `setup.*`.

- [ ] **Step 1: Ajouter les clés de localisation**

Run (depuis `ios/`) :
```bash
python3 - <<'EOF'
import json
path = "RestaurantPOS/Resources/Localizable.xcstrings"
catalog = json.load(open(path, encoding="utf-8"))
new = {
    "setup.title": ("Set up this iPad", "Configurer cet iPad", "إعداد هذا الـ iPad"),
    "setup.subtitle": ("Create the first manager account and describe your restaurant.", "Créez le premier compte responsable et décrivez votre établissement.", "أنشئ حساب المسؤول الأول وصف مؤسستك."),
    "setup.section_manager": ("Manager", "Responsable", "المسؤول"),
    "setup.section_restaurant": ("Restaurant", "Établissement", "المؤسسة"),
    "setup.field_name": ("Manager name", "Nom du responsable", "اسم المسؤول"),
    "setup.field_pin": ("4-digit PIN", "Code PIN à 4 chiffres", "رمز PIN من 4 أرقام"),
    "setup.field_pin_confirm": ("Confirm PIN", "Confirmer le code PIN", "تأكيد رمز PIN"),
    "setup.field_company": ("Restaurant name", "Nom de l'établissement", "اسم المؤسسة"),
    "setup.field_address": ("Address", "Adresse", "العنوان"),
    "setup.field_siret": ("SIRET (14 digits)", "SIRET (14 chiffres)", "SIRET (14 رقمًا)"),
    "setup.field_vat": ("VAT number (optional)", "N° de TVA (facultatif)", "رقم ضريبة القيمة المضافة (اختياري)"),
    "setup.submit": ("Create and continue", "Créer et continuer", "إنشاء ومتابعة"),
    "setup.pin_mismatch": ("The two PINs do not match.", "Les deux codes PIN ne correspondent pas.", "رمزا PIN غير متطابقين."),
}
for key, (en, fr, ar) in new.items():
    assert key not in catalog["strings"], key
    catalog["strings"][key] = {"extractionState": "manual", "localizations": {lang: {"stringUnit": {"state": "translated", "value": value}} for lang, value in (("ar", ar), ("en", en), ("fr", fr))}}
open(path, "w", encoding="utf-8").write(json.dumps(catalog, indent=2, ensure_ascii=False) + "\n")
print(len(new), "clés ajoutées")
EOF
```
Expected: `13 clés ajoutées`.

- [ ] **Step 2: Écrire l'écran**

Créer `ios/RestaurantPOS/App/FirstRunScreen.swift` :
```swift
import SwiftUI
import PosKit

/// Première configuration d'une installation autonome : premier responsable et identité de l'établissement.
/// Aucun autre écran n'est accessible tant que cette configuration n'est pas faite.
struct FirstRunScreen: View {
    @Environment(AppEnvironment.self) private var environment
    @State private var managerName = ""
    @State private var pin = ""
    @State private var pinConfirm = ""
    @State private var company = ""
    @State private var address = ""
    @State private var siret = ""
    @State private var vat = ""
    @State private var error: String?
    @State private var isSaving = false

    private var canSubmit: Bool {
        !isSaving && !isBlank(managerName) && pin.count == SessionStore.pinLength && !isBlank(company) && siret.count == 14
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("setup.subtitle").font(.footnote).foregroundStyle(.secondary)
                }
                managerSection
                restaurantSection
                submitSection
            }
            .scrollDismissesKeyboard(.interactively)
            .navigationTitle("setup.title")
        }
    }

    private var managerSection: some View {
        Section("setup.section_manager") {
            TextField("setup.field_name", text: $managerName)
                .textContentType(.name)
                .accessibilityIdentifier("setup.name")
            SecureField("setup.field_pin", text: $pin)
                .keyboardType(.numberPad)
                .onChange(of: pin) { _, value in pin = Self.digits(value, limit: SessionStore.pinLength) }
                .accessibilityIdentifier("setup.pin")
            SecureField("setup.field_pin_confirm", text: $pinConfirm)
                .keyboardType(.numberPad)
                .onChange(of: pinConfirm) { _, value in pinConfirm = Self.digits(value, limit: SessionStore.pinLength) }
                .accessibilityIdentifier("setup.pinConfirm")
        }
    }

    private var restaurantSection: some View {
        Section("setup.section_restaurant") {
            TextField("setup.field_company", text: $company)
                .accessibilityIdentifier("setup.company")
            TextField("setup.field_address", text: $address, axis: .vertical)
                .lineLimit(1...3)
                .accessibilityIdentifier("setup.address")
            TextField("setup.field_siret", text: $siret)
                .keyboardType(.numberPad)
                .onChange(of: siret) { _, value in siret = Self.digits(value, limit: 14) }
                .accessibilityIdentifier("setup.siret")
            TextField("setup.field_vat", text: $vat)
                .textInputAutocapitalization(.characters)
                .autocorrectionDisabled()
                .accessibilityIdentifier("setup.vat")
        }
    }

    private var submitSection: some View {
        Section {
            if let error {
                Text(error).foregroundStyle(Theme.danger).accessibilityIdentifier("setup.error")
            }
            Button {
                Task { await submit() }
            } label: {
                HStack {
                    Text("setup.submit")
                    if isSaving { Spacer(); ProgressView() }
                }
            }
            .disabled(!canSubmit)
            .accessibilityIdentifier("setup.submit")
        }
    }

    private func isBlank(_ text: String) -> Bool { text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    private static func digits(_ text: String, limit: Int) -> String { String(text.filter { $0.isASCII && $0.isNumber }.prefix(limit)) }

    private func submit() async {
        guard pin == pinConfirm else {
            error = String(localized: "setup.pin_mismatch")
            return
        }
        isSaving = true
        defer { isSaving = false }
        error = nil
        do {
            try await environment.completeSetup(LocalSetup(
                managerName: managerName, managerPin: pin, companyName: company, address: address, siret: siret, vatNumber: vat
            ))
        } catch {
            self.error = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
        }
    }
}
```

- [ ] **Step 3: Vérifier**

Run (depuis `ios/`) la commande de build de l'application. Expected: aucune sortie `error:` ni `warning:`.
Run : `cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"` → Expected: `335 tests … passed` (les 13 clés sont validées dans les trois langues).

- [ ] **Step 4: Suivi, commit**

Mettre à jour `docs/plans/ipad-standalone-1e-progress.md` (tâche 3 `fait`, PosKit `335`).

```bash
git add ios/RestaurantPOS docs/plans/ipad-standalone-1e-progress.md
git commit -m "feat(ios): écran de première configuration du mode autonome"
```

---

### Task 4: Tests UI, documentation et conditions d'entrée du sous-projet 2

**Files:**
- Modify: `ios/RestaurantPOSUITests/PosUITestCase.swift`
- Create: `ios/RestaurantPOSUITests/StandaloneUITests.swift`
- Modify: `ios/README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/plans/ipad-standalone-1e-progress.md`

**Interfaces:**
- Consumes (tâches 2-3) : modes de test `-UITestLocal`, `-UITestLocalBlank`, `-UITestModeChoice` ; identifiants `mode.server`, `mode.standalone`, `setup.*`, `standalone.banner`, `lock.standalone`, `pin.*`, `nav.*`, `header.operator`, `pairing.code` ; helpers `PosUITestCase` (`tap`, `assertExists`, `assertNotExists`, `element`, `label`, `waitLabel`, `waitToast`, `typePin`, `addProduct`).
- Produces : `PosUITestCase.launchStandalone(blank:modeChoice:pin:language:)` ; classe `StandaloneUITests`.

- [ ] **Step 1: Ajouter le lanceur de test du mode autonome**

Dans `PosUITestCase.swift`, remplacer :
```swift
    // MARK: Raccourcis
```
par :
```swift
    /// Lance l'app en mode autonome sur une base SQLite en mémoire.
    /// - Parameters:
    ///   - blank: base vierge (première configuration) au lieu des données de démonstration.
    ///   - modeChoice: aucun mode mémorisé, l'écran « Serveur / Autonome » s'affiche.
    ///   - pin: déverrouillage automatique (données de démonstration uniquement ; nil = rester sur l'écran PIN).
    @discardableResult
    func launchStandalone(blank: Bool = false, modeChoice: Bool = false, pin: String? = "1234", language: String = "fr") -> XCUIApplication {
        app = XCUIApplication()
        app.launchArguments = ["-UITestMode"]
        if modeChoice {
            app.launchArguments.append("-UITestModeChoice")
        } else {
            app.launchArguments.append(blank ? "-UITestLocalBlank" : "-UITestLocal")
        }
        let unlocks = pin != nil && !blank && !modeChoice
        if let pin, unlocks { app.launchArguments += ["-UITestPin", pin] }
        app.launchArguments += ["-AppleLanguages", "(\(language))", "-AppleLocale", language == "ar" ? "ar" : "\(language)_FR"]
        app.launch()
        if unlocks {
            XCTAssertTrue(app.buttons["nav.order"].waitForExistence(timeout: 10), "La coque principale doit s'afficher après connexion")
        }
        return app
    }

    // MARK: Raccourcis
```

- [ ] **Step 2: Écrire les tests UI**

Créer `ios/RestaurantPOSUITests/StandaloneUITests.swift` :
```swift
import XCTest

/// Mode autonome : tout vit sur l'iPad (`LocalPosAPI` sur SQLite en mémoire pendant les tests).
final class StandaloneUITests: PosUITestCase {

    /// Saisit un champ du formulaire de première configuration.
    private func fill(_ id: String, _ text: String) {
        let field = element(id)
        XCTAssertTrue(field.waitForExistence(timeout: 10), "« \(id) » introuvable")
        field.tap()
        field.typeText(text)
    }

    private func fillFirstRun(pin: String = "4321", confirmation: String = "4321") {
        fill("setup.name", "Marie Curie")
        fill("setup.pin", pin)
        fill("setup.pinConfirm", confirmation)
        fill("setup.company", "Chez Marie")
        fill("setup.siret", "12345678901234")
    }

    private func openTable(_ number: String, covers: Int = 2) {
        tap("nav.floor")
        tap("table.\(number)")
        tap("covers.\(covers)")
        tap("covers.confirm")
        waitLabel("ticket.title", contains: "Table \(number)")
    }

    func testModeChoiceThenFirstRunThenUnlock() {
        launchStandalone(modeChoice: true, pin: nil)
        assertExists("mode.server", timeout: 10)
        tap("mode.standalone")
        assertExists("setup.name", timeout: 10)
        assertNotExists("pin.1")

        fillFirstRun()
        tap("setup.submit")
        assertExists("pin.1", timeout: 10)
        assertExists("lock.standalone")
        typePin("4321")
        assertExists("nav.order", timeout: 10)
        assertExists("standalone.banner")
        XCTAssertTrue(label(of: "header.operator").contains("Marie Curie"))
    }

    func testChoosingServerModeLeadsToPairing() {
        launchStandalone(modeChoice: true, pin: nil)
        tap("mode.server", timeout: 10)
        assertExists("pairing.code", timeout: 10)
        assertNotExists("setup.name")
    }

    func testBlankInstallationRequiresFirstRunBeforeAnyPin() {
        launchStandalone(blank: true, pin: nil)
        assertExists("setup.name", timeout: 10)
        assertNotExists("pin.1")
        assertNotExists("nav.order")
    }

    func testFirstRunRejectsMismatchedPins() {
        launchStandalone(blank: true, pin: nil)
        fillFirstRun(pin: "4321", confirmation: "1234")
        tap("setup.submit")
        waitLabel("setup.error", contains: "ne correspondent pas")
        assertNotExists("pin.1")
    }

    func testFullTableJourneyWorksOnTheLocalBackend() {
        launchStandalone()
        assertExists("standalone.banner")
        openTable("T1", covers: 3)
        addProduct("Pizza Margherita AOP")
        addProduct("Pizza Margherita AOP")
        addProduct("Tiramisu Maison")
        waitLabel("line.qty.Pizza Margherita AOP", contains: "2")
        waitLabel("ticket.total", contains: "32,50")

        tap("ticket.send")
        waitToast(containing: "envoyée en cuisine")
        assertExists("table.T1")
        XCTAssertTrue(label(of: "table.T1").contains("32,50"))

        tap("table.T1")
        waitLabel("ticket.total", contains: "32,50")
        tap("ticket.pay")
        tap("payment.method.cash")
        tap("payment.cash.50")
        waitLabel("payment.change", contains: "17,50")
        tap("payment.validate")
        waitLabel("result.change", contains: "17,50")
        tap("result.done")
        assertExists("table.T1")
        XCTAssertTrue(label(of: "table.T1").contains("Libre"))
    }

    func testFiscalSectionIsHiddenAndWaiterKeepsHisRights() {
        launchStandalone()
        assertExists("nav.admin")
        assertNotExists("nav.fiscal")
        app.terminate()

        launchStandalone(pin: "2468")
        assertExists("nav.floor")
        assertNotExists("nav.fiscal")
        assertNotExists("nav.admin")
        assertExists("standalone.banner")
    }
}
```

- [ ] **Step 3: Exécuter les tests UI du mode autonome**

Run (depuis `ios/`, voir « Conventions ») : `-only-testing:RestaurantPOSUITests/StandaloneUITests`.
Expected: `** TEST SUCCEEDED **`, 6 tests passés. Si un test échoue parce qu'un champ est masqué par le clavier (`tap` impossible), faire défiler d'abord (`app.swipeUp()`) ou fermer le clavier avant de toucher le bouton, sans modifier le code de production sauf défaut réel ; consigner dans le rapport ce qui a été nécessaire.

- [ ] **Step 4: Non-régression complète**

Run (depuis `ios/`) : `./scripts/test.sh all`
Expected: tests PosKit (335) et tests UI tous verts (comptage du rapport `build/test-results/ui.xcresult`). Noter le nombre de tests UI dans le suivi.

- [ ] **Step 5: Documentation**

Dans `ios/README.md`, remplacer :
```markdown
le choix Serveur/Autonome au lancement viendra au plan 1e.
```
par :
```markdown
le choix Serveur/Autonome est proposé au premier lancement (plan 1e).
```

Dans `ios/README.md`, remplacer :
```markdown
Le mode n'est pas encore sélectionnable dans l'app.
```
par :
```markdown
Au premier lancement, l'app propose « Serveur » ou « Autonome » ; un iPad déjà appairé reste en mode serveur. En mode autonome, la base vit dans `Application Support/RestaurantPOS/pos-local.sqlite`, la première configuration crée le responsable et l'établissement, un bandeau rappelle que le mode n'est pas fiscal et la section Fiscal est masquée. Changer de mode suppose de réinstaller l'application. Arguments de lancement des tests UI : `-UITestLocal` (autonome avec données de démonstration), `-UITestLocalBlank` (autonome vierge), `-UITestModeChoice` (écran de choix).
```

Dans `CLAUDE.md`, remplacer :
```markdown
`-UITestSection floor|kitchen|fiscal|admin`, `-UITestHappyHour`.
```
par :
```markdown
`-UITestSection floor|kitchen|fiscal|admin`, `-UITestHappyHour`, `-UITestLocal` (mode autonome, base SQLite en mémoire avec données de démonstration), `-UITestLocalBlank` (mode autonome vierge : première configuration), `-UITestModeChoice` (écran « Serveur / Autonome »).
```

- [ ] **Step 6: Suivi, conditions d'entrée du sous-projet 2, commit**

Mettre à jour `docs/plans/ipad-standalone-1e-progress.md` : tâche 4 `fait`, PosKit `335`, tests UI (nombre relevé à l'étape 4), puis ajouter la section suivante :
```markdown
Sous-projet 1 « Socle local » : terminé (conditions de sortie : parcours table complet en mode autonome, `testFullTableJourneyWorksOnTheLocalBackend`).

Conditions d'entrée du sous-projet 2 (fiscal NF525) :
1. Remplacer `NonFiscalPayments` par `FiscalReceipts`/`PaymentTenders` signés (migration dédiée) ; retirer le bandeau « non fiscal » et rendre la section Fiscal visible quand les clôtures existent (sous-projet 3).
2. Clôture d'une commande à solde nul (100 % offerte ou remisée) et `chargeRoom` sans reçu `NF-…` (conditions 6 et 7 du plan 1c).
3. Sauvegarde : export de la base **chiffré** avant tout envoi vers Fichiers/iCloud (le hash SHA-256 d'un PIN à 4 chiffres est cassé en millisecondes), sauvegarde quotidienne automatique, protection de fichier de la copie, fichier partiel en cas de disque plein (`backup(to:)`).
4. Audit JET des dérogations Happy Hour et des annulations de mise en attente.
5. Ne pas signer du texte décimal relu tel quel de la base (la base écrit « 20 », le .NET « 20.0 »).
6. Écran de réglages de la base (emplacement, version de schéma, sauvegardes) si le besoin se confirme ; basculement de mode sans réinstallation seulement avec la synchronisation.
```

```bash
git add ios docs/plans/ipad-standalone-1e-progress.md CLAUDE.md
git commit -m "test(ios): tests UI du mode autonome et documentation du plan 1e"
```

---

## Self-Review

**1. Couverture de la spec (sous-projet 1) et des conditions d'entrée du plan 1d**
- « Au premier lancement, l'app propose Serveur ou Autonome » : tâche 2 (`ModeChoiceScreen`, `AppEnvironment.choose`). « Les deux modes coexistent ; le code serveur n'est pas modifié » : le mode serveur garde ses écrans, le chemin d'appairage et les tests existants (non-régression exécutée aux tâches 2 et 4).
- Conditions du plan 1d : choix du mode (T2) ; instance unique (T2, `localAPI` mémorisé) ; première configuration branchée sur `needsSetup`/`completeFirstRun` (T3) ; aucun compte de démonstration en production, avec test (T1 `openStandaloneNeverSeedsDemoAccounts`, T2 `openLocal`) ; identité `T01` sans appairage (T1 `DeviceCredentials.standalone`, T2) ; bandeau « non fiscal » (T2) ; blocage sur `needsSetup` et alignement du prédicat avec la garde 409 (T1, T2 `RootView`) ; XCUITest du parcours table complet (T4) ; clés FR/EN/AR (T2, T3). L'export/sauvegarde chiffré et la sauvegarde quotidienne sont reportés au sous-projet 2 (décision 8, consignés en T4), ce que la spec place d'ailleurs dans son chapitre fiscal.
- Condition de sortie de la spec : « Parcours table complet en mode autonome (tests UI adaptés) » → `testFullTableJourneyWorksOnTheLocalBackend`.

**2. Placeholders** : aucun « TBD »/« TODO » ; chaque étape de code contient le code complet ; les remplacements donnent le texte avant/après exact. Le fichier provisoire `FirstRunScreen` de la tâche 2 est explicitement remplacé par la tâche 3.

**3. Cohérence des types** : `AppMode` (T1) utilisé par `AppEnvironment` (T2) ; `DeviceCredentials.standalone` (T1) par `standaloneSettings` (T2) ; `LocalPosAPI.openStandalone` (T1) par `openLocal` (T2) ; `LocalSetup` (plan 1d) par `completeSetup` (T2) et `FirstRunScreen` (T3) ; identifiants d'accessibilité définis en T2/T3 et utilisés en T4 (`mode.server`, `mode.standalone`, `lock.standalone`, `standalone.banner`, `setup.*`).

**4. Review Focus** : les cinq points ont chacun un test nommé (tâches 1 et 4).

**5. Lacunes connues, hors test** : cas où la base locale est illisible (écran d'erreur, couvert côté package par `garbageFileIsRejectedAndLeftUntouched`, non exercé par un test UI) ; protection de fichier iOS réelle non exécutable en simulateur ; aucune migration de données entre modes (décision 1).

## Execution Handoff

Plan à relire avant toute exécution. Vérification prévue à la rédaction : les blocs « Créer / remplacer / supprimer » de ce document sont appliqués mécaniquement, tâche par tâche, sur une copie propre du dépôt à l'état de `main` ; `swift test` doit donner 335 tests verts, le build de l'application ne doit produire ni erreur ni avertissement, et `StandaloneUITests` doit passer (6 tests).
