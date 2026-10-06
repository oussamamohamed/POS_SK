# iPad standalone — plan 1a : socle SQLite (`LocalPosAPI`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Un backend iPad autonome `LocalPosAPI` (conforme au protocole `PosAPI`) adossé à une base SQLite locale, qui sait déjà authentifier un opérateur par PIN, gérer personnel, catalogue, grille tactile, salle (tables) et réglages — sans serveur .NET.

**Architecture:** `LocalPosAPI` est un `actor` qui possède une `SQLiteDatabase` (enveloppe mince sur `import SQLite3`). Le schéma reproduit les tables et colonnes de `AppDbContext` ; il est créé par `LocalMigrator` (versionné par `PRAGMA user_version`). Un dépôt (`Local*Repository`) par domaine fait la traduction lignes ↔ modèles PosKit existants ; l'acteur applique les règles d'accès et d'erreur du serveur. Les méthodes de `PosAPI` pas encore portées répondent `501` ; les plans 1b et 1c les remplacent. Aucune modification de l'UI ni des stores dans ce plan.

**Tech Stack:** Swift 6 (tools-version 6.0), `SQLite3` (module système), `CryptoKit`, Swift Testing, SwiftPM (`ios/Packages/PosKit`).

**Spec:** `docs/superpowers/specs/2026-10-06-ipad-standalone-design.md` (sous-projet 1 « Socle local », choix B : schéma relationnel complet identique au .NET). Ce plan est le premier des trois plans du sous-projet 1 :
- **1a (ce plan)** : SQLite, schéma, authentification, personnel, catalogue, grille, salle, réglages.
- **1b** : commandes, comptoir, mise en attente, remises, cuisine, paiement (non fiscal), chambres d'hôtel.
- **1c** : imprimantes (configuration), Happy Hour, réseau/sync, choix « Serveur / Autonome » au premier lancement, identité de terminal `T01`.

## Global Constraints

- `PosKit` reste **sans dépendance externe** : uniquement `Foundation`, `SQLite3`, `CryptoKit`.
- Swift 6 (`swift-tools-version: 6.0`), plateformes `.iOS(.v17)` et `.macOS(.v14)` ; pas d'avertissement de compilation.
- Montants en **centimes entiers** (`Money`) ; jamais de `Double`/`Float` pour un montant. Taux de TVA en `Decimal`, stockés en texte.
- Tables et colonnes **nommées comme dans `AppDbContext`** (`Users`, `Categories`, `Products`, `ModifierGroups`, `ModifierOptions`, `DiningTables`, `RestaurantSettings`, `GridLayouts`, `GridSlots`). Guid = `TEXT` en majuscules ; `DateTimeOffset` = `TEXT` ISO 8601 UTC ; enum = `INTEGER` ; `Money` = `INTEGER`.
- Migrations versionnées par `PRAGMA user_version`, **échec explicite**, jamais de `try/catch` silencieux. On ne modifie pas un script publié : on en ajoute un.
- Hachage PIN identique au .NET : SHA-256 de `salt + pin`, hexadécimal minuscule.
- Messages d'erreur et codes identiques à `InMemoryPosAPI` / au serveur (en français), par ex. `Code PIN ou identifiants incorrects`, `Action réservée à un responsable.`.
- Éviter les noms de types qui entrent en collision avec SwiftUI/Foundation (`CLAUDE.md`) : tous les types du plan sont préfixés `Local…`/`SQL…` ou déjà uniques.
- Commentaires et chaînes en français, comme le reste du code ; identifiants de code en anglais.
- Les tests passent par Swift Testing (`import Testing`, `@Test`, `#expect`), comme `Tests/PosKitTests/StoreTests.swift`.
- Ne pas toucher à `HTTPPosAPI`, `InMemoryPosAPI` (hors réutilisation de `Seed.make()`), aux stores ni aux vues.

## Review Focus

Entrées ou pannes que la spec implique mais qu'aucun test « nominal » n'exerce ; chacune a son test dans la tâche indiquée.

1. **Zéros de tête dans un PIN** (`0042` ≠ `42`) : le PIN est une chaîne, jamais un entier → test `pinsKeepLeadingZeros` (tâche 3).
2. **Texte non latin / emoji** dans les noms (opérateur arabe, article avec emoji) : aller-retour sans altération → `nonLatinNamesRoundTrip` (tâche 3), `unicodeProductNameRoundTrips` (tâche 4).
3. **Erreurs de saisie qui se cumulent** : 4 PIN faux, une connexion réussie, puis 4 PIN faux ne doivent pas verrouiller → `successResetsTheFailureCounter` (tâche 3).
4. **Sauvegarde de grille qui échoue en cours de route** (article inconnu) : l'ancienne grille doit rester intacte, pas de grille à moitié écrite → `failedSaveKeepsThePreviousGrid` (tâche 6).
5. **Fichier qui n'est pas une base SQLite** (corrompu, ou autre fichier au même chemin) : l'ouverture échoue proprement et ne modifie pas le fichier → `garbageFileIsRejectedAndLeftUntouched` (tâche 7).

Lacune connue, hors test : l'expiration du verrouillage de 30 s n'est pas testée (aucune horloge injectable, YAGNI) ; elle repose sur `Date()` et sur la même règle que `PinRateLimiterService`.

## File Structure

Tout est sous `ios/Packages/PosKit/`.

| Fichier | Responsabilité |
|---|---|
| `Sources/PosKit/Local/SQLiteDatabase.swift` | `SQLValue`, `SQLRow`, `SQLDate`, `SQLiteError`, `SQLiteDatabase` : ouvrir, exécuter, interroger, transaction, `user_version`. |
| `Sources/PosKit/Local/PinHasher.swift` | Hachage et sel des PIN, compatible .NET. |
| `Sources/PosKit/Local/LocalMigrator.swift` | Liste ordonnée des scripts de schéma (v1) et application par `user_version`. |
| `Sources/PosKit/Local/LocalSeeder.swift` | Données du premier lancement (via `Seed.make()`). |
| `Sources/PosKit/Local/LocalStaffRepository.swift` | `Users` ↔ `StaffMember`, vérification de PIN. |
| `Sources/PosKit/Local/LocalCatalogRepository.swift` | `Categories`, `Products`, `ModifierGroups/Options` ↔ `MenuCategory`, `Product`. |
| `Sources/PosKit/Local/LocalFloorRepository.swift` | `DiningTables`, `RestaurantSettings` ↔ `DiningTable`, `RestaurantSettings`. |
| `Sources/PosKit/Local/LocalGridRepository.swift` | `GridLayouts/Slots` ↔ `TouchGridLayout`. |
| `Sources/PosKit/Local/LocalPosAPI.swift` | L'acteur : init, session (token), verrouillage PIN, garde-fous d'accès. |
| `Sources/PosKit/Local/LocalPosAPI+Staff.swift` | `staff`, `createStaff`, `updateStaff`, `deactivateStaff`. |
| `Sources/PosKit/Local/LocalPosAPI+Catalog.swift` | `categories`, `products`, `create/updateCategory`, `create/update/archiveProduct`. |
| `Sources/PosKit/Local/LocalPosAPI+Floor.swift` | `tables`, `createTable`, `settings`, `saveSettings`. |
| `Sources/PosKit/Local/LocalPosAPI+Grid.swift` | `gridLayout`, `saveGridLayout`, `swapGridSlots`, `updateGridDimensions`. |
| `Sources/PosKit/Local/LocalPosAPI+Unsupported.swift` | Méthodes de `PosAPI` pas encore portées : `501`. |
| `Tests/PosKitTests/Local/*.swift` | Un fichier de tests par tâche (+ `LocalTestSupport.swift`). |
| `docs/plans/ipad-standalone-1a-progress.md` | Suivi d'exécution (exigé par `CLAUDE.md`). |

## Conventions pour toutes les tâches

- Répertoire de travail des commandes `swift` : `ios/Packages/PosKit`. Commande de test ciblée : `swift test --filter <NomDeSuite>` ; suite complète : `swift test`.
- Base de référence avant ce plan : **154 tests PosKit passent**. Comptes attendus cumulés à la fin de chaque tâche : T1 160 · T2 165 · T3 182 · T4 188 · T5 193 · T6 200 · T7 203.
- Chaque tâche se termine par : suite PosKit complète verte, mise à jour de `docs/plans/ipad-standalone-1a-progress.md`, commit. Le fichier de suivi est modifié dans le même commit que la tâche.
- Les commits suivent le style du dépôt : `feat(ios-local): …`, en français, avec le trailer de co-signature habituel de la session.
- Un nouveau fichier source est automatiquement pris par SwiftPM (pas de `project.yml` à modifier : le code vit dans le package).

---

### Task 1: Enveloppe SQLite (`SQLiteDatabase`)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/SQLiteDatabase.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/SQLiteDatabaseTests.swift`
- Create: `docs/plans/ipad-standalone-1a-progress.md`

**Interfaces:**
- Consumes: rien.
- Produces (internes au module, `@testable` dans les tests) :
  - `enum SQLValue { null, int(Int64), double(Double), text(String), blob(Data) }` + constructeurs `.integer(Int?)`, `.bool(Bool)`, `.string(String?)`, `.real(Double?)`, `.uuid(UUID?)`, `.decimal(Decimal?)`, `.date(Date?)`.
  - `enum SQLDate { static func format(_: Date) -> String; static func parse(_: String) -> Date? }`
  - `struct SQLRow { subscript(String) -> SQLValue; string(_:) -> String?; int(_:) -> Int?; double(_:) -> Double?; bool(_:) -> Bool; uuid(_:) -> UUID?; decimal(_:) -> Decimal?; date(_:) -> Date? }`
  - `struct SQLiteError: Error, LocalizedError { code: Int32; message: String }`
  - `final class SQLiteDatabase { init(path: String) throws; exec(_:) throws; run(_:_:) throws -> Int; query(_:_:) throws -> [SQLRow]; transaction<T>(_:) throws -> T; userVersion() throws -> Int }`

- [ ] **Step 0: Relever la base de référence et créer le fichier de suivi**

Run (depuis la racine du dépôt) :
```bash
cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run"
cd ../../.. && dotnet test RestaurantPos.slnx 2>&1 | grep -E "Passed!|Failed!|Réussi|Échec" | tail -5
```
Expected: `Test run with 154 tests … passed` ; les lignes `dotnet test` donnent le nombre de tests .NET (à reporter tel quel).

Créer `docs/plans/ipad-standalone-1a-progress.md` :
```markdown
# iPad standalone 1a — suivi d'exécution

Plan : `docs/superpowers/plans/2026-10-06-ipad-standalone-1a-socle-sqlite.md`

Base de référence (avant tâche 1) : PosKit 154 · .NET <N> · web <non exécuté, inchangé>

| Tâche | Statut | PosKit | .NET | Web | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Enveloppe SQLite | à faire | | | | |
| 2 Schéma v1 et PinHasher | à faire | | | | |
| 3 Auth et personnel | à faire | | | | |
| 4 Catalogue | à faire | | | | |
| 5 Salle et réglages | à faire | | | | |
| 6 Grille tactile | à faire | | | | |
| 7 Persistance et docs | à faire | | | | |
```
Remplacer `<N>` par le compte .NET relevé.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/SQLiteDatabaseTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("SQLiteDatabase")
struct SQLiteDatabaseTests {
    private func makeDB() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try db.exec("CREATE TABLE T (Id TEXT PRIMARY KEY, N INTEGER, R REAL, S TEXT, B BLOB)")
        return db
    }

    @Test func roundTripsEveryValueType() throws {
        let db = try makeDB()
        let id = UUID()
        let date = Date(timeIntervalSince1970: 1_790_000_000.123)
        try db.run("INSERT INTO T (Id, N, R, S, B) VALUES (?, ?, ?, ?, ?)",
                   [.uuid(id), .integer(42), .real(1.5), .string("à l'emporter"), .blob(Data([1, 2, 3]))])
        let row = try #require(try db.query("SELECT * FROM T").first)
        #expect(row.uuid("Id") == id)
        #expect(row.int("N") == 42)
        #expect(row.double("R") == 1.5)
        #expect(row.string("S") == "à l'emporter")
        #expect(row["B"] == .blob(Data([1, 2, 3])))
        #expect(SQLDate.parse(SQLDate.format(date)).map { abs($0.timeIntervalSince(date)) < 0.002 } == true)
    }

    @Test func nullsAndDecimals() throws {
        let db = try makeDB()
        try db.run("INSERT INTO T (Id, S) VALUES (?, ?)", [.text("a"), .decimal(5.5)])
        try db.run("INSERT INTO T (Id, S) VALUES (?, ?)", [.text("b"), .string(nil)])
        let rows = try db.query("SELECT * FROM T ORDER BY Id")
        #expect(rows[0].decimal("S") == 5.5)
        #expect(rows[1].string("S") == nil)
        #expect(rows[1].int("N") == nil)
    }

    @Test func rollsBackFailedTransaction() throws {
        let db = try makeDB()
        struct Boom: Error {}
        #expect(throws: Boom.self) {
            try db.transaction {
                try db.run("INSERT INTO T (Id) VALUES ('x')")
                throw Boom()
            }
        }
        #expect(try db.query("SELECT * FROM T").isEmpty)
    }

    @Test func commitsSuccessfulTransaction() throws {
        let db = try makeDB()
        try db.transaction { try db.run("INSERT INTO T (Id) VALUES ('x')") }
        #expect(try db.query("SELECT * FROM T").count == 1)
    }

    @Test func surfacesSQLErrors() throws {
        let db = try makeDB()
        #expect(throws: SQLiteError.self) { try db.run("INSERT INTO Missing VALUES (1)") }
        try db.run("INSERT INTO T (Id) VALUES ('dup')")
        #expect(throws: SQLiteError.self) { try db.run("INSERT INTO T (Id) VALUES ('dup')") }
    }

    @Test func tracksUserVersion() throws {
        let db = try makeDB()
        #expect(try db.userVersion() == 0)
        try db.exec("PRAGMA user_version = 3")
        #expect(try db.userVersion() == 3)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter SQLiteDatabase 2>&1 | tail -5`
Expected: erreur de compilation `cannot find 'SQLiteDatabase' in scope`.

- [ ] **Step 3: Implémenter**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/SQLiteDatabase.swift` :
```swift
import Foundation
import SQLite3

/// Valeur liée à un paramètre SQL ou lue dans une colonne.
enum SQLValue: Equatable, Sendable {
    case null
    case int(Int64)
    case double(Double)
    case text(String)
    case blob(Data)

    static func integer(_ v: Int?) -> SQLValue { v.map { .int(Int64($0)) } ?? .null }
    static func bool(_ v: Bool) -> SQLValue { .int(v ? 1 : 0) }
    static func string(_ v: String?) -> SQLValue { v.map { .text($0) } ?? .null }
    static func real(_ v: Double?) -> SQLValue { v.map { .double($0) } ?? .null }
    static func uuid(_ v: UUID?) -> SQLValue { v.map { .text($0.uuidString) } ?? .null }
    static func decimal(_ v: Decimal?) -> SQLValue { v.map { .text("\($0)") } ?? .null }
    static func date(_ v: Date?) -> SQLValue { v.map { .text(SQLDate.format($0)) } ?? .null }
}

/// Dates stockées en texte ISO 8601 UTC (`2026-10-06T19:00:00.123Z`) : l'ordre alphabétique est l'ordre chronologique.
enum SQLDate {
    private static let withFraction = Date.ISO8601FormatStyle(includingFractionalSeconds: true)
    private static let plain = Date.ISO8601FormatStyle()

    static func format(_ date: Date) -> String { date.formatted(withFraction) }

    static func parse(_ text: String) -> Date? {
        (try? withFraction.parse(text)) ?? (try? plain.parse(text))
    }
}

/// Une ligne de résultat. Les accesseurs renvoient `nil` pour NULL ou un type inattendu.
struct SQLRow {
    let values: [String: SQLValue]

    subscript(_ column: String) -> SQLValue { values[column] ?? .null }

    func string(_ c: String) -> String? { if case .text(let s) = self[c] { s } else { nil } }
    func int(_ c: String) -> Int? { if case .int(let v) = self[c] { Int(v) } else { nil } }
    func double(_ c: String) -> Double? {
        switch self[c] {
        case .double(let d): d
        case .int(let i): Double(i)
        default: nil
        }
    }
    func bool(_ c: String) -> Bool { (int(c) ?? 0) != 0 }
    func uuid(_ c: String) -> UUID? { string(c).flatMap { UUID(uuidString: $0) } }
    func decimal(_ c: String) -> Decimal? { string(c).flatMap { Decimal(string: $0, locale: nil) } }
    func date(_ c: String) -> Date? { string(c).flatMap(SQLDate.parse) }
}

struct SQLiteError: Error, CustomStringConvertible {
    let code: Int32
    let message: String
    var description: String { "SQLite \(code) : \(message)" }
}

/// Pour que les stores affichent le message SQLite dans un toast plutôt qu'un texte générique.
extension SQLiteError: LocalizedError {
    var errorDescription: String? { message }
}

private let sqliteTransient = unsafeBitCast(-1, to: sqlite3_destructor_type.self)

/// Mince enveloppe autour de `sqlite3`. Non thread-safe : chaque instance appartient à un seul acteur (`LocalPosAPI`).
final class SQLiteDatabase {
    private var handle: OpaquePointer?

    /// `path` : chemin d'un fichier, ou `":memory:"`.
    init(path: String) throws {
        var db: OpaquePointer?
        let rc = sqlite3_open_v2(path, &db, SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX, nil)
        guard rc == SQLITE_OK, let db else {
            let message = db.map { String(cString: sqlite3_errmsg($0)) } ?? "ouverture impossible"
            sqlite3_close(db)
            throw SQLiteError(code: rc, message: message)
        }
        handle = db
        try exec("PRAGMA foreign_keys = ON")
        try exec("PRAGMA journal_mode = WAL")
    }

    deinit { sqlite3_close(handle) }

    /// Exécute un ou plusieurs ordres sans paramètres (scripts de schéma, PRAGMA).
    func exec(_ sql: String) throws {
        var message: UnsafeMutablePointer<CChar>?
        guard sqlite3_exec(handle, sql, nil, nil, &message) == SQLITE_OK else {
            let text = message.map { String(cString: $0) } ?? "échec"
            sqlite3_free(message)
            throw SQLiteError(code: sqlite3_errcode(handle), message: text)
        }
    }

    /// Exécute un ordre d'écriture ; renvoie le nombre de lignes modifiées.
    @discardableResult
    func run(_ sql: String, _ params: [SQLValue] = []) throws -> Int {
        let stmt = try prepare(sql, params)
        defer { sqlite3_finalize(stmt) }
        let rc = sqlite3_step(stmt)
        guard rc == SQLITE_DONE || rc == SQLITE_ROW else { throw lastError() }
        return Int(sqlite3_changes(handle))
    }

    func query(_ sql: String, _ params: [SQLValue] = []) throws -> [SQLRow] {
        let stmt = try prepare(sql, params)
        defer { sqlite3_finalize(stmt) }
        var rows: [SQLRow] = []
        while true {
            let rc = sqlite3_step(stmt)
            if rc == SQLITE_DONE { break }
            guard rc == SQLITE_ROW else { throw lastError() }
            var values: [String: SQLValue] = [:]
            for i in 0..<sqlite3_column_count(stmt) {
                let name = String(cString: sqlite3_column_name(stmt, i))
                switch sqlite3_column_type(stmt, i) {
                case SQLITE_INTEGER: values[name] = .int(sqlite3_column_int64(stmt, i))
                case SQLITE_FLOAT: values[name] = .double(sqlite3_column_double(stmt, i))
                case SQLITE_TEXT: values[name] = .text(sqlite3_column_text(stmt, i).map { String(cString: $0) } ?? "")
                case SQLITE_BLOB:
                    let count = Int(sqlite3_column_bytes(stmt, i))
                    values[name] = .blob(count == 0 ? Data() : Data(bytes: sqlite3_column_blob(stmt, i), count: count))
                default: values[name] = .null
                }
            }
            rows.append(SQLRow(values: values))
        }
        return rows
    }

    /// Transaction atomique : COMMIT si `body` réussit, ROLLBACK sinon. Pas d'imbrication.
    func transaction<T>(_ body: () throws -> T) throws -> T {
        try exec("BEGIN IMMEDIATE")
        do {
            let result = try body()
            try exec("COMMIT")
            return result
        } catch {
            try? exec("ROLLBACK")
            throw error
        }
    }

    func userVersion() throws -> Int { try query("PRAGMA user_version").first?.int("user_version") ?? 0 }

    private func prepare(_ sql: String, _ params: [SQLValue]) throws -> OpaquePointer {
        var stmt: OpaquePointer?
        guard sqlite3_prepare_v2(handle, sql, -1, &stmt, nil) == SQLITE_OK, let stmt else { throw lastError() }
        for (offset, param) in params.enumerated() {
            let index = Int32(offset + 1)
            let rc: Int32
            switch param {
            case .null: rc = sqlite3_bind_null(stmt, index)
            case .int(let v): rc = sqlite3_bind_int64(stmt, index, v)
            case .double(let v): rc = sqlite3_bind_double(stmt, index, v)
            case .text(let v): rc = sqlite3_bind_text(stmt, index, v, -1, sqliteTransient)
            case .blob(let v): rc = v.withUnsafeBytes { sqlite3_bind_blob(stmt, index, $0.baseAddress, Int32(v.count), sqliteTransient) }
            }
            guard rc == SQLITE_OK else {
                sqlite3_finalize(stmt)
                throw lastError()
            }
        }
        return stmt
    }

    private func lastError() -> SQLiteError {
        SQLiteError(code: sqlite3_errcode(handle), message: String(cString: sqlite3_errmsg(handle)))
    }
}
```

- [ ] **Step 4: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter SQLiteDatabase 2>&1 | grep -E "✘|Test run"`
Expected: `Test run with 6 tests … passed`.

- [ ] **Step 5: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `160 tests … passed`.

Dans le fichier de suivi, passer la ligne « 1 Enveloppe SQLite » à `fait`, PosKit `160`, .NET/Web `inchangé`.

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/SQLiteDatabase.swift ios/Packages/PosKit/Tests/PosKitTests/Local/SQLiteDatabaseTests.swift docs/plans/ipad-standalone-1a-progress.md
git commit -m "feat(ios-local): enveloppe SQLite (SQLiteDatabase) pour le mode autonome"
```

---

### Task 2: Schéma v1, migrations et hachage des PIN

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/PinHasher.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalMigrator.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalMigratorTests.swift`

**Interfaces:**
- Consumes: `SQLiteDatabase` (`exec`, `transaction`, `userVersion`, `query`), `SQLiteError` (tâche 1).
- Produces :
  - `enum PinHasher { static func hash(pin: String, salt: String) -> String; static func makeSalt() -> String }`
  - `enum LocalMigrator { static let steps: [String]; static let schemaV1: String; static func migrate(_ db: SQLiteDatabase) throws }`

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalMigratorTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalMigrator et PinHasher")
struct LocalMigratorTests {
    @Test func createsSchemaAndRecordsVersion() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        let tables = try db.query("SELECT name FROM sqlite_master WHERE type = 'table'").compactMap { $0.string("name") }
        for expected in ["Users", "Categories", "Products", "ModifierGroups", "ModifierOptions", "DiningTables", "RestaurantSettings", "GridLayouts", "GridSlots"] {
            #expect(tables.contains(expected))
        }
    }

    @Test func isIdempotent() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try LocalMigrator.migrate(db)
        #expect(try db.userVersion() == LocalMigrator.steps.count)
    }

    @Test func rejectsDatabaseNewerThanTheApp() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try db.exec("PRAGMA user_version = \(LocalMigrator.steps.count + 1)")
        #expect(throws: SQLiteError.self) { try LocalMigrator.migrate(db) }
    }

    @Test func pinHashMatchesDotNetAlgorithm() {
        // SHA-256("00ff" + "1234"), hexadécimal minuscule — même calcul que OperatorAuthenticationService.HashPin.
        #expect(PinHasher.hash(pin: "1234", salt: "00ff") == "60dfb59aeea14604cda73d3d6d1d95f4fbf93cb8dad2d5d4b4ae93ae28c4e330")
    }

    @Test func saltsAre32LowercaseHexCharsAndDiffer() {
        let a = PinHasher.makeSalt(), b = PinHasher.makeSalt()
        #expect(a.count == 32 && a == a.lowercased() && a.allSatisfy(\.isHexDigit))
        #expect(a != b)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalMigrator" 2>&1 | tail -5`
Expected: erreur de compilation `cannot find 'LocalMigrator' in scope`.

- [ ] **Step 3: Implémenter `PinHasher`**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/PinHasher.swift` :
```swift
import CryptoKit
import Foundation

/// Hachage des PIN compatible avec `OperatorAuthenticationService.HashPin` : SHA-256 de `salt + pin`, en hexadécimal minuscule.
enum PinHasher {
    static func hash(pin: String, salt: String) -> String {
        SHA256.hash(data: Data((salt + pin).utf8)).map { String(format: "%02x", $0) }.joined()
    }

    static func makeSalt() -> String {
        (0..<16).map { _ in String(format: "%02x", UInt8.random(in: .min ... .max)) }.joined()
    }
}
```

- [ ] **Step 4: Implémenter `LocalMigrator` et le schéma v1**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalMigrator.swift` :
```swift
import Foundation

/// Migrations versionnées par `PRAGMA user_version`. Ne jamais modifier un script déjà publié : en ajouter un.
enum LocalMigrator {
    static let steps: [String] = [schemaV1]

    static func migrate(_ db: SQLiteDatabase) throws {
        let current = try db.userVersion()
        guard current <= steps.count else {
            throw SQLiteError(code: -1, message: "Base plus récente que l'application (version \(current))")
        }
        try db.transaction {
            for (index, script) in steps.enumerated() where index >= current {
                try db.exec(script)
                try db.exec("PRAGMA user_version = \(index + 1)")
            }
        }
    }

    // Tables nommées d'après les `DbSet` de `AppDbContext` ; Guid = TEXT majuscules, Money = INTEGER (centimes),
    // decimal = TEXT, DateTimeOffset = TEXT ISO 8601 UTC, enum = INTEGER.
    static let schemaV1 = """
    CREATE TABLE Users (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        Role INTEGER NOT NULL,
        PinHash TEXT NOT NULL,
        PinSalt TEXT NOT NULL,
        IsActive INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_Users_IsActive ON Users (IsActive);

    CREATE TABLE Categories (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        IconName TEXT,
        ColorHex TEXT,
        PreparationStationId TEXT,
        DisplayOrder INTEGER NOT NULL,
        IsActive INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_Categories_DisplayOrder ON Categories (DisplayOrder);

    CREATE TABLE Products (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        CategoryId TEXT NOT NULL,
        Description TEXT,
        Price INTEGER NOT NULL,
        TaxRatePercent TEXT NOT NULL,
        TaxRateTakeawayPercent TEXT,
        IsFoodVoucherEligible INTEGER NOT NULL,
        ColorHex TEXT,
        DisplayOrder INTEGER NOT NULL,
        IsAvailable INTEGER NOT NULL,
        IsActive INTEGER NOT NULL,
        IsQuickKey INTEGER NOT NULL,
        PreparationStationId TEXT,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT NOT NULL,
        Modifiers TEXT NOT NULL DEFAULT '[]'
    );

    CREATE TABLE ModifierGroups (
        Id TEXT NOT NULL PRIMARY KEY,
        ProductId TEXT NOT NULL,
        GroupName TEXT NOT NULL,
        MinSelections INTEGER NOT NULL,
        MaxSelections INTEGER NOT NULL,
        DisplayOrder INTEGER NOT NULL
    );

    CREATE TABLE ModifierOptions (
        Id TEXT NOT NULL PRIMARY KEY,
        GroupId TEXT NOT NULL REFERENCES ModifierGroups (Id) ON DELETE CASCADE,
        Name TEXT NOT NULL,
        ExtraPrice INTEGER NOT NULL,
        IsDefault INTEGER NOT NULL,
        DisplayOrder INTEGER NOT NULL
    );

    CREATE TABLE DiningTables (
        TableNumber TEXT NOT NULL PRIMARY KEY,
        Capacity INTEGER NOT NULL,
        Status INTEGER NOT NULL,
        PositionX REAL NOT NULL,
        PositionY REAL NOT NULL,
        AssignedWaiterName TEXT,
        AssignedWaiterId TEXT,
        CoversCount INTEGER NOT NULL,
        ActiveOrderId TEXT,
        OpenedAtUtc TEXT,
        UpdatedAtUtc TEXT NOT NULL
    );

    CREATE TABLE RestaurantSettings (
        Id INTEGER NOT NULL PRIMARY KEY,
        ReceiptLanguage TEXT NOT NULL,
        KitchenTicketLanguage TEXT NOT NULL,
        CompanyName TEXT NOT NULL,
        AddressLines TEXT NOT NULL,
        Siret TEXT NOT NULL,
        VatNumber TEXT NOT NULL,
        CertificateNumber TEXT,
        FiscalYearStartMonth INTEGER NOT NULL,
        FiscalYearStartDay INTEGER NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );

    CREATE TABLE GridLayouts (
        Id TEXT NOT NULL PRIMARY KEY,
        CategoryId TEXT NOT NULL,
        Name TEXT NOT NULL,
        ColumnsCount INTEGER NOT NULL,
        RowsCount INTEGER NOT NULL,
        PageIndex INTEGER NOT NULL,
        Version INTEGER NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );
    CREATE UNIQUE INDEX IX_GridLayouts_CategoryId_PageIndex ON GridLayouts (CategoryId, PageIndex);

    CREATE TABLE GridSlots (
        Id TEXT NOT NULL PRIMARY KEY,
        GridLayoutId TEXT NOT NULL REFERENCES GridLayouts (Id) ON DELETE CASCADE,
        ProductId TEXT REFERENCES Products (Id) ON DELETE SET NULL,
        RowIndex INTEGER NOT NULL,
        ColumnIndex INTEGER NOT NULL,
        SlotIndex INTEGER NOT NULL,
        CustomLabel TEXT,
        CustomColorHex TEXT,
        IsDisabled INTEGER NOT NULL
    );
    CREATE UNIQUE INDEX IX_GridSlots_GridLayoutId_RowIndex_ColumnIndex ON GridSlots (GridLayoutId, RowIndex, ColumnIndex);
    """
}
```

- [ ] **Step 5: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalMigrator" 2>&1 | grep -E "✘|Test run"`
Expected: `Test run with 5 tests … passed`.

- [ ] **Step 6: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `165 tests … passed`.

Mettre à jour le fichier de suivi (tâche 2 `fait`, PosKit `165`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/PinHasher.swift ios/Packages/PosKit/Sources/PosKit/Local/LocalMigrator.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalMigratorTests.swift docs/plans/ipad-standalone-1a-progress.md
git commit -m "feat(ios-local): schéma SQLite v1 (tables du .NET), migrations par user_version, hachage PIN"
```

---

### Task 3: Acteur `LocalPosAPI` : authentification et personnel

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalStaffRepository.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSeeder.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Staff.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalTestSupport.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalAuthTests.swift`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalStaffTests.swift`

**Interfaces:**
- Consumes: `SQLiteDatabase`, `SQLRow`, `SQLValue`, `SQLiteError` (tâche 1) ; `LocalMigrator`, `PinHasher` (tâche 2) ; modèles PosKit existants `StaffMember`, `UserRole`, `LoginResponse`, `APIError`, `PosAPI` ; `Seed.make()` (existant, `InMemoryPosAPI.swift`).
- Produces :
  - `public actor LocalPosAPI: PosAPI { public init(path: String) throws; let db: SQLiteDatabase; static let pinLength = 4; var staffRepository: LocalStaffRepository; func unsupported(_ name: String = #function) -> APIError; @discardableResult func requireAuth() throws -> StaffMember; @discardableResult func requireManager() throws -> StaffMember }`
  - `struct LocalStaffRepository { let db; members() ; member(id:) ; activeMember(pin:) ; isPinInUse(_:excluding:) ; insert(_:pin:) ; update(id:name:role:pin:isActive:) -> Bool }`
  - `extension UserRole { var storageValue: Int; init(storageValue: Int) }`
  - `enum LocalSeeder { static func seedIfEmpty(_ db: SQLiteDatabase) throws }` (seed du personnel uniquement dans cette tâche).
  - Helpers de test : `func makeLocalAPI(pin: String = "1234") async throws -> LocalPosAPI`, `func temporaryDatabasePath() -> String`.

- [ ] **Step 1: Écrire les helpers et les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalTestSupport.swift` :
```swift
import Foundation
@testable import PosKit

/// API locale en mémoire, déjà déverrouillée avec le PIN donné (1234 = responsable, 2468 = serveur, 9999 = admin).
func makeLocalAPI(pin: String = "1234") async throws -> LocalPosAPI {
    let api = try LocalPosAPI(path: ":memory:")
    _ = try await api.login(pin: pin)
    return api
}

func temporaryDatabasePath() -> String {
    FileManager.default.temporaryDirectory.appendingPathComponent("pos-\(UUID().uuidString).sqlite").path
}
```

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalAuthTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : authentification")
struct LocalAuthTests {
    @Test func conformsToPosAPI() throws {
        let api: PosAPI = try LocalPosAPI(path: ":memory:")
        _ = api
    }

    @Test func validPinReturnsOperatorAndToken() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        let result = try await api.login(pin: "1234")
        #expect(result.success)
        #expect(result.role == .floorManager)
        #expect(result.token != nil)
    }

    @Test func wrongPinFailsWithServerMessage() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        let result = try await api.login(pin: "0000")
        #expect(!result.success)
        #expect(result.errorMessage == "Code PIN ou identifiants incorrects")
    }

    @Test func fiveFailuresLockLoginEvenWithTheRightPin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        for _ in 0..<5 { _ = try await api.login(pin: "0000") }
        await #expect(throws: APIError.rateLimited(nil)) { try await api.login(pin: "1234") }
    }

    @Test func successResetsTheFailureCounter() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        for _ in 0..<4 { _ = try await api.login(pin: "0000") }
        #expect(try await api.login(pin: "1234").success)
        for _ in 0..<4 { _ = try await api.login(pin: "0000") }
        #expect(try await api.login(pin: "1234").success)
    }

    @Test func protectedCallsNeedLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.createStaff(name: "X", role: .waiter, pin: "1357") }
    }

    @Test func waiterCannotUseManagerActions() async throws {
        let api = try await makeLocalAPI(pin: "2468")
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) {
            try await api.createStaff(name: "X", role: .waiter, pin: "1357")
        }
    }

    @Test func clearingTheTokenLocksTheSession() async throws {
        let api = try await makeLocalAPI()
        await api.setToken(nil)
        await #expect(throws: APIError.unauthorized) { try await api.createStaff(name: "X", role: .waiter, pin: "1357") }
    }

    @Test func unsupportedMethodsAnswer501() async throws {
        let api = try await makeLocalAPI()
        await #expect(throws: APIError.server(status: 501, message: "Non disponible en mode autonome : kitchenTickets()")) {
            try await api.kitchenTickets()
        }
    }
}
```

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalStaffTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : personnel")
struct LocalStaffTests {
    @Test func seedsFourAccounts() async throws {
        let api = try await makeLocalAPI()
        let staff = try await api.staff()
        #expect(staff.map(\.role) == [.floorManager, .waiter, .kitchenStaff, .admin])
    }

    @Test func createdMemberCanLogIn() async throws {
        let api = try await makeLocalAPI()
        try await api.createStaff(name: "Léa", role: .cashier, pin: "4321")
        let result = try await api.login(pin: "4321")
        #expect(result.success && result.operatorName == "Léa" && result.role == .cashier)
    }

    @Test func rejectsDuplicateAndMalformedPins() async throws {
        let api = try await makeLocalAPI()
        await #expect(throws: APIError.server(status: 400, message: "Ce code PIN est déjà attribué.")) {
            try await api.createStaff(name: "Copie", role: .waiter, pin: "2468")
        }
        for bad in ["123", "12345", "12a4", "١٢٣٤"] {
            await #expect(throws: APIError.server(status: 400, message: "Le code PIN doit comporter 4 chiffres.")) {
                try await api.createStaff(name: "Mauvais", role: .waiter, pin: bad)
            }
        }
    }

    @Test func pinsKeepLeadingZeros() async throws {
        let api = try await makeLocalAPI()
        try await api.createStaff(name: "Zéro", role: .waiter, pin: "0042")
        #expect(try await api.login(pin: "0042").success)
        #expect(try await api.login(pin: "42").success == false)
    }

    @Test func nonLatinNamesRoundTrip() async throws {
        let api = try await makeLocalAPI()
        try await api.createStaff(name: "ليلى 🍓", role: .waiter, pin: "3141")
        #expect(try await api.staff().last?.name == "ليلى 🍓")
        #expect(try await api.login(pin: "3141").operatorName == "ليلى 🍓")
    }

    @Test func updateChangesRoleAndPin() async throws {
        let api = try await makeLocalAPI()
        let waiter = try #require(try await api.staff().first { $0.role == .waiter })
        try await api.updateStaff(id: waiter.id, name: "Sophie", role: .cashier, pin: "1111", isActive: true)
        #expect(try await api.login(pin: "2468").success == false)
        let result = try await api.login(pin: "1111")
        #expect(result.success && result.role == .cashier)
    }

    @Test func deactivatedMemberCannotLogIn() async throws {
        let api = try await makeLocalAPI()
        let waiter = try #require(try await api.staff().first { $0.role == .waiter })
        try await api.deactivateStaff(id: waiter.id)
        #expect(try await api.login(pin: "2468").success == false)
    }

    @Test func lastActiveManagerCannotBeRemoved() async throws {
        let api = try await makeLocalAPI()
        let staff = try await api.staff()
        let floorManager = try #require(staff.first { $0.role == .floorManager })
        let admin = try #require(staff.first { $0.role == .admin })
        // Connecté en responsable (1234) : retirer l'admin est permis tant qu'un autre responsable reste actif…
        try await api.deactivateStaff(id: admin.id)
        // …mais pas se retirer soi-même (désactiver ou rétrograder) : ce serait le dernier.
        let blocked = APIError.server(status: 400, message: "Au moins un responsable actif est requis.")
        await #expect(throws: blocked) { try await api.deactivateStaff(id: floorManager.id) }
        await #expect(throws: blocked) { try await api.updateStaff(id: floorManager.id, name: floorManager.name, role: .waiter, pin: nil, isActive: true) }
        #expect(try await api.login(pin: "1234").success)
        #expect(try await api.login(pin: "9999").success == false)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalPosAPI" 2>&1 | tail -5`
Expected: erreur de compilation `cannot find 'LocalPosAPI' in scope`.

- [ ] **Step 3: Implémenter le dépôt du personnel**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalStaffRepository.swift` :
```swift
import Foundation

extension UserRole {
    /// Valeur de l'enum `UserRole` du .NET (stockée en entier dans `Users.Role`).
    var storageValue: Int {
        switch self {
        case .waiter: 0
        case .cashier: 1
        case .kitchenStaff: 2
        case .floorManager: 3
        case .admin: 4
        }
    }

    init(storageValue: Int) {
        self = UserRole.allCases.first { $0.storageValue == storageValue } ?? .waiter
    }
}

struct LocalStaffRepository {
    let db: SQLiteDatabase

    func members() throws -> [StaffMember] {
        try db.query("SELECT * FROM Users ORDER BY rowid").map(Self.decode)
    }

    func member(id: UUID) throws -> StaffMember? {
        try db.query("SELECT * FROM Users WHERE Id = ?", [.uuid(id)]).first.map(Self.decode)
    }

    func activeMember(pin: String) throws -> StaffMember? {
        let rows = try db.query("SELECT * FROM Users WHERE IsActive = 1")
        return rows.first { Self.matches($0, pin: pin) }.map(Self.decode)
    }

    func isPinInUse(_ pin: String, excluding id: UUID? = nil) throws -> Bool {
        try db.query("SELECT * FROM Users").contains { $0.uuid("Id") != id && Self.matches($0, pin: pin) }
    }

    func insert(_ member: StaffMember, pin: String) throws {
        let salt = PinHasher.makeSalt()
        let now = Date()
        try db.run(
            "INSERT INTO Users (Id, Name, Role, PinHash, PinSalt, IsActive, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            [.uuid(member.id), .text(member.name), .integer(member.role.storageValue), .text(PinHasher.hash(pin: pin, salt: salt)),
             .text(salt), .bool(member.isActive), .date(now), .date(now)]
        )
    }

    /// `false` si l'identifiant est inconnu. `pin == nil` : code inchangé.
    func update(id: UUID, name: String, role: UserRole, pin: String?, isActive: Bool) throws -> Bool {
        let changed = try db.run(
            "UPDATE Users SET Name = ?, Role = ?, IsActive = ?, UpdatedAtUtc = ? WHERE Id = ?",
            [.text(name), .integer(role.storageValue), .bool(isActive), .date(Date()), .uuid(id)]
        )
        guard changed > 0 else { return false }
        if let pin {
            let salt = PinHasher.makeSalt()
            try db.run("UPDATE Users SET PinHash = ?, PinSalt = ? WHERE Id = ?", [.text(PinHasher.hash(pin: pin, salt: salt)), .text(salt), .uuid(id)])
        }
        return true
    }

    private static func matches(_ row: SQLRow, pin: String) -> Bool {
        PinHasher.hash(pin: pin, salt: row.string("PinSalt") ?? "") == row.string("PinHash")
    }

    private static func decode(_ row: SQLRow) -> StaffMember {
        StaffMember(id: row.uuid("Id")!, name: row.string("Name") ?? "", role: UserRole(storageValue: row.int("Role") ?? 0), isActive: row.bool("IsActive"))
    }
}
```

- [ ] **Step 4: Implémenter le seeder (personnel uniquement pour l'instant)**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalSeeder.swift` :
```swift
import Foundation

/// Données d'amorçage du premier lancement : mêmes comptes, familles, articles et tables que `Program.SeedDatabase`
/// (réutilise `Seed.make()` du backend en mémoire).
enum LocalSeeder {
    static func seedIfEmpty(_ db: SQLiteDatabase) throws {
        guard try db.query("SELECT COUNT(*) AS n FROM Users").first?.int("n") == 0 else { return }
        let seed = Seed.make()
        try db.transaction {
            let staff = LocalStaffRepository(db: db)
            for record in seed.staff { try staff.insert(record.member, pin: record.pin) }
        }
    }
}
```

- [ ] **Step 5: Implémenter l'acteur (noyau : session, verrouillage, garde-fous)**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift` :
```swift
import Foundation

/// Backend autonome de l'iPad : implémente `PosAPI` sur une base SQLite locale, sans serveur .NET.
/// Les méthodes pas encore portées répondent `501` (voir `LocalPosAPI+Unsupported.swift`).
public actor LocalPosAPI: PosAPI {
    let db: SQLiteDatabase
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

    var staffRepository: LocalStaffRepository { LocalStaffRepository(db: db) }

    func unsupported(_ name: String = #function) -> APIError {
        .server(status: 501, message: "Non disponible en mode autonome : \(name)")
    }

    // MARK: Authentification

    public func setToken(_ token: String?) { self.token = token }
    public func setDeviceToken(_ token: String?) {}

    public func login(pin: String) async throws -> LoginResponse {
        let now = Date()
        if let until = lockedUntil, until > now { throw APIError.rateLimited(nil) }
        guard let member = try staffRepository.activeMember(pin: pin) else {
            failedLogins = failedLogins.filter { now.timeIntervalSince($0) < Self.failureWindow } + [now]
            if failedLogins.count >= Self.maxFailedLogins {
                lockedUntil = now.addingTimeInterval(Self.lockoutDuration)
                failedLogins = []
            }
            return LoginResponse(success: false, operatorId: nil, operatorName: nil, role: nil, token: nil, errorMessage: "Code PIN ou identifiants incorrects")
        }
        failedLogins = []
        lockedUntil = nil
        token = Self.tokenPrefix + member.id.uuidString
        return LoginResponse(success: true, operatorId: member.id, operatorName: member.name, role: member.role, token: token)
    }

    @discardableResult
    func requireAuth() throws -> StaffMember {
        guard let token, token.hasPrefix(Self.tokenPrefix),
              let id = UUID(uuidString: String(token.dropFirst(Self.tokenPrefix.count))),
              let member = try staffRepository.member(id: id), member.isActive
        else { throw APIError.unauthorized }
        return member
    }

    @discardableResult
    func requireManager() throws -> StaffMember {
        let member = try requireAuth()
        guard member.role.isManager else { throw APIError.forbidden("Action réservée à un responsable.") }
        return member
    }

    public func health() async throws -> TimeInterval { 0 }
}
```

- [ ] **Step 6: Implémenter la gestion du personnel**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Staff.swift` :
```swift
import Foundation

extension LocalPosAPI {
    public func staff() async throws -> [StaffMember] { try staffRepository.members() }

    public func createStaff(name: String, role: UserRole, pin: String) async throws {
        try requireManager()
        try validate(pin: pin)
        guard try !staffRepository.isPinInUse(pin) else { throw APIError.server(status: 400, message: "Ce code PIN est déjà attribué.") }
        try staffRepository.insert(StaffMember(name: name, role: role), pin: pin)
    }

    public func updateStaff(id: UUID, name: String, role: UserRole, pin: String?, isActive: Bool) async throws {
        try requireManager()
        if let pin {
            try validate(pin: pin)
            guard try !staffRepository.isPinInUse(pin, excluding: id) else { throw APIError.server(status: 400, message: "Ce code PIN est déjà attribué.") }
        }
        guard let current = try staffRepository.member(id: id) else { throw APIError.notFound(nil) }
        try ensureManagerRemains(after: current, becoming: (role, isActive))
        _ = try staffRepository.update(id: id, name: name, role: role, pin: pin, isActive: isActive)
    }

    public func deactivateStaff(id: UUID) async throws {
        try requireManager()
        guard let current = try staffRepository.member(id: id) else { throw APIError.notFound(nil) }
        try ensureManagerRemains(after: current, becoming: (current.role, false))
        _ = try staffRepository.update(id: id, name: current.name, role: current.role, pin: nil, isActive: false)
    }

    private func validate(pin: String) throws {
        guard pin.count == Self.pinLength, pin.allSatisfy(\.isASCII), pin.allSatisfy(\.isNumber) else {
            throw APIError.server(status: 400, message: "Le code PIN doit comporter \(Self.pinLength) chiffres.")
        }
    }

    /// L'iPad n'a pas de serveur de secours : perdre le dernier responsable actif verrouillerait le back-office pour toujours.
    private func ensureManagerRemains(after member: StaffMember, becoming new: (role: UserRole, isActive: Bool)) throws {
        guard member.isActive, member.role.isManager, !(new.isActive && new.role.isManager) else { return }
        let activeManagers = try staffRepository.members().filter { $0.isActive && $0.role.isManager }
        if activeManagers.count <= 1 { throw APIError.server(status: 400, message: "Au moins un responsable actif est requis.") }
    }
}
```

- [ ] **Step 7: Déclarer les méthodes pas encore portées (501)**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift`. Les sections « retiré par la tâche N » sont supprimées au fil du plan ; les autres sont reprises par les plans 1b et 1c.
```swift
import Foundation

/// Méthodes de `PosAPI` pas encore portées sur la base locale. Chaque plan suivant retire ses lignes d'ici.
extension LocalPosAPI {
    // MARK: Catalogue (retiré par la tâche 4)
    public func categories() async throws -> [MenuCategory] { throw unsupported() }
    public func products() async throws -> [Product] { throw unsupported() }
    public func createCategory(name: String, colorHex: String, displayOrder: Int) async throws { throw unsupported() }
    public func updateCategory(id: String, name: String, colorHex: String, displayOrder: Int, preparationStationId: String?) async throws { throw unsupported() }
    public func createProduct(_ draft: ProductDraft) async throws { throw unsupported() }
    public func updateProduct(id: UUID, _ draft: ProductDraft) async throws { throw unsupported() }
    public func archiveProduct(id: UUID) async throws { throw unsupported() }

    // MARK: Salle et réglages (retiré par la tâche 5)
    public func tables() async throws -> [DiningTable] { throw unsupported() }
    public func createTable(number: String, capacity: Int) async throws { throw unsupported() }
    public func settings() async throws -> RestaurantSettings { throw unsupported() }
    public func saveSettings(_ settings: RestaurantSettings) async throws -> RestaurantSettings { throw unsupported() }

    // MARK: Grille tactile (retiré par la tâche 6)
    public func gridLayout(categoryId: String, page: Int) async throws -> TouchGridLayout { throw unsupported() }
    public func saveGridLayout(_ request: UpdateGridLayoutRequest) async throws -> TouchGridLayout { throw unsupported() }
    public func swapGridSlots(layoutId: UUID, from: GridPosition, to: GridPosition) async throws -> TouchGridLayout { throw unsupported() }
    public func updateGridDimensions(categoryId: String, columns: Int, rows: Int, applyToAll: Bool) async throws -> [TouchGridLayout] { throw unsupported() }

    // MARK: Appairage : sans objet en mode autonome (le poste est son propre terminal)
    public func pair(code: String) async throws -> PairResponse { throw unsupported() }

    // MARK: Salle & commandes (plan 1b)
    public func openTable(number: String, covers: Int, operatorId: UUID?, waiterName: String?) async throws { throw unsupported() }
    public func activeOrder(table: String) async throws -> ActiveOrder? { throw unsupported() }
    public func addItems(table: String, items: [OrderItemInput]) async throws -> ActiveOrder { throw unsupported() }
    public func dispatch(table: String) async throws { throw unsupported() }
    public func fireSuite(table: String) async throws { throw unsupported() }
    public func transfer(from: String, to: String, merge: Bool) async throws -> OperationResult { throw unsupported() }
    public func setDestination(orderId: UUID, destination: OrderDestination) async throws { throw unsupported() }
    public func applyDiscount(orderId: UUID, type: DiscountType, value: Decimal, reason: String, operatorId: UUID?) async throws { throw unsupported() }
    public func removeDiscount(orderId: UUID) async throws { throw unsupported() }
    public func compItem(orderId: UUID, lineId: UUID, reason: String, operatorId: UUID?) async throws { throw unsupported() }

    // MARK: Encaissement (plan 1b)
    public func pay(_ request: PaymentRequest) async throws -> PaymentResult { throw unsupported() }
    public func hotelRooms() async throws -> [HotelRoom] { throw unsupported() }
    public func chargeRoom(_ request: RoomChargeRequest) async throws -> OperationResult { throw unsupported() }

    // MARK: Comptoir & vente à emporter (plan 1b)
    public func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder { throw unsupported() }
    public func holdOrder(orderId: UUID, terminalId: String, label: String) async throws { throw unsupported() }
    public func heldOrders(terminalId: String) async throws -> [HeldOrder] { throw unsupported() }
    public func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder { throw unsupported() }
    public func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws { throw unsupported() }
    public func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult { throw unsupported() }

    // MARK: Cuisine (plan 1b)
    public func kitchenTickets() async throws -> [KitchenTicket] { throw unsupported() }
    public func bumpTicket(id: UUID) async throws { throw unsupported() }

    // MARK: Fiscal (sous-projets 2 et 3)
    public func xReport(terminalId: String) async throws -> FiscalReport { throw unsupported() }
    public func latestClosure(terminalId: String) async throws -> FiscalReport? { throw unsupported() }
    public func zClosure(terminalId: String, managerId: UUID, managerName: String) async throws -> FiscalReport { throw unsupported() }
    public func printXReport(terminalId: String) async throws -> Bool { throw unsupported() }
    public func reprintLatestClosure(terminalId: String) async throws -> ReprintResult { throw unsupported() }
    public func reprintReceipt(receiptIdentifier: String) async throws -> ReprintResult { throw unsupported() }
    public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data) { throw unsupported() }
    public func dashboard(from: Date, to: Date) async throws -> FinancialDashboard { throw unsupported() }
    public func verifyChains() async throws -> FiscalVerificationResult { throw unsupported() }
    public func periodClosures(terminalId: String?, periodType: FiscalPeriodType?) async throws -> [FiscalPeriodClosure] { throw unsupported() }
    public func executePeriodClosure(terminalId: String, periodType: FiscalPeriodType, periodKey: String) async throws -> FiscalPeriodClosure { throw unsupported() }
    public func archives() async throws -> [FiscalArchive] { throw unsupported() }
    public func createArchive(periodClosureId: UUID) async throws -> FiscalArchive { throw unsupported() }
    public func verifyArchive(data: Data, fileName: String) async throws -> ArchiveVerificationResult { throw unsupported() }

    // MARK: Imprimantes (plan 1c)
    public func printers() async throws -> [Printer] { throw unsupported() }
    public func savePrinter(_ printer: Printer, isNew: Bool) async throws { throw unsupported() }
    public func testPrinter(id: UUID) async throws -> TestPrintResult { throw unsupported() }
    public func printerStatuses() async throws -> [PrinterStatus] { throw unsupported() }
    public func printJobs(printerId: UUID) async throws -> [PrintJobInfo] { throw unsupported() }
    public func retryPrintJob(id: UUID) async throws { throw unsupported() }
    public func cancelPrintJob(id: UUID) async throws { throw unsupported() }

    // MARK: Happy Hour (plan 1c)
    public func happyHourStatus(terminalId: String) async throws -> HappyHourStatus { throw unsupported() }
    public func happyHourPricing(terminalId: String) async throws -> HappyHourPricingTable { throw unsupported() }
    public func activateHappyHourOverride(terminalId: String, pin: String, minutes: Int, reason: String) async throws -> OperationResult { throw unsupported() }
    public func stopHappyHourOverride(terminalId: String, pin: String, reason: String) async throws -> OperationResult { throw unsupported() }
    public func happyHourSchedules() async throws -> [HappyHourSchedule] { throw unsupported() }
    public func createHappyHourSchedule(_ schedule: HappyHourSchedule) async throws -> UUID? { throw unsupported() }
    public func deleteHappyHourSchedule(id: UUID) async throws { throw unsupported() }
    public func applyHappyHourRules(scheduleId: UUID, _ request: BatchPriceRulesRequest) async throws -> Int { throw unsupported() }
    public func deleteHappyHourRules(scheduleId: UUID, ruleIds: [UUID]) async throws { throw unsupported() }

    // MARK: Réseau (plan 1c)
    public func networkInfo() async throws -> NetworkInfo { throw unsupported() }
    public func syncStatus() async throws -> SyncStatus { throw unsupported() }
    public func forceSync() async throws { throw unsupported() }
}
```

- [ ] **Step 8: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalPosAPI" 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 17 tests … passed` (9 d'authentification + 8 de personnel), aucune erreur de compilation ni avertissement.

- [ ] **Step 9: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `182 tests … passed`.

Mettre à jour le fichier de suivi (tâche 3 `fait`, PosKit `182`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1a-progress.md
git commit -m "feat(ios-local): LocalPosAPI (auth PIN, verrouillage, personnel) sur SQLite"
```

---

### Task 4: Catalogue (familles, articles, modificateurs)

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalCatalogRepository.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Catalog.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift` (accesseur `catalogRepository`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSeeder.swift` (amorçage des familles et articles)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift` (retirer la section Catalogue)
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalCatalogTests.swift`

**Interfaces:**
- Consumes: `LocalPosAPI.requireManager()`, `db` (tâche 3) ; `SQLiteDatabase` (tâche 1) ; modèles `MenuCategory`, `Product`, `ProductDraft`, `ModifierGroup`, `ModifierOption`, `Money`.
- Produces: `struct LocalCatalogRepository { let db; activeCategories(); insert(_: MenuCategory); updateCategory(id:name:colorHex:displayOrder:stationId:) -> Bool; activeProducts(); insert(_: Product); update(productId:_:) -> Bool; archive(productId:) -> Bool }` ; `LocalPosAPI.catalogRepository`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalCatalogTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : catalogue")
struct LocalCatalogTests {
    @Test func seedsTheDemoCatalogue() async throws {
        let api = try await makeLocalAPI()
        let categories = try await api.categories()
        let products = try await api.products()
        #expect(categories.count == 5)
        #expect(products.count == 11)
        let burger = try #require(products.first { $0.name == "Burger Gourmet Rossini" })
        #expect(burger.modifierGroups.count == 2)
        let cooking = try #require(burger.modifierGroups.first)
        #expect(cooking.groupName == "Cuisson de la Viande")
        #expect(cooking.isMandatory && cooking.isSingleChoice)
        #expect(cooking.options.map(\.name) == ["Bleu", "Saignant", "À Point", "Bien Cuit"])
        #expect(cooking.options.first { $0.isDefault }?.name == "À Point")
        let pizza = try #require(products.first { $0.name == "Pizza Margherita AOP" })
        #expect(pizza.taxRatePercent == 10 && pizza.taxRateTakeawayPercent == 5.5)
        #expect(pizza.price == Money(cents: 1250))
    }

    @Test func waiterCannotEditCatalogue() async throws {
        let api = try await makeLocalAPI(pin: "2468")
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) {
            try await api.createCategory(name: "X", colorHex: "#000000", displayOrder: 9)
        }
    }

    @Test func createdCategoryAppearsInOrder() async throws {
        let api = try await makeLocalAPI()
        try await api.createCategory(name: "Brunch", colorHex: "#112233", displayOrder: 99)
        let last = try #require(try await api.categories().last)
        #expect(last.name == "Brunch" && last.colorHex == "#112233" && last.id.hasPrefix("CAT_"))
    }

    @Test func updateCategoryStationRules() async throws {
        let api = try await makeLocalAPI()
        try await api.updateCategory(id: "CAT_DRINKS", name: "Boissons", colorHex: "#000001", displayOrder: 5, preparationStationId: "BAR")
        #expect(try await api.categories().first { $0.id == "CAT_DRINKS" }?.preparationStationId == "BAR")
        // nil = inchangé
        try await api.updateCategory(id: "CAT_DRINKS", name: "Boissons", colorHex: "#000001", displayOrder: 5, preparationStationId: nil)
        #expect(try await api.categories().first { $0.id == "CAT_DRINKS" }?.preparationStationId == "BAR")
        // "" = effacer
        try await api.updateCategory(id: "CAT_DRINKS", name: "Boissons", colorHex: "#000001", displayOrder: 5, preparationStationId: "")
        #expect(try await api.categories().first { $0.id == "CAT_DRINKS" }?.preparationStationId == nil)
        await #expect(throws: APIError.notFound(nil)) {
            try await api.updateCategory(id: "NOPE", name: "x", colorHex: "#000000", displayOrder: 0, preparationStationId: nil)
        }
    }

    @Test func unicodeProductNameRoundTrips() async throws {
        let api = try await makeLocalAPI()
        try await api.createProduct(ProductDraft(name: "Crêpe Suzette 🍊 قهوة", categoryId: "CAT_DESSERTS", price: Money(cents: 700), description: "Flambée à l'Grand-Marnier"))
        let created = try #require(try await api.products().first { $0.name.hasPrefix("Crêpe") })
        #expect(created.name == "Crêpe Suzette 🍊 قهوة" && created.description == "Flambée à l'Grand-Marnier")
    }

    @Test func productLifecycle() async throws {
        let api = try await makeLocalAPI()
        let draft = ProductDraft(name: "Mojito", categoryId: "CAT_DRINKS", price: Money(cents: 850), taxRatePercent: 20, stationId: "BAR", isQuickKey: true, displayOrder: 7, description: "Menthe")
        try await api.createProduct(draft)
        var created = try #require(try await api.products().first { $0.name == "Mojito" })
        #expect(created.price == Money(cents: 850) && created.taxRatePercent == 20 && created.isQuickKey)
        #expect(created.description == "Menthe" && created.preparationStationId == "BAR")

        var edited = ProductDraft(product: created)
        edited.name = "Mojito Maison"
        edited.price = Money(cents: 900)
        try await api.updateProduct(id: created.id, edited)
        created = try #require(try await api.products().first { $0.id == created.id })
        #expect(created.name == "Mojito Maison" && created.price == Money(cents: 900))

        try await api.archiveProduct(id: created.id)
        #expect(try await api.products().contains { $0.id == created.id } == false)
        await #expect(throws: APIError.notFound(nil)) { try await api.archiveProduct(id: UUID()) }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalPosAPI : catalogue" 2>&1 | grep -E "✘|Test run" | head`
Expected: échecs `APIError.server(status: 501 …)` (les méthodes répondent encore « non disponible »).

- [ ] **Step 3: Implémenter le dépôt du catalogue**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalCatalogRepository.swift` :
```swift
import Foundation

struct LocalCatalogRepository {
    let db: SQLiteDatabase

    func activeCategories() throws -> [MenuCategory] {
        try db.query("SELECT * FROM Categories WHERE IsActive = 1 ORDER BY DisplayOrder, rowid").map {
            MenuCategory(id: $0.string("Id") ?? "", name: $0.string("Name") ?? "", iconName: $0.string("IconName"), colorHex: $0.string("ColorHex"),
                         displayOrder: $0.int("DisplayOrder"), isActive: $0.bool("IsActive"), preparationStationId: $0.string("PreparationStationId"))
        }
    }

    func insert(_ c: MenuCategory) throws {
        let now = Date()
        try db.run(
            "INSERT INTO Categories (Id, Name, IconName, ColorHex, PreparationStationId, DisplayOrder, IsActive, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [.text(c.id), .text(c.name), .string(c.iconName), .string(c.colorHex), .string(c.preparationStationId),
             .integer(c.displayOrder ?? 0), .bool(c.isActive ?? true), .date(now), .date(now)]
        )
    }

    /// `stationId` : `nil` = inchangé, `""` = effacer (même règle que le PUT de l'API). `false` si la famille est inconnue.
    func updateCategory(id: String, name: String, colorHex: String, displayOrder: Int, stationId: String?) throws -> Bool {
        try db.run(
            """
            UPDATE Categories SET Name = ?1, ColorHex = ?2, DisplayOrder = ?3,
                PreparationStationId = CASE WHEN ?4 IS NULL THEN PreparationStationId WHEN ?4 = '' THEN NULL ELSE ?4 END,
                UpdatedAtUtc = ?5
            WHERE Id = ?6
            """,
            [.text(name), .text(colorHex), .integer(displayOrder), .string(stationId), .date(Date()), .text(id)]
        ) > 0
    }

    func activeProducts() throws -> [Product] {
        let groups = try modifierGroupsByProduct()
        return try db.query("SELECT * FROM Products WHERE IsActive = 1 ORDER BY DisplayOrder, rowid").map {
            Product(
                id: $0.uuid("Id")!, name: $0.string("Name") ?? "", categoryId: $0.string("CategoryId") ?? "", description: $0.string("Description"),
                price: Money(cents: $0.int("Price") ?? 0), taxRatePercent: $0.decimal("TaxRatePercent") ?? 10,
                taxRateTakeawayPercent: $0.decimal("TaxRateTakeawayPercent"), colorHex: $0.string("ColorHex"), displayOrder: $0.int("DisplayOrder"),
                isQuickKey: $0.bool("IsQuickKey"), preparationStationId: $0.string("PreparationStationId"),
                isAvailable: $0.bool("IsAvailable"), isActive: $0.bool("IsActive"), modifierGroups: groups[$0.uuid("Id")!] ?? []
            )
        }
    }

    func insert(_ p: Product) throws {
        let now = Date()
        try db.run(
            """
            INSERT INTO Products (Id, Name, CategoryId, Description, Price, TaxRatePercent, TaxRateTakeawayPercent, IsFoodVoucherEligible, ColorHex,
                DisplayOrder, IsAvailable, IsActive, IsQuickKey, PreparationStationId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(p.id), .text(p.name), .text(p.categoryId), .string(p.description), .integer(p.price.cents), .decimal(p.taxRatePercent),
             .decimal(p.taxRateTakeawayPercent), .string(p.colorHex), .integer(p.displayOrder ?? 0), .bool(p.isAvailable ?? true),
             .bool(p.isActive ?? true), .bool(p.isQuickKey), .string(p.preparationStationId), .date(now), .date(now)]
        )
        // Un groupe appartient à un seul article (`ModifierGroups.ProductId`) : les lignes reçoivent de nouveaux identifiants,
        // car un même `ModifierGroup` du modèle peut être partagé par plusieurs articles (ex. « Cuisson »).
        for (groupIndex, group) in p.modifierGroups.enumerated() {
            let groupId = UUID()
            try db.run(
                "INSERT INTO ModifierGroups (Id, ProductId, GroupName, MinSelections, MaxSelections, DisplayOrder) VALUES (?, ?, ?, ?, ?, ?)",
                [.uuid(groupId), .uuid(p.id), .text(group.groupName), .integer(group.minSelections), .integer(group.maxSelections), .integer(groupIndex)]
            )
            for (optionIndex, option) in group.options.enumerated() {
                try db.run(
                    "INSERT INTO ModifierOptions (Id, GroupId, Name, ExtraPrice, IsDefault, DisplayOrder) VALUES (?, ?, ?, ?, ?, ?)",
                    [.uuid(UUID()), .uuid(groupId), .text(option.name), .integer(option.extraPrice.cents), .bool(option.isDefault), .integer(optionIndex)]
                )
            }
        }
    }

    /// `false` si l'article est inconnu.
    func update(productId: UUID, _ d: ProductDraft) throws -> Bool {
        try db.run(
            """
            UPDATE Products SET Name = ?, CategoryId = ?, Description = ?, Price = ?, TaxRatePercent = ?, ColorHex = ?, DisplayOrder = ?,
                IsQuickKey = ?, PreparationStationId = ?, UpdatedAtUtc = ?
            WHERE Id = ?
            """,
            [.text(d.name), .text(d.categoryId), .string(d.description.isEmpty ? nil : d.description), .integer(d.price.cents), .decimal(d.taxRatePercent),
             .string(d.colorHex), .integer(d.displayOrder), .bool(d.isQuickKey), .string(d.stationId), .date(Date()), .uuid(productId)]
        ) > 0
    }

    /// Archivage logique (l'article reste référencé par les commandes passées). `false` si inconnu.
    func archive(productId: UUID) throws -> Bool {
        try db.run("UPDATE Products SET IsActive = 0, UpdatedAtUtc = ? WHERE Id = ?", [.date(Date()), .uuid(productId)]) > 0
    }

    private func modifierGroupsByProduct() throws -> [UUID: [ModifierGroup]] {
        let options = Dictionary(grouping: try db.query("SELECT * FROM ModifierOptions ORDER BY DisplayOrder, rowid"), by: { $0.uuid("GroupId")! })
        var result: [UUID: [ModifierGroup]] = [:]
        for row in try db.query("SELECT * FROM ModifierGroups ORDER BY DisplayOrder, rowid") {
            let id = row.uuid("Id")!
            let minSelections = row.int("MinSelections") ?? 0
            let maxSelections = row.int("MaxSelections") ?? 1
            let group = ModifierGroup(
                id: id, groupName: row.string("GroupName") ?? "", minSelections: minSelections, maxSelections: maxSelections,
                isMandatory: minSelections > 0, isSingleChoice: maxSelections == 1,
                options: (options[id] ?? []).map {
                    ModifierOption(id: $0.uuid("Id")!, name: $0.string("Name") ?? "", extraPrice: Money(cents: $0.int("ExtraPrice") ?? 0), isDefault: $0.bool("IsDefault"))
                }
            )
            result[row.uuid("ProductId")!, default: []].append(group)
        }
        return result
    }
}
```

- [ ] **Step 4: Brancher l'acteur : accesseur, méthodes, seed, retrait des 501**

Dans `LocalPosAPI.swift`, remplacer :
```swift
    var staffRepository: LocalStaffRepository { LocalStaffRepository(db: db) }
```
par :
```swift
    var staffRepository: LocalStaffRepository { LocalStaffRepository(db: db) }
    var catalogRepository: LocalCatalogRepository { LocalCatalogRepository(db: db) }
```

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Catalog.swift` :
```swift
import Foundation

extension LocalPosAPI {
    public func categories() async throws -> [MenuCategory] { try catalogRepository.activeCategories() }
    public func products() async throws -> [Product] { try catalogRepository.activeProducts() }

    public func createCategory(name: String, colorHex: String, displayOrder: Int) async throws {
        try requireManager()
        try catalogRepository.insert(MenuCategory(id: "CAT_\(UUID().uuidString.prefix(8))", name: name, iconName: "utensils", colorHex: colorHex, displayOrder: displayOrder))
    }

    public func updateCategory(id: String, name: String, colorHex: String, displayOrder: Int, preparationStationId: String?) async throws {
        try requireManager()
        guard try catalogRepository.updateCategory(id: id, name: name, colorHex: colorHex, displayOrder: displayOrder, stationId: preparationStationId) else {
            throw APIError.notFound(nil)
        }
    }

    public func createProduct(_ d: ProductDraft) async throws {
        try requireManager()
        try catalogRepository.insert(Product(
            name: d.name, categoryId: d.categoryId, description: d.description, price: d.price, taxRatePercent: d.taxRatePercent,
            colorHex: d.colorHex, displayOrder: d.displayOrder, isQuickKey: d.isQuickKey, preparationStationId: d.stationId
        ))
    }

    public func updateProduct(id: UUID, _ d: ProductDraft) async throws {
        try requireManager()
        guard try catalogRepository.update(productId: id, d) else { throw APIError.notFound(nil) }
    }

    public func archiveProduct(id: UUID) async throws {
        try requireManager()
        guard try catalogRepository.archive(productId: id) else { throw APIError.notFound(nil) }
    }
}
```

Dans `LocalSeeder.swift`, remplacer :
```swift
            for record in seed.staff { try staff.insert(record.member, pin: record.pin) }
```
par :
```swift
            for record in seed.staff { try staff.insert(record.member, pin: record.pin) }
            let catalog = LocalCatalogRepository(db: db)
            for category in seed.categories { try catalog.insert(category) }
            for product in seed.products { try catalog.insert(product) }
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Catalogue (retiré par la tâche 4)` : ce commentaire et les 7 lignes `categories`, `products`, `createCategory`, `updateCategory`, `createProduct`, `updateProduct`, `archiveProduct`, ainsi que la ligne vide qui suit.

- [ ] **Step 5: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalPosAPI" 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 23 tests … passed` (9 + 8 + 6), aucune erreur ni avertissement.

- [ ] **Step 6: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `188 tests … passed`.

Mettre à jour le fichier de suivi (tâche 4 `fait`, PosKit `188`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1a-progress.md
git commit -m "feat(ios-local): catalogue local (familles, articles, modificateurs) sur SQLite"
```

---

### Task 5: Salle (tables) et réglages du restaurant

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFloorRepository.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Floor.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift` (accesseur `floorRepository`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalSeeder.swift` (tables et réglages)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift` (retirer la section Salle et réglages)
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFloorTests.swift`

**Interfaces:**
- Consumes: `requireAuth()`, `requireManager()`, `db`, `catalogRepository` accessor pattern (tâches 3-4) ; modèles `DiningTable`, `TableStatus`, `RestaurantSettings`.
- Produces: `struct LocalFloorRepository { let db; tables(); tableExists(_:); insert(_: DiningTable); insertDefaultSettings(); settings(); save(_: RestaurantSettings) -> RestaurantSettings }` ; `LocalPosAPI.floorRepository`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFloorTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : salle et réglages")
struct LocalFloorTests {
    @Test func seedsEightTablesInOrder() async throws {
        let api = try await makeLocalAPI()
        let tables = try await api.tables()
        #expect(tables.map(\.tableNumber) == ["T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8"])
        #expect(tables.map(\.capacity) == [2, 4, 6, 2, 8, 4, 2, 4])
        #expect(tables.allSatisfy { $0.status == .free && $0.activeOrderId == nil && $0.activeOrderTotalTtc == .zero })
    }

    @Test func tableCreationNeedsLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.createTable(number: "T9", capacity: 2) }
    }

    @Test func createTableRejectsDuplicates() async throws {
        let api = try await makeLocalAPI()
        try await api.createTable(number: "T10", capacity: 6)
        #expect(try await api.tables().last?.tableNumber == "T10")
        await #expect(throws: APIError.server(status: 400, message: "Table existante")) { try await api.createTable(number: "T10", capacity: 2) }
    }

    @Test func settingsDefaultsAndPartialSave() async throws {
        let api = try await makeLocalAPI()
        let initial = try await api.settings()
        #expect(initial.receiptLanguage == "fr" && initial.companyName == "RESTAURANT L'ANTIGRAVITE")
        #expect(initial.fiscalYearStartMonth == 1 && initial.fiscalYearStartDay == 1)

        let saved = try await api.saveSettings(RestaurantSettings(receiptLanguage: "en", kitchenTicketLanguage: "ar", siret: "11122233344455"))
        #expect(saved.receiptLanguage == "en" && saved.kitchenTicketLanguage == "ar" && saved.siret == "11122233344455")
        #expect(saved.companyName == "RESTAURANT L'ANTIGRAVITE")
    }

    @Test func settingsValidationAndPermissions() async throws {
        let manager = try await makeLocalAPI()
        await #expect(throws: APIError.server(status: 400, message: "Langue non prise en charge.")) {
            try await manager.saveSettings(RestaurantSettings(receiptLanguage: "de"))
        }
        let waiter = try await makeLocalAPI(pin: "2468")
        _ = try await waiter.settings()
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) {
            try await waiter.saveSettings(RestaurantSettings(receiptLanguage: "fr"))
        }
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter "salle et réglages" 2>&1 | grep -E "✘|Test run" | head`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Implémenter le dépôt salle et réglages**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalFloorRepository.swift` :
```swift
import Foundation

struct LocalFloorRepository {
    let db: SQLiteDatabase

    func tables() throws -> [DiningTable] {
        try db.query("SELECT * FROM DiningTables ORDER BY rowid").map {
            DiningTable(
                tableNumber: $0.string("TableNumber") ?? "", capacity: $0.int("Capacity") ?? 2, status: TableStatus(rawValue: $0.int("Status") ?? 0) ?? .free,
                positionX: $0.double("PositionX"), positionY: $0.double("PositionY"), assignedWaiterName: $0.string("AssignedWaiterName"),
                coversCount: $0.int("CoversCount") ?? 0, activeOrderId: $0.uuid("ActiveOrderId"), openedAtUtc: $0.date("OpenedAtUtc")
            )
        }
    }

    func tableExists(_ number: String) throws -> Bool {
        try !db.query("SELECT 1 AS x FROM DiningTables WHERE TableNumber = ?", [.text(number)]).isEmpty
    }

    func insert(_ t: DiningTable) throws {
        try db.run(
            """
            INSERT INTO DiningTables (TableNumber, Capacity, Status, PositionX, PositionY, AssignedWaiterName, CoversCount, ActiveOrderId, OpenedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.text(t.tableNumber), .integer(t.capacity), .integer(t.status.rawValue), .real(t.positionX ?? 0), .real(t.positionY ?? 0),
             .string(t.assignedWaiterName), .integer(t.coversCount), .uuid(t.activeOrderId), .date(t.openedAtUtc), .date(Date())]
        )
    }

    /// Réglages initiaux : valeurs par défaut de l'entité `RestaurantSettings` du .NET.
    func insertDefaultSettings() throws {
        try db.run(
            """
            INSERT INTO RestaurantSettings (Id, ReceiptLanguage, KitchenTicketLanguage, CompanyName, AddressLines, Siret, VatNumber,
                CertificateNumber, FiscalYearStartMonth, FiscalYearStartDay, UpdatedAtUtc)
            VALUES (1, 'fr', 'fr', ?, ?, ?, ?, NULL, 1, 1, ?)
            """,
            [.text("RESTAURANT L'ANTIGRAVITE"), .text("12 Rue de la Gastronomie\n75001 Paris"), .text("88877766600012"), .text("FR12888777666"), .date(Date())]
        )
    }

    func settings() throws -> RestaurantSettings {
        guard let row = try db.query("SELECT * FROM RestaurantSettings WHERE Id = 1").first else {
            throw SQLiteError(code: -1, message: "Réglages du restaurant absents")
        }
        return RestaurantSettings(
            receiptLanguage: row.string("ReceiptLanguage") ?? "fr", kitchenTicketLanguage: row.string("KitchenTicketLanguage"),
            companyName: row.string("CompanyName"), addressLines: row.string("AddressLines"), siret: row.string("Siret"), vatNumber: row.string("VatNumber"),
            certificateNumber: row.string("CertificateNumber"), fiscalYearStartMonth: row.int("FiscalYearStartMonth"), fiscalYearStartDay: row.int("FiscalYearStartDay")
        )
    }

    /// Un champ `nil` garde la valeur actuelle (comme le PUT de l'API).
    func save(_ s: RestaurantSettings) throws -> RestaurantSettings {
        let current = try settings()
        try db.run(
            """
            UPDATE RestaurantSettings SET ReceiptLanguage = ?, KitchenTicketLanguage = ?, CompanyName = ?, AddressLines = ?, Siret = ?, VatNumber = ?,
                CertificateNumber = ?, FiscalYearStartMonth = ?, FiscalYearStartDay = ?, UpdatedAtUtc = ?
            WHERE Id = 1
            """,
            [.text(s.receiptLanguage), .text(s.kitchenTicketLanguage), .text(s.companyName ?? current.companyName ?? ""),
             .text(s.addressLines ?? current.addressLines ?? ""), .text(s.siret ?? current.siret ?? ""), .text(s.vatNumber ?? current.vatNumber ?? ""),
             .string(s.certificateNumber ?? current.certificateNumber), .integer(s.fiscalYearStartMonth ?? current.fiscalYearStartMonth ?? 1),
             .integer(s.fiscalYearStartDay ?? current.fiscalYearStartDay ?? 1), .date(Date())]
        )
        return try settings()
    }
}
```

- [ ] **Step 4: Brancher l'acteur : accesseur, méthodes, seed, retrait des 501**

Dans `LocalPosAPI.swift`, remplacer :
```swift
    var catalogRepository: LocalCatalogRepository { LocalCatalogRepository(db: db) }
```
par :
```swift
    var catalogRepository: LocalCatalogRepository { LocalCatalogRepository(db: db) }
    var floorRepository: LocalFloorRepository { LocalFloorRepository(db: db) }
```

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Floor.swift` :
```swift
import Foundation

extension LocalPosAPI {
    public func tables() async throws -> [DiningTable] { try floorRepository.tables() }

    public func createTable(number: String, capacity: Int) async throws {
        try requireAuth()
        guard try !floorRepository.tableExists(number) else { throw APIError.server(status: 400, message: "Table existante") }
        try floorRepository.insert(DiningTable(tableNumber: number, capacity: capacity))
    }

    public func settings() async throws -> RestaurantSettings {
        try requireAuth()
        return try floorRepository.settings()
    }

    public func saveSettings(_ s: RestaurantSettings) async throws -> RestaurantSettings {
        try requireManager()
        guard ["en", "fr", "ar"].contains(s.receiptLanguage), ["en", "fr", "ar"].contains(s.kitchenTicketLanguage) else {
            throw APIError.server(status: 400, message: "Langue non prise en charge.")
        }
        return try floorRepository.save(s)
    }
}
```

Dans `LocalSeeder.swift`, remplacer :
```swift
            for product in seed.products { try catalog.insert(product) }
```
par :
```swift
            for product in seed.products { try catalog.insert(product) }
            let floor = LocalFloorRepository(db: db)
            for table in seed.tables { try floor.insert(table) }
            try floor.insertDefaultSettings()
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Salle et réglages (retiré par la tâche 5)` : ce commentaire, les 4 lignes `tables`, `createTable`, `settings`, `saveSettings`, et la ligne vide qui suit.

- [ ] **Step 5: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalPosAPI" 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 28 tests … passed` (9 + 8 + 6 + 5).

- [ ] **Step 6: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `193 tests … passed`.

Mettre à jour le fichier de suivi (tâche 5 `fait`, PosKit `193`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1a-progress.md
git commit -m "feat(ios-local): tables et réglages du restaurant sur SQLite"
```

---

### Task 6: Grille tactile

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalGridRepository.swift`
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Grid.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI.swift` (accesseur `gridRepository`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Unsupported.swift` (retirer la section Grille tactile)
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalGridTests.swift`

**Interfaces:**
- Consumes: `requireAuth()`, `db`, `catalogRepository.activeProducts()` ; modèles `TouchGridLayout`, `GridSlot`, `GridPosition`, `UpdateGridLayoutRequest`, `UpdateGridSlotItem`, `ProductSummary`, `Money` ; extension existante `Array[safe:]`.
- Produces: `struct LocalGridRepository { let db; layout(categoryId:page:); layout(id:); layouts(); store(_:) }` ; `LocalPosAPI.gridRepository`.

- [ ] **Step 1: Écrire les tests qui échouent**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalGridTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : grille tactile")
struct LocalGridTests {
    @Test func firstReadGeneratesA4x4GridFromTheCategory() async throws {
        let api = try await makeLocalAPI()
        let layout = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        #expect(layout.columnsCount == 4 && layout.rowsCount == 4 && layout.totalPages == 1)
        #expect(layout.slots.count == 16)
        let names = layout.slots.compactMap { $0.product?.name }
        #expect(names == ["Bière Artisanale IPA 33cl", "Verre Bordeaux AOP 12cl", "Eau Pétillante 50cl"])
        // Relecture : même grille, pas de régénération.
        #expect(try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0).id == layout.id)
    }

    @Test func unknownPageIsNotFound() async throws {
        let api = try await makeLocalAPI()
        await #expect(throws: APIError.notFound("Aucune grille")) { try await api.gridLayout(categoryId: "CAT_DRINKS", page: 3) }
    }

    @Test func saveCreatesAnotherPageAndBumpsVersion() async throws {
        let api = try await makeLocalAPI()
        let product = try #require(try await api.products().first)
        let request = UpdateGridLayoutRequest(categoryId: "CAT_STARTERS", columnsCount: 3, rowsCount: 2, pageIndex: 1,
                                              slots: [UpdateGridSlotItem(rowIndex: 1, columnIndex: 2, productId: product.id, customLabel: "Star")])
        let page = try await api.saveGridLayout(request)
        #expect(page.pageIndex == 1 && page.totalPages == 2 && page.version == 1)
        #expect(page.slots.count == 1 && page.slots[0].customLabel == "Star" && page.slots[0].slotIndex == 5)
        #expect(page.slots[0].product?.id == product.id)
        let again = try await api.saveGridLayout(request)
        #expect(again.id == page.id && again.version == 2)
    }

    @Test func failedSaveKeepsThePreviousGrid() async throws {
        let api = try await makeLocalAPI()
        let before = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        let bad = UpdateGridLayoutRequest(categoryId: "CAT_DRINKS", columnsCount: 4, rowsCount: 4, pageIndex: 0,
                                          slots: [UpdateGridSlotItem(rowIndex: 0, columnIndex: 0, productId: UUID())])
        await #expect(throws: SQLiteError.self) { try await api.saveGridLayout(bad) }
        let after = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        #expect(after.version == before.version && after.slots.map(\.productId) == before.slots.map(\.productId))
    }

    @Test func swapMovesAndExchangesSlots() async throws {
        let api = try await makeLocalAPI()
        let layout = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        let first = layout.slots[0].productId, second = layout.slots[1].productId
        let swapped = try await api.swapGridSlots(layoutId: layout.id, from: GridPosition(row: 0, column: 0), to: GridPosition(row: 0, column: 1))
        #expect(swapped.slot(at: GridPosition(row: 0, column: 0))?.productId == second)
        #expect(swapped.slot(at: GridPosition(row: 0, column: 1))?.productId == first)
        #expect(swapped.version == 2)
        await #expect(throws: APIError.notFound("Grille introuvable")) {
            try await api.swapGridSlots(layoutId: UUID(), from: GridPosition(row: 0, column: 0), to: GridPosition(row: 0, column: 1))
        }
    }

    @Test func shrinkingDimensionsDropsOutOfRangeSlots() async throws {
        let api = try await makeLocalAPI()
        _ = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        _ = try await api.gridLayout(categoryId: "CAT_MAINS", page: 0)
        let updated = try await api.updateGridDimensions(categoryId: "CAT_DRINKS", columns: 2, rows: 2, applyToAll: false)
        #expect(updated.count == 1 && updated[0].columnsCount == 2 && updated[0].slots.count == 4)
        #expect(try await api.gridLayout(categoryId: "CAT_MAINS", page: 0).columnsCount == 4)
        #expect(try await api.updateGridDimensions(categoryId: "x", columns: 3, rows: 3, applyToAll: true).count == 2)
    }

    @Test func archivedProductStaysInExistingSlots() async throws {
        let api = try await makeLocalAPI()
        let layout = try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0)
        let productId = try #require(layout.slots[0].productId)
        try await api.archiveProduct(id: productId)
        // Archivage logique : l'article reste référencé, comme sur le serveur.
        #expect(try await api.gridLayout(categoryId: "CAT_DRINKS", page: 0).slots[0].productId == productId)
    }
}
```

- [ ] **Step 2: Vérifier l'échec**

Run: `cd ios/Packages/PosKit && swift test --filter "grille tactile" 2>&1 | grep -E "✘|Test run" | head`
Expected: échecs `APIError.server(status: 501 …)`.

- [ ] **Step 3: Implémenter le dépôt de la grille**

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalGridRepository.swift` :
```swift
import Foundation

struct LocalGridRepository {
    let db: SQLiteDatabase

    func layout(categoryId: String, page: Int) throws -> TouchGridLayout? {
        guard let row = try db.query("SELECT * FROM GridLayouts WHERE CategoryId = ? AND PageIndex = ?", [.text(categoryId), .integer(page)]).first else { return nil }
        return try hydrate(row)
    }

    func layout(id: UUID) throws -> TouchGridLayout? {
        guard let row = try db.query("SELECT * FROM GridLayouts WHERE Id = ?", [.uuid(id)]).first else { return nil }
        return try hydrate(row)
    }

    func layouts() throws -> [TouchGridLayout] {
        try db.query("SELECT * FROM GridLayouts ORDER BY rowid").map(hydrate)
    }

    /// Insère ou met à jour la grille puis remplace tous ses emplacements. À appeler dans une transaction.
    func store(_ layout: TouchGridLayout) throws {
        try db.run(
            """
            INSERT INTO GridLayouts (Id, CategoryId, Name, ColumnsCount, RowsCount, PageIndex, Version, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, ColumnsCount = excluded.ColumnsCount, RowsCount = excluded.RowsCount,
                Version = excluded.Version, UpdatedAtUtc = excluded.UpdatedAtUtc
            """,
            [.uuid(layout.id), .text(layout.categoryId), .text(layout.name ?? "Défaut"), .integer(layout.columnsCount), .integer(layout.rowsCount),
             .integer(layout.pageIndex), .integer(layout.version ?? 1), .date(Date())]
        )
        try db.run("DELETE FROM GridSlots WHERE GridLayoutId = ?", [.uuid(layout.id)])
        for slot in layout.slots {
            try db.run(
                """
                INSERT INTO GridSlots (Id, GridLayoutId, ProductId, RowIndex, ColumnIndex, SlotIndex, CustomLabel, CustomColorHex, IsDisabled)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(slot.id ?? UUID()), .uuid(layout.id), .uuid(slot.productId), .integer(slot.rowIndex), .integer(slot.columnIndex),
                 .integer(slot.rowIndex * layout.columnsCount + slot.columnIndex), .string(slot.customLabel), .string(slot.customColorHex), .bool(slot.isDisabled ?? false)]
            )
        }
    }

    private func hydrate(_ row: SQLRow) throws -> TouchGridLayout {
        let id = row.uuid("Id")!
        let categoryId = row.string("CategoryId") ?? ""
        let maxPage = try db.query("SELECT MAX(PageIndex) AS m FROM GridLayouts WHERE CategoryId = ?", [.text(categoryId)]).first?.int("m") ?? 0
        let slots = try db.query(
            """
            SELECT s.*, p.Name AS PName, p.Price AS PPrice, p.PreparationStationId AS PStation, p.ColorHex AS PColor
            FROM GridSlots s LEFT JOIN Products p ON p.Id = s.ProductId
            WHERE s.GridLayoutId = ? ORDER BY s.SlotIndex
            """,
            [.uuid(id)]
        ).map { s -> GridSlot in
            let productId = s.uuid("ProductId")
            let summary = productId.map { ProductSummary(id: $0, name: s.string("PName") ?? "", price: Money(cents: s.int("PPrice") ?? 0), preparationStationId: s.string("PStation"), colorHex: s.string("PColor")) }
            return GridSlot(id: s.uuid("Id"), gridLayoutId: id, productId: productId, rowIndex: s.int("RowIndex") ?? 0, columnIndex: s.int("ColumnIndex") ?? 0,
                            slotIndex: s.int("SlotIndex"), customLabel: s.string("CustomLabel"), customColorHex: s.string("CustomColorHex"),
                            isDisabled: s.bool("IsDisabled"), product: summary)
        }
        return TouchGridLayout(id: id, categoryId: categoryId, name: row.string("Name"), columnsCount: row.int("ColumnsCount") ?? 4, rowsCount: row.int("RowsCount") ?? 4,
                               pageIndex: row.int("PageIndex") ?? 0, totalPages: max(1, maxPage + 1), version: row.int("Version"), slots: slots)
    }
}
```

- [ ] **Step 4: Brancher l'acteur : accesseur, méthodes, retrait des 501**

Dans `LocalPosAPI.swift`, remplacer :
```swift
    var floorRepository: LocalFloorRepository { LocalFloorRepository(db: db) }
```
par :
```swift
    var floorRepository: LocalFloorRepository { LocalFloorRepository(db: db) }
    var gridRepository: LocalGridRepository { LocalGridRepository(db: db) }
```

Créer `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Grid.swift` :
```swift
import Foundation

extension LocalPosAPI {
    public func gridLayout(categoryId: String, page: Int) async throws -> TouchGridLayout {
        if let layout = try gridRepository.layout(categoryId: categoryId, page: page) { return layout }
        guard page == 0 else { throw APIError.notFound("Aucune grille") }
        // Comme le serveur : génération automatique d'une grille 4×4 à la première lecture.
        let products = try catalogRepository.activeProducts().filter { categoryId == "ALL" || $0.categoryId == categoryId }
        let slots = (0..<16).map { GridSlot(productId: products[safe: $0]?.id, rowIndex: $0 / 4, columnIndex: $0 % 4) }
        let layout = TouchGridLayout(categoryId: categoryId, name: categoryId, slots: slots)
        try db.transaction { try gridRepository.store(layout) }
        return try gridRepository.layout(id: layout.id) ?? layout
    }

    public func saveGridLayout(_ request: UpdateGridLayoutRequest) async throws -> TouchGridLayout {
        try requireAuth()
        var layout = try gridRepository.layout(categoryId: request.categoryId, page: request.pageIndex)
            ?? TouchGridLayout(categoryId: request.categoryId, pageIndex: request.pageIndex, version: 0)
        layout.columnsCount = request.columnsCount
        layout.rowsCount = request.rowsCount
        layout.version = (layout.version ?? 0) + 1
        layout.slots = request.slots.map {
            GridSlot(productId: $0.productId, rowIndex: $0.rowIndex, columnIndex: $0.columnIndex, customLabel: $0.customLabel, customColorHex: $0.customColorHex)
        }
        try db.transaction { try gridRepository.store(layout) }
        return try gridRepository.layout(id: layout.id) ?? layout
    }

    public func swapGridSlots(layoutId: UUID, from: GridPosition, to: GridPosition) async throws -> TouchGridLayout {
        try requireAuth()
        guard var layout = try gridRepository.layout(id: layoutId) else { throw APIError.notFound("Grille introuvable") }
        let a = layout.slots.firstIndex { $0.rowIndex == from.row && $0.columnIndex == from.column }
        let b = layout.slots.firstIndex { $0.rowIndex == to.row && $0.columnIndex == to.column }
        switch (a, b) {
        case let (a?, b?):
            (layout.slots[a].rowIndex, layout.slots[b].rowIndex) = (layout.slots[b].rowIndex, layout.slots[a].rowIndex)
            (layout.slots[a].columnIndex, layout.slots[b].columnIndex) = (layout.slots[b].columnIndex, layout.slots[a].columnIndex)
        case let (a?, nil):
            layout.slots[a].rowIndex = to.row
            layout.slots[a].columnIndex = to.column
        default:
            break
        }
        layout.version = (layout.version ?? 0) + 1
        try db.transaction { try gridRepository.store(layout) }
        return try gridRepository.layout(id: layoutId) ?? layout
    }

    public func updateGridDimensions(categoryId: String, columns: Int, rows: Int, applyToAll: Bool) async throws -> [TouchGridLayout] {
        try requireAuth()
        let targets = try gridRepository.layouts().filter { applyToAll || $0.categoryId == categoryId }
        try db.transaction {
            for var layout in targets {
                layout.columnsCount = columns
                layout.rowsCount = rows
                layout.slots.removeAll { $0.rowIndex >= rows || $0.columnIndex >= columns }
                try gridRepository.store(layout)
            }
        }
        return try targets.compactMap { try gridRepository.layout(id: $0.id) }
    }
}
```

Dans `LocalPosAPI+Unsupported.swift`, supprimer la section `// MARK: Grille tactile (retiré par la tâche 6)` : ce commentaire, les 4 lignes `gridLayout`, `saveGridLayout`, `swapGridSlots`, `updateGridDimensions`, et la ligne vide qui suit.

- [ ] **Step 5: Vérifier le succès**

Run: `cd ios/Packages/PosKit && swift test --filter "LocalPosAPI" 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 35 tests … passed` (9 + 8 + 6 + 5 + 7).

- [ ] **Step 6: Suite complète, suivi, commit**

Run: `cd ios/Packages/PosKit && swift test 2>&1 | grep "Test run"` → Expected: `200 tests … passed`.

Mettre à jour le fichier de suivi (tâche 6 `fait`, PosKit `200`).

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local ios/Packages/PosKit/Tests/PosKitTests/Local docs/plans/ipad-standalone-1a-progress.md
git commit -m "feat(ios-local): grille tactile locale (génération 4×4, sauvegarde atomique, permutation)"
```

---

### Task 7: Persistance sur fichier, robustesse et documentation

**Files:**
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPersistenceTests.swift`
- Modify: `CLAUDE.md` (consigner la règle de synchronisation .NET ↔ Swift)
- Modify: `ios/README.md` (section sur le backend local)

**Interfaces:**
- Consumes: `LocalPosAPI(path:)`, `temporaryDatabasePath()`, `SQLiteDatabase`, `LocalMigrator` (tâches 1-6).
- Produces: rien de nouveau dans le code ; tests de persistance et de robustesse ; documentation.

- [ ] **Step 1: Écrire les tests**

Créer `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPersistenceTests.swift` :
```swift
import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : persistance")
struct LocalPersistenceTests {
    @Test func dataSurvivesReopeningTheFile() async throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        do {
            let first = try LocalPosAPI(path: path)
            _ = try await first.login(pin: "1234")
            try await first.createTable(number: "T42", capacity: 5)
            try await first.createStaff(name: "Persistant", role: .cashier, pin: "7777")
        }
        let reopened = try LocalPosAPI(path: path)
        #expect(try await reopened.login(pin: "7777").success)
        #expect(try await reopened.tables().count == 9)
        #expect(try await reopened.staff().count == 5)
        #expect(try await reopened.categories().count == 5)
    }

    @Test func garbageFileIsRejectedAndLeftUntouched() throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        let garbage = Data("ceci n'est pas une base SQLite, juste du texte assez long pour ressembler à un en-tête invalide".utf8)
        try garbage.write(to: URL(fileURLWithPath: path))
        #expect(throws: SQLiteError.self) { try LocalPosAPI(path: path) }
        #expect(try Data(contentsOf: URL(fileURLWithPath: path)) == garbage)
    }

    @Test func foreignKeysAreEnforced() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        #expect(throws: SQLiteError.self) {
            try db.run("INSERT INTO ModifierOptions (Id, GroupId, Name, ExtraPrice, IsDefault, DisplayOrder) VALUES ('a', 'missing', 'x', 0, 0, 0)")
        }
    }
}
```

- [ ] **Step 2: Lancer les tests**

Run: `cd ios/Packages/PosKit && swift test --filter "persistance" 2>&1 | grep -E "error:|✘|Test run"`
Expected: `Test run with 3 tests … passed` (le code est déjà en place : ces tests verrouillent le comportement).

- [ ] **Step 3: Documenter la règle de synchronisation dans `CLAUDE.md`**

Dans `CLAUDE.md`, remplacer :
```
(`TableManagementService.DestinationForTable`).

Receipt-writing routes
```
par :
```
(`TableManagementService.DestinationForTable`).

Le mode autonome de l'iPad (`LocalPosAPI`, SQLite, `ios/Packages/PosKit/Sources/PosKit/Local/`, en cours de construction : spec `docs/superpowers/specs/2026-10-06-ipad-standalone-design.md`) réimplémente en Swift la logique du backend avec les mêmes tables et colonnes que `AppDbContext`. Toute modification de schéma, de règle métier, de message d'erreur ou d'arrondi côté .NET doit être reportée dans `Local/` (nouvelle migration, jamais de modification d'un script publié) et dans ses tests `Tests/PosKitTests/Local/`.

Receipt-writing routes
```

- [ ] **Step 4: Documenter dans `ios/README.md`**

Ajouter à la fin de `ios/README.md` :
```markdown

## Mode autonome (en cours)

`LocalPosAPI` (`Packages/PosKit/Sources/PosKit/Local/`) implémente `PosAPI` sur une base SQLite locale, sans serveur .NET. Plan 1a livré : authentification par PIN (avec verrouillage), personnel, catalogue, grille tactile, tables et réglages. Les autres méthodes répondent `501` (« Non disponible en mode autonome ») jusqu'aux plans 1b (commandes, paiement non fiscal, cuisine) et 1c (imprimantes, Happy Hour, choix Serveur/Autonome au lancement).

Le mode n'est pas encore sélectionnable dans l'app. Les tests : `cd Packages/PosKit && swift test --filter LocalPosAPI`.
```

- [ ] **Step 5: Vérification finale complète**

Run:
```bash
cd ios/Packages/PosKit && swift test 2>&1 | grep -E "Test run|error:|warning:"
cd ../../.. && dotnet build RestaurantPos.slnx 2>&1 | tail -3
cd ios && xcodegen generate && xcodebuild -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'generic/platform=iOS Simulator' build 2>&1 | tail -3
```
Expected: `Test run with 203 tests … passed`, aucun avertissement ; `dotnet build` réussi (aucun fichier .NET modifié) ; build iOS `BUILD SUCCEEDED` (le package compile avec l'app). Si le nom du schéma Xcode diffère, lire `ios/project.yml` pour le nom exact.

- [ ] **Step 6: Suivi et commit**

Mettre à jour le fichier de suivi : tâche 7 `fait`, PosKit `203`, .NET et web `inchangés (build seul vérifié)`, findings ouverts : `aucun` ou la liste.

```bash
git add ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPersistenceTests.swift CLAUDE.md ios/README.md docs/plans/ipad-standalone-1a-progress.md
git commit -m "feat(ios-local): tests de persistance et de robustesse, documentation du mode autonome"
```

---

## Self-Review

**1. Couverture de la spec (sous-projet 1, plan 1a)**
- « SQLite, mêmes tables et noms de colonnes que `Domain/Entities` » → tâche 2 (schéma v1) et tâches 3-6 (dépôts).
- « Montants en centimes entiers » → colonnes `Price`/`ExtraPrice` `INTEGER`, `Money(cents:)` dans les dépôts (tâche 4).
- « Migrations versionnées par `PRAGMA user_version` (échec explicite) » → tâche 2, test `rejectsDatabaseNewerThanTheApp` et rollback transactionnel.
- « Clé de signature / identifiant terminal `T01` / sauvegarde automatique » → hors périmètre 1a (plans 1c et sous-projet 2) ; indiqué dans l'en-tête.
- « Choix Serveur/Autonome au premier lancement » → plan 1c (non couvert ici, annoncé).
- « Parité .NET/Swift » : le hachage PIN est vérifié contre une valeur SHA-256 calculée indépendamment (tâche 2) ; la parité des hashes fiscaux relève du sous-projet 2.
- Règle de synchronisation .NET ↔ Swift à consigner dans `CLAUDE.md` (risque listé dans la spec) → tâche 7.

**2. Placeholders** : aucun « TBD », « TODO » ni « similaire à la tâche N » ; chaque étape de code contient le code complet. Les remplacements (`Edit`) donnent le texte exact avant/après.

**3. Cohérence des types** : `LocalStaffRepository`/`LocalCatalogRepository`/`LocalFloorRepository`/`LocalGridRepository` et leurs accesseurs `staffRepository`/`catalogRepository`/`floorRepository`/`gridRepository` portent les mêmes noms dans les tâches 3 à 6 ; `requireAuth()`/`requireManager()` sont déclarés en tâche 3 et réutilisés tels quels ; `LocalPosAPI.pinLength` (interne) est utilisé par `LocalPosAPI+Staff.swift` ; `makeLocalAPI(pin:)`/`temporaryDatabasePath()` sont créés en tâche 3 et réutilisés en tâches 4-7.

**4. Review Focus** : les cinq points ont chacun un test nommé dans la tâche propriétaire (tâches 3, 4, 6, 7).

## Execution Handoff

Plan à relire avant toute exécution. Vérification faite à la rédaction : les blocs « Créer / remplacer / supprimer » de ce document ont été appliqués mécaniquement, tâche par tâche, sur une copie propre de PosKit ; `swift test` donne exactement 160, 165, 182, 188, 193, 200 puis 203 tests verts, sans avertissement de compilation. Non exécutés lors de cette vérification : les commandes `dotnet` et `xcodebuild` de la tâche 7 et les éditions de `CLAUDE.md`/`ios/README.md` (l'ancre de `CLAUDE.md` a été vérifiée : une seule occurrence).
