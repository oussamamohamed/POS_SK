# iPad standalone — plan 1d : imprimantes (configuration), Happy Hour, terminal fixe et durcissement du socle local (`LocalPosAPI`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `LocalPosAPI` porte les dernières méthodes non fiscales de `PosAPI` (configuration des imprimantes, Happy Hour, réseau/synchronisation) et remplit les conditions d'entrée du mode sélectionnable : verrouillage des PIN persistant, terminal fixe `T01`, durcissement et sauvegarde de la base SQLite, base vierge avec configuration initiale (sans comptes ni données de démonstration).

**Architecture:** Migration v4 (tables `PrinterConfigurations`, `HappyHourSchedules`, `HappyHourPriceRules`, `HappyHourOverrideSessions`, mêmes noms et colonnes que `AppDbContext`, plus `LocalPinFailures` et `LocalPinLockout` propres à l'iPad). Une horloge et un calendrier injectables rendent le verrouillage des PIN et le Happy Hour testables sans attente. Le PIN superviseur du Happy Hour partage le verrouillage de la connexion. Aucune impression réelle : la configuration des imprimantes est stockée, les envois restent au sous-projet 4.

**Tech Stack:** Swift 6, `SQLite3`, Swift Testing, SwiftPM (`ios/Packages/PosKit`). Aucune dépendance nouvelle.

**Spec:** `docs/superpowers/specs/2026-10-06-ipad-standalone-design.md` (sous-projet 1 « Socle local »). Suite du plan 1c (`docs/superpowers/plans/2026-10-07-ipad-standalone-1c-paiement-comptoir.md`, mergé dans `main`). **Ce plan se base sur `main`** (288 tests PosKit). Sous-projet 1 :
- **1a, 1b, 1c** (faits) : SQLite, authentification, personnel, catalogue, grille, tables, commandes, cuisine, remises, transferts, paiement non fiscal, comptoir, mise en attente, chambres.
- **1d (ce plan)** : tout ce qui reste côté `PosKit` (imprimantes en configuration, Happy Hour, réseau, terminal fixe, durcissement, base vierge).
- **1e** (suite, côté application SwiftUI) : choix « Serveur / Autonome » au lancement, câblage `AppEnvironment`, écrans de première configuration, bandeau « non fiscal », instance unique de l'acteur, XCUITest en mode autonome. Les points qu'il reprend sont consignés à la fin de la tâche 7.

## Global Constraints

- `PosKit` reste **sans dépendance externe** : uniquement `Foundation`, `SQLite3`, `CryptoKit`.
- Swift 6 (`swift-tools-version: 6.0`), plateformes `.iOS(.v17)` et `.macOS(.v14)` ; aucun avertissement de compilation (une variable jamais modifiée, un résultat non utilisé comptent comme avertissement).
- Montants en **centimes entiers** (`Money`), jamais de `Double`/`Float` ; taux en `Decimal`, stockés en texte.
- Tables et colonnes nommées comme dans `AppDbContext` (sauf `LocalPinFailures` et `LocalPinLockout`, propres à l'iPad) ; Guid = `TEXT` en majuscules ; `DateTimeOffset` = `TEXT` ISO 8601 UTC ; enum = `INTEGER` ; `TimeOnly` = `TEXT` `HH:mm:ss`.
- Migrations versionnées par `PRAGMA user_version` : un script publié n'est jamais modifié (on ajoute le script v4 ; la v3 est publiée dans `main`).
- Messages d'erreur en français, identiques au serveur quand il en a un (`SharedResource.fr.resx`).
- Une opération qui touche plusieurs tables s'exécute dans **une seule** `db.transaction` (non ré-entrante) ; les refus se décident avant la première écriture.
- Types préfixés `Local…` pour éviter les collisions avec SwiftUI/Foundation.
- Commentaires et chaînes en français ; identifiants de code en anglais ; tests Swift Testing.
- Ne pas toucher à `HTTPPosAPI`, `InMemoryPosAPI`, aux stores ni aux vues.
- Le mode autonome reste « non fiscal » (sous-projet 2) : ne rien écrire dans le journal fiscal (JET), qui n'existe pas encore.

## Décisions de conception (écarts assumés vis-à-vis du serveur .NET)

1. **Découpage** : ce plan ne contient que de la logique `PosKit` (testable par `swift test`). Le choix Serveur/Autonome, les écrans et les tests XCUITest dépendent de builds Xcode et vont au plan 1e.
2. **Horloge injectable** : `LocalPosAPI.init(path:clock:calendar:)`. Le verrouillage des PIN (jusqu'ici en mémoire, non testable après 30 s) et l'heure locale du Happy Hour en dépendent. Valeurs par défaut : `Date()` et `Calendar.current`.
3. **Verrouillage des PIN persistant** (condition 1) : mêmes règles qu'avant (5 échecs en 60 s verrouillent 30 s), stockées dans `LocalPinFailures`/`LocalPinLockout`. Relancer l'app ne remet plus le compteur à zéro. Connexion, annulation d'une mise en attente et dérogation Happy Hour partagent le même verrouillage. Un PIN valide sans droit de responsable ne compte pas comme échec (même décision qu'au plan 1c). Une connexion réussie remet le compteur à zéro (comme le .NET).
4. **Terminal fixe `T01`** (condition 4) : le serveur prend le terminal de l'appareil et ignore celui de la requête ; l'iPad autonome fait de même. `normalizedTerminal` renvoie toujours `LocalPosAPI.standaloneTerminalId` (`T01`), pour les reçus `NF-T01-…`, les numéros de retrait `#A-…` et les mises en attente. La spec prévoit un identifiant dans le Keychain ; pour un iPad unique, une constante n'a ni mode de défaillance ni donnée à perdre (YAGNI). Si un second poste apparaît, c'est un changement de conception.
5. **Durcissement SQLite** (condition 3) : `busy_timeout` à 5 s, `synchronous = FULL` (une coupure ne doit pas faire perdre la dernière vente), sauvegarde cohérente par `VACUUM INTO` (réservée aux responsables, refuse d'écraser un fichier), emplacement `Application Support/RestaurantPOS/` avec protection de fichier iOS `completeUntilFirstUserAuthentication`. « Une seule instance par fichier » est une propriété du cycle de vie de l'application : elle revient au plan 1e (`AppEnvironment` détient l'unique acteur).
6. **Imprimantes** : configuration seulement (table `PrinterConfigurations`). Pas de table `PrintJobs` ni d'envoi (sous-projet 4) : le test d'impression répond `success: false` avec un message clair, les statuts n'ont ni état en ligne ni file, la liste des impressions est vide. Écarts : un nom ou une IP vide est refusé en 400 (le .NET lève une exception, donc 500) ; une imprimante inconnue donne 404.
7. **Happy Hour** : port de `HappyHourPricingService` (priorité des plannings, fin de plage exclusive, pas de passage de minuit, ordre de calcul produit puis catégorie, libellé `CategoryDiscount` pour toute règle non « prix fixe »). `ResolveItemPriceAsync` n'est pas porté : le serveur .NET ne l'appelle nulle part, le client envoie ses prix. La création, la suppression de plannings et la pose/suppression de règles exigent un **responsable** (le .NET les laisse anonymes) : écart de sécurité assumé. La dérogation ne demande pas de session (le PIN superviseur suffit, comme le .NET). L'audit JET de l'activation/arrêt est reporté au sous-projet 2.
8. **Réseau/synchronisation** : valeurs fixes « Standalone » (aucun serveur, rien à synchroniser) ; `forceSync` ne fait rien ; `hostName` reste nul (la résolution du nom d'hôte peut bloquer).
9. **Base vierge** (condition 2) : `LocalSeedMode.demo` (défaut, tests et démonstration : comptes 1234/2468/5678/9999, catalogue, tables, chambres, imprimantes, planning) ou `.blank` (rien). Sur une base vierge, `completeFirstRun` crée le premier responsable (rôle `admin`) et l'identité de l'établissement (nom et SIRET à 14 chiffres obligatoires, exigés par le fiscal). Il refuse (409) dès qu'un compte existe : impossible d'écraser une installation.
10. **Conditions 5, 6 et 7 du plan 1c restent ouvertes** (bandeau « non fiscal » : plan 1e ; clôture d'une commande à solde nul et `chargeRoom` sans reçu : sous-projet 2).

## Review Focus

Entrées ou pannes que la spec implique mais qu'aucun test nominal n'exerce ; chacune a son test dans la tâche indiquée.

1. **Relancer l'app pour contourner le verrouillage des PIN** → échecs et verrouillage lus depuis la base à la réouverture → `lockoutSurvivesReopeningTheFile`, `failureCountSurvivesReopeningTheFile` (tâche 1).
2. **Un client envoie un autre identifiant de terminal** pour ouvrir une autre série de reçus ou de retraits → ignoré → `requestedTerminalIsIgnoredForTablePayments`, `requestedTerminalIsIgnoredAtTheCounter`, `heldOrdersAlwaysBelongToTheStandaloneTerminal` (tâche 2).
3. **Limites du Happy Hour** (début inclus, fin exclue, dimanche, planning désactivé, dérogation expirée, deux plannings simultanés) → `statusIsInactiveOutsideTheWindowTheDaysOrWhenDisabled`, `higherPriorityWinsAndTiesKeepTheFirst`, `supervisorOverrideStartsExtendsAndExpires` (tâche 6).
4. **Une installation réelle qui hérite des comptes de démonstration**, ou une configuration initiale rejouée qui écrase une installation existante → `blankDatabaseHoldsNoDemoData`, `completeFirstRunRefusesAnAlreadyConfiguredDatabase` (tâche 7).
5. **Sauvegarde qui écrase un fichier existant, ou lancée par un non-responsable** → `backupRefusesToOverwriteAnExistingFile`, `backupNeedsAManager` (tâche 3).

## File Structure

Tout est sous `ios/Packages/PosKit/`.

| Fichier | Responsabilité |
|---|---|
| `Sources/PosKit/Local/LocalSchemaV4.swift` | Script de la migration v4. |
| `Sources/PosKit/Local/LocalMigrator.swift` | (modifié) ajoute `schemaV4` à `steps`. |
| `Sources/PosKit/Local/LocalPinGuard.swift` | Échecs de PIN et verrouillage, persistants. |
| `Sources/PosKit/Local/LocalPosAPI.swift` | (modifié) horloge, calendrier, verrouillage persistant, accesseurs, constante du terminal. |
| `Sources/PosKit/Local/LocalPosAPI+Payments.swift`, `+Counter.swift` | (modifiés) terminal fixe, `try` sur le verrouillage. |
| `Sources/PosKit/Local/SQLiteDatabase.swift` | (modifié) `busy_timeout`, `synchronous`, `backup(to:)`. |
| `Sources/PosKit/Local/LocalDatabaseLocation.swift` | Emplacement du fichier et protection iOS. |
| `Sources/PosKit/Local/LocalPosAPI+Backup.swift` | `backup(to:)` pour les responsables. |
| `Sources/PosKit/Local/LocalPrinterRepository.swift`, `LocalPosAPI+Printers.swift` | Configuration des imprimantes. |
| `Sources/PosKit/Local/LocalTimeOfDay.swift` | Lecture/écriture des heures `HH:mm[:ss]`. |
| `Sources/PosKit/Local/LocalHappyHourRepository.swift` | Plannings, règles, dérogations. |
| `Sources/PosKit/Local/LocalPosAPI+HappyHourSchedules.swift` | Plannings et règles (administration). |
| `Sources/PosKit/Local/LocalHappyHourPricing.swift` | Calcul des prix (pur). |
| `Sources/PosKit/Local/LocalPosAPI+HappyHour.swift` | Statut, grille tarifaire, dérogations. |
| `Sources/PosKit/Local/LocalPosAPI+Network.swift` | Réseau et synchronisation (valeurs fixes). |
| `Sources/PosKit/Local/LocalPosAPI+Setup.swift` | Première configuration d'une base vierge. |
| `Sources/PosKit/Local/LocalSeeder.swift`, `LocalFloorRepository.swift`, `LocalPosAPI+Staff.swift` | (modifiés) mode d'amorçage, réglages initiaux, validation du PIN partagée. |
| `Sources/PosKit/Local/LocalPosAPI+Unsupported.swift` | (modifié) retire les stubs portés. |
| `Tests/PosKitTests/Local/*.swift` | Un fichier de tests par tâche. |
| `docs/plans/ipad-standalone-1d-progress.md` | Suivi d'exécution (exigé par `CLAUDE.md`). |

## Conventions pour toutes les tâches

- Les commandes `swift` se lancent depuis `ios/Packages/PosKit`. **`swift test --filter` porte sur les noms de types Swift** (ex. `--filter LocalPrinterTests`). La suite complète (`swift test`) fait foi.
- Base de référence avant ce plan (`main`) : **288 tests PosKit passent**. Totaux attendus cumulés à la fin de chaque tâche : T1 294 · T2 295 · T3 300 · T4 305 · T5 312 · T6 318 · T7 325.
- Chaque tâche se termine par : suite PosKit complète verte, mise à jour de `docs/plans/ipad-standalone-1d-progress.md`, commit.
- Commits au style du dépôt : `feat(ios-local): …`, en français, avec le trailer de co-signature de la session.
- Les tests de Happy Hour partent d'une base de démonstration : ils commencent par supprimer les plannings existants (`clearSchedules`) pour ne pas dépendre de l'amorçage.

---

### Task 1: Migration v4, horloge injectable et verrouillage des PIN persistant

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV4.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalMigrator.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPinGuard.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Counter.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalClockTestSupport.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPinGuardTests.swift`
- Create: `docs/plans/ipad-standalone-1d-progress.md`

**Interfaces:**
- Consumes (plans 1a-1c) : `SQLiteDatabase`, `SQLRow`, `SQLValue`, `SQLDate`, `LocalMigrator.steps`, `LocalPosAPI` (`login`, `voidHeldOrder`), `APIError.rateLimited`.
- Produces :
  - `LocalPosAPI.init(path: String, clock: @escaping @Sendable () -> Date = { Date() }, calendar: Calendar = .current) throws` ; propriétés internes `clock: @Sendable () -> Date` et `calendar: Calendar`.
  - `struct LocalPinGuard { let db; ensureAllowed(now:) throws; recordFailure(now:) throws; reset() throws }`.
  - `LocalPosAPI.ensurePinAttemptsAllowed() throws`, `.recordFailedPin() throws`, `.resetFailedPins() throws` (désormais `throws`).
  - Tables v4 : `PrinterConfigurations`, `HappyHourSchedules`, `HappyHourPriceRules`, `HappyHourOverrideSessions`, `LocalPinFailures`, `LocalPinLockout`.
  - Helpers de test : `LocalTestClock` (`now`, `advance(_:)`, `set(_:)`), `localTestCalendar`, `makeLocalAPI(pin:clock:)`.

- [ ] **Step 0: Relever la base de référence et créer la branche et le fichier de suivi**

Run (depuis la racine du dépôt) :
```bash
git branch --show-current
cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run"
```
Expected: `main` ; `Test run with 288 tests … passed`. Créer ensuite la branche de travail : `git switch -c feat/ipad-standalone-1d`.

Créer `docs/plans/ipad-standalone-1d-progress.md` :
```markdown
# iPad standalone 1d — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-08-ipad-standalone-1d-imprimantes-happy-hour-durcissement.md`

Base de référence (avant tâche 1) : PosKit 288 · .NET et web inchangés (aucun fichier .NET ni web modifié)

| Tâche | Statut | PosKit | Findings de revue ouverts |
|---|---|---|---|
| 1 Migration v4, horloge, PIN persistants | à faire | | |
| 2 Terminal fixe T01 | à faire | | |
| 3 Durcissement SQLite et sauvegarde | à faire | | |
| 4 Configuration des imprimantes | à faire | | |
| 5 Happy Hour : plannings et règles | à faire | | |
| 6 Happy Hour : statut, tarifs, dérogations | à faire | | |
| 7 Réseau, base vierge, première configuration | à faire | | |
```

- [ ] **Step 1: Écrire les helpers et les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalClockTestSupport.swift` :
```swift
import Foundation
@testable import PosKit

/// Horloge de test, avancée à la main. Par défaut : lundi 12 octobre 2026, 18 h 30 UTC.
final class LocalTestClock: @unchecked Sendable {
    private let lock = NSLock()
    private var current: Date

    init(_ iso: String = "2026-10-12T18:30:00Z") { current = SQLDate.parse(iso) ?? Date() }

    var now: Date {
        lock.lock()
        defer { lock.unlock() }
        return current
    }

    func advance(_ seconds: TimeInterval) {
        lock.lock()
        current = current.addingTimeInterval(seconds)
        lock.unlock()
    }

    func set(_ iso: String) {
        lock.lock()
        current = SQLDate.parse(iso) ?? current
        lock.unlock()
    }
}

/// Calendrier grégorien en UTC : l'heure « locale » des tests ne dépend pas du fuseau de la machine.
let localTestCalendar: Calendar = {
    var calendar = Calendar(identifier: .gregorian)
    calendar.timeZone = .gmt
    return calendar
}()

/// API locale en mémoire pilotée par une horloge de test, déjà déverrouillée avec le PIN donné.
func makeLocalAPI(pin: String = "1234", clock: LocalTestClock) async throws -> LocalPosAPI {
    let api = try LocalPosAPI(path: ":memory:", clock: { clock.now }, calendar: localTestCalendar)
    _ = try await api.login(pin: pin)
    return api
}
```

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPinGuardTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : verrouillage des PIN persistant")
struct LocalPinGuardTests {
    private func failLogin(_ api: LocalPosAPI, times: Int) async throws {
        for _ in 0..<times { _ = try await api.login(pin: "0000") }
    }

    private func cleanup(_ path: String) {
        for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) }
    }

    @Test func schemaV4CreatesTheNewTables() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        #expect(LocalMigrator.steps.count >= 4)
        let tables = try db.query("SELECT name FROM sqlite_master WHERE type = 'table'").compactMap { $0.string("name") }
        for expected in ["PrinterConfigurations", "HappyHourSchedules", "HappyHourPriceRules", "HappyHourOverrideSessions", "LocalPinFailures", "LocalPinLockout"] {
            #expect(tables.contains(expected))
        }
    }

    @Test func fiveFailuresLockUntilThirtySecondsHavePassed() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await failLogin(api, times: 5)
        await #expect(throws: APIError.rateLimited(nil)) { try await api.login(pin: "1234") }
        clock.advance(29)
        await #expect(throws: APIError.rateLimited(nil)) { try await api.login(pin: "1234") }
        clock.advance(2)
        #expect(try await api.login(pin: "1234").success)
    }

    @Test func failuresOlderThanAMinuteDoNotAccumulate() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await failLogin(api, times: 4)
        clock.advance(61)
        try await failLogin(api, times: 4)
        #expect(try await api.login(pin: "1234").success)
    }

    @Test func aSuccessfulLoginClearsTheFailures() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await failLogin(api, times: 4)
        #expect(try await api.login(pin: "1234").success)
        try await failLogin(api, times: 4)
        #expect(try await api.login(pin: "1234").success)
    }

    @Test func lockoutSurvivesReopeningTheFile() async throws {
        let path = temporaryDatabasePath()
        defer { cleanup(path) }
        let clock = LocalTestClock()
        do {
            let first = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
            try await failLogin(first, times: 5)
        }
        let reopened = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
        await #expect(throws: APIError.rateLimited(nil)) { try await reopened.login(pin: "1234") }
        clock.advance(31)
        #expect(try await reopened.login(pin: "1234").success)
    }

    @Test func failureCountSurvivesReopeningTheFile() async throws {
        let path = temporaryDatabasePath()
        defer { cleanup(path) }
        let clock = LocalTestClock()
        do {
            let first = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
            try await failLogin(first, times: 3)
        }
        let reopened = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
        try await failLogin(reopened, times: 2)
        await #expect(throws: APIError.rateLimited(nil)) { try await reopened.login(pin: "1234") }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalPinGuardTests 2>&1 | tail -5`
Expected: erreur de compilation `extra argument 'clock' in call` (le nouvel `init` n'existe pas encore).

- [ ] **Step 3: Écrire la migration v4**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalSchemaV4.swift` :
```swift
import Foundation

extension LocalMigrator {
    /// Imprimantes (configuration), Happy Hour (plannings, règles, dérogations) : mêmes tables et colonnes que `AppDbContext`
    /// (`TimeOnly` stocké `HH:mm:ss`, jours de la semaine en entiers séparés par des virgules, 0 = dimanche, prix fixe en centimes).
    /// Deux tables propres à l'iPad : `LocalPinFailures` et `LocalPinLockout` (verrouillage des PIN, en mémoire côté .NET).
    static let schemaV4 = """
    CREATE TABLE PrinterConfigurations (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        IpAddress TEXT NOT NULL,
        Port INTEGER NOT NULL,
        PaperWidthMm INTEGER NOT NULL,
        OpenCashDrawerOnReceipt INTEGER NOT NULL,
        TextMode INTEGER NOT NULL,
        AssignedStationIds TEXT NOT NULL,
        IsActive INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );

    CREATE TABLE HappyHourSchedules (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        DaysOfWeek TEXT NOT NULL,
        StartTime TEXT NOT NULL,
        EndTime TEXT NOT NULL,
        IsActive INTEGER NOT NULL,
        AppliesToTakeaway INTEGER NOT NULL,
        Priority INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT
    );

    CREATE TABLE HappyHourPriceRules (
        Id TEXT NOT NULL PRIMARY KEY,
        ScheduleId TEXT NOT NULL REFERENCES HappyHourSchedules (Id) ON DELETE CASCADE,
        TargetType INTEGER NOT NULL,
        TargetId TEXT NOT NULL,
        TargetName TEXT NOT NULL,
        PricingMode INTEGER NOT NULL,
        FixedPrice INTEGER,
        DiscountPercent TEXT,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_HappyHourPriceRules_ScheduleId ON HappyHourPriceRules (ScheduleId);

    CREATE TABLE HappyHourOverrideSessions (
        Id TEXT NOT NULL PRIMARY KEY,
        TerminalId TEXT NOT NULL,
        OperatorId TEXT NOT NULL,
        OperatorName TEXT NOT NULL,
        OverrideType INTEGER NOT NULL,
        StartsAtUtc TEXT NOT NULL,
        ExpiresAtUtc TEXT NOT NULL,
        Reason TEXT NOT NULL,
        IsActive INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_HappyHourOverrideSessions_TerminalId_IsActive ON HappyHourOverrideSessions (TerminalId, IsActive);

    CREATE TABLE LocalPinFailures (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        AttemptedAtUtc TEXT NOT NULL
    );

    CREATE TABLE LocalPinLockout (
        Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
        LockedUntilUtc TEXT NOT NULL
    );
    """
}
```

Dans `LocalMigrator.swift`, remplacer :
```swift
    static let steps: [String] = [schemaV1, schemaV2, schemaV3]
```
par :
```swift
    static let steps: [String] = [schemaV1, schemaV2, schemaV3, schemaV4]
```

- [ ] **Step 4: Écrire le verrouillage persistant**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPinGuard.swift` :
```swift
import Foundation

/// Même règle que `PinRateLimiterService` : 5 échecs en 1 min verrouillent 30 s. L'état vit dans la base (`LocalPinFailures`,
/// `LocalPinLockout`) : relancer l'application ne le remet pas à zéro. Les méthodes ne doivent pas être appelées dans une transaction.
struct LocalPinGuard {
    let db: SQLiteDatabase

    static let maxFailures = 5
    static let window: TimeInterval = 60
    static let lockout: TimeInterval = 30

    func ensureAllowed(now: Date) throws {
        guard let until = try db.query("SELECT LockedUntilUtc FROM LocalPinLockout WHERE Id = 1").first?.date("LockedUntilUtc"), until > now else { return }
        throw APIError.rateLimited(nil)
    }

    func recordFailure(now: Date) throws {
        try db.transaction {
            try db.run("DELETE FROM LocalPinFailures WHERE AttemptedAtUtc <= ?", [.date(now.addingTimeInterval(-Self.window))])
            try db.run("INSERT INTO LocalPinFailures (AttemptedAtUtc) VALUES (?)", [.date(now)])
            let count = try db.query("SELECT COUNT(*) AS n FROM LocalPinFailures").first?.int("n") ?? 0
            guard count >= Self.maxFailures else { return }
            try db.run(
                "INSERT INTO LocalPinLockout (Id, LockedUntilUtc) VALUES (1, ?) ON CONFLICT(Id) DO UPDATE SET LockedUntilUtc = excluded.LockedUntilUtc",
                [.date(now.addingTimeInterval(Self.lockout))]
            )
            try db.run("DELETE FROM LocalPinFailures")
        }
    }

    func reset() throws {
        try db.transaction {
            try db.run("DELETE FROM LocalPinFailures")
            try db.run("DELETE FROM LocalPinLockout")
        }
    }
}
```

Dans `LocalPosAPI.swift`, remplacer :
```swift
    private var token: String?
    private var failedLogins: [Date] = []
    private var lockedUntil: Date?

    private static let tokenPrefix = "local-"
    /// Identique à `SessionStore.pinLength` (isolé au MainActor, donc non réutilisable ici).
    static let pinLength = 4
    /// Même règle que `PinRateLimiterService` : 5 échecs en 1 min verrouillent 30 s.
    private static let maxFailedLogins = 5
    private static let failureWindow: TimeInterval = 60
    private static let lockoutDuration: TimeInterval = 30

    /// `path` : fichier SQLite à créer ou rouvrir, ou `":memory:"` (tests).
    public init(path: String) throws {
        let db = try SQLiteDatabase(path: path)
        try LocalMigrator.migrate(db)
        try LocalSeeder.seedIfEmpty(db)
        self.db = db
    }
```
par :
```swift
    private var token: String?
    /// Horloge et calendrier injectables : verrouillage des PIN, Happy Hour (heure locale) et tests sans attente réelle.
    let clock: @Sendable () -> Date
    let calendar: Calendar

    private static let tokenPrefix = "local-"
    /// Identique à `SessionStore.pinLength` (isolé au MainActor, donc non réutilisable ici).
    static let pinLength = 4

    /// `path` : fichier SQLite à créer ou rouvrir, ou `":memory:"` (tests).
    public init(path: String, clock: @escaping @Sendable () -> Date = { Date() }, calendar: Calendar = .current) throws {
        let db = try SQLiteDatabase(path: path)
        try LocalMigrator.migrate(db)
        try LocalSeeder.seedIfEmpty(db)
        self.db = db
        self.clock = clock
        self.calendar = calendar
    }
```

Dans `LocalPosAPI.swift`, remplacer :
```swift
            recordFailedPin()
            return LoginResponse(success: false
```
par :
```swift
            try recordFailedPin()
            return LoginResponse(success: false
```

Dans `LocalPosAPI.swift`, remplacer :
```swift
        resetFailedPins()
        token = Self.tokenPrefix
```
par :
```swift
        try resetFailedPins()
        token = Self.tokenPrefix
```

Dans `LocalPosAPI.swift`, remplacer :
```swift
    func ensurePinAttemptsAllowed() throws {
        if let until = lockedUntil, until > Date() { throw APIError.rateLimited(nil) }
    }

    func recordFailedPin() {
        let now = Date()
        failedLogins = failedLogins.filter { now.timeIntervalSince($0) < Self.failureWindow } + [now]
        if failedLogins.count >= Self.maxFailedLogins {
            lockedUntil = now.addingTimeInterval(Self.lockoutDuration)
            failedLogins = []
        }
    }

    func resetFailedPins() {
        failedLogins = []
        lockedUntil = nil
    }
```
par :
```swift
    var pinGuard: LocalPinGuard { LocalPinGuard(db: db) }

    func ensurePinAttemptsAllowed() throws { try pinGuard.ensureAllowed(now: clock()) }

    func recordFailedPin() throws { try pinGuard.recordFailure(now: clock()) }

    func resetFailedPins() throws { try pinGuard.reset() }
```

Dans `LocalPosAPI+Counter.swift`, remplacer :
```swift
            recordFailedPin()
            throw insufficient
```
par :
```swift
            try recordFailedPin()
            throw insufficient
```

Dans `LocalPosAPI+Counter.swift`, remplacer :
```swift
        resetFailedPins()
```
par :
```swift
        try resetFailedPins()
```

- [ ] **Step 5: Regrouper les stubs par tâche**

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    // MARK: Imprimantes (plan 1d)
```
par :
```swift
    // MARK: Imprimantes (retiré par la tâche 4)
```

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    // MARK: Happy Hour (plan 1d)
```
par :
```swift
    // MARK: Happy Hour, statut et dérogations (retiré par la tâche 6)
```

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    public func stopHappyHourOverride(terminalId: String, pin: String, reason: String) async throws -> OperationResult { throw unsupported() }
```
par :
```swift
    public func stopHappyHourOverride(terminalId: String, pin: String, reason: String) async throws -> OperationResult { throw unsupported() }

    // MARK: Happy Hour, plannings (retiré par la tâche 5)
```

Dans `LocalPosAPI+Unsupported.swift`, remplacer :
```swift
    // MARK: Réseau (plan 1d)
```
par :
```swift
    // MARK: Réseau (retiré par la tâche 7)
```

- [ ] **Step 6: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalPinGuardTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 6 tests … passed`, aucun avertissement.

- [ ] **Step 7: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"` → Expected: `294 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1d-progress.md` (tâche 1 `fait`, PosKit `294`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1d-progress.md
git commit -m "feat(ios-local): migration v4, horloge injectable et verrouillage des PIN persistant"
```

---

### Task 2: Terminal fixe `T01`

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Payments.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Counter.swift`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalCounterTests.swift`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalCounterCheckoutTests.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalTerminalTests.swift`

**Interfaces:**
- Consumes : `normalizedTerminal(_:)` (appelé par `settle` et `counterCheckout`), `holdOrder`, `heldOrders`.
- Produces : `LocalPosAPI.standaloneTerminalId` (`"T01"`, `public static let`) ; `normalizedTerminal(_:)` renvoie toujours cette valeur ; mises en attente et liste d'attente toujours rattachées à `T01`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalTerminalTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : terminal du poste autonome")
struct LocalTerminalTests {
    @Test func requestedTerminalIsIgnoredForTablePayments() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)])
        let order = try #require(try await api.activeOrder(table: "T1"))
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)], terminal: "T09"))
        #expect(result.receiptNumber == "NF-T01-000001")
        #expect(LocalPosAPI.standaloneTerminalId == "T01")
    }
}
```

Dans `LocalCounterTests.swift`, remplacer :
```swift
    @Test func heldOrdersAreFilteredByTerminal() async throws {
```
par :
```swift
    @Test func heldOrdersAlwaysBelongToTheStandaloneTerminal() async throws {
```

Dans `LocalCounterTests.swift`, remplacer :
```swift
        #expect(try await api.heldOrders(terminalId: "T01").map(\.customerLabel) == ["A"])
        #expect(try await api.heldOrders(terminalId: "T02").map(\.customerLabel) == ["B"])
        #expect(try await api.heldOrders(terminalId: "").count == 2)
```
par :
```swift
        // Le poste autonome est son propre terminal : le terminal demandé est ignoré, comme le fait le serveur avec celui de la requête.
        for requested in ["T01", "T02", ""] {
            #expect(try await api.heldOrders(terminalId: requested).compactMap(\.customerLabel).sorted() == ["A", "B"])
        }
        #expect(try await api.heldOrders(terminalId: "T01").allSatisfy { $0.terminalId == "T01" })
```

Dans `LocalCounterCheckoutTests.swift`, remplacer :
```swift
    @Test func pickupNumbersFollowTheTerminalPrefix() async throws {
```
par :
```swift
    @Test func requestedTerminalIsIgnoredAtTheCounter() async throws {
```

Dans `LocalCounterCheckoutTests.swift`, remplacer :
```swift
        #expect(result.pickupNumber == "#B-01" && result.receiptNumber == "NF-T02-000001")
```
par :
```swift
        #expect(result.pickupNumber == "#A-01" && result.receiptNumber == "NF-T01-000001")
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalTerminalTests|LocalCounterTests|LocalCounterCheckoutTests" 2>&1 | grep -E "error:|✘|Test run" | head -8`
Expected: erreur de compilation `type 'LocalPosAPI' has no member 'standaloneTerminalId'`.

- [ ] **Step 3: Fixer le terminal**

Dans `LocalPosAPI.swift`, remplacer :
```swift
    static let pinLength = 4
```
par :
```swift
    static let pinLength = 4
    /// Le poste autonome est son propre terminal : les numéros de reçu (`NF-T01-…`) et de retrait (`#A-…`) en dépendent.
    public static let standaloneTerminalId = "T01"
```

Dans `LocalPosAPI+Payments.swift`, remplacer :
```swift
    /// Identifiant de terminal utilisable dans un numéro de reçu (le poste autonome s'appellera `T01` au plan 1d).
    func normalizedTerminal(_ id: String) -> String {
        let trimmed = id.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? "LOCAL" : trimmed
    }
```
par :
```swift
    /// Terminal des reçus, retraits et mises en attente. Comme le serveur, qui prend le terminal de l'appareil et ignore celui de la requête,
    /// le poste autonome n'a qu'un terminal : `T01`.
    func normalizedTerminal(_ requested: String) -> String { Self.standaloneTerminalId }
```

Dans `LocalPosAPI+Counter.swift`, remplacer :
```swift
terminalId: terminalId.isEmpty ? "POS_MAIN_TERM" : terminalId,
```
par :
```swift
terminalId: normalizedTerminal(terminalId),
```

Dans `LocalPosAPI+Counter.swift`, remplacer :
```swift
        return try holdRepository.active(terminalId: terminalId)
```
par :
```swift
        return try holdRepository.active(terminalId: normalizedTerminal(terminalId))
```

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalTerminalTests|LocalCounterTests|LocalCounterCheckoutTests|LocalPaymentTests|LocalReviewFixTests" 2>&1 | grep -E "error:|✘|Test run"`
Expected: tous les tests passent. Si un test existant échoue parce qu'il supposait un terminal autre que `T01` (par exemple `NF-LOCAL-…` ou `NF-T02-…`), l'adapter au terminal fixe et le signaler dans le rapport.

- [ ] **Step 5: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"` → Expected: `295 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1d-progress.md` (tâche 2 `fait`, PosKit `295`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1d-progress.md
git commit -m "feat(ios-local): terminal fixe T01 pour les reçus, retraits et mises en attente"
```

---

### Task 3: Durcissement SQLite et sauvegarde

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/SQLiteDatabase.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalDatabaseLocation.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Backup.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalHardeningTests.swift`

**Interfaces:**
- Consumes : `SQLiteDatabase.init(path:)`, `LocalPosAPI.requireManager()`, `LocalPosAPI.db`.
- Produces :
  - `SQLiteDatabase.backup(to path: String) throws` (copie cohérente via `VACUUM INTO` ; refuse un fichier existant).
  - `LocalPosAPI.backup(to url: URL) async throws` (responsables seulement).
  - `public enum LocalDatabaseLocation { static func defaultURL(base: URL? = nil) throws -> URL }`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalHardeningTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : durcissement SQLite et sauvegarde")
struct LocalHardeningTests {
    private func cleanup(_ path: String) {
        for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) }
    }

    @Test func busyTimeoutAndFullSynchronousAreSet() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        #expect(try db.query("PRAGMA busy_timeout").first?.int("timeout") == 5000)
        #expect(try db.query("PRAGMA synchronous").first?.int("synchronous") == 2)
    }

    @Test func backupIsAConsistentOpenableCopy() async throws {
        let path = temporaryDatabasePath(), copy = temporaryDatabasePath()
        defer { cleanup(path); cleanup(copy) }
        let api = try LocalPosAPI(path: path)
        _ = try await api.login(pin: "1234")
        try await api.createTable(number: "T77", capacity: 3)
        try await api.backup(to: URL(fileURLWithPath: copy))

        let restored = try LocalPosAPI(path: copy)
        #expect(try await restored.tables().contains { $0.tableNumber == "T77" })
        #expect(try await restored.login(pin: "1234").success)
    }

    @Test func backupRefusesToOverwriteAnExistingFile() async throws {
        let path = temporaryDatabasePath(), existing = temporaryDatabasePath()
        defer { cleanup(path); cleanup(existing) }
        let api = try LocalPosAPI(path: path)
        _ = try await api.login(pin: "1234")
        let content = Data("à ne pas écraser".utf8)
        try content.write(to: URL(fileURLWithPath: existing))
        await #expect(throws: SQLiteError.self) { try await api.backup(to: URL(fileURLWithPath: existing)) }
        #expect(try Data(contentsOf: URL(fileURLWithPath: existing)) == content)
    }

    @Test func backupNeedsAManager() async throws {
        let copy = temporaryDatabasePath()
        defer { cleanup(copy) }
        let waiter = try await makeLocalAPI(pin: "2468")
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await waiter.backup(to: URL(fileURLWithPath: copy)) }
        let anonymous = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await anonymous.backup(to: URL(fileURLWithPath: copy)) }
        #expect(!FileManager.default.fileExists(atPath: copy))
    }

    @Test func defaultLocationCreatesTheApplicationDirectory() throws {
        let base = FileManager.default.temporaryDirectory.appendingPathComponent("loc-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: base) }
        let url = try LocalDatabaseLocation.defaultURL(base: base)
        #expect(url.lastPathComponent == "pos-local.sqlite")
        #expect(url.deletingLastPathComponent().lastPathComponent == "RestaurantPOS")
        var isDirectory: ObjCBool = false
        #expect(FileManager.default.fileExists(atPath: url.deletingLastPathComponent().path, isDirectory: &isDirectory) && isDirectory.boolValue)
        #expect(try LocalDatabaseLocation.defaultURL(base: base) == url)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalHardeningTests 2>&1 | tail -5`
Expected: erreur de compilation `cannot find 'LocalDatabaseLocation' in scope`.

- [ ] **Step 3: Durcir la connexion et ajouter la sauvegarde**

Dans `SQLiteDatabase.swift`, remplacer :
```swift
        try exec("PRAGMA journal_mode = WAL")
```
par :
```swift
        try exec("PRAGMA journal_mode = WAL")
        // Attend jusqu'à 5 s un verrou tenu par une autre connexion au lieu d'échouer aussitôt.
        try exec("PRAGMA busy_timeout = 5000")
        // FULL : une coupure de courant ne doit pas faire perdre la dernière vente validée (NORMAL, défaut du mode WAL, le permet).
        try exec("PRAGMA synchronous = FULL")
```

Dans `SQLiteDatabase.swift`, remplacer :
```swift
    func userVersion() throws -> Int { try query("PRAGMA user_version").first?.int("user_version") ?? 0 }
```
par :
```swift
    /// Copie cohérente de la base (même en mode WAL) dans un nouveau fichier. `VACUUM INTO` refuse d'écraser un fichier existant
    /// et ne s'exécute pas dans une transaction.
    func backup(to path: String) throws {
        try run("VACUUM INTO ?", [.text(path)])
    }

    func userVersion() throws -> Int { try query("PRAGMA user_version").first?.int("user_version") ?? 0 }
```

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Backup.swift` :
```swift
import Foundation

extension LocalPosAPI {
    /// Sauvegarde cohérente de la base vers `url` (le fichier ne doit pas exister). Réservé aux responsables : la base contient
    /// les comptes, les ventes et, plus tard, la chaîne fiscale.
    public func backup(to url: URL) async throws {
        try requireManager()
        try db.backup(to: url.path)
    }
}
```

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalDatabaseLocation.swift` :
```swift
import Foundation

/// Emplacement du fichier de la base locale.
public enum LocalDatabaseLocation {
    /// `…/Application Support/RestaurantPOS/pos-local.sqlite`. Sur iOS, le dossier est créé avec la protection
    /// `completeUntilFirstUserAuthentication` : lisible après le premier déverrouillage de l'iPad, y compris écran verrouillé
    /// (l'app doit pouvoir encaisser en arrière-plan). `base` remplace `Application Support` (tests).
    public static func defaultURL(base: URL? = nil) throws -> URL {
        let root = try base ?? FileManager.default.url(for: .applicationSupportDirectory, in: .userDomainMask, appropriateFor: nil, create: true)
        let directory = root.appendingPathComponent("RestaurantPOS", isDirectory: true)
        #if os(iOS)
        let attributes: [FileAttributeKey: Any]? = [.protectionKey: FileProtectionType.completeUntilFirstUserAuthentication]
        #else
        let attributes: [FileAttributeKey: Any]? = nil
        #endif
        try FileManager.default.createDirectory(at: directory, withIntermediateDirectories: true, attributes: attributes)
        return directory.appendingPathComponent("pos-local.sqlite")
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalHardeningTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 5 tests … passed`.

- [ ] **Step 5: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"` → Expected: `300 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1d-progress.md` (tâche 3 `fait`, PosKit `300`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1d-progress.md
git commit -m "feat(ios-local): busy_timeout, synchronous FULL, sauvegarde cohérente et emplacement protégé de la base"
```

---

### Task 4: Configuration des imprimantes

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPrinterRepository.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Printers.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPrinterTests.swift`

**Interfaces:**
- Consumes : table `PrinterConfigurations` (tâche 1), `requireAuth()`, `requireManager()`, modèles `Printer`, `PrinterStatus`, `PrintJobInfo`, `TestPrintResult`.
- Produces : `struct LocalPrinterRepository { let db; all() -> [Printer]; printer(id:) -> Printer?; insert(_:) ; update(_:) -> Bool }` ; `LocalPosAPI.printerRepository` ; méthodes `PosAPI` `printers()`, `savePrinter(_:isNew:)`, `testPrinter(id:)`, `printerStatuses()`, `printJobs(printerId:)`, `retryPrintJob(id:)`, `cancelPrintJob(id:)`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPrinterTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : configuration des imprimantes")
struct LocalPrinterTests {
    private static let notFound = APIError.notFound("Imprimante introuvable.")

    @Test func createUpdateAndListPrinters() async throws {
        let api = try await makeLocalAPI()
        let baseline = try await api.printers().count
        let bar = Printer(name: "Bar", ipAddress: "192.168.1.50", assignedStationIds: ["BAR"])
        var kitchen = Printer(name: "Cuisine", ipAddress: "192.168.1.51", port: 9101, paperWidthMm: 58, assignedStationIds: ["HOT_KITCHEN", "GRILL"], textMode: true)
        try await api.savePrinter(bar, isNew: true)
        try await api.savePrinter(kitchen, isNew: true)

        let listed = try await api.printers()
        #expect(listed.count == baseline + 2)
        #expect(listed.first { $0.id == kitchen.id } == kitchen)

        kitchen.name = "Cuisine chaude"
        kitchen.isActive = false
        kitchen.openCashDrawerOnReceipt = true
        kitchen.assignedStationIds = ["HOT_KITCHEN"]
        try await api.savePrinter(kitchen, isNew: false)
        #expect(try await api.printers().first { $0.id == kitchen.id } == kitchen)
        let names = try await api.printers().map(\.name)
        #expect(names == names.sorted())
    }

    @Test func savePrinterNormalizesLikeTheServer() async throws {
        let api = try await makeLocalAPI()
        try await api.savePrinter(Printer(name: "  Caisse  ", ipAddress: " 10.0.0.9 ", port: 0, paperWidthMm: 72), isNew: true)
        let stored = try #require(try await api.printers().first { $0.name == "Caisse" })
        #expect(stored.ipAddress == "10.0.0.9" && stored.port == 9100 && stored.paperWidthMm == 80 && stored.isActive)

        let inactive = Printer(name: "Inactive", ipAddress: "10.0.0.10", isActive: false)
        try await api.savePrinter(inactive, isNew: true)
        #expect(try await api.printers().first { $0.id == inactive.id }?.isActive == true)
    }

    @Test func savePrinterRefusals() async throws {
        let api = try await makeLocalAPI()
        let required = APIError.server(status: 400, message: "Le nom et l'adresse IP de l'imprimante sont requis.")
        await #expect(throws: required) { try await api.savePrinter(Printer(name: "  ", ipAddress: "10.0.0.1"), isNew: true) }
        await #expect(throws: required) { try await api.savePrinter(Printer(name: "X", ipAddress: ""), isNew: true) }
        await #expect(throws: Self.notFound) { try await api.savePrinter(Printer(name: "X", ipAddress: "10.0.0.1"), isNew: false) }
        let known = Printer(name: "Y", ipAddress: "10.0.0.2")
        try await api.savePrinter(known, isNew: true)
        await #expect(throws: APIError.server(status: 409, message: "Imprimante déjà enregistrée.")) { try await api.savePrinter(known, isNew: true) }
    }

    @Test func writingNeedsLoginButListingDoesNot() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        _ = try await api.printers()
        await #expect(throws: APIError.unauthorized) { try await api.savePrinter(Printer(name: "X", ipAddress: "10.0.0.1"), isNew: true) }
        await #expect(throws: APIError.unauthorized) { try await api.printerStatuses() }
        await #expect(throws: APIError.unauthorized) { try await api.testPrinter(id: UUID()) }
    }

    @Test func statusesJobsAndTestPrintAreInertUntilDirectPrinting() async throws {
        let api = try await makeLocalAPI()
        let printer = Printer(name: "Z", ipAddress: "10.0.0.3")
        try await api.savePrinter(printer, isNew: true)

        let status = try #require(try await api.printerStatuses().first { $0.printerId == printer.id })
        #expect(status.name == "Z" && status.isActive && status.isOnline == nil && status.pendingCount == 0 && status.failedCount == 0)
        #expect(try await api.printJobs(printerId: printer.id).isEmpty)
        await #expect(throws: Self.notFound) { try await api.printJobs(printerId: UUID()) }

        let test = try await api.testPrinter(id: printer.id)
        #expect(test.success == false && test.message == "L'impression directe n'est pas encore disponible en mode autonome.")
        await #expect(throws: Self.notFound) { try await api.testPrinter(id: UUID()) }
        await #expect(throws: APIError.notFound("Impression introuvable.")) { try await api.retryPrintJob(id: UUID()) }
        await #expect(throws: APIError.notFound("Impression introuvable.")) { try await api.cancelPrintJob(id: UUID()) }

        let waiter = try await makeLocalAPI(pin: "2468")
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await waiter.printJobs(printerId: printer.id) }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalPrinterTests 2>&1 | grep -E "✘|Test run" | head -4`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Écrire le dépôt**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPrinterRepository.swift` :
```swift
import Foundation

/// Configuration des imprimantes (`PrinterConfigurations`). Les stations affectées sont stockées en tableau JSON, comme EF Core.
struct LocalPrinterRepository {
    let db: SQLiteDatabase

    func all() throws -> [Printer] {
        try db.query("SELECT * FROM PrinterConfigurations ORDER BY Name, rowid").compactMap(Self.decode)
    }

    func printer(id: UUID) throws -> Printer? {
        try db.query("SELECT * FROM PrinterConfigurations WHERE Id = ?", [.uuid(id)]).first.flatMap(Self.decode)
    }

    func insert(_ p: Printer) throws {
        let now = Date()
        try db.run(
            """
            INSERT INTO PrinterConfigurations (Id, Name, IpAddress, Port, PaperWidthMm, OpenCashDrawerOnReceipt, TextMode, AssignedStationIds,
                IsActive, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(p.id), .text(p.name), .text(p.ipAddress), .integer(p.port), .integer(p.paperWidthMm), .bool(p.openCashDrawerOnReceipt),
             .bool(p.textMode), .text(Self.encode(stations: p.assignedStationIds)), .bool(p.isActive), .date(now), .date(now)]
        )
    }

    /// `false` si l'identifiant est inconnu.
    func update(_ p: Printer) throws -> Bool {
        try db.run(
            """
            UPDATE PrinterConfigurations SET Name = ?, IpAddress = ?, Port = ?, PaperWidthMm = ?, OpenCashDrawerOnReceipt = ?, TextMode = ?,
                AssignedStationIds = ?, IsActive = ?, UpdatedAtUtc = ?
            WHERE Id = ?
            """,
            [.text(p.name), .text(p.ipAddress), .integer(p.port), .integer(p.paperWidthMm), .bool(p.openCashDrawerOnReceipt), .bool(p.textMode),
             .text(Self.encode(stations: p.assignedStationIds)), .bool(p.isActive), .date(Date()), .uuid(p.id)]
        ) > 0
    }

    private static func encode(stations: [String]) -> String {
        let encoder = JSONEncoder()
        encoder.outputFormatting = .withoutEscapingSlashes
        return (try? encoder.encode(stations)).flatMap { String(data: $0, encoding: .utf8) } ?? "[]"
    }

    private static func decode(_ row: SQLRow) -> Printer? {
        guard let id = row.uuid("Id") else { return nil }
        let stations = row.string("AssignedStationIds").flatMap { try? JSONDecoder().decode([String].self, from: Data($0.utf8)) } ?? []
        return Printer(
            id: id, name: row.string("Name") ?? "", ipAddress: row.string("IpAddress") ?? "", port: row.int("Port") ?? 9100,
            paperWidthMm: row.int("PaperWidthMm") ?? 80, openCashDrawerOnReceipt: row.bool("OpenCashDrawerOnReceipt"), assignedStationIds: stations,
            isActive: row.bool("IsActive"), textMode: row.bool("TextMode")
        )
    }
}
```

Dans `LocalPosAPI.swift`, remplacer :
```swift
    var hotelRepository: LocalHotelRepository { LocalHotelRepository(db: db) }
```
par :
```swift
    var hotelRepository: LocalHotelRepository { LocalHotelRepository(db: db) }
    var printerRepository: LocalPrinterRepository { LocalPrinterRepository(db: db) }
```

- [ ] **Step 4: Implémenter l'API**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Printers.swift` :
```swift
import Foundation

extension LocalPosAPI {
    static let printerNotFound = APIError.notFound("Imprimante introuvable.")

    /// Liste anonyme, comme `GET /api/printers`.
    public func printers() async throws -> [Printer] { try printerRepository.all() }

    /// Création (`isNew`) ou mise à jour. Comme le serveur : nom et adresse nettoyés, port 9100 par défaut, papier 58 ou 80 mm (sinon 80),
    /// imprimante créée active. Écarts : nom ou IP vide refusé en 400 (le .NET lève une exception), identifiant inconnu en 404.
    public func savePrinter(_ printer: Printer, isNew: Bool) async throws {
        try requireAuth()
        let name = printer.name.trimmingCharacters(in: .whitespacesAndNewlines)
        let address = printer.ipAddress.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, !address.isEmpty else {
            throw APIError.server(status: 400, message: "Le nom et l'adresse IP de l'imprimante sont requis.")
        }
        var clean = printer
        clean.name = name
        clean.ipAddress = address
        clean.port = printer.port > 0 ? printer.port : 9100
        clean.paperWidthMm = [58, 80].contains(printer.paperWidthMm) ? printer.paperWidthMm : 80
        if isNew {
            clean.isActive = true
            guard try printerRepository.printer(id: clean.id) == nil else { throw APIError.server(status: 409, message: "Imprimante déjà enregistrée.") }
            try printerRepository.insert(clean)
        } else {
            guard try printerRepository.update(clean) else { throw Self.printerNotFound }
        }
    }

    /// L'envoi direct arrive avec le sous-projet « impression » : le test répond clairement qu'il n'est pas disponible.
    public func testPrinter(id: UUID) async throws -> TestPrintResult {
        try requireAuth()
        guard try printerRepository.printer(id: id) != nil else { throw Self.printerNotFound }
        return TestPrintResult(success: false, message: "L'impression directe n'est pas encore disponible en mode autonome.")
    }

    /// Aucune file d'impression locale : ni état en ligne, ni impression en attente ou en échec.
    public func printerStatuses() async throws -> [PrinterStatus] {
        try requireAuth()
        return try printerRepository.all().map { PrinterStatus(printerId: $0.id, name: $0.name, isActive: $0.isActive) }
    }

    public func printJobs(printerId: UUID) async throws -> [PrintJobInfo] {
        try requireManager()
        guard try printerRepository.printer(id: printerId) != nil else { throw Self.printerNotFound }
        return []
    }

    public func retryPrintJob(id: UUID) async throws {
        try requireManager()
        throw APIError.notFound("Impression introuvable.")
    }

    public func cancelPrintJob(id: UUID) async throws {
        try requireManager()
        throw APIError.notFound("Impression introuvable.")
    }
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Imprimantes (retiré par la tâche 4)` : ce commentaire, les 7 lignes `printers`, `savePrinter`, `testPrinter`, `printerStatuses`, `printJobs`, `retryPrintJob`, `cancelPrintJob`, et la ligne vide qui suit.

- [ ] **Step 5: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalPrinterTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 5 tests … passed`.

- [ ] **Step 6: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"` → Expected: `305 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1d-progress.md` (tâche 4 `fait`, PosKit `305`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1d-progress.md
git commit -m "feat(ios-local): configuration des imprimantes sur SQLite (sans impression directe)"
```

---

### Task 5: Happy Hour — plannings et règles

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalTimeOfDay.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalHappyHourRepository.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+HappyHourSchedules.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalHappyHourScheduleTests.swift`

**Interfaces:**
- Consumes : tables Happy Hour (tâche 1), `requireManager()`, `HappyHourSchedule`, `HappyHourRule`, `BatchPriceRulesRequest`, `Money`.
- Produces :
  - `enum LocalTimeOfDay { static func seconds(from: String) -> Int?; static func stored(_: Int) -> String; static func display(_: Int) -> String }`.
  - `struct LocalHappyHourRepository { let db; schedules() -> [HappyHourSchedule]; schedule(id:) -> HappyHourSchedule?; insert(_:) -> UUID; delete(id:) -> Bool; upsertRule(scheduleId:targetType:targetId:targetName:mode:fixedPrice:discountPercent:); deleteRules(scheduleId:ids:) -> Int; targetName(type:id:) -> String?; activeOverride(terminalId:now:) -> (startsAt: Date, expiresAt: Date)?; deactivateOverrides(terminalId:); insertOverride(terminalId:operatorId:operatorName:startsAt:expiresAt:reason:) }`.
  - `LocalPosAPI.happyHourRepository`.
  - Méthodes `PosAPI` `happyHourSchedules()`, `createHappyHourSchedule(_:)`, `deleteHappyHourSchedule(id:)`, `applyHappyHourRules(scheduleId:_:)`, `deleteHappyHourRules(scheduleId:ruleIds:)`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalHappyHourScheduleTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : plannings Happy Hour")
struct LocalHappyHourScheduleTests {
    private static let unknownSchedule = APIError.server(status: 400, message: "Planning Happy Hour introuvable.")

    private func afterwork(rules: [HappyHourRule] = []) -> HappyHourSchedule {
        HappyHourSchedule(name: "Afterwork", daysOfWeek: [1, 2, 3, 4, 5], startTime: "17:00", endTime: "20:00", priceRules: rules)
    }

    private func newSchedule(_ api: LocalPosAPI, rules: [HappyHourRule] = []) async throws -> UUID {
        guard let id = try await api.createHappyHourSchedule(afterwork(rules: rules)) else { throw APIError.notFound("planning") }
        return id
    }

    private func schedule(_ api: LocalPosAPI, _ id: UUID) async throws -> HappyHourSchedule {
        guard let found = try await api.happyHourSchedules().first(where: { $0.id == id }) else { throw APIError.notFound("planning") }
        return found
    }

    @Test func createListAndDeleteASchedule() async throws {
        let api = try await makeLocalAPI()
        let ipa = try await localProduct(api, "Bière Artisanale IPA 33cl")
        let id = try await newSchedule(api, rules: [
            HappyHourRule(targetType: .product, targetId: ipa.id.uuidString.lowercased(), targetName: ipa.name, pricingMode: .fixedPrice, fixedPrice: Money(cents: 500)),
            HappyHourRule(targetType: .category, targetId: "CAT_DRINKS", targetName: "Boissons & Vins", pricingMode: .percentageDiscount, discountPercent: 20),
        ])

        let created = try await schedule(api, id)
        #expect(created.name == "Afterwork" && created.daysOfWeek.sorted() == [1, 2, 3, 4, 5] && created.startTime == "17:00" && created.endTime == "20:00")
        #expect(created.isActive && !created.appliesToTakeaway && created.priority == 1 && created.priceRules.count == 2)
        let fixed = try #require(created.priceRules.first { $0.targetType == .product })
        #expect(fixed.fixedPrice == Money(cents: 500) && fixed.discountPercent == nil && fixed.pricingMode == .fixedPrice && fixed.id != nil)
        let percent = try #require(created.priceRules.first { $0.targetType == .category })
        #expect(percent.discountPercent == 20 && percent.fixedPrice == nil && percent.pricingMode == .percentageDiscount)

        try await api.deleteHappyHourSchedule(id: id)
        #expect(try await api.happyHourSchedules().contains { $0.id == id } == false)
        await #expect(throws: APIError.notFound("Plage horaire introuvable.")) { try await api.deleteHappyHourSchedule(id: id) }
    }

    @Test func createRejectsInvalidSchedules() async throws {
        let api = try await makeLocalAPI()
        let badTime = APIError.server(status: 400, message: "Format horaire invalide (HH:mm requis).")
        for (start, end) in [("25:00", "20:00"), ("17:00", "abc"), ("", "20:00"), ("17:60", "20:00"), ("17", "20:00"), ("17:00", "24:00")] {
            await #expect(throws: badTime) {
                try await api.createHappyHourSchedule(HappyHourSchedule(name: "X", daysOfWeek: [1], startTime: start, endTime: end))
            }
        }
        await #expect(throws: APIError.server(status: 400, message: "Le nom de la plage est requis.")) {
            try await api.createHappyHourSchedule(HappyHourSchedule(name: "  ", daysOfWeek: [1], startTime: "17:00", endTime: "20:00"))
        }
        await #expect(throws: APIError.server(status: 400, message: "Jour de la semaine invalide (0 à 6).")) {
            try await api.createHappyHourSchedule(HappyHourSchedule(name: "X", daysOfWeek: [7], startTime: "17:00", endTime: "20:00"))
        }
    }

    @Test func applyRulesCreatesThenReplacesRulesOfTheSameTarget() async throws {
        let api = try await makeLocalAPI()
        let ipa = try await localProduct(api, "Bière Artisanale IPA 33cl")
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let id = try await newSchedule(api)

        let products = try await api.applyHappyHourRules(scheduleId: id, BatchPriceRulesRequest(
            targetType: .product, targetIds: [ipa.id.uuidString.lowercased(), burger.id.uuidString.lowercased()],
            pricingMode: .fixedPrice, fixedPrice: Money(cents: 500), discountPercent: nil))
        let categories = try await api.applyHappyHourRules(scheduleId: id, BatchPriceRulesRequest(
            targetType: .category, targetIds: ["CAT_DRINKS"], pricingMode: .percentageDiscount, fixedPrice: nil, discountPercent: 20))
        #expect(products == 2 && categories == 1)

        var current = try await schedule(api, id)
        #expect(current.priceRules.count == 3)
        #expect(current.priceRules.first { $0.targetType == .category }?.targetName == "Boissons & Vins")
        #expect(Set(current.priceRules.filter { $0.targetType == .product }.map(\.targetName)) == [ipa.name, burger.name])

        _ = try await api.applyHappyHourRules(scheduleId: id, BatchPriceRulesRequest(
            targetType: .product, targetIds: [ipa.id.uuidString.uppercased()], pricingMode: .percentageDiscount, fixedPrice: nil, discountPercent: 15))
        current = try await schedule(api, id)
        #expect(current.priceRules.count == 3)
        let replaced = try #require(current.priceRules.first { $0.targetName == ipa.name })
        #expect(replaced.pricingMode == .percentageDiscount && replaced.discountPercent == 15 && replaced.fixedPrice == nil)
    }

    @Test func applyRulesRefusals() async throws {
        let api = try await makeLocalAPI()
        let id = try await newSchedule(api)
        func request(_ mode: HappyHourPricingMode, targets: [String] = ["CAT_DRINKS"], fixed: Int? = nil, percent: Decimal? = nil) -> BatchPriceRulesRequest {
            BatchPriceRulesRequest(targetType: .category, targetIds: targets, pricingMode: mode, fixedPrice: fixed.map { Money(cents: $0) }, discountPercent: percent)
        }
        await #expect(throws: Self.unknownSchedule) { try await api.applyHappyHourRules(scheduleId: UUID(), request(.fixedPrice, fixed: 500)) }
        await #expect(throws: APIError.server(status: 400, message: "Aucun élément sélectionné.")) {
            try await api.applyHappyHourRules(scheduleId: id, request(.fixedPrice, targets: [], fixed: 500))
        }
        let fixedPrice = APIError.server(status: 400, message: "Le prix fixe doit être strictement supérieur à 0.")
        await #expect(throws: fixedPrice) { try await api.applyHappyHourRules(scheduleId: id, request(.fixedPrice)) }
        await #expect(throws: fixedPrice) { try await api.applyHappyHourRules(scheduleId: id, request(.fixedPrice, fixed: 0)) }
        let percentRange = APIError.server(status: 400, message: "Le pourcentage de remise doit être compris entre 0.01% et 100%.")
        await #expect(throws: percentRange) { try await api.applyHappyHourRules(scheduleId: id, request(.percentageDiscount)) }
        await #expect(throws: percentRange) { try await api.applyHappyHourRules(scheduleId: id, request(.percentageDiscount, percent: 0)) }
        await #expect(throws: percentRange) { try await api.applyHappyHourRules(scheduleId: id, request(.percentageDiscount, percent: 101)) }
        #expect(try await schedule(api, id).priceRules.isEmpty)
    }

    @Test func deleteRulesRemovesOnlyTheGivenOnes() async throws {
        let api = try await makeLocalAPI()
        let id = try await newSchedule(api)
        _ = try await api.applyHappyHourRules(scheduleId: id, BatchPriceRulesRequest(
            targetType: .category, targetIds: ["CAT_DRINKS", "CAT_MAINS", "CAT_PIZZAS"], pricingMode: .percentageDiscount, fixedPrice: nil, discountPercent: 10))
        let rules = try await schedule(api, id).priceRules
        #expect(rules.count == 3)
        let toDelete = rules.prefix(2).compactMap(\.id) + [UUID()]
        try await api.deleteHappyHourRules(scheduleId: id, ruleIds: toDelete)
        let remaining = try await schedule(api, id).priceRules
        #expect(remaining.count == 1 && remaining.first?.id == rules.last?.id)

        await #expect(throws: APIError.server(status: 400, message: "Aucune règle sélectionnée pour la suppression.")) {
            try await api.deleteHappyHourRules(scheduleId: id, ruleIds: [])
        }
        await #expect(throws: Self.unknownSchedule) { try await api.deleteHappyHourRules(scheduleId: UUID(), ruleIds: [UUID()]) }
    }

    @Test func administrationNeedsAManagerButReadingDoesNot() async throws {
        let waiter = try await makeLocalAPI(pin: "2468")
        let notManager = APIError.forbidden("Action réservée à un responsable.")
        _ = try await waiter.happyHourSchedules()
        await #expect(throws: notManager) { try await waiter.createHappyHourSchedule(afterwork()) }
        await #expect(throws: notManager) { try await waiter.deleteHappyHourSchedule(id: UUID()) }
        await #expect(throws: notManager) {
            try await waiter.applyHappyHourRules(scheduleId: UUID(), BatchPriceRulesRequest(targetType: .category, targetIds: ["X"], pricingMode: .fixedPrice, fixedPrice: Money(cents: 1), discountPercent: nil))
        }
        await #expect(throws: notManager) { try await waiter.deleteHappyHourRules(scheduleId: UUID(), ruleIds: [UUID()]) }
        let anonymous = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await anonymous.createHappyHourSchedule(afterwork()) }
    }

    @Test func storageFormatMatchesTheServer() async throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        let api = try LocalPosAPI(path: path)
        _ = try await api.login(pin: "1234")
        let id = try await newSchedule(api, rules: [
            HappyHourRule(targetType: .category, targetId: "CAT_DRINKS", targetName: "Boissons & Vins", pricingMode: .percentageDiscount, discountPercent: 20),
            HappyHourRule(targetType: .product, targetId: UUID().uuidString.lowercased(), targetName: "Produit", pricingMode: .fixedPrice, fixedPrice: Money(cents: 500)),
        ])

        let raw = try SQLiteDatabase(path: path)
        let row = try #require(try raw.query("SELECT * FROM HappyHourSchedules WHERE Id = ?", [.uuid(id)]).first)
        #expect(row.string("DaysOfWeek") == "1,2,3,4,5" && row.string("StartTime") == "17:00:00" && row.string("EndTime") == "20:00:00")
        #expect(row.int("IsActive") == 1 && row.int("AppliesToTakeaway") == 0 && row.int("Priority") == 1)
        let rules = try raw.query("SELECT * FROM HappyHourPriceRules WHERE ScheduleId = ? ORDER BY rowid", [.uuid(id)])
        #expect(rules.count == 2)
        #expect(rules[0].int("TargetType") == 1 && rules[0].int("PricingMode") == 1 && rules[0].string("DiscountPercent") == "20" && rules[0].int("FixedPrice") == nil)
        #expect(rules[1].int("TargetType") == 0 && rules[1].int("PricingMode") == 0 && rules[1].int("FixedPrice") == 500 && rules[1].string("DiscountPercent") == nil)

        try await api.deleteHappyHourSchedule(id: id)
        #expect(try raw.query("SELECT COUNT(*) AS n FROM HappyHourPriceRules WHERE ScheduleId = ?", [.uuid(id)]).first?.int("n") == 0)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalHappyHourScheduleTests 2>&1 | grep -E "✘|Test run" | head -4`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Écrire les heures et le dépôt**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalTimeOfDay.swift` :
```swift
import Foundation

/// Heures d'un planning : saisie `HH:mm` ou `HH:mm:ss`, stockage `HH:mm:ss` (comme `TimeOnly` d'EF Core), affichage `HH:mm`.
enum LocalTimeOfDay {
    /// Secondes depuis minuit ; `nil` si le texte n'est pas une heure valide (0 à 23 h, 0 à 59 min, 0 à 59 s).
    static func seconds(from text: String) -> Int? {
        let parts = text.split(separator: ":", omittingEmptySubsequences: false)
        guard parts.count == 2 || parts.count == 3 else { return nil }
        let numbers = parts.compactMap { part -> Int? in
            guard (1...2).contains(part.count), part.allSatisfy(\.isASCII), part.allSatisfy(\.isNumber) else { return nil }
            return Int(part)
        }
        guard numbers.count == parts.count, numbers[0] < 24, numbers[1] < 60 else { return nil }
        let seconds = numbers.count == 3 ? numbers[2] : 0
        guard seconds < 60 else { return nil }
        return numbers[0] * 3600 + numbers[1] * 60 + seconds
    }

    static func stored(_ seconds: Int) -> String {
        String(format: "%02d:%02d:%02d", seconds / 3600, seconds % 3600 / 60, seconds % 60)
    }

    static func display(_ seconds: Int) -> String {
        String(format: "%02d:%02d", seconds / 3600, seconds % 3600 / 60)
    }
}
```

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalHappyHourRepository.swift` :
```swift
import Foundation

/// Plannings Happy Hour, règles tarifaires et dérogations de responsable (tables de `AppDbContext`).
struct LocalHappyHourRepository {
    let db: SQLiteDatabase

    // MARK: Plannings

    func schedules() throws -> [HappyHourSchedule] {
        try db.query("SELECT * FROM HappyHourSchedules ORDER BY rowid").map(hydrate)
    }

    func schedule(id: UUID) throws -> HappyHourSchedule? {
        try db.query("SELECT * FROM HappyHourSchedules WHERE Id = ?", [.uuid(id)]).first.map(hydrate)
    }

    /// Insère le planning et ses règles. À appeler dans une transaction.
    @discardableResult
    func insert(_ s: HappyHourSchedule) throws -> UUID {
        guard let start = LocalTimeOfDay.seconds(from: s.startTime), let end = LocalTimeOfDay.seconds(from: s.endTime) else {
            throw SQLiteError(code: -1, message: "Heure de planning invalide : \(s.startTime) - \(s.endTime)")
        }
        let id = s.id ?? UUID()
        try db.run(
            """
            INSERT INTO HappyHourSchedules (Id, Name, DaysOfWeek, StartTime, EndTime, IsActive, AppliesToTakeaway, Priority, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL)
            """,
            [.uuid(id), .text(s.name), .text(s.daysOfWeek.map(String.init).joined(separator: ",")), .text(LocalTimeOfDay.stored(start)),
             .text(LocalTimeOfDay.stored(end)), .bool(s.isActive), .bool(s.appliesToTakeaway), .integer(s.priority), .date(Date())]
        )
        for rule in s.priceRules { try insertRule(rule, scheduleId: id) }
        return id
    }

    /// `false` si le planning est inconnu. Les règles sont supprimées en cascade.
    func delete(id: UUID) throws -> Bool {
        try db.run("DELETE FROM HappyHourSchedules WHERE Id = ?", [.uuid(id)]) > 0
    }

    // MARK: Règles

    func insertRule(_ r: HappyHourRule, scheduleId: UUID) throws {
        try db.run(
            """
            INSERT INTO HappyHourPriceRules (Id, ScheduleId, TargetType, TargetId, TargetName, PricingMode, FixedPrice, DiscountPercent, CreatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(r.id ?? UUID()), .uuid(scheduleId), .integer(r.targetType.rawValue), .text(r.targetId), .text(r.targetName),
             .integer(r.pricingMode.rawValue), .integer(r.fixedPrice?.cents), .decimal(r.discountPercent), .date(Date())]
        )
    }

    /// Remplace la règle de même cible (identifiant comparé sans tenir compte de la casse), sinon en crée une. À appeler dans une transaction.
    func upsertRule(scheduleId: UUID, targetType: HappyHourTargetType, targetId: String, targetName: String, mode: HappyHourPricingMode,
                    fixedPrice: Money?, discountPercent: Decimal?) throws {
        let existing = try db.query(
            "SELECT Id FROM HappyHourPriceRules WHERE ScheduleId = ? AND TargetType = ? AND lower(TargetId) = lower(?)",
            [.uuid(scheduleId), .integer(targetType.rawValue), .text(targetId)]
        ).first?.uuid("Id")
        if let existing {
            try db.run(
                "UPDATE HappyHourPriceRules SET TargetName = ?, PricingMode = ?, FixedPrice = ?, DiscountPercent = ? WHERE Id = ?",
                [.text(targetName), .integer(mode.rawValue), .integer(fixedPrice?.cents), .decimal(discountPercent), .uuid(existing)]
            )
        } else {
            try insertRule(
                HappyHourRule(targetType: targetType, targetId: targetId, targetName: targetName, pricingMode: mode, fixedPrice: fixedPrice, discountPercent: discountPercent),
                scheduleId: scheduleId
            )
        }
    }

    /// Nombre de règles supprimées ; les identifiants inconnus sont ignorés.
    func deleteRules(scheduleId: UUID, ids: [UUID]) throws -> Int {
        guard !ids.isEmpty else { return 0 }
        let marks = Array(repeating: "?", count: ids.count).joined(separator: ",")
        return try db.run("DELETE FROM HappyHourPriceRules WHERE ScheduleId = ? AND Id IN (\(marks))", [.uuid(scheduleId)] + ids.map { SQLValue.uuid($0) })
    }

    /// Nom affiché d'un produit (identifiant UUID) ou d'une catégorie ; `nil` si la cible est inconnue.
    func targetName(type: HappyHourTargetType, id: String) throws -> String? {
        switch type {
        case .product:
            guard let uuid = UUID(uuidString: id) else { return nil }
            return try db.query("SELECT Name FROM Products WHERE Id = ?", [.uuid(uuid)]).first?.string("Name")
        case .category:
            return try db.query("SELECT Name FROM Categories WHERE Id = ?", [.text(id)]).first?.string("Name")
        }
    }

    // MARK: Dérogations

    /// Dérogation active et non expirée du terminal (la plus longue si plusieurs).
    func activeOverride(terminalId: String, now: Date) throws -> (startsAt: Date, expiresAt: Date)? {
        try db.query("SELECT StartsAtUtc, ExpiresAtUtc FROM HappyHourOverrideSessions WHERE TerminalId = ? AND IsActive = 1", [.text(terminalId)])
            .compactMap { row -> (startsAt: Date, expiresAt: Date)? in
                guard let starts = row.date("StartsAtUtc"), let expires = row.date("ExpiresAtUtc"), expires > now else { return nil }
                return (startsAt: starts, expiresAt: expires)
            }
            .max { $0.expiresAt < $1.expiresAt }
    }

    func deactivateOverrides(terminalId: String) throws {
        try db.run("UPDATE HappyHourOverrideSessions SET IsActive = 0 WHERE TerminalId = ? AND IsActive = 1", [.text(terminalId)])
    }

    func insertOverride(terminalId: String, operatorId: UUID, operatorName: String, startsAt: Date, expiresAt: Date, reason: String) throws {
        try db.run(
            """
            INSERT INTO HappyHourOverrideSessions (Id, TerminalId, OperatorId, OperatorName, OverrideType, StartsAtUtc, ExpiresAtUtc, Reason, IsActive, CreatedAtUtc)
            VALUES (?, ?, ?, ?, 0, ?, ?, ?, 1, ?)
            """,
            [.uuid(UUID()), .text(terminalId), .uuid(operatorId), .text(operatorName), .date(startsAt), .date(expiresAt), .text(reason), .date(Date())]
        )
    }

    // MARK: Lecture

    private func hydrate(_ row: SQLRow) throws -> HappyHourSchedule {
        guard let id = row.uuid("Id") else { throw SQLiteError(code: -1, message: "Planning Happy Hour sans identifiant") }
        let rules = try db.query("SELECT * FROM HappyHourPriceRules WHERE ScheduleId = ? ORDER BY rowid", [.uuid(id)]).map { r in
            HappyHourRule(
                id: r.uuid("Id"), targetType: HappyHourTargetType(rawValue: r.int("TargetType") ?? 0) ?? .fallback, targetId: r.string("TargetId") ?? "",
                targetName: r.string("TargetName") ?? "", pricingMode: HappyHourPricingMode(rawValue: r.int("PricingMode") ?? 0) ?? .fallback,
                fixedPrice: r.int("FixedPrice").map { Money(cents: $0) }, discountPercent: r.decimal("DiscountPercent")
            )
        }
        return HappyHourSchedule(
            id: id, name: row.string("Name") ?? "", daysOfWeek: (row.string("DaysOfWeek") ?? "").split(separator: ",").compactMap { Int($0) },
            startTime: LocalTimeOfDay.display(LocalTimeOfDay.seconds(from: row.string("StartTime") ?? "") ?? 0),
            endTime: LocalTimeOfDay.display(LocalTimeOfDay.seconds(from: row.string("EndTime") ?? "") ?? 0),
            isActive: row.bool("IsActive"), appliesToTakeaway: row.bool("AppliesToTakeaway"), priority: row.int("Priority") ?? 1, priceRules: rules
        )
    }
}
```

Dans `LocalPosAPI.swift`, remplacer :
```swift
    var printerRepository: LocalPrinterRepository { LocalPrinterRepository(db: db) }
```
par :
```swift
    var printerRepository: LocalPrinterRepository { LocalPrinterRepository(db: db) }
    var happyHourRepository: LocalHappyHourRepository { LocalHappyHourRepository(db: db) }
```

- [ ] **Step 4: Implémenter l'administration**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+HappyHourSchedules.swift` :
```swift
import Foundation

extension LocalPosAPI {
    private static let unknownSchedule = APIError.server(status: 400, message: "Planning Happy Hour introuvable.")

    /// Lecture ouverte, comme `GET /api/happy-hour/schedules`.
    public func happyHourSchedules() async throws -> [HappyHourSchedule] { try happyHourRepository.schedules() }

    /// Écart de sécurité assumé : le .NET laisse la création anonyme, l'iPad la réserve aux responsables.
    public func createHappyHourSchedule(_ schedule: HappyHourSchedule) async throws -> UUID? {
        try requireManager()
        guard !schedule.name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw APIError.server(status: 400, message: "Le nom de la plage est requis.")
        }
        guard LocalTimeOfDay.seconds(from: schedule.startTime) != nil, LocalTimeOfDay.seconds(from: schedule.endTime) != nil else {
            throw APIError.server(status: 400, message: "Format horaire invalide (HH:mm requis).")
        }
        guard schedule.daysOfWeek.allSatisfy({ (0...6).contains($0) }) else {
            throw APIError.server(status: 400, message: "Jour de la semaine invalide (0 à 6).")
        }
        let repository = happyHourRepository
        return try db.transaction { try repository.insert(schedule) }
    }

    public func deleteHappyHourSchedule(id: UUID) async throws {
        try requireManager()
        guard try happyHourRepository.delete(id: id) else { throw APIError.notFound("Plage horaire introuvable.") }
    }

    /// Pose ou remplace une règle par cible. Retourne le nombre de cibles traitées. Tous les refus précèdent les écritures.
    public func applyHappyHourRules(scheduleId: UUID, _ request: BatchPriceRulesRequest) async throws -> Int {
        try requireManager()
        let repository = happyHourRepository
        return try db.transaction { () throws -> Int in
            guard try repository.schedule(id: scheduleId) != nil else { throw Self.unknownSchedule }
            guard !request.targetIds.isEmpty else { throw APIError.server(status: 400, message: "Aucun élément sélectionné.") }
            var fixed: Money?
            var percent: Decimal?
            switch request.pricingMode {
            case .fixedPrice:
                guard let price = request.fixedPrice, price.cents > 0 else {
                    throw APIError.server(status: 400, message: "Le prix fixe doit être strictement supérieur à 0.")
                }
                fixed = price
            case .percentageDiscount:
                guard let value = request.discountPercent, value > 0, value <= 100 else {
                    throw APIError.server(status: 400, message: "Le pourcentage de remise doit être compris entre 0.01% et 100%.")
                }
                percent = value
            }
            for target in request.targetIds {
                let name = try repository.targetName(type: request.targetType, id: target) ?? target
                try repository.upsertRule(
                    scheduleId: scheduleId, targetType: request.targetType, targetId: target, targetName: name,
                    mode: request.pricingMode, fixedPrice: fixed, discountPercent: percent
                )
            }
            return request.targetIds.count
        }
    }

    public func deleteHappyHourRules(scheduleId: UUID, ruleIds: [UUID]) async throws {
        try requireManager()
        let repository = happyHourRepository
        try db.transaction {
            guard try repository.schedule(id: scheduleId) != nil else { throw Self.unknownSchedule }
            guard !ruleIds.isEmpty else { throw APIError.server(status: 400, message: "Aucune règle sélectionnée pour la suppression.") }
            _ = try repository.deleteRules(scheduleId: scheduleId, ids: ruleIds)
        }
    }
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Happy Hour, plannings (retiré par la tâche 5)` : ce commentaire, les 5 lignes `happyHourSchedules`, `createHappyHourSchedule`, `deleteHappyHourSchedule`, `applyHappyHourRules`, `deleteHappyHourRules`, et la ligne vide qui suit.

- [ ] **Step 5: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalHappyHourScheduleTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 7 tests … passed`.

- [ ] **Step 6: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"` → Expected: `312 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1d-progress.md` (tâche 5 `fait`, PosKit `312`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1d-progress.md
git commit -m "feat(ios-local): plannings et règles Happy Hour sur SQLite"
```

---

### Task 6: Happy Hour — statut, grille tarifaire, dérogations

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalHappyHourPricing.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+HappyHour.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalHappyHourStatusTests.swift`

**Interfaces:**
- Consumes (tâches 1, 2, 5) : `clock`, `calendar`, `LocalPosAPI.standaloneTerminalId`, `happyHourRepository`, `catalogRepository.activeProducts()`, `staffRepository.activeMember(pin:)`, `ensurePinAttemptsAllowed()`, `recordFailedPin()`, `resetFailedPins()`, `LocalTimeOfDay`, `HappyHourStatus`, `HappyHourWindow`, `HappyHourPricingTable`, `HappyHourPrice`, `OperationResult`.
- Produces : `enum LocalHappyHourPricing { static func price(productId:categoryId:standard:rules:) -> (effective: Money, isHappyHour: Bool); static func ruleType(productId:categoryId:rules:) -> String }` ; `LocalPosAPI.currentHappyHourStatus(now:)`, `.authenticateSupervisor(pin:)` ; méthodes `PosAPI` `happyHourStatus(terminalId:)`, `happyHourPricing(terminalId:)`, `activateHappyHourOverride(terminalId:pin:minutes:reason:)`, `stopHappyHourOverride(terminalId:pin:reason:)`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalHappyHourStatusTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : Happy Hour (statut, tarifs, dérogations)")
struct LocalHappyHourStatusTests {
    private static let insufficient = APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")

    private func fixedRule(_ product: Product, cents: Int) -> HappyHourRule {
        HappyHourRule(targetType: .product, targetId: product.id.uuidString.lowercased(), targetName: product.name, pricingMode: .fixedPrice, fixedPrice: Money(cents: cents))
    }

    private func clearSchedules(_ api: LocalPosAPI) async throws {
        for schedule in try await api.happyHourSchedules() {
            if let id = schedule.id { try await api.deleteHappyHourSchedule(id: id) }
        }
    }

    @discardableResult
    private func addSchedule(_ api: LocalPosAPI, _ name: String, days: [Int] = [1, 2, 3, 4, 5], start: String = "17:00", end: String = "20:00",
                             priority: Int = 1, isActive: Bool = true, rules: [HappyHourRule] = []) async throws -> UUID {
        let schedule = HappyHourSchedule(name: name, daysOfWeek: days, startTime: start, endTime: end, isActive: isActive, priority: priority, priceRules: rules)
        guard let id = try await api.createHappyHourSchedule(schedule) else { throw APIError.notFound(name) }
        return id
    }

    @Test func statusInsideTheWindow() async throws {
        let clock = LocalTestClock()  // lundi 18 h 30 UTC
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        let id = try await addSchedule(api, "Afterwork")

        let status = try await api.happyHourStatus(terminalId: "T01")
        #expect(status.isActive && !status.isOverride && !status.appliesToTakeaway)
        #expect(status.activeScheduleName == "Afterwork" && status.activeScheduleId == id)
        #expect(status.currentWindow?.startTime == "17:00:00" && status.currentWindow?.endTime == "20:00:00" && status.currentWindow?.remainingMinutes == 90)
    }

    @Test func statusIsInactiveOutsideTheWindowTheDaysOrWhenDisabled() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        try await addSchedule(api, "Afterwork")
        let moments: [(String, Bool)] = [
            ("2026-10-12T16:59:59Z", false), ("2026-10-12T17:00:00Z", true), ("2026-10-12T19:59:59Z", true), ("2026-10-12T20:00:00Z", false),
            ("2026-10-11T18:30:00Z", false), ("2026-10-17T18:30:00Z", false),
        ]
        for (moment, expected) in moments {
            clock.set(moment)
            #expect(try await api.happyHourStatus(terminalId: "T01").isActive == expected, "\(moment)")
        }

        try await clearSchedules(api)
        try await addSchedule(api, "Éteint", isActive: false)
        clock.set("2026-10-12T18:30:00Z")
        #expect(try await api.happyHourStatus(terminalId: "T01").isActive == false)
    }

    @Test func higherPriorityWinsAndTiesKeepTheFirst() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        try await addSchedule(api, "Basse", priority: 1)
        try await addSchedule(api, "Haute", start: "18:00", end: "19:00", priority: 5)
        #expect(try await api.happyHourStatus(terminalId: "T01").activeScheduleName == "Haute")
        clock.set("2026-10-12T17:30:00Z")
        #expect(try await api.happyHourStatus(terminalId: "T01").activeScheduleName == "Basse")

        try await clearSchedules(api)
        try await addSchedule(api, "A", priority: 2)
        try await addSchedule(api, "B", priority: 2)
        #expect(try await api.happyHourStatus(terminalId: "T01").activeScheduleName == "A")
    }

    @Test func pricingTableAppliesProductThenCategoryRules() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        let ipa = try await localProduct(api, "Bière Artisanale IPA 33cl")
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let pizza = try await localProduct(api, "Pizza Margherita AOP")
        let bordeaux = try await localProduct(api, "Verre Bordeaux AOP 12cl")
        let eau = try await localProduct(api, "Eau Pétillante 50cl")
        try await addSchedule(api, "Afterwork", rules: [
            fixedRule(ipa, cents: 500),
            fixedRule(burger, cents: 2500),  // plus cher que le tarif normal : ignorée
            HappyHourRule(targetType: .product, targetId: pizza.id.uuidString, targetName: pizza.name, pricingMode: .percentageDiscount, discountPercent: 10),
            HappyHourRule(targetType: .category, targetId: "CAT_DRINKS", targetName: "Boissons & Vins", pricingMode: .percentageDiscount, discountPercent: 15),
        ])

        let table = try await api.happyHourPricing(terminalId: "T01")
        #expect(table.isActive && table.items.count == 4)
        func price(_ product: Product) -> HappyHourPrice? { table.items.first { $0.productId == product.id } }
        #expect(price(ipa)?.standardPrice == Money(cents: 600) && price(ipa)?.happyHourPrice == Money(cents: 500) && price(ipa)?.ruleType == "FixedPrice")
        #expect(price(burger) == nil)
        #expect(price(pizza)?.happyHourPrice == Money(cents: 1125) && price(pizza)?.ruleType == "CategoryDiscount")
        #expect(price(bordeaux)?.happyHourPrice == Money(cents: 468) && price(bordeaux)?.ruleType == "CategoryDiscount")  // 550 × 0,85 = 467,5 → 468
        #expect(price(eau)?.happyHourPrice == Money(cents: 383))  // 450 × 0,85 = 382,5 → 383

        clock.set("2026-10-12T21:00:00Z")
        let closed = try await api.happyHourPricing(terminalId: "T01")
        #expect(!closed.isActive && closed.items.isEmpty)

        try await clearSchedules(api)
        try await addSchedule(api, "Vide")
        clock.set("2026-10-12T18:30:00Z")
        let empty = try await api.happyHourPricing(terminalId: "T01")
        #expect(empty.isActive && empty.items.isEmpty)
    }

    @Test func supervisorOverrideStartsExtendsAndExpires() async throws {
        let clock = LocalTestClock("2026-10-14T10:00:00Z")  // mercredi, hors plage
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        #expect(try await api.happyHourStatus(terminalId: "T01").isActive == false)

        let started = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 30, reason: "Test")
        #expect(started.success == true && started.message == "Happy Hour activé/prolongé de 30 minutes avec succès.")
        var status = try await api.happyHourStatus(terminalId: "T01")
        #expect(status.isActive && status.isOverride && status.activeScheduleName == "Dérogation Responsable" && status.activeScheduleId == nil)
        #expect(status.currentWindow?.remainingMinutes == 30 && status.currentWindow?.startTime == "10:00:00" && status.currentWindow?.endTime == "10:30:00")
        clock.advance(29 * 60)
        #expect(try await api.happyHourStatus(terminalId: "T01").currentWindow?.remainingMinutes == 1)
        clock.advance(2 * 60)
        #expect(try await api.happyHourStatus(terminalId: "T01").isActive == false)

        // Une nouvelle dérogation remplace la précédente ; sans durée, elle dure 60 minutes.
        _ = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 90, reason: "")
        _ = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 10, reason: "")
        #expect(try await api.happyHourStatus(terminalId: "T01").currentWindow?.remainingMinutes == 10)
        _ = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 0, reason: "")
        status = try await api.happyHourStatus(terminalId: "T01")
        #expect(status.currentWindow?.remainingMinutes == 60)

        // Avec un planning actif dans la base, la dérogation en reprend le nom et les règles.
        let ipa = try await localProduct(api, "Bière Artisanale IPA 33cl")
        let scheduleId = try await addSchedule(api, "Mercredi", days: [3], rules: [fixedRule(ipa, cents: 500)])
        status = try await api.happyHourStatus(terminalId: "T01")
        #expect(status.isOverride && status.activeScheduleName == "Mercredi" && status.activeScheduleId == scheduleId)
        let table = try await api.happyHourPricing(terminalId: "T01")
        #expect(table.isActive && table.items.first { $0.productId == ipa.id }?.happyHourPrice == Money(cents: 500))

        let stopped = try await api.stopHappyHourOverride(terminalId: "T01", pin: "1234", reason: "Fin")
        #expect(stopped.success == true && stopped.message == "Dérogation Happy Hour désactivée.")
        #expect(try await api.happyHourStatus(terminalId: "T01").isActive == false)
    }

    @Test func overrideNeedsASupervisorPinAndSharesTheLockout() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        // Un serveur (PIN valide sans droit de responsable) est refusé sans compter comme échec.
        await #expect(throws: Self.insufficient) { try await api.activateHappyHourOverride(terminalId: "T01", pin: "2468", minutes: 30, reason: "") }
        await #expect(throws: Self.insufficient) { try await api.stopHappyHourOverride(terminalId: "T01", pin: "2468", reason: "") }
        #expect(try await api.happyHourStatus(terminalId: "T01").isActive == false)

        // Cinq PIN inconnus verrouillent aussi la connexion (compteur partagé), pendant 30 s.
        for _ in 0..<5 {
            await #expect(throws: Self.insufficient) { try await api.activateHappyHourOverride(terminalId: "T01", pin: "0000", minutes: 30, reason: "") }
        }
        await #expect(throws: APIError.rateLimited(nil)) { try await api.login(pin: "1234") }
        await #expect(throws: APIError.rateLimited(nil)) { try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 30, reason: "") }
        clock.advance(31)
        let result = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 30, reason: "")
        #expect(result.success == true)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalHappyHourStatusTests 2>&1 | grep -E "✘|Test run" | head -4`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Écrire le calcul des prix**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalHappyHourPricing.swift` :
```swift
import Foundation

/// Port de `HappyHourPricingService.CalculatePriceForRule` : prix fixe produit (seulement s'il est plus bas), remise produit,
/// remise catégorie, prix fixe catégorie (seulement s'il est plus bas). Remises arrondies « away from zero », jamais négatives.
enum LocalHappyHourPricing {
    static func price(productId: UUID, categoryId: String, standard: Money, rules: [HappyHourRule]) -> (effective: Money, isHappyHour: Bool) {
        func isProduct(_ rule: HappyHourRule) -> Bool {
            rule.targetType == .product && rule.targetId.caseInsensitiveCompare(productId.uuidString) == .orderedSame
        }
        func isCategory(_ rule: HappyHourRule) -> Bool { rule.targetType == .category && matchesCategory(rule, categoryId) }
        func discounted(_ percent: Decimal) -> Money { standard.multiplied(by: 1 - percent / 100).clampedAtZero() }

        if let fixed = rules.first(where: { isProduct($0) && $0.pricingMode == .fixedPrice && $0.fixedPrice != nil })?.fixedPrice, fixed < standard {
            return (fixed, true)
        }
        if let percent = rules.first(where: { isProduct($0) && $0.pricingMode == .percentageDiscount && ($0.discountPercent ?? 0) > 0 })?.discountPercent {
            return (discounted(percent), true)
        }
        if let percent = rules.first(where: { isCategory($0) && $0.pricingMode == .percentageDiscount && ($0.discountPercent ?? 0) > 0 })?.discountPercent {
            return (discounted(percent), true)
        }
        if let fixed = rules.first(where: { isCategory($0) && $0.pricingMode == .fixedPrice && $0.fixedPrice != nil })?.fixedPrice, fixed < standard {
            return (fixed, true)
        }
        return (standard, false)
    }

    /// Libellé du type de règle, repris tel quel du serveur : toute règle qui n'est pas un prix fixe s'appelle `CategoryDiscount`,
    /// même pour une remise propre à un produit.
    static func ruleType(productId: UUID, categoryId: String, rules: [HappyHourRule]) -> String {
        let rule = rules.first { $0.targetType == .product && $0.targetId.caseInsensitiveCompare(productId.uuidString) == .orderedSame }
            ?? rules.first { $0.targetType == .category && matchesCategory($0, categoryId) }
        return rule?.pricingMode == .fixedPrice ? "FixedPrice" : "CategoryDiscount"
    }

    private static func matchesCategory(_ rule: HappyHourRule, _ categoryId: String) -> Bool {
        rule.targetId.caseInsensitiveCompare(categoryId) == .orderedSame || rule.targetName.caseInsensitiveCompare(categoryId) == .orderedSame
    }
}
```

- [ ] **Step 4: Implémenter le statut, la grille et les dérogations**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+HappyHour.swift` :
```swift
import Foundation

extension LocalPosAPI {
    static let overrideFallbackName = "Dérogation Responsable"

    /// Dérogation active d'abord, sinon planning actif à l'heure locale de l'appareil : jour de la semaine, début inclus, fin exclue
    /// (pas de plage à cheval sur minuit, comme le serveur), priorité la plus haute, le premier créé à égalité.
    func currentHappyHourStatus(now: Date) throws -> HappyHourStatus {
        let repository = happyHourRepository
        let schedules = try repository.schedules()
        if let session = try repository.activeOverride(terminalId: Self.standaloneTerminalId, now: now) {
            let first = schedules.first { $0.isActive }
            return HappyHourStatus(
                isActive: true, isOverride: true, activeScheduleName: first?.name ?? Self.overrideFallbackName, activeScheduleId: first?.id,
                appliesToTakeaway: first?.appliesToTakeaway ?? false,
                currentWindow: HappyHourWindow(
                    startTime: Self.utcTime(session.startsAt), endTime: Self.utcTime(session.expiresAt),
                    remainingMinutes: Self.remainingMinutes(session.expiresAt.timeIntervalSince(now))
                )
            )
        }
        let parts = calendar.dateComponents([.weekday, .hour, .minute, .second], from: now)
        let day = (parts.weekday ?? 1) - 1
        let seconds = (parts.hour ?? 0) * 3600 + (parts.minute ?? 0) * 60 + (parts.second ?? 0)
        let candidates = schedules.compactMap { schedule -> (schedule: HappyHourSchedule, start: Int, end: Int)? in
            guard schedule.isActive, schedule.daysOfWeek.contains(day),
                  let start = LocalTimeOfDay.seconds(from: schedule.startTime), let end = LocalTimeOfDay.seconds(from: schedule.endTime),
                  start <= seconds, seconds < end
            else { return nil }
            return (schedule, start, end)
        }
        guard let best = candidates.max(by: { $0.schedule.priority < $1.schedule.priority }) else { return .inactive }
        return HappyHourStatus(
            isActive: true, isOverride: false, activeScheduleName: best.schedule.name, activeScheduleId: best.schedule.id,
            appliesToTakeaway: best.schedule.appliesToTakeaway,
            currentWindow: HappyHourWindow(
                startTime: LocalTimeOfDay.stored(best.start), endTime: LocalTimeOfDay.stored(best.end),
                remainingMinutes: Self.remainingMinutes(TimeInterval(best.end - seconds))
            )
        )
    }

    /// Lecture ouverte, comme `GET /api/happy-hour/status`. Le terminal demandé est ignoré (terminal fixe).
    public func happyHourStatus(terminalId: String) async throws -> HappyHourStatus {
        try currentHappyHourStatus(now: clock())
    }

    /// Produits dont le tarif baisse pendant la plage en cours. Le prix appliqué à une ligne est celui que le client envoie avec
    /// `isHappyHourApplied` (le serveur .NET ne le recalcule pas non plus).
    public func happyHourPricing(terminalId: String) async throws -> HappyHourPricingTable {
        let status = try currentHappyHourStatus(now: clock())
        guard status.isActive else { return HappyHourPricingTable(isActive: false, items: []) }
        let schedules = try happyHourRepository.schedules()
        let schedule = status.activeScheduleId.flatMap { id in schedules.first { $0.id == id } }
            ?? schedules.filter(\.isActive).max { $0.priority < $1.priority }
        guard let schedule, !schedule.priceRules.isEmpty else { return HappyHourPricingTable(isActive: true, items: []) }
        var items: [HappyHourPrice] = []
        for product in try catalogRepository.activeProducts() {
            let result = LocalHappyHourPricing.price(productId: product.id, categoryId: product.categoryId, standard: product.price, rules: schedule.priceRules)
            guard result.isHappyHour, result.effective < product.price else { continue }
            items.append(HappyHourPrice(
                productId: product.id, productName: product.name, standardPrice: product.price, happyHourPrice: result.effective,
                ruleType: LocalHappyHourPricing.ruleType(productId: product.id, categoryId: product.categoryId, rules: schedule.priceRules)
            ))
        }
        return HappyHourPricingTable(isActive: true, items: items)
    }

    /// PIN d'un responsable. Les PIN inconnus comptent dans le verrouillage partagé avec la connexion ; un PIN valide sans droit de
    /// responsable est refusé sans compter comme échec.
    func authenticateSupervisor(pin: String) throws -> StaffMember {
        try ensurePinAttemptsAllowed()
        let insufficient = APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")
        guard let supervisor = try staffRepository.activeMember(pin: pin) else {
            try recordFailedPin()
            throw insufficient
        }
        guard supervisor.role.isManager else { throw insufficient }
        try resetFailedPins()
        return supervisor
    }

    /// Dérogation de responsable : remplace la précédente, durée par défaut 60 minutes. L'audit JET viendra avec le sous-projet fiscal.
    public func activateHappyHourOverride(terminalId: String, pin: String, minutes: Int, reason: String) async throws -> OperationResult {
        let supervisor = try authenticateSupervisor(pin: pin)
        let duration = minutes > 0 ? minutes : 60
        let now = clock()
        let repository = happyHourRepository
        try db.transaction {
            try repository.deactivateOverrides(terminalId: Self.standaloneTerminalId)
            try repository.insertOverride(
                terminalId: Self.standaloneTerminalId, operatorId: supervisor.id, operatorName: supervisor.name, startsAt: now,
                expiresAt: now.addingTimeInterval(TimeInterval(duration * 60)), reason: reason.isEmpty ? "Dérogation manuelle responsable" : reason
            )
        }
        return OperationResult(success: true, message: "Happy Hour activé/prolongé de \(duration) minutes avec succès.")
    }

    public func stopHappyHourOverride(terminalId: String, pin: String, reason: String) async throws -> OperationResult {
        _ = try authenticateSupervisor(pin: pin)
        try happyHourRepository.deactivateOverrides(terminalId: Self.standaloneTerminalId)
        return OperationResult(success: true, message: "Dérogation Happy Hour désactivée.")
    }

    private static func remainingMinutes(_ seconds: TimeInterval) -> Int { Int(max(0, (seconds / 60).rounded(.up))) }

    private static func utcTime(_ date: Date) -> String {
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = .gmt
        let parts = utc.dateComponents([.hour, .minute, .second], from: date)
        return LocalTimeOfDay.stored((parts.hour ?? 0) * 3600 + (parts.minute ?? 0) * 60 + (parts.second ?? 0))
    }
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Happy Hour, statut et dérogations (retiré par la tâche 6)` : ce commentaire, les 4 lignes `happyHourStatus`, `happyHourPricing`, `activateHappyHourOverride`, `stopHappyHourOverride`, et la ligne vide qui suit.

- [ ] **Step 5: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalHappyHourStatusTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 6 tests … passed`.

- [ ] **Step 6: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"` → Expected: `318 tests … passed`.

Mettre à jour `docs/plans/ipad-standalone-1d-progress.md` (tâche 6 `fait`, PosKit `318`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1d-progress.md
git commit -m "feat(ios-local): Happy Hour (statut, grille tarifaire, dérogations de responsable) sur SQLite"
```

---

### Task 7: Réseau, base vierge, première configuration et documentation

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Network.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Setup.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSeeder.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFloorRepository.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Staff.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalSetupTests.swift`
- Modify: `ios/README.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes : `LocalPrinterRepository.insert`, `LocalHappyHourRepository.insert`, `Seed.make()` (champs `printers`, `schedules`), `staffRepository`, `floorRepository`, `validate(pin:)`, `NetworkInfo`, `SyncStatus`.
- Produces :
  - `public enum LocalSeedMode { case demo, blank }` ; `LocalPosAPI.init(path:seed:clock:calendar:)` (`seed` vaut `.demo` par défaut).
  - `public struct LocalSetup` (`managerName`, `managerPin`, `companyName`, `address`, `siret`, `vatNumber`) ; `LocalPosAPI.needsSetup() async throws -> Bool` ; `LocalPosAPI.completeFirstRun(_:) async throws`.
  - `LocalFloorRepository.insertSettings(companyName:addressLines:siret:vatNumber:)`.
  - Méthodes `PosAPI` `networkInfo()`, `syncStatus()`, `forceSync()`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalSetupTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : base vierge, première configuration, réseau")
struct LocalSetupTests {
    private static let setup = LocalSetup(
        managerName: "Marie Curie", managerPin: "4321", companyName: "Chez Marie", address: "1 rue de la Paix\n75002 Paris",
        siret: "12345678901234", vatNumber: "FR00123456789"
    )

    @Test func blankDatabaseHoldsNoDemoData() async throws {
        let api = try LocalPosAPI(path: ":memory:", seed: .blank)
        #expect(try await api.needsSetup())
        for pin in ["1234", "2468", "5678", "9999"] {
            #expect(try await api.login(pin: pin).success == false)
        }
        #expect(try await api.staff().isEmpty)
        #expect(try await api.categories().isEmpty)
        #expect(try await api.tables().isEmpty)
        #expect(try await api.printers().isEmpty)
        #expect(try await api.happyHourSchedules().isEmpty)
    }

    @Test func completeFirstRunCreatesTheManagerAndTheSettings() async throws {
        let api = try LocalPosAPI(path: ":memory:", seed: .blank)
        try await api.completeFirstRun(Self.setup)
        #expect(try await api.needsSetup() == false)

        let login = try await api.login(pin: "4321")
        #expect(login.success && login.role == .admin && login.operatorName == "Marie Curie")
        let settings = try await api.settings()
        #expect(settings.companyName == "Chez Marie" && settings.siret == "12345678901234" && settings.vatNumber == "FR00123456789")
        #expect(settings.addressLines == "1 rue de la Paix\n75002 Paris" && settings.receiptLanguage == "fr")
        #expect(try await api.staff().count == 1)
    }

    @Test func completeFirstRunRefusesAnAlreadyConfiguredDatabase() async throws {
        let configured = try LocalPosAPI(path: ":memory:", seed: .blank)
        try await configured.completeFirstRun(Self.setup)
        let refused = APIError.server(status: 409, message: "L'installation est déjà configurée.")
        await #expect(throws: refused) { try await configured.completeFirstRun(Self.setup) }
        #expect(try await configured.staff().count == 1)

        let demo = try LocalPosAPI(path: ":memory:")
        #expect(try await demo.needsSetup() == false)
        await #expect(throws: refused) { try await demo.completeFirstRun(Self.setup) }
        #expect(try await demo.staff().count == 4)
    }

    @Test func completeFirstRunValidatesBeforeWriting() async throws {
        let api = try LocalPosAPI(path: ":memory:", seed: .blank)
        var noName = Self.setup; noName.managerName = "  "
        var shortPin = Self.setup; shortPin.managerPin = "12"
        var letterPin = Self.setup; letterPin.managerPin = "12ab"
        var noCompany = Self.setup; noCompany.companyName = ""
        var shortSiret = Self.setup; shortSiret.siret = "123"
        var letterSiret = Self.setup; letterSiret.siret = "1234567890123A"
        let pinMessage = "Le code PIN doit comporter 4 chiffres."
        let siretMessage = "Le SIRET doit comporter 14 chiffres."
        let cases: [(LocalSetup, String)] = [
            (noName, "Le nom du responsable est requis."), (shortPin, pinMessage), (letterPin, pinMessage),
            (noCompany, "Le nom de l'établissement est requis."), (shortSiret, siretMessage), (letterSiret, siretMessage),
        ]
        for (setup, message) in cases {
            await #expect(throws: APIError.server(status: 400, message: message)) { try await api.completeFirstRun(setup) }
        }
        #expect(try await api.needsSetup())
        #expect(try await api.staff().isEmpty)
    }

    @Test func firstRunSurvivesReopeningTheFile() async throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        do {
            let first = try LocalPosAPI(path: path, seed: .blank)
            try await first.completeFirstRun(Self.setup)
        }
        let reopened = try LocalPosAPI(path: path, seed: .blank)
        #expect(try await reopened.needsSetup() == false)
        #expect(try await reopened.login(pin: "4321").success)
        #expect(try await reopened.staff().count == 1)
        #expect(try await reopened.login(pin: "1234").success == false)
    }

    @Test func demoSeedIncludesPrintersAndTheAfterworkSchedule() async throws {
        let api = try await makeLocalAPI()
        let printers = try await api.printers()
        #expect(printers.map(\.name).sorted() == ["Imprimante Bar & Boissons", "Imprimante Caisse Comptoir", "Imprimante Cuisine Chaude"])
        let cash = try #require(printers.first { $0.name == "Imprimante Caisse Comptoir" })
        #expect(cash.openCashDrawerOnReceipt && cash.assignedStationIds == ["RECEIPT"] && cash.ipAddress == "192.168.1.200")

        let schedules = try await api.happyHourSchedules()
        #expect(schedules.count == 1)
        let afterwork = try #require(schedules.first)
        #expect(afterwork.name == "Afterwork Standard" && afterwork.daysOfWeek.sorted() == [1, 2, 3, 4, 5] && afterwork.priceRules.count == 2)
    }

    @Test func networkAndSyncReportStandalone() async throws {
        let api = try await makeLocalAPI()
        let info = try await api.networkInfo()
        #expect(info.serverName == "Mode autonome (cet iPad)" && info.status == "Standalone" && info.primaryIp == nil && info.hostName == nil)
        let sync = try await api.syncStatus()
        #expect(sync.totalMessages == 0 && sync.completedMessages == 0 && sync.pendingMessages == 0 && sync.status == "Standalone" && sync.lastSyncUtc == nil)
        try await api.forceSync()
        #expect(try await api.health() == 0)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter LocalSetupTests 2>&1 | tail -5`
Expected: erreur de compilation `cannot find 'LocalSetup' in scope`.

- [ ] **Step 3: Réseau et synchronisation**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Network.swift` :
```swift
import Foundation

extension LocalPosAPI {
    /// Aucun serveur : l'iPad se présente comme tel. Pas de nom d'hôte (sa résolution peut bloquer) ni d'adresse.
    public func networkInfo() async throws -> NetworkInfo {
        NetworkInfo(hostName: nil, primaryIp: nil, ipAddresses: [], port: nil, serverName: "Mode autonome (cet iPad)", status: "Standalone", version: nil)
    }

    /// Rien à synchroniser tant qu'il n'y a ni serveur ni second poste.
    public func syncStatus() async throws -> SyncStatus {
        SyncStatus(totalMessages: 0, completedMessages: 0, pendingMessages: 0, status: "Standalone", lastSyncUtc: nil)
    }

    public func forceSync() async throws {}
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Réseau (retiré par la tâche 7)` : ce commentaire, les 3 lignes `networkInfo`, `syncStatus`, `forceSync`, et la ligne vide qui suit s'il y en a une.

- [ ] **Step 4: Mode d'amorçage et réglages initiaux**

Dans `LocalSeeder.swift`, remplacer :
```swift
/// Données d'amorçage du premier lancement : mêmes comptes, familles, articles et tables que `Program.SeedDatabase`
/// (réutilise `Seed.make()` du backend en mémoire).
enum LocalSeeder {
    static func seedIfEmpty(_ db: SQLiteDatabase) throws {
        guard try db.query("SELECT COUNT(*) AS n FROM Users").first?.int("n") == 0 else { return }
```
par :
```swift
/// Contenu d'une base neuve.
public enum LocalSeedMode: Sendable {
    /// Comptes (1234, 2468, 5678, 9999), catalogue, tables, chambres, imprimantes et planning de démonstration : tests et démonstration.
    case demo
    /// Aucune donnée : `completeFirstRun` crée le premier responsable et l'identité de l'établissement. Mode des installations réelles.
    case blank
}

/// Données d'amorçage du premier lancement : mêmes comptes, familles, articles et tables que `Program.SeedDatabase`
/// (réutilise `Seed.make()` du backend en mémoire).
enum LocalSeeder {
    static func seedIfEmpty(_ db: SQLiteDatabase, mode: LocalSeedMode) throws {
        guard mode == .demo, try db.query("SELECT COUNT(*) AS n FROM Users").first?.int("n") == 0 else { return }
```

Dans `LocalSeeder.swift`, remplacer :
```swift
            for room in seed.rooms { try hotel.insert(room, checkIn: now.addingTimeInterval(-86_400), checkOut: now.addingTimeInterval(3 * 86_400)) }
```
par :
```swift
            for room in seed.rooms { try hotel.insert(room, checkIn: now.addingTimeInterval(-86_400), checkOut: now.addingTimeInterval(3 * 86_400)) }
            // Imprimantes et planning Happy Hour de démonstration (comme `Program.SeedDatabase`).
            let printers = LocalPrinterRepository(db: db)
            for printer in seed.printers { try printers.insert(printer) }
            let happyHour = LocalHappyHourRepository(db: db)
            for schedule in seed.schedules { try happyHour.insert(schedule) }
```

Dans `LocalPosAPI.swift`, remplacer :
```swift
    public init(path: String, clock: @escaping @Sendable () -> Date = { Date() }, calendar: Calendar = .current) throws {
        let db = try SQLiteDatabase(path: path)
        try LocalMigrator.migrate(db)
        try LocalSeeder.seedIfEmpty(db)
```
par :
```swift
    public init(path: String, seed: LocalSeedMode = .demo, clock: @escaping @Sendable () -> Date = { Date() }, calendar: Calendar = .current) throws {
        let db = try SQLiteDatabase(path: path)
        try LocalMigrator.migrate(db)
        try LocalSeeder.seedIfEmpty(db, mode: seed)
```

Dans `LocalFloorRepository.swift`, remplacer :
```swift
    /// Réglages initiaux : valeurs par défaut de l'entité `RestaurantSettings` du .NET.
    func insertDefaultSettings() throws {
        try db.run(
```
par :
```swift
    /// Réglages de démonstration (valeurs de `Program.SeedDatabase`).
    func insertDefaultSettings() throws {
        try insertSettings(companyName: "RESTAURANT L'ANTIGRAVITE", addressLines: "12 Rue de la Gastronomie\n75001 Paris", siret: "88877766600012", vatNumber: "FR12888777666")
    }

    /// Réglages initiaux : langues et exercice par défaut de l'entité `RestaurantSettings` du .NET.
    func insertSettings(companyName: String, addressLines: String, siret: String, vatNumber: String) throws {
        try db.run(
```

Dans `LocalFloorRepository.swift`, remplacer :
```swift
            [.text("RESTAURANT L'ANTIGRAVITE"), .text("12 Rue de la Gastronomie\n75001 Paris"), .text("88877766600012"), .text("FR12888777666"), .date(Date())]
```
par :
```swift
            [.text(companyName), .text(addressLines), .text(siret), .text(vatNumber), .date(Date())]
```

Dans `LocalPosAPI+Staff.swift`, remplacer :
```swift
    private func validate(pin: String) throws {
```
par :
```swift
    func validate(pin: String) throws {
```

- [ ] **Step 5: Première configuration**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Setup.swift` :
```swift
import Foundation

/// Saisie de la première configuration d'une installation réelle.
public struct LocalSetup: Sendable {
    public var managerName: String
    public var managerPin: String
    public var companyName: String
    public var address: String
    public var siret: String
    public var vatNumber: String

    public init(managerName: String, managerPin: String, companyName: String, address: String, siret: String, vatNumber: String) {
        self.managerName = managerName
        self.managerPin = managerPin
        self.companyName = companyName
        self.address = address
        self.siret = siret
        self.vatNumber = vatNumber
    }
}

extension LocalPosAPI {
    /// `true` tant qu'aucun responsable actif n'existe (base vierge) : l'application doit alors proposer la configuration initiale.
    public func needsSetup() async throws -> Bool {
        try staffRepository.members().allSatisfy { !($0.isActive && $0.role.isManager) }
    }

    /// Crée le premier responsable (rôle `admin`) et l'identité de l'établissement d'une base vierge. Refusé (409) dès qu'un compte
    /// existe : une installation en service ne peut pas être réinitialisée par cette voie. Nom et SIRET (14 chiffres) sont obligatoires,
    /// le fiscal en a besoin.
    public func completeFirstRun(_ setup: LocalSetup) async throws {
        let name = setup.managerName.trimmingCharacters(in: .whitespacesAndNewlines)
        let company = setup.companyName.trimmingCharacters(in: .whitespacesAndNewlines)
        let siret = setup.siret.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty else { throw APIError.server(status: 400, message: "Le nom du responsable est requis.") }
        try validate(pin: setup.managerPin)
        guard !company.isEmpty else { throw APIError.server(status: 400, message: "Le nom de l'établissement est requis.") }
        guard siret.count == 14, siret.allSatisfy(\.isASCII), siret.allSatisfy(\.isNumber) else {
            throw APIError.server(status: 400, message: "Le SIRET doit comporter 14 chiffres.")
        }
        let staff = staffRepository, floor = floorRepository
        try db.transaction {
            guard try staff.members().isEmpty else { throw APIError.server(status: 409, message: "L'installation est déjà configurée.") }
            try staff.insert(StaffMember(name: name, role: .admin), pin: setup.managerPin)
            try floor.insertSettings(
                companyName: company, addressLines: setup.address.trimmingCharacters(in: .whitespacesAndNewlines), siret: siret,
                vatNumber: setup.vatNumber.trimmingCharacters(in: .whitespacesAndNewlines)
            )
        }
    }
}
```

- [ ] **Step 6: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter LocalSetupTests 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 7 tests … passed`.

- [ ] **Step 7: Documentation**

Dans `ios/README.md`, remplacer :
```markdown
Les autres méthodes répondent `501` (« Non disponible en mode autonome ») jusqu'au plan 1d (imprimantes, Happy Hour, réseau, choix Serveur/Autonome au lancement) et aux sous-projets fiscaux.
```
par :
```markdown
Le plan 1d ajoute la configuration des imprimantes (sans impression directe), le Happy Hour, le terminal fixe `T01`, le verrouillage des PIN persistant, le durcissement SQLite avec sauvegarde (`backup(to:)`) et la base vierge avec configuration initiale (`LocalSeedMode.blank`, `completeFirstRun`). Les autres méthodes (fiscal, clôtures) répondent `501` (« Non disponible en mode autonome ») jusqu'aux sous-projets fiscaux ; le choix Serveur/Autonome au lancement viendra au plan 1e.
```

Dans `CLAUDE.md`, remplacer :
```markdown
sont propres à l'iPad et absentes de `AppDbContext`.
```
par :
```markdown
sont propres à l'iPad et absentes de `AppDbContext`, de même que `LocalPinFailures` et `LocalPinLockout` (verrouillage des PIN persistant). Le poste autonome est toujours le terminal `T01` : comme le serveur, il ignore le terminal envoyé par le client. `LocalSeedMode.demo` (défaut, tests) amorce comptes et données de démonstration ; une installation réelle utilise `.blank` puis `completeFirstRun`.
```

- [ ] **Step 8: Vérification finale, suivi, commit**

Run:
```bash
cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"
cd ../../.. && dotnet build RestaurantPos.slnx 2>&1 | tail -3
```
Expected: `Test run with 325 tests … passed`, aucun avertissement ; `dotnet build` réussi (aucun fichier .NET modifié).

Mettre à jour `docs/plans/ipad-standalone-1d-progress.md` : tâche 7 `fait`, PosKit `325`, puis ajouter la section suivante :
```markdown
Conditions d'entrée du plan 1e (application SwiftUI : le mode devient sélectionnable) :
1. Choix « Serveur / Autonome » au premier lancement, mémorisé ; `AppEnvironment.makeModel` construit `AppModel(api: LocalPosAPI(path: LocalDatabaseLocation.defaultURL().path, seed: .blank))` en mode autonome.
2. **Une seule instance de `LocalPosAPI` par fichier**, détenue par `AppEnvironment` et jamais recréée tant que l'app tourne (la création d'une seconde instance sur le même fichier est interdite) ; la sauvegarde `backup(to:)` passe par cet acteur.
3. Écrans de première configuration branchés sur `needsSetup()` / `completeFirstRun(_:)` (responsable, PIN à 4 chiffres, établissement, SIRET) ; aucun compte de démonstration en production.
4. `TerminalSettings` pour le mode autonome : identité de poste `T01` (`LocalPosAPI.standaloneTerminalId`) sans écran d'appairage ni découverte Bonjour.
5. Bandeau « non fiscal » visible tant que le sous-projet 2 n'est pas livré (condition 5 du plan 1c).
6. Export de la sauvegarde par la feuille de partage iOS (Fichiers/iCloud) ; planification de la sauvegarde quotidienne.
7. XCUITest en mode autonome (`-UITestLocal` : `LocalPosAPI` en mémoire, `seed: .demo`) couvrant le parcours table complet — condition de sortie du sous-projet 1.
8. Clés de localisation (FR/EN/AR) des nouveaux écrans ; messages d'erreur de `Local/` laissés en français comme les autres.
Restent ouverts (sous-projet 2) : clôture d'une commande à solde nul, `chargeRoom` sans reçu `NF-…`, audit JET des dérogations Happy Hour et des annulations de mise en attente.
```

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local ios/README.md CLAUDE.md docs/plans/ipad-standalone-1d-progress.md
git commit -m "feat(ios-local): réseau autonome, base vierge, première configuration et documentation du plan 1d"
```

---

## Self-Review

**1. Couverture de la spec (sous-projet 1) et des conditions d'entrée du plan 1c**
- Conditions 1 (PIN persistants : tâche 1), 2 (pas de comptes de démonstration : tâche 7), 3 (busy_timeout, synchronous, sauvegarde sûre en WAL, protection de fichier : tâche 3 ; instance unique : renvoyée au plan 1e, consignée), 4 (terminal `T01` : tâche 2). Les conditions 5, 6 et 7 sont explicitement reportées (1e / sous-projet 2).
- Méthodes `PosAPI` restantes non fiscales : imprimantes (tâche 4), Happy Hour (tâches 5-6), réseau/synchronisation (tâche 7). Le fichier `LocalPosAPI+Unsupported.swift` ne garde que l'appairage et le fiscal.
- « Choix Serveur / Autonome au lancement » et tests UI : volontairement hors de ce plan (décision 1), repris par le plan 1e avec ses conditions d'entrée.
- Montants en centimes (`Money`), taux `Decimal`, aucun `Double` pour l'argent ; seul `Double` : minutes restantes (durée), sans lien avec les montants.

**2. Placeholders** : aucun « TBD »/« TODO » ; chaque étape de code contient le code complet ; les remplacements donnent le texte avant/après exact.

**3. Cohérence des types** : `LocalPinGuard` (tâche 1) et ses trois méthodes `throws` utilisées par `authenticateSupervisor` (tâche 6) et `voidHeldOrder` ; `standaloneTerminalId` et `normalizedTerminal` (tâche 2) utilisés par les tâches 6 ; `printerRepository` (tâche 4) et `happyHourRepository` (tâche 5) réutilisés par `LocalSeeder` (tâche 7) ; `LocalHappyHourRepository.insert(_:)` appelé par `createHappyHourSchedule` (tâche 5) et par le seeder (tâche 7) ; `LocalTimeOfDay` (tâche 5) réutilisé par la tâche 6 ; `validate(pin:)` rendu interne (tâche 7) ; paramètre `seed:` ajouté à l'`init` de la tâche 1 sans casser les appelants (arguments nommés et valeurs par défaut).

**4. Review Focus** : les cinq points ont chacun un test nommé dans la tâche propriétaire (tâches 1, 2, 3, 6, 7).

**5. Lacunes connues, hors test** : protection de fichier iOS (`#if os(iOS)`, non exécutable par `swift test` sur Mac) ; pas de contrôle de chevauchement entre plannings ni de passage de minuit (comme le serveur) ; le fuseau horaire de l'appareil détermine « l'heure locale » du Happy Hour ; audit JET des dérogations (sous-projet 2) ; impression réelle (sous-projet 4).

## Execution Handoff

Plan à relire avant toute exécution. Vérification prévue à la rédaction : les blocs « Créer / remplacer / supprimer » de ce document sont appliqués mécaniquement, tâche par tâche, sur une copie propre de PosKit à l'état de `main`, et `swift test` doit donner exactement 294, 295, 300, 305, 312, 318 puis 325 tests verts, sans avertissement.
