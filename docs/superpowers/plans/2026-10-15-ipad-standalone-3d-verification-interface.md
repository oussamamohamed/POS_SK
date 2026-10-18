# iPad autonome 3d : vérification des chaînes et interface Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Le mode autonome vérifie toutes ses chaînes fiscales comme le .NET (y compris clôtures de période et archives), et sa section Fiscal permet à un responsable de clôturer un mois ou un exercice, d'archiver, de partager l'archive et le FEC, et de voir le verdict de vérification, en FR / EN / AR.

**Architecture:** Côté PosKit, `LocalFiscalRepository+PeriodChains` ajoute les chaînes `period_closures` et `archives` à `verifyAllChains` (miroir de `FiscalChainVerificationService.cs`), `LocalPosAPI.verifyChains` journalise `CHAIN_BREAK_DETECTED`, les refus métier deviennent des textes `L10n`, `archiveFile(id:)` sait récupérer un fichier perdu (renommage du `.tmp`, sinon reconstruction contrôlée par le SHA-256), et le FEC filtre la période en SQL et valide le SIREN. `archiveFile(id:)` entre dans le protocole `PosAPI` (HTTP : `GET /api/fiscal/archives/{id}/file`). Côté app, l'écran Fiscal existant (déjà construit pour le mode serveur et déjà visible en autonome, commit 7343e6e) est découpé en cartes dans `FiscalArchiveCards.swift` et complété : libellés des chaînes et des ruptures, verdict d'archive, partage du ZIP, champ société, contrôle du SIREN.

**Tech Stack:** Swift 6 / Swift Testing / SQLite (PosKit) ; SwiftUI iPadOS 17 / XCUITest (app) ; aucun changement .NET.

**Spec:** `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` (lot 3d, Décisions, Risques, Règle de suivi). Code de référence : `src/RestaurantPos.Infrastructure/Services/FiscalChainVerificationService.cs` (`VerifyAllChainsAsync`, `VerifyPeriodClosuresChain`, `VerifyArchivesChain`), `src/RestaurantPos.Api/Endpoints/FiscalEndpoints.cs` (`/verify`, `/fec`, `/archives/{id}/file`).

## État constaté avant le lot (lecture du code, HEAD `d1a13a9`)

Déjà en place, rien à reconstruire :
- `LocalPosAPI` implémente `periodClosures`, `executePeriodClosure`, `archives`, `createArchive`, `verifyArchive`, `exportFec`, `verifyChains` ; `archiveFile(id:)` existe mais hors protocole.
- `FiscalStore` (`ios/Packages/PosKit/Sources/PosKit/Stores/OperationsStores.swift`) : `loadPeriodClosures`, `executePeriodClosure`, `loadArchives`, `createArchive`, `verifyArchive`, `exportFec`, `verify`.
- `FiscalScreen.swift` : carte de vérification (noms de chaînes bruts), carte de clôtures de période (sélecteur mensuelle/annuelle, clé, historique, bouton « Archiver »), carte d'archives (liste, `.fileImporter` qui ne garde que le toast), carte FEC (dates, SIREN, `ShareLink`). Section visible pour un responsable en autonome.

Manque (objet du lot) : chaînes `period_closures` / `archives` dans `verifyAllChains` ; partage du ZIP d'archive ; affichage durable du verdict d'archive ; libellés lisibles des chaînes et ruptures ; refus métier localisés ; champ société ; validation du SIREN ; tri de l'historique (le dépôt rend les clôtures de la plus ancienne à la plus récente, l'écran affiche `prefix(5)`) ; bouton de vérification d'archive libellé « Vérifier l'intégrité » (clé de la vérification des chaînes) ; reports des revues 3b / 3c.

## Global Constraints

- Branche : partir de `feat/ipad-standalone-3c-fec` (HEAD `d1a13a9`, lots 3a, 3b, 3c faits) et créer `feat/ipad-standalone-3d-verification-interface`. Pas de push.
- Aucune migration : le schéma ne change pas (V9 reste la dernière). Aucune modification .NET ni de fixture (`fiscal_reference.json`, `period_closure_reference.json`, `archive_reference.json`, `fec_reference.json` doivent continuer à passer sans régénération).
- Chaînes vérifiées, dans l'ordre du .NET : `receipts`, `z_closures`, `period_closures`, `journal`, `archives`. Codes de rupture : `sequence_gap`, `previous_hash_mismatch`, `signature_mismatch`. Références : `M-<clé>` / `A-<clé>` (clôtures de période), `ARC-<séquence>` (archives). Sans clôture de période : une ligne `("period_closures", terminalId: nil, 0, valide)` ; sans archive : `("archives", nil, 0, valide)`. Clôtures : une ligne par poste, chaque type chaîné depuis `FiscalHashing.genesisHash`, `checkedCount` = toutes les clôtures du poste.
- Rupture détectée : entrée JET `CHAIN_BREAK_DETECTED` (première chaîne rompue), charge utile `{"Chain":…,"TerminalId":…,"SequenceNumber":…,"Reference":…,"Kind":…}` (PascalCase, `null` si absent), poste = celui de la chaîne, opérateur nul ; un échec d'écriture du JET ne fait pas échouer la vérification (comme le `catch` du .NET).
- Textes : les refus métier touchés par ce lot passent par `L10n.string` (`ios/Packages/PosKit/Sources/PosKit/Resources/{en,fr,ar}.lproj/Localizable.strings`, clés `^[a-z]+\.[a-z0-9_.]+$`, mêmes spécificateurs dans les trois langues, chiffres occidentaux en arabe). Les autres messages de `Local/` restent en français (hors périmètre). Les textes de l'app vont dans `ios/RestaurantPOS/Resources/Localizable.xcstrings` (`extractionState: manual`, `state: translated` en `en`, `fr`, `ar`), écrit par `json.dumps(…, ensure_ascii=False, indent=2, separators=(",", ": ")) + "\n"` (aller-retour vérifié octet pour octet), nouvelles clés ajoutées en fin de dictionnaire.
- SIREN : 9 chiffres ASCII ou vide après rognage (contrôle propre à l'iPad, le .NET n'en fait pas). Le JET `FEC_EXPORTED` garde le SIREN tel que reçu (`""` si vide) et `Company` nul si non renseignée.
- Nom de fichier écrit par l'app dans un dossier local : toujours passé par `SafeFileName.sanitized` (dernier composant, `[A-Za-z0-9_.-]`, jamais `.`/`..`).
- iOS : après création d'un fichier Swift dans `ios/RestaurantPOS/`, lancer `cd ios && xcodegen generate` (ne jamais éditer le `.xcodeproj`). Pas de type nommé comme un type SwiftUI / Foundation (`Archive`, `Category`, `GridLayout`…). Corps de vue découpés en sous-vues. Ce lot ne touche pas l'appairage : pas de build `CODE_SIGNING_ALLOWED=NO` pour les tests UI.
- Tests : `cd ios/Packages/PosKit && swift test` (suite complète avant chaque « fait » ; base : 521 tests, 73 suites). Tests UI (~8 min la suite) : seulement le nouveau test en tâche 9, toute la suite en tâche 10. `dotnet test RestaurantPos.slnx` une fois en tâche 10 (base 766 : Domain 69, Infrastructure 440, Api 257).
- Après chaque tâche : mettre à jour `docs/plans/ipad-standalone-3d-progress.md` (statut, nombres de tests .NET / PosKit / UI, findings ouverts). Commits par `git add <fichiers>` explicites ; jamais `git add -f` sous `.superpowers`.

## Review Focus

- SIREN saisi avec des espaces (`123 456 789`), au clavier arabe (`١٢٣٤٥٦٧٨٩`) ou avec des caractères de chemin (`../x`) : chiffres ramenés à l'ASCII et espaces retirés avant l'envoi, sinon refus lisible 400 sans entrée JET ; le fichier écrit ne sort jamais du dossier temporaire — Task 7 (API, `Siren`), Task 8 (store, `SafeFileName`).
- ZIP d'archive absent avec un `.tmp` tronqué (coupure pendant l'écriture) ou une base modifiée depuis l'archivage : jamais servi tel quel ; reconstruction servie seulement si son SHA-256 est celui de la ligne signée, sinon 404 « non reconstructible » sans fichier ni JET écrit — Task 5.
- Installation neuve (aucune clôture, aucune archive) et poste avec mensuelles **et** annuelle : les cinq chaînes sont listées, les vides sont valides, la première annuelle n'est pas comparée à la dernière mensuelle — Task 1.
- Bornes du FEC à une fraction de milliseconde, dates inversées, reçu pile sur une borne, date stockée sous une autre forme que `storedText` : même sortie qu'avant le filtre SQL, ou arrêt explicite, jamais un reçu perdu en silence — Task 6.
- Fichier choisi pour la vérification très volumineux (plusieurs centaines de Mo) : refusé avant lecture avec un message lisible ; clé de période saisie avec des espaces (` 2026-09 `) : acceptée — Task 8.

---

### Task 1: `verifyAllChains` couvre les clôtures de période et les archives

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository+PeriodChains.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository.swift` (`verifyChains()` l. 479-536, `verifyAllChains(now:)` l. 539-660)
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalChainVerificationTests.swift`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalReportTests.swift` (`verifyChainsReturnsStructuredValidChains`, l. 143)
- Create: `docs/plans/ipad-standalone-3d-progress.md`

**Interfaces:**
- Consumes: `FiscalHashing.periodClosureHash(previousHash:terminalId:periodType:periodKey:totalTtcCents:totalHtCents:taxesJson:tendersJson:perpetualGrandTotalCents:periodEnd:) -> String`, `FiscalHashing.archiveHash(previousHash:periodType:periodKey:fileSha256:createdAt:) -> String`, `FiscalChainStatus(chain:terminalId:checkedCount:legacyCount:isValid:break:)`, `FiscalChainBreak(sequenceNumber:reference:kind:)`.
- Produces: `func periodClosureChainStatuses() throws -> [FiscalChainStatus]` et `func archiveChainStatus() throws -> FiscalChainStatus` (extension de `LocalFiscalRepository`, internes) ; `verifyAllChains(now:)` rend 5 chaînes dans l'ordre .NET ; `verifyChains() -> [String]` (utilisé par `LocalBackupService.restore`) signale aussi ces deux chaînes, au format `"<chain> <poste|-> <référence>#<séquence>: <code>"`.

- [ ] **Step 1: Créer le fichier de suivi**

`docs/plans/ipad-standalone-3d-progress.md` :

```markdown
# iPad standalone 3d — suivi d'exécution (vérification et interface)

Spec : `docs/superpowers/specs/2026-10-13-ipad-standalone-3-clotures-archives-design.md` · Plan : `docs/superpowers/plans/2026-10-15-ipad-standalone-3d-verification-interface.md`

Base : .NET 766 (Domain 69, Infrastructure 440, Api 257) · PosKit 521 (73 suites)

| Tâche | Statut | .NET | PosKit | Tests UI | Findings de revue ouverts |
|---|---|---|---|---|---|
| 1 Chaînes `period_closures` et `archives` | en cours | - | - | - | - |
| 2 JET `CHAIN_BREAK_DETECTED` | à faire | - | - | - | - |
| 3 Refus métier localisés | à faire | - | - | - | - |
| 4 Archives : ligne illisible, voisin illisible | à faire | - | - | - | - |
| 5 `archiveFile` : récupération du fichier | à faire | - | - | - | - |
| 6 FEC : filtre SQL, ordre des séquences | à faire | - | - | - | - |
| 7 FEC : SIREN, société, charge utile du JET | à faire | - | - | - | - |
| 8 `FiscalStore` : partage, verdict, plafond, noms sûrs | à faire | - | - | - | - |
| 9 Écran Fiscal autonome + XCUITest | à faire | - | - | - | - |
| 10 Documentation et vérification finale | à faire | - | - | - | - |
```

- [ ] **Step 2: Écrire les tests qui échouent**

`ios/Packages/PosKit/Tests/PosKitTests/Local/LocalChainVerificationTests.swift` :

```swift
import Foundation
import Testing
@testable import PosKit

@Suite("Vérification des chaînes : clôtures de période et archives")
struct LocalChainVerificationTests {
    private struct Context {
        let api: LocalPosAPI
        let clock: LocalTestClock
    }

    private func makeContext() async throws -> Context {
        let clock = LocalTestClock("2026-09-10T12:00:00Z")
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("archives-\(UUID().uuidString)", isDirectory: true)
        let api = try LocalPosAPI(path: temporaryDatabasePath(), clock: { clock.now }, calendar: localTestCalendar, archivesDirectory: folder)
        _ = try await api.login(pin: "1234")
        return Context(api: api, clock: clock)
    }

    /// Septembre (une vente et son Z) puis octobre (vide) clôturés et archivés : deux maillons dans chaque chaîne.
    private func closeAndArchiveTwoMonths(_ c: Context) async throws {
        let burger = try await localProduct(c.api, "Burger Gourmet Rossini")
        try await seatTable(c.api, "T1", items: [localInput(burger)])
        let order = try #require(try await c.api.activeOrder(table: "T1"))
        _ = try await c.api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1950)]))
        c.clock.set("2026-09-10T21:00:00Z")
        _ = try await c.api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
        c.clock.set("2026-11-02T09:00:00Z")
        for key in ["2026-09", "2026-10"] {
            let closure = try await c.api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: key)
            _ = try await c.api.createArchive(periodClosureId: closure.id)
        }
    }

    /// Copie de la base (sauvegarde SQLite) sans triggers d'immuabilité : falsification « hors application », la base de l'API reste intacte.
    private func tamperableCopy(_ c: Context) async throws -> SQLiteDatabase {
        let url = URL(fileURLWithPath: temporaryDatabasePath())
        try await c.api.backup(to: url)
        let copy = try SQLiteDatabase(path: url.path)
        try copy.exec(
            "DROP TRIGGER TR_PeriodClosures_NoUpdate; DROP TRIGGER TR_PeriodClosures_NoDelete; DROP TRIGGER TR_FiscalArchives_NoUpdate; DROP TRIGGER TR_FiscalArchives_NoDelete"
        )
        return copy
    }

    private func status(_ db: SQLiteDatabase, _ chain: String) throws -> FiscalChainStatus? {
        try LocalFiscalRepository(db: db).verifyAllChains(now: Date()).chains.first { $0.chain == chain }
    }

    @Test func validChainsAreListedInTheDotNetOrder() async throws {
        let c = try await makeContext()
        try await closeAndArchiveTwoMonths(c)
        let result = try await c.api.verifyChains()
        #expect(result.isValid)
        #expect(result.chains.map(\.chain) == ["receipts", "z_closures", "period_closures", "journal", "archives"])
        #expect(result.chains[2] == FiscalChainStatus(chain: "period_closures", terminalId: "T01", checkedCount: 2, isValid: true))
        #expect(result.chains[4] == FiscalChainStatus(chain: "archives", terminalId: nil, checkedCount: 2, isValid: true))
    }

    @Test func aFreshInstallationListsEmptyValidPeriodAndArchiveChains() async throws {
        let c = try await makeContext()
        let chains = try await c.api.verifyChains().chains
        #expect(chains.first { $0.chain == "period_closures" } == FiscalChainStatus(chain: "period_closures", terminalId: nil, checkedCount: 0, isValid: true))
        #expect(chains.first { $0.chain == "archives" } == FiscalChainStatus(chain: "archives", terminalId: nil, checkedCount: 0, isValid: true))
    }

    @Test func aTamperedPeriodClosureGivesTheDotNetCodeAndReference() async throws {
        let c = try await makeContext()
        try await closeAndArchiveTwoMonths(c)
        let cases: [(sql: String, expected: FiscalChainBreak)] = [
            ("UPDATE PeriodClosures SET TotalTtcAmount = 1 WHERE PeriodKey = '2026-09'",
             FiscalChainBreak(sequenceNumber: 1, reference: "M-2026-09", kind: "signature_mismatch")),
            ("UPDATE PeriodClosures SET PreviousSignatureHash = 'x' WHERE PeriodKey = '2026-10'",
             FiscalChainBreak(sequenceNumber: 2, reference: "M-2026-10", kind: "previous_hash_mismatch")),
            ("UPDATE PeriodClosures SET ClosureSequence = 5 WHERE PeriodKey = '2026-10'",
             FiscalChainBreak(sequenceNumber: 5, reference: "M-2026-10", kind: "sequence_gap")),
        ]
        for (sql, expected) in cases {
            let copy = try await tamperableCopy(c)
            try copy.exec(sql)
            let broken = try status(copy, "period_closures")
            #expect(broken?.isValid == false, Comment(rawValue: sql))
            #expect(broken?.terminalId == "T01" && broken?.checkedCount == 2, Comment(rawValue: sql))
            #expect(broken?.break == expected, Comment(rawValue: sql))
            #expect(try LocalFiscalRepository(db: copy).verifyAllChains(now: Date()).isValid == false)
            #expect(try LocalFiscalRepository(db: copy).verifyChains()
                .contains("period_closures T01 \(expected.reference)#\(expected.sequenceNumber): \(expected.kind)"), Comment(rawValue: sql))
        }
    }

    @Test func aTamperedArchiveGivesTheDotNetCodeAndReference() async throws {
        let c = try await makeContext()
        try await closeAndArchiveTwoMonths(c)
        let cases: [(sql: String, expected: FiscalChainBreak)] = [
            ("UPDATE FiscalArchives SET FileSha256 = 'x' WHERE ArchiveSequence = 1",
             FiscalChainBreak(sequenceNumber: 1, reference: "ARC-1", kind: "signature_mismatch")),
            ("UPDATE FiscalArchives SET PreviousSignatureHash = 'x' WHERE ArchiveSequence = 2",
             FiscalChainBreak(sequenceNumber: 2, reference: "ARC-2", kind: "previous_hash_mismatch")),
            ("DELETE FROM FiscalArchives WHERE ArchiveSequence = 1",
             FiscalChainBreak(sequenceNumber: 2, reference: "ARC-2", kind: "sequence_gap")),
        ]
        for (sql, expected) in cases {
            let copy = try await tamperableCopy(c)
            try copy.exec(sql)
            let broken = try status(copy, "archives")
            #expect(broken?.isValid == false && broken?.terminalId == nil, Comment(rawValue: sql))
            #expect(broken?.break == expected, Comment(rawValue: sql))
            #expect(try LocalFiscalRepository(db: copy).verifyChains()
                .contains("archives - \(expected.reference)#\(expected.sequenceNumber): \(expected.kind)"), Comment(rawValue: sql))
        }
    }

    /// Clôture signée insérée directement (sans passer par l'algorithme de clôture) ; rend sa signature.
    @discardableResult
    private static func insertSignedClosure(_ db: SQLiteDatabase, type: Int, key: String, sequence: Int, previous: String, end: String) throws -> String {
        let periodEnd = SQLDate.parse(end)!
        let signature = FiscalHashing.periodClosureHash(
            previousHash: previous, terminalId: "T01", periodType: type, periodKey: key, totalTtcCents: 1000, totalHtCents: 900,
            taxesJson: "{}", tendersJson: "{}", perpetualGrandTotalCents: 1000, periodEnd: periodEnd
        )
        try db.run(
            """
            INSERT INTO PeriodClosures (Id, TerminalId, PeriodType, PeriodKey, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalTtcAmount, TotalHtAmount,
                TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, DailyClosureCount, PreviousSignatureHash, SignatureHash, SealedByUserId,
                SealedByUserName, CreatedAtUtc)
            VALUES (?, 'T01', ?, ?, ?, '2026-01-01T00:00:00.000Z', ?, 1000, 900, '{}', '{}', 1000, 0, ?, ?, ?, 'Chef', ?)
            """,
            [.uuid(UUID()), .integer(type), .text(key), .integer(sequence), .text(end), .text(previous), .text(signature), .uuid(UUID()), .text(end)]
        )
        return signature
    }

    @Test func monthlyAndAnnualClosuresAreTwoChainsEachFromGenesis() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        let december = try Self.insertSignedClosure(db, type: 1, key: "2026-12", sequence: 1, previous: FiscalHashing.genesisHash, end: "2027-01-01T00:00:00.000Z")
        try Self.insertSignedClosure(db, type: 2, key: "2026", sequence: 1, previous: FiscalHashing.genesisHash, end: "2027-01-01T00:00:00.000Z")
        #expect(try status(db, "period_closures") == FiscalChainStatus(chain: "period_closures", terminalId: "T01", checkedCount: 2, isValid: true))

        // Une annuelle chaînée sur la dernière mensuelle au lieu de l'annuelle précédente : rupture dans la chaîne annuelle.
        try Self.insertSignedClosure(db, type: 2, key: "2027", sequence: 2, previous: december, end: "2028-01-01T00:00:00.000Z")
        #expect(try status(db, "period_closures")?.break == FiscalChainBreak(sequenceNumber: 2, reference: "A-2027", kind: "previous_hash_mismatch"))
    }
}
```

Dans `LocalFiscalReportTests.swift`, remplacer dans `verifyChainsReturnsStructuredValidChains` :

```swift
        #expect(result.chains.count == 3)
```

par :

```swift
        #expect(result.chains.map(\.chain) == ["receipts", "z_closures", "period_closures", "journal", "archives"])
```

- [ ] **Step 3: Lancer les tests pour les voir échouer**

Run: `cd ios/Packages/PosKit && swift test --filter 'LocalChainVerificationTests|LocalFiscalReportTests'`
Expected: FAIL (chaînes `period_closures` / `archives` absentes ; `chains.map(\.chain)` vaut `["receipts", "z_closures", "journal"]`).

- [ ] **Step 4: Écrire l'implémentation**

`ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository+PeriodChains.swift` :

```swift
import Foundation

/// Vérification des chaînes de clôtures de période et d'archives : miroir de `VerifyPeriodClosuresChain` et `VerifyArchivesChain`
/// (`FiscalChainVerificationService.cs`). Les lignes sont lues brutes (sans `periodClosureRecord` / `archiveRecord`, qui complètent
/// les valeurs manquantes) : une colonne vide doit casser la chaîne, pas être remplacée.
extension LocalFiscalRepository {
    /// Une ligne par poste ; aucun poste : une ligne vide valide sans poste, comme le .NET.
    func periodClosureChainStatuses() throws -> [FiscalChainStatus] {
        let terminals = try db.query("SELECT DISTINCT TerminalId FROM PeriodClosures ORDER BY TerminalId").compactMap { $0.string("TerminalId") }
        guard !terminals.isEmpty else { return [FiscalChainStatus(chain: "period_closures", terminalId: nil, checkedCount: 0, isValid: true)] }
        return try terminals.map { try periodClosureChainStatus(terminal: $0) }
    }

    /// Dans un poste, chaque type (1 mensuelle, 2 annuelle) est une chaîne depuis la genèse. Le .NET parcourt les types dans l'ordre
    /// d'apparition ; ici mensuelle puis annuelle (ne change le verdict que si les deux types sont rompus).
    private func periodClosureChainStatus(terminal: String) throws -> FiscalChainStatus {
        let rows = try db.query("SELECT * FROM PeriodClosures WHERE TerminalId = ? ORDER BY PeriodType, ClosureSequence", [.text(terminal)])
        for type in Set(rows.compactMap { $0.int("PeriodType") }).sorted() {
            var previous = FiscalHashing.genesisHash
            var expected = 1
            for row in rows where row.int("PeriodType") == type {
                let sequence = row.int("ClosureSequence") ?? 0
                let key = row.string("PeriodKey") ?? ""
                let broken = { (kind: String) in
                    FiscalChainStatus(
                        chain: "period_closures", terminalId: terminal, checkedCount: rows.count, isValid: false,
                        break: FiscalChainBreak(sequenceNumber: sequence, reference: "\(type == 1 ? "M" : "A")-\(key)", kind: kind)
                    )
                }
                if sequence != expected { return broken("sequence_gap") }
                if row.string("PreviousSignatureHash") != previous { return broken("previous_hash_mismatch") }
                guard let periodEnd = row.date("PeriodEndUtc") else { return broken("signature_mismatch") }
                let signature = FiscalHashing.periodClosureHash(
                    previousHash: previous, terminalId: terminal, periodType: type, periodKey: key,
                    totalTtcCents: row.int("TotalTtcAmount") ?? 0, totalHtCents: row.int("TotalHtAmount") ?? 0,
                    taxesJson: row.string("TaxesSummaryJson") ?? "", tendersJson: row.string("TenderTotalsJson") ?? "",
                    perpetualGrandTotalCents: row.int("PerpetualGrandTotalCents") ?? 0, periodEnd: periodEnd
                )
                if signature != row.string("SignatureHash") { return broken("signature_mismatch") }
                previous = row.string("SignatureHash") ?? ""
                expected += 1
            }
        }
        return FiscalChainStatus(chain: "period_closures", terminalId: terminal, checkedCount: rows.count, isValid: true)
    }

    /// Chaîne globale des archives (tous postes), par `ArchiveSequence`.
    func archiveChainStatus() throws -> FiscalChainStatus {
        let rows = try db.query("SELECT * FROM FiscalArchives ORDER BY ArchiveSequence")
        var previous = FiscalHashing.genesisHash
        var expected = 1
        for row in rows {
            let sequence = row.int("ArchiveSequence") ?? 0
            let broken = { (kind: String) in
                FiscalChainStatus(
                    chain: "archives", terminalId: nil, checkedCount: rows.count, isValid: false,
                    break: FiscalChainBreak(sequenceNumber: sequence, reference: "ARC-\(sequence)", kind: kind)
                )
            }
            if sequence != expected { return broken("sequence_gap") }
            if row.string("PreviousSignatureHash") != previous { return broken("previous_hash_mismatch") }
            guard let createdAt = row.date("CreatedAtUtc") else { return broken("signature_mismatch") }
            let signature = FiscalHashing.archiveHash(
                previousHash: previous, periodType: row.int("PeriodType") ?? 0, periodKey: row.string("PeriodKey") ?? "",
                fileSha256: row.string("FileSha256") ?? "", createdAt: createdAt
            )
            if signature != row.string("SignatureHash") { return broken("signature_mismatch") }
            previous = row.string("SignatureHash") ?? ""
            expected += 1
        }
        return FiscalChainStatus(chain: "archives", terminalId: nil, checkedCount: rows.count, isValid: true)
    }
}
```

Dans `LocalFiscalRepository.swift`, `verifyChains()` : juste avant le `return issues` final (l. 535), ajouter :

```swift
        // Clôtures de période et archives : même contrôle que `verifyAllChains` (la restauration d'une sauvegarde les couvre ainsi).
        for status in try periodClosureChainStatuses() + [archiveChainStatus()] {
            guard let broken = status.break else { continue }
            issues.append("\(status.chain) \(status.terminalId ?? "-") \(broken.reference)#\(broken.sequenceNumber): \(broken.kind)")
        }
```

et mettre à jour son commentaire de documentation :

```swift
    /// Recalcule les chaînes (reçus par terminal, clôtures Z, JET, clôtures de période, archives). Renvoie les anomalies
    /// (`sequence_gap`, `previous_hash_mismatch`, `signature_mismatch`).
```

`verifyAllChains(now:)` : remplacer le commentaire `/// Vérification complète structurée de toutes les chaînes locales (reçus, clôtures journalières Z, JET).` par
`/// Vérification complète structurée, dans l'ordre du .NET : reçus, Z, clôtures de période, JET, archives.`, puis remplacer

```swift
        // 3. Journal des événements techniques (JET)
```

par

```swift
        // 3. Clôtures de période (par poste, chaque type depuis la genèse)
        chainStatuses += try periodClosureChainStatuses()

        // 4. Journal des événements techniques (JET)
```

et remplacer

```swift
        chainStatuses.append(FiscalChainStatus(chain: "journal", terminalId: nil, checkedCount: jetRows.count, isValid: jetValid, break: jetBreak))
```

par

```swift
        chainStatuses.append(FiscalChainStatus(chain: "journal", terminalId: nil, checkedCount: jetRows.count, isValid: jetValid, break: jetBreak))

        // 5. Archives (chaîne globale)
        chainStatuses.append(try archiveChainStatus())
```

- [ ] **Step 5: Lancer les tests**

Run: `cd ios/Packages/PosKit && swift test --filter 'LocalChainVerificationTests|LocalFiscalReportTests|LocalBackupTests'`
Expected: PASS.
Run: `cd ios/Packages/PosKit && swift test`
Expected: PASS (521 + 5 = 526 tests).

- [ ] **Step 6: Suivi et commit**

Mettre la ligne 1 du suivi à « fait » avec le nombre de tests PosKit relevé.

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository+PeriodChains.swift ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalChainVerificationTests.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalReportTests.swift docs/plans/ipad-standalone-3d-progress.md
git commit -m "feat(ios): vérification des chaînes de clôtures de période et d'archives (codes du .NET)"
```

---

### Task 2: Rupture journalisée `CHAIN_BREAK_DETECTED`

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalJournalEvents.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Fiscal.swift` (`verifyChains()`, l. 191-194)
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalChainVerificationTests.swift`

**Interfaces:**
- Consumes: `verifyAllChains(now:)` (Task 1), `LocalFiscalRepository.appendJournal(eventType:payloadJson:terminalId:operatorId:now:)`, `FiscalJSON.object(_:)`, `FiscalJSON.string(_:)`.
- Produces: `LocalJournalEvents.chainBreakDetected = "CHAIN_BREAK_DETECTED"` ; `LocalPosAPI.verifyChains()` inchangé en signature.

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `LocalChainVerificationTests` :

```swift
    /// API sur fichier avec une seconde connexion (les triggers retirés par cette connexion valent pour l'API).
    private func makeAPIWithDatabase(clock: LocalTestClock) async throws -> (LocalPosAPI, SQLiteDatabase) {
        let path = temporaryDatabasePath()
        let api = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
        _ = try await api.login(pin: "1234")
        return (api, try SQLiteDatabase(path: path))
    }

    @Test func aBrokenChainIsJournaledAndTheJournalStaysChained() async throws {
        let clock = LocalTestClock("2026-10-02T09:00:00Z")
        let (api, db) = try await makeAPIWithDatabase(clock: clock)
        _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        #expect(try await api.verifyChains().isValid)
        #expect(try db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'CHAIN_BREAK_DETECTED'").isEmpty)

        try db.exec("DROP TRIGGER TR_PeriodClosures_NoUpdate")
        try db.run("UPDATE PeriodClosures SET TotalTtcAmount = 1")
        #expect(try await api.verifyChains().isValid == false)
        let entry = try #require(try db.query("SELECT * FROM JournalEntries WHERE EventType = 'CHAIN_BREAK_DETECTED'").first)
        #expect(entry.string("PayloadJson")
            == #"{"Chain":"period_closures","TerminalId":"T01","SequenceNumber":1,"Reference":"M-2026-09","Kind":"signature_mismatch"}"#)
        #expect(entry.string("TerminalId") == "T01")
        #expect(entry.string("OperatorId") == nil)
        #expect(try await api.verifyChains().chains.first { $0.chain == "journal" }?.isValid == true)
    }

    @Test func aFailingBreakJournalDoesNotHideTheVerdict() async throws {
        let clock = LocalTestClock("2026-10-02T09:00:00Z")
        let (api, db) = try await makeAPIWithDatabase(clock: clock)
        _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        try db.exec("DROP TRIGGER TR_PeriodClosures_NoUpdate")
        try db.run("UPDATE PeriodClosures SET TotalTtcAmount = 1")
        try db.exec("CREATE TRIGGER failBreak BEFORE INSERT ON JournalEntries WHEN NEW.EventType = 'CHAIN_BREAK_DETECTED' BEGIN SELECT RAISE(ABORT, 'boom'); END")
        let result = try await api.verifyChains()
        #expect(result.isValid == false)
        #expect(result.chains.first { $0.chain == "period_closures" }?.break?.kind == "signature_mismatch")
    }
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run: `cd ios/Packages/PosKit && swift test --filter LocalChainVerificationTests`
Expected: FAIL sur `aBrokenChainIsJournaledAndTheJournalStaysChained` (aucune entrée `CHAIN_BREAK_DETECTED`).

- [ ] **Step 3: Écrire l'implémentation**

`LocalJournalEvents.swift`, ajouter après `archiveExported` :

```swift
    static let chainBreakDetected = "CHAIN_BREAK_DETECTED"
```

`LocalPosAPI+Fiscal.swift`, remplacer `verifyChains()` par :

```swift
    /// Vérifie toutes les chaînes (`POST /api/fiscal/verify`). Comme le .NET, une rupture est journalisée (`CHAIN_BREAK_DETECTED`, première
    /// chaîne rompue, poste de la chaîne, sans opérateur) ; un échec de cette écriture ne change pas le verdict rendu.
    public func verifyChains() async throws -> FiscalVerificationResult {
        try requireManager()
        let fiscal = fiscalRepository
        let result = try fiscal.verifyAllChains(now: clock())
        if let broken = result.chains.first(where: { !$0.isValid }) {
            let now = clock()
            _ = try? db.transaction {
                try fiscal.appendJournal(
                    eventType: LocalJournalEvents.chainBreakDetected,
                    payloadJson: FiscalJSON.object([
                        ("Chain", FiscalJSON.string(broken.chain)),
                        ("TerminalId", broken.terminalId.map(FiscalJSON.string) ?? "null"),
                        ("SequenceNumber", broken.break.map { "\($0.sequenceNumber)" } ?? "null"),
                        ("Reference", broken.break.map { FiscalJSON.string($0.reference) } ?? "null"),
                        ("Kind", broken.break.map { FiscalJSON.string($0.kind) } ?? "null"),
                    ]),
                    terminalId: broken.terminalId, operatorId: nil, now: now
                )
            }
        }
        return result
    }
```

- [ ] **Step 4: Lancer les tests**

Run: `cd ios/Packages/PosKit && swift test`
Expected: PASS (528 tests).

- [ ] **Step 5: Suivi et commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalJournalEvents.swift ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Fiscal.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalChainVerificationTests.swift docs/plans/ipad-standalone-3d-progress.md
git commit -m "feat(ios): JET CHAIN_BREAK_DETECTED à la vérification, comme le .NET"
```

---

### Task 3: Refus métier localisés (clôtures de période, archives)

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+PeriodClosures.swift` (`apiError(_:)`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Archives.swift` (constantes d'erreur, `createArchive`, `verifyArchive`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Resources/en.lproj/Localizable.strings`, `fr.lproj/…`, `ar.lproj/…`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPeriodClosureAPITests.swift`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalArchiveAPITests.swift` (l. 93, 103, 119, 137-138)

**Interfaces:**
- Consumes: `L10n.string(_:) -> String` (PosKit, interne).
- Produces: clés PosKit `fiscal.period_error_invalid_key`, `fiscal.period_error_not_ended`, `fiscal.period_error_already_closed`, `fiscal.period_error_missing_daily` (`%@`), `fiscal.period_error_missing_monthly` (`%@`), `fiscal.archive_error_exists`, `fiscal.archive_error_chain_unreadable`, `fiscal.archive_error_file_missing`, `fiscal.archive_error_closure_missing`, `fiscal.archive_error_empty_file` ; dans `LocalPosAPI+Archives.swift` : `private static var archiveExists`, `archiveChainCorrupt`, `archiveFileMissing`, `closureMissing`, `emptyArchiveFile: APIError` (Tasks 4 et 5 s'en servent). Statuts HTTP inchangés.

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `LocalPeriodClosureAPITests` :

```swift
    @Test func refusalsCarryLocalizedReadableMessages() async throws {
        let clock = LocalTestClock("2026-09-10T12:00:00Z")
        let api = try await makeLocalAPI(clock: clock)
        await #expect(throws: APIError.server(status: 409, message: L10n.string("fiscal.period_error_not_ended"))) {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        }
        try await payOneBurger(api, table: "T1")
        clock.set("2026-10-02T09:00:00Z")
        await #expect(throws: APIError.server(status: 409, message: String(format: L10n.string("fiscal.period_error_missing_daily"), "2026-09-10"))) {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        }
        await #expect(throws: APIError.server(status: 400, message: L10n.string("fiscal.period_error_invalid_key"))) {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-13")
        }
        let months = (1...12).map { String(format: "2025-%02d", $0) }.joined(separator: ", ")
        await #expect(throws: APIError.server(status: 409, message: String(format: L10n.string("fiscal.period_error_missing_monthly"), months))) {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .annual, periodKey: "2025")
        }
        _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-08")
        await #expect(throws: APIError.server(status: 409, message: L10n.string("fiscal.period_error_already_closed"))) {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-08")
        }
    }
```

Dans `LocalArchiveAPITests.swift`, remplacer les messages littéraux :

```swift
        await #expect(throws: APIError.server(status: 409, message: "Une archive existe déjà pour cette clôture.")) {
```
→
```swift
        await #expect(throws: APIError.server(status: 409, message: L10n.string("fiscal.archive_error_exists"))) {
```

```swift
        await #expect(throws: APIError.notFound("Clôture de période introuvable.")) {
```
→
```swift
        await #expect(throws: APIError.notFound(L10n.string("fiscal.archive_error_closure_missing"))) {
```

```swift
        await #expect(throws: APIError.server(status: 400, message: "Aucun fichier reçu.")) {
```
→
```swift
        await #expect(throws: APIError.server(status: 400, message: L10n.string("fiscal.archive_error_empty_file"))) {
```

et les deux lignes 137-138 :

```swift
        await #expect(throws: APIError.notFound("Fichier d'archive introuvable.")) { _ = try await c.api.archiveFile(id: archive.id) }
        await #expect(throws: APIError.notFound("Fichier d'archive introuvable.")) { _ = try await c.api.archiveFile(id: UUID()) }
```
→
```swift
        await #expect(throws: APIError.notFound(L10n.string("fiscal.archive_error_file_missing"))) { _ = try await c.api.archiveFile(id: archive.id) }
        await #expect(throws: APIError.notFound(L10n.string("fiscal.archive_error_file_missing"))) { _ = try await c.api.archiveFile(id: UUID()) }
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run: `cd ios/Packages/PosKit && swift test --filter 'LocalPeriodClosureAPITests|LocalArchiveAPITests'`
Expected: FAIL (messages français codés en dur ≠ `L10n.string(...)`, qui rend la clé brute tant qu'elle n'existe pas).

- [ ] **Step 3: Ajouter les textes**

Ajouter à la fin de `en.lproj/Localizable.strings` :

```
"fiscal.period_error_invalid_key" = "Invalid period key: expected YYYY-MM (monthly) or YYYY (annual).";
"fiscal.period_error_not_ended" = "The fiscal period has not ended yet.";
"fiscal.period_error_already_closed" = "This fiscal period is already closed.";
"fiscal.period_error_missing_daily" = "Missing daily (Z) closures for days with receipts: %@.";
"fiscal.period_error_missing_monthly" = "Missing monthly closures for the fiscal year: %@.";
"fiscal.archive_error_exists" = "An archive already exists for this closure.";
"fiscal.archive_error_chain_unreadable" = "The fiscal archive chain is unreadable.";
"fiscal.archive_error_file_missing" = "Archive file not found.";
"fiscal.archive_error_closure_missing" = "Period closure not found.";
"fiscal.archive_error_empty_file" = "No file received.";
```

à la fin de `fr.lproj/Localizable.strings` :

```
"fiscal.period_error_invalid_key" = "Clé de période invalide : attendu AAAA-MM (mensuelle) ou AAAA (annuelle).";
"fiscal.period_error_not_ended" = "La période fiscale n'est pas encore terminée.";
"fiscal.period_error_already_closed" = "Cette période fiscale est déjà clôturée.";
"fiscal.period_error_missing_daily" = "Clôtures journalières (Z) manquantes pour les jours avec des reçus : %@.";
"fiscal.period_error_missing_monthly" = "Clôtures mensuelles manquantes pour l'exercice : %@.";
"fiscal.archive_error_exists" = "Une archive existe déjà pour cette clôture.";
"fiscal.archive_error_chain_unreadable" = "La chaîne des archives fiscales est illisible.";
"fiscal.archive_error_file_missing" = "Fichier d'archive introuvable.";
"fiscal.archive_error_closure_missing" = "Clôture de période introuvable.";
"fiscal.archive_error_empty_file" = "Aucun fichier reçu.";
```

à la fin de `ar.lproj/Localizable.strings` :

```
"fiscal.period_error_invalid_key" = "مفتاح الفترة غير صالح: المتوقع YYYY-MM (شهري) أو YYYY (سنوي).";
"fiscal.period_error_not_ended" = "لم تنتهِ الفترة المالية بعد.";
"fiscal.period_error_already_closed" = "تم إقفال هذه الفترة المالية بالفعل.";
"fiscal.period_error_missing_daily" = "إقفالات يومية (Z) ناقصة للأيام التي تتضمن إيصالات: %@.";
"fiscal.period_error_missing_monthly" = "إقفالات شهرية ناقصة للسنة المالية: %@.";
"fiscal.archive_error_exists" = "يوجد أرشيف بالفعل لهذا الإقفال.";
"fiscal.archive_error_chain_unreadable" = "سلسلة الأرشيفات المالية غير مقروءة.";
"fiscal.archive_error_file_missing" = "ملف الأرشيف غير موجود.";
"fiscal.archive_error_closure_missing" = "إقفال الفترة غير موجود.";
"fiscal.archive_error_empty_file" = "لم يتم استلام أي ملف.";
```

- [ ] **Step 4: Brancher les messages**

`LocalPosAPI+PeriodClosures.swift`, remplacer `apiError(_:)` :

```swift
    /// Refus métier lisibles dans la langue de l'app (le .NET renvoie un code et un message anglais) ; statuts identiques au serveur.
    private static func apiError(_ error: PeriodClosureError) -> APIError {
        switch error {
        case .invalidKey:
            .server(status: 400, message: L10n.string("fiscal.period_error_invalid_key"))
        case .notEnded:
            .server(status: 409, message: L10n.string("fiscal.period_error_not_ended"))
        case .alreadyClosed:
            .server(status: 409, message: L10n.string("fiscal.period_error_already_closed"))
        case .missingDailyClosures(let days):
            .server(status: 409, message: String(format: L10n.string("fiscal.period_error_missing_daily"), days.joined(separator: ", ")))
        case .missingMonthlyClosures(let months):
            .server(status: 409, message: String(format: L10n.string("fiscal.period_error_missing_monthly"), months.joined(separator: ", ")))
        }
    }
```

`LocalPosAPI+Archives.swift`, remplacer les trois constantes du haut :

```swift
    private static let archiveExists = APIError.server(status: 409, message: "Une archive existe déjà pour cette clôture.")
    private static let archiveChainCorrupt = APIError.server(status: 500, message: "La chaîne des archives fiscales est illisible : création refusée.")
    private static let archiveFileMissing = APIError.notFound("Fichier d'archive introuvable.")
```

par (propriétés calculées : le texte suit la langue courante) :

```swift
    private static var archiveExists: APIError { .server(status: 409, message: L10n.string("fiscal.archive_error_exists")) }
    private static var archiveChainCorrupt: APIError { .server(status: 500, message: L10n.string("fiscal.archive_error_chain_unreadable")) }
    private static var archiveFileMissing: APIError { .notFound(L10n.string("fiscal.archive_error_file_missing")) }
    private static var closureMissing: APIError { .notFound(L10n.string("fiscal.archive_error_closure_missing")) }
    private static var emptyArchiveFile: APIError { .server(status: 400, message: L10n.string("fiscal.archive_error_empty_file")) }
```

Dans `createArchive`, remplacer `throw APIError.notFound("Clôture de période introuvable.")` par `throw Self.closureMissing` ; dans `verifyArchive`, remplacer `throw APIError.server(status: 400, message: "Aucun fichier reçu.")` par `throw Self.emptyArchiveFile`.

- [ ] **Step 5: Lancer les tests**

Run: `cd ios/Packages/PosKit && swift test --filter 'LocalPeriodClosureAPITests|LocalArchiveAPITests|LocalizationTests'`
Expected: PASS (dont `everyKeyIsTranslated` pour `en`, `fr`, `ar` et `keysAreSemantic`).
Run: `cd ios/Packages/PosKit && swift test`
Expected: PASS (529 tests).

- [ ] **Step 6: Suivi et commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+PeriodClosures.swift ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Archives.swift ios/Packages/PosKit/Sources/PosKit/Resources/en.lproj/Localizable.strings ios/Packages/PosKit/Sources/PosKit/Resources/fr.lproj/Localizable.strings ios/Packages/PosKit/Sources/PosKit/Resources/ar.lproj/Localizable.strings ios/Packages/PosKit/Tests/PosKitTests/Local/LocalPeriodClosureAPITests.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalArchiveAPITests.swift docs/plans/ipad-standalone-3d-progress.md
git commit -m "feat(ios): refus de clôture et d'archive localisés FR/EN/AR"
```

---

### Task 4: Archives : ligne illisible = erreur lisible, voisin illisible = `chain_break`

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Archives.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository+Archives.swift` (`verifyArchive`, lecture de l'archive précédente)
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalArchiveRepositoryTests.swift`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalArchiveAPITests.swift`

**Interfaces:**
- Consumes: `FiscalArchiveError.corruptChain`, `Self.archiveChainCorrupt` (Task 3).
- Produces: `private func readingArchives<T>(_ body: () throws -> T) throws -> T` dans `LocalPosAPI+Archives.swift` (Task 5 l'utilise dans `archiveFile`). `archives()`, `createArchive`, `verifyArchive`, `archiveFile` ne laissent plus sortir `FiscalArchiveError` (500 lisible).

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `LocalFiscalArchiveRepositoryTests` :

```swift
    @Test func anUnreadablePreviousArchiveIsAChainBreakNotAnError() throws {
        let f = try Self.makeFixture()
        try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        let (february, februaryZip) = try Self.archive(f, f.february, at: "2025-03-02T10:00:00.000Z")
        // Falsification hors application : le trigger d'immuabilité est contourné sur cette base de test.
        try f.db.exec("DROP TRIGGER TR_FiscalArchives_NoUpdate")
        try f.db.run("UPDATE FiscalArchives SET Id = 'pas-un-uuid' WHERE ArchiveSequence = 1")
        #expect(try f.repo.verifyArchive(data: februaryZip, fileName: nil)
            == ArchiveVerificationResult(isValid: false, archiveId: february.id, reason: "chain_break"))
    }
```

Ajouter à `LocalArchiveAPITests` :

```swift
    @Test func anUnreadableArchiveRowGivesAReadableErrorEverywhere() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        let zip = try Data(contentsOf: c.folder.appendingPathComponent(archive.fileName))
        try c.db.exec("DROP TRIGGER TR_FiscalArchives_NoUpdate")
        try c.db.run("UPDATE FiscalArchives SET Id = 'pas-un-uuid'")
        let unreadable = APIError.server(status: 500, message: L10n.string("fiscal.archive_error_chain_unreadable"))
        await #expect(throws: unreadable) { _ = try await c.api.archives() }
        await #expect(throws: unreadable) { _ = try await c.api.verifyArchive(data: zip, fileName: archive.fileName) }
        await #expect(throws: unreadable) { _ = try await c.api.createArchive(periodClosureId: archive.periodClosureId) }
    }
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run: `cd ios/Packages/PosKit && swift test --filter 'LocalFiscalArchiveRepositoryTests|LocalArchiveAPITests'`
Expected: FAIL (`FiscalArchiveError.corruptChain` levée au lieu de `chain_break` / de l'`APIError` attendue).

- [ ] **Step 3: Écrire l'implémentation**

`LocalFiscalRepository+Archives.swift`, dans `verifyArchive`, remplacer :

```swift
            let previous = try firstArchive(where: "ArchiveSequence = ?", .integer(archive.archiveSequence - 1))
```

par :

```swift
            // Archive précédente illisible : le lien ne peut pas être prouvé, c'est une rupture (verdict, pas une erreur).
            let previous = try? firstArchive(where: "ArchiveSequence = ?", .integer(archive.archiveSequence - 1))
```

`LocalPosAPI+Archives.swift` : ajouter avant la dernière accolade de l'extension :

```swift
    /// Une ligne de `FiscalArchives` illisible remonte en erreur lisible (500) au lieu de `FiscalArchiveError` brute.
    private func readingArchives<T>(_ body: () throws -> T) throws -> T {
        do { return try body() } catch FiscalArchiveError.corruptChain { throw Self.archiveChainCorrupt }
    }
```

puis :
- `archives()` : remplacer `return try fiscalRepository.fiscalArchives()` par `return try readingArchives { try fiscalRepository.fiscalArchives() }` ;
- `createArchive` : remplacer `guard try fiscal.fiscalArchive(periodClosureId: periodClosureId) == nil else { throw Self.archiveExists }` par `guard try readingArchives({ try fiscal.fiscalArchive(periodClosureId: periodClosureId) }) == nil else { throw Self.archiveExists }` ;
- `verifyArchive` : remplacer `return try fiscalRepository.verifyArchive(data: data, fileName: fileName)` par `return try readingArchives { try fiscalRepository.verifyArchive(data: data, fileName: fileName) }` ;
- `archiveFile` : remplacer `guard let archive = try fiscal.fiscalArchive(id: id) else { throw Self.archiveFileMissing }` par `guard let archive = try readingArchives({ try fiscal.fiscalArchive(id: id) }) else { throw Self.archiveFileMissing }`.

- [ ] **Step 4: Lancer les tests**

Run: `cd ios/Packages/PosKit && swift test`
Expected: PASS (531 tests).

- [ ] **Step 5: Suivi et commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Archives.swift ios/Packages/PosKit/Sources/PosKit/Local/LocalFiscalRepository+Archives.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalArchiveRepositoryTests.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalArchiveAPITests.swift docs/plans/ipad-standalone-3d-progress.md
git commit -m "fix(ios): archive illisible en erreur lisible, voisin illisible en chain_break"
```

---

### Task 5: `archiveFile(id:)` récupère un fichier perdu (`.tmp`, puis reconstruction contrôlée)

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Archives.swift` (`archiveFile`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Resources/{en,fr,ar}.lproj/Localizable.strings`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalArchiveAPITests.swift`

**Interfaces:**
- Consumes: `LocalFiscalRepository.archiveZip(for:archiveId:createdAt:) -> Data`, `periodClosure(id:) -> PeriodClosureRecord?`, `FiscalHashing.sha256Hex(_:)`, `FiscalHashing.guidText(_:)`, `readingArchives` (Task 4).
- Produces: `public func archiveFile(id: UUID) async throws -> URL` (même signature) ; clé `fiscal.archive_error_unrecoverable` ; erreur `APIError.notFound(L10n.string("fiscal.archive_error_unrecoverable"))` quand rien n'est servable.

- [ ] **Step 1: Écrire les tests qui échouent**

Dans `LocalArchiveAPITests.theFileIsServedAndTheExportJournaledWithoutTerminalOrOperator`, supprimer les deux lignes :

```swift
        try FileManager.default.removeItem(at: url)
        await #expect(throws: APIError.notFound(L10n.string("fiscal.archive_error_file_missing"))) { _ = try await c.api.archiveFile(id: archive.id) }
```

(la ligne `archiveFile(id: UUID())` reste). Ajouter :

```swift
    @Test func aMissingFileIsRebuiltIdenticallyAndServed() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        try FileManager.default.removeItem(at: c.folder.appendingPathComponent(archive.fileName))
        let url = try await c.api.archiveFile(id: archive.id)
        let data = try Data(contentsOf: url)
        #expect(FiscalHashing.sha256Hex(data) == archive.fileSha256)
        #expect(folderContents(c) == [archive.fileName])
        #expect(try await c.api.verifyArchive(data: data, fileName: archive.fileName).isValid)
        #expect(try c.db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'ARCHIVE_EXPORTED'").count == 1)
    }

    @Test func aTemporaryFileLeftByAFailedMoveIsRenamed() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        let temporary = c.folder.appendingPathComponent("\(FiscalHashing.guidText(archive.id)).tmp")
        try FileManager.default.moveItem(at: c.folder.appendingPathComponent(archive.fileName), to: temporary)
        let url = try await c.api.archiveFile(id: archive.id)
        #expect(folderContents(c) == [archive.fileName]) // renommé, pas recopié à côté
        #expect(FiscalHashing.sha256Hex(try Data(contentsOf: url)) == archive.fileSha256)
    }

    @Test func aTruncatedTemporaryFileIsIgnoredAndTheZipRebuilt() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        let temporary = c.folder.appendingPathComponent("\(FiscalHashing.guidText(archive.id)).tmp")
        try FileManager.default.removeItem(at: c.folder.appendingPathComponent(archive.fileName))
        try Data("PK tronqué".utf8).write(to: temporary)
        let url = try await c.api.archiveFile(id: archive.id)
        #expect(FiscalHashing.sha256Hex(try Data(contentsOf: url)) == archive.fileSha256)
        #expect(Set(folderContents(c)) == [archive.fileName, temporary.lastPathComponent])
    }

    @Test func aFileThatCannotBeRebuiltIdenticallyIsRefusedAndNothingIsWritten() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        try FileManager.default.removeItem(at: c.folder.appendingPathComponent(archive.fileName))
        try c.db.exec("DROP TRIGGER TR_FiscalReceipts_NoUpdate")
        try c.db.run("UPDATE FiscalReceipts SET ReceiptNumber = 'ALTERE'")
        await #expect(throws: APIError.notFound(L10n.string("fiscal.archive_error_unrecoverable"))) {
            _ = try await c.api.archiveFile(id: archive.id)
        }
        #expect(folderContents(c).isEmpty)
        #expect(try c.db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'ARCHIVE_EXPORTED'").isEmpty)
    }
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run: `cd ios/Packages/PosKit && swift test --filter LocalArchiveAPITests`
Expected: FAIL (`archiveFile` répond `archive_error_file_missing` dès que le ZIP manque).

- [ ] **Step 3: Ajouter le texte**

`en.lproj` : `"fiscal.archive_error_unrecoverable" = "Archive file missing and it cannot be rebuilt identically (fingerprint differs).";`
`fr.lproj` : `"fiscal.archive_error_unrecoverable" = "Fichier d'archive manquant et non reconstructible à l'identique (empreinte différente).";`
`ar.lproj` : `"fiscal.archive_error_unrecoverable" = "ملف الأرشيف مفقود ولا يمكن إعادة بنائه بشكل مطابق (البصمة مختلفة).";`

- [ ] **Step 4: Écrire l'implémentation**

`LocalPosAPI+Archives.swift` : ajouter la constante

```swift
    private static var archiveUnrecoverable: APIError { .notFound(L10n.string("fiscal.archive_error_unrecoverable")) }
```

remplacer dans `archiveFile` :

```swift
        guard file.path.hasPrefix(folder.path + "/"), FileManager.default.fileExists(atPath: file.path) else { throw Self.archiveFileMissing }
```

par :

```swift
        guard file.path.hasPrefix(folder.path + "/") else { throw Self.archiveFileMissing }
        if !FileManager.default.fileExists(atPath: file.path) { try recoverArchiveFile(archive, to: file, in: folder) }
```

et mettre à jour son commentaire : `/// Fichier d'une archive (…), pour le partage. Fichier absent : voir `recoverArchiveFile`. Journalise …` (garder la suite). Ajouter :

```swift
    /// Fichier absent (déplacement raté après le COMMIT, restauration d'une sauvegarde `.posbak` qui n'inclut pas les archives) :
    /// renomme le `<id>.tmp` laissé par `createArchive` s'il a l'empreinte signée ; sinon reconstruit le ZIP depuis la base (mêmes
    /// identifiant et horodatage, données immuables, ZIP déterministe) et ne l'écrit que si son SHA-256 est celui de la ligne.
    private func recoverArchiveFile(_ archive: FiscalArchive, to file: URL, in folder: URL) throws {
        let expected = archive.fileSha256.lowercased()
        let temporary = folder.appendingPathComponent("\(FiscalHashing.guidText(archive.id)).tmp")
        if let data = try? Data(contentsOf: temporary), FiscalHashing.sha256Hex(data) == expected {
            try FileManager.default.moveItem(at: temporary, to: file)
            return
        }
        let fiscal = fiscalRepository
        guard let closure = try fiscal.periodClosure(id: archive.periodClosureId) else { throw Self.archiveUnrecoverable }
        let zip = try fiscal.archiveZip(for: closure, archiveId: archive.id, createdAt: archive.createdAtUtc)
        guard FiscalHashing.sha256Hex(zip) == expected else { throw Self.archiveUnrecoverable }
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        try zip.write(to: file, options: .withoutOverwriting)
    }
```

- [ ] **Step 5: Lancer les tests**

Run: `cd ios/Packages/PosKit && swift test`
Expected: PASS (535 tests, dont `LocalizationTests`).

- [ ] **Step 6: Suivi et commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Archives.swift ios/Packages/PosKit/Sources/PosKit/Resources/en.lproj/Localizable.strings ios/Packages/PosKit/Sources/PosKit/Resources/fr.lproj/Localizable.strings ios/Packages/PosKit/Sources/PosKit/Resources/ar.lproj/Localizable.strings ios/Packages/PosKit/Tests/PosKitTests/Local/LocalArchiveAPITests.swift docs/plans/ipad-standalone-3d-progress.md
git commit -m "feat(ios): fichier d'archive perdu récupéré (.tmp ou reconstruction contrôlée par SHA-256)"
```

---

### Task 6: FEC : filtre de période en SQL, ordre des séquences

**Files:**
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalFecExporter.swift` (fichier entier)
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecReferenceTests.swift`

**Interfaces:**
- Consumes: `FiscalHashing.storedText(_:) -> String`, `LocalFecWriter.export(receipts:from:to:siren:) -> FecExport` (refiltre et inverse les dates lui-même).
- Produces: `LocalFecExporter.export(from:to:siren:) throws -> FecExport` (même signature, sortie inchangée) ; `static let storedDatePattern: String`.

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `LocalFecReferenceTests` :

```swift
    private static func migrated() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return db
    }

    /// Reçu de 11,00 € (TVA 10 %) réglé en espèces.
    private static func insertReceipt(_ db: SQLiteDatabase, number: String, sequence: Int, at stored: String) throws {
        let id = UUID().uuidString
        try db.run(
            """
            INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid)
            VALUES (?, 'T01', ?, ?, ?, 1100, 1000, '{"10":100}', '', '', ?, 0)
            """,
            [.text(id), .text(number), .uuid(UUID()), .integer(sequence), .text(stored)]
        )
        try db.run(
            "INSERT INTO PaymentTenders (Id, FiscalReceiptId, Method, Amount, Tendered, ChangeGiven) VALUES (?, ?, 0, 1100, 1100, 0)",
            [.uuid(UUID()), .text(id)]
        )
    }

    /// Pièces (`PieceRef`, 9e colonne) dans l'ordre des écritures, sans doublon consécutif.
    private static func pieces(_ data: Data) -> [String] {
        var seen: [String] = []
        for line in String(decoding: data, as: UTF8.self).split(separator: "\n").dropFirst() {
            let piece = String(line.split(separator: "\t", omittingEmptySubsequences: false)[8])
            if seen.last != piece { seen.append(piece) }
        }
        return seen
    }

    @Test func receiptsAreWrittenInSequenceOrderWhateverTheInsertionOrder() throws {
        let db = try Self.migrated()
        try Self.insertReceipt(db, number: "T01-000002", sequence: 2, at: "2026-10-05T10:00:00.000Z")
        try Self.insertReceipt(db, number: "T01-000001", sequence: 1, at: "2026-10-05T11:00:00.000Z")
        let year = try LocalFecExporter(db: db).export(
            from: try #require(SQLDate.parse("2026-01-01T00:00:00.000Z")), to: try #require(SQLDate.parse("2026-12-31T23:59:59.999Z")), siren: ""
        )
        #expect(Self.pieces(year.data) == ["T01-000001", "T01-000002"])
    }

    @Test func theSQLPeriodFilterKeepsTheExactBounds() throws {
        let db = try Self.migrated()
        try Self.insertReceipt(db, number: "T01-000001", sequence: 1, at: "2026-09-30T23:59:59.999Z")
        try Self.insertReceipt(db, number: "T01-000002", sequence: 2, at: "2026-10-01T00:00:00.000Z")
        try Self.insertReceipt(db, number: "T01-000003", sequence: 3, at: "2026-10-31T23:59:59.999Z")
        try Self.insertReceipt(db, number: "T01-000004", sequence: 4, at: "2026-11-01T00:00:00.000Z")
        let exporter = LocalFecExporter(db: db)
        let first = try #require(SQLDate.parse("2026-10-01T00:00:00.000Z"))
        let last = try #require(SQLDate.parse("2026-10-31T23:59:59.999Z"))
        #expect(Self.pieces(try exporter.export(from: first, to: last, siren: "").data) == ["T01-000002", "T01-000003"])
        #expect(Self.pieces(try exporter.export(from: last, to: first, siren: "").data) == ["T01-000002", "T01-000003"]) // dates inversées
        // Borne à une fraction de milliseconde : le texte SQL l'arrondit, le filtre exact de `LocalFecWriter` tranche.
        let afterFirst = first.addingTimeInterval(0.0004)
        let december = try #require(SQLDate.parse("2026-12-01T00:00:00.000Z"))
        #expect(Self.pieces(try exporter.export(from: afterFirst, to: december, siren: "").data) == ["T01-000003", "T01-000004"])
    }

    @Test func aDateStoredInAnotherFormStopsTheExport() throws {
        let db = try Self.migrated()
        try Self.insertReceipt(db, number: "T01-000001", sequence: 1, at: "2026-10-05 10:00:00")
        #expect(throws: APIError.self) {
            _ = try LocalFecExporter(db: db).export(
                from: try #require(SQLDate.parse("2026-10-01T00:00:00.000Z")), to: try #require(SQLDate.parse("2026-10-31T00:00:00.000Z")), siren: ""
            )
        }
    }
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run: `cd ios/Packages/PosKit && swift test --filter LocalFecReferenceTests`
Expected: FAIL sur `aDateStoredInAnotherFormStopsTheExport` (la date `2026-10-05 10:00:00` est relue par `SQLDate.parse` et exportée). Les deux autres passent déjà (ils fixent le comportement à conserver).

- [ ] **Step 3: Écrire l'implémentation**

Remplacer `LocalFecExporter.swift` par :

```swift
import Foundation

/// Export FEC du mode autonome : lit les reçus de la période (`FiscalReceipts`, ordre de séquence) et leurs règlements (`PaymentTenders`,
/// ordre d'enregistrement), puis délègue à `LocalFecWriter`. Comme le .NET, aucun filtre de poste (le poste autonome est seul, `T01`).
/// Le filtre de période est fait en SQL sur le texte stocké (`FiscalHashing.storedText`, qui se trie comme les dates) ; `LocalFecWriter`
/// refait le filtre exact sur les dates, la sortie reste identique au .NET octet pour octet (`LocalFecReferenceTests`).
/// ponytail: les reçus de la période sont chargés en mémoire (quelques milliers par mois) ; au-delà, écrire les lignes au fil de la lecture.
struct LocalFecExporter {
    let db: SQLiteDatabase

    /// Forme exacte de `FiscalHashing.storedText` : seule une date ainsi écrite se compare correctement en texte.
    static let storedDatePattern = "[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z"

    func export(from: Date, to: Date, siren: String) throws -> FecExport {
        // Une date d'une autre forme serait mal classée par le filtre SQL : arrêt plutôt qu'un reçu perdu en silence.
        if let odd = try db.query(
            "SELECT ReceiptNumber FROM FiscalReceipts WHERE CreatedAtUtc IS NULL OR CreatedAtUtc NOT GLOB ? LIMIT 1", [.text(Self.storedDatePattern)]
        ).first {
            throw APIError.server(status: 500, message: "Date illisible pour le reçu \(odd.string("ReceiptNumber") ?? "") : export FEC impossible.")
        }
        // Bornes arrondies à la milliseconde : un reçu (daté à la milliseconde) de [début ; fin] est toujours dans [texte(début) ; texte(fin)].
        let (start, end) = from > to ? (to, from) : (from, to)
        let bounds: [SQLValue] = [.text(FiscalHashing.storedText(start)), .text(FiscalHashing.storedText(end))]

        var tenders: [String: [FecTender]] = [:]
        for row in try db.query(
            """
            SELECT FiscalReceiptId, Method, Amount FROM PaymentTenders
            WHERE upper(FiscalReceiptId) IN (SELECT upper(Id) FROM FiscalReceipts WHERE CreatedAtUtc >= ? AND CreatedAtUtc <= ?)
            ORDER BY rowid
            """,
            bounds
        ) {
            let receiptId = (row.string("FiscalReceiptId") ?? "").uppercased()
            tenders[receiptId, default: []].append(FecTender(method: row.int("Method") ?? 0, amountCents: row.int("Amount") ?? 0))
        }
        let receipts = try db.query(
            """
            SELECT Id, ReceiptNumber, CreatedAtUtc, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson, HtBreakdownJson, VoidedReceiptId
            FROM FiscalReceipts WHERE CreatedAtUtc >= ? AND CreatedAtUtc <= ? ORDER BY SequenceNumber, rowid
            """,
            bounds
        ).map { row throws -> FecReceipt in
            let number = row.string("ReceiptNumber") ?? ""
            guard let createdAt = row.date("CreatedAtUtc") else {
                throw APIError.server(status: 500, message: "Date illisible pour le reçu \(number) : export FEC impossible.")
            }
            return FecReceipt(
                receiptNumber: number, createdAtUtc: createdAt, ttcCents: row.int("TotalTtcAmount") ?? 0, htCents: row.int("TotalHtAmount") ?? 0,
                taxBreakdownJson: row.string("TaxBreakdownJson") ?? "{}", htBreakdownJson: row.string("HtBreakdownJson"),
                voidsReceipt: row.string("VoidedReceiptId") != nil, tenders: tenders[(row.string("Id") ?? "").uppercased()] ?? []
            )
        }
        return LocalFecWriter.export(receipts: receipts, from: from, to: to, siren: siren)
    }
}
```

- [ ] **Step 4: Lancer les tests**

Run: `cd ios/Packages/PosKit && swift test --filter 'LocalFec'`
Expected: PASS, en particulier `everyExportOfTheDotNetFixtureIsByteIdentical` (parité octet pour octet conservée) et `anUnreadableReceiptDateStopsTheExportInsteadOfDroppingTheReceipt`.
Run: `cd ios/Packages/PosKit && swift test`
Expected: PASS (538 tests).

- [ ] **Step 5: Suivi et commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Local/LocalFecExporter.swift ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecReferenceTests.swift docs/plans/ipad-standalone-3d-progress.md
git commit -m "perf(ios): FEC filtré par période en SQL, ordre des séquences et dates hors format couverts"
```

---

### Task 7: FEC : SIREN validé, société, charge utile du JET

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Core/Siren.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Networking/PosAPI.swift` (l. 95 + extension)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Networking/HTTPPosAPI.swift` (`exportFec`, l. 346-357)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Testing/InMemoryPosAPI.swift` (`exportFec`, l. 610)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Fec.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Resources/{en,fr,ar}.lproj/Localizable.strings`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecAPITests.swift`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/NetworkingTests.swift`

**Interfaces:**
- Consumes: `L10n.string`, `FiscalJSON.string`, `LocalFecExporter.export` (Task 6).
- Produces:
  - `public enum Siren { public static func normalized(_ raw: String) -> String; public static func isValid(_ siren: String) -> Bool }` (Task 8 et 9).
  - Protocole : `func exportFec(from: Date, to: Date, siren: String, company: String?) async throws -> (fileName: String, data: Data)` ; extension `PosAPI.exportFec(from:to:siren:)` qui passe `company: nil`.
  - `static func attachmentName(_ response: HTTPURLResponse) -> String?` dans `HTTPPosAPI` (Task 8 l'utilise).
  - Clé `fiscal.fec_error_invalid_siren` ; `LocalPosAPI.exportFec` lève `APIError.server(status: 400, message: L10n.string("fiscal.fec_error_invalid_siren"))`.
  - Décision (consignée dans le doc de la méthode) : JET `FEC_EXPORTED` = SIREN tel que reçu (`""` si vide, comme un client HTTP qui envoie toujours `siren=`), `Company` = texte ou `null`, dates UTC `+00:00` telles que reçues.

- [ ] **Step 1: Écrire les tests qui échouent**

Ajouter à `LocalFecAPITests` :

```swift
    @Test func anInvalidSirenIsRefusedBeforeAnythingIsWritten() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        for siren in ["12345678", "1234567890", "../x", "12345678A", "١٢٣٤٥٦٧٨٩", "123 456 789"] {
            await #expect(throws: APIError.server(status: 400, message: L10n.string("fiscal.fec_error_invalid_siren")), Comment(rawValue: siren)) {
                _ = try await api.exportFec(from: clock.now, to: clock.now, siren: siren)
            }
        }
        #expect(try db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'FEC_EXPORTED'").isEmpty)
        let (fileName, _) = try await api.exportFec(from: clock.now, to: clock.now, siren: " 123456789 ")
        #expect(fileName == "123456789FEC20261012.txt")
    }

    @Test func aFailedExportWritesNoJournalEntry() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        try db.run(
            """
            INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid)
            VALUES (?, 'T01', 'T01-000001', ?, 1, 1100, 1000, '{"10":100}', '', '', 'pas une date', 0)
            """,
            [.uuid(UUID()), .uuid(UUID())]
        )
        await #expect(throws: APIError.self) {
            _ = try await api.exportFec(from: clock.now.addingTimeInterval(-86_400), to: clock.now, siren: "123456789")
        }
        #expect(try db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'FEC_EXPORTED'").isEmpty)
    }

    @Test func theJournalKeepsAnEmptySirenAndTheCompanyAsGiven() async throws {
        let (api, db, _) = try await makeAPIWithDatabase()
        let from = try #require(SQLDate.parse("2026-10-01T00:00:00.000Z"))
        let to = try #require(SQLDate.parse("2026-10-12T12:00:00.000Z"))
        _ = try await api.exportFec(from: from, to: to, siren: "", company: "Chez \"Marie\" & Cie")
        _ = try await api.exportFec(from: from, to: to, siren: "")
        let payloads = try db.query("SELECT PayloadJson FROM JournalEntries WHERE EventType = 'FEC_EXPORTED' ORDER BY ChainSequence")
            .compactMap { $0.string("PayloadJson") }
        #expect(payloads == [
            #"{"StartDateUtc":"2026-10-01T00:00:00+00:00","EndDateUtc":"2026-10-12T12:00:00+00:00","Siren":"","Company":"Chez "Marie" & Cie","FileName":"000000000FEC20261012.txt"}"#,
            #"{"StartDateUtc":"2026-10-01T00:00:00+00:00","EndDateUtc":"2026-10-12T12:00:00+00:00","Siren":"","Company":null,"FileName":"000000000FEC20261012.txt"}"#,
        ])
    }

    @Test func sirenInputIsNormalizedToAsciiDigitsWithoutSpaces() {
        #expect(Siren.normalized("١٢٣ ٤٥٦ ٧٨٩") == "123456789")
        #expect(Siren.normalized(" 123 456 789 ") == "123456789")
        #expect(Siren.normalized("12A/..") == "12A/..")
        #expect(Siren.isValid("") && Siren.isValid("  ") && Siren.isValid(" 123456789 "))
        #expect(!Siren.isValid("12345678") && !Siren.isValid("١٢٣٤٥٦٧٨٩") && !Siren.isValid("../x"))
    }
```

Ajouter à `HTTPPosAPITests` (`NetworkingTests.swift`) :

```swift
    @Test func fecExportSendsTheCompanyOnlyWhenGiven() async throws {
        let api = makeAPI { _ in .init(status: 200, body: "x", headers: ["Content-Disposition": "attachment; filename=123456789FEC20260924.txt"]) }
        let named = try await api.exportFec(from: Date(timeIntervalSince1970: 0), to: Date(), siren: "123456789", company: "Chez Marie")
        #expect(named.fileName == "123456789FEC20260924.txt")
        #expect(last.url?.query?.contains("company=Chez%20Marie") == true)
        _ = try await api.exportFec(from: Date(timeIntervalSince1970: 0), to: Date(), siren: "123456789")
        #expect(last.url?.query?.contains("company") == false)
    }
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run: `cd ios/Packages/PosKit && swift test --filter 'LocalFecAPITests|HTTPPosAPITests'`
Expected: FAIL à la compilation (`Siren` inconnu, pas de paramètre `company`).

- [ ] **Step 3: Écrire l'implémentation**

`ios/Packages/PosKit/Sources/PosKit/Core/Siren.swift` :

```swift
import Foundation

/// SIREN saisi pour le FEC : 9 chiffres, ou vide (le nom du fichier porte alors `000000000`, comme le .NET).
public enum Siren {
    /// Chiffres de toute écriture (clavier arabe : ٠-٩) ramenés à l'ASCII, espaces retirés ; le reste est gardé tel quel pour être refusé.
    public static func normalized(_ raw: String) -> String {
        String(raw.compactMap { character -> Character? in
            if character.isWhitespace { return nil }
            if character.unicodeScalars.count == 1, character.unicodeScalars.first?.properties.numericType == .decimal,
               let digit = character.wholeNumberValue {
                return Character(String(digit))
            }
            return character
        })
    }

    /// Vide après rognage, ou exactement 9 chiffres ASCII (le nom du fichier FEC en dérive : rien d'autre n'y entre).
    public static func isValid(_ siren: String) -> Bool {
        let trimmed = siren.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty || (trimmed.unicodeScalars.count == 9 && trimmed.unicodeScalars.allSatisfy { ("0"..."9").contains($0) })
    }
}
```

`PosAPI.swift` : remplacer

```swift
    func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data)
```

par

```swift
    /// Export FEC ; `company` (facultatif) n'est que journalisé (`FEC_EXPORTED`), comme le paramètre `company` du .NET.
    func exportFec(from: Date, to: Date, siren: String, company: String?) async throws -> (fileName: String, data: Data)
```

et ajouter à la fin du fichier :

```swift
public extension PosAPI {
    /// Export FEC sans société.
    func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data) {
        try await exportFec(from: from, to: to, siren: siren, company: nil)
    }
}
```

`HTTPPosAPI.swift` : remplacer `exportFec` par :

```swift
    public func exportFec(from: Date, to: Date, siren: String, company: String?) async throws -> (fileName: String, data: Data) {
        var query = ["from": Self.iso(from), "to": Self.iso(to), "siren": siren]
        if let company, !company.isEmpty { query["company"] = company }
        let (data, response) = try await send(makeRequest("GET", "fiscal/fec", query: query))
        return (Self.attachmentName(response) ?? "\(siren)FEC.txt", data)
    }

    /// `filename=` de l'en-tête `Content-Disposition` (`Results.File` du .NET), sans guillemets ; `nil` si absent.
    static func attachmentName(_ response: HTTPURLResponse) -> String? {
        guard let disposition = response.value(forHTTPHeaderField: "Content-Disposition"), let range = disposition.range(of: "filename=") else {
            return nil
        }
        return disposition[range.upperBound...].split(separator: ";").first.map(String.init)?
            .trimmingCharacters(in: CharacterSet(charactersIn: "\"' "))
    }
```

`InMemoryPosAPI.swift` : remplacer la signature `public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data) {` par `public func exportFec(from: Date, to: Date, siren: String, company: String?) async throws -> (fileName: String, data: Data) {` (corps inchangé).

`LocalPosAPI+Fec.swift` : remplacer la méthode par :

```swift
    /// Export FEC (`GET /api/fiscal/fec`) : fichier identique au .NET pour les mêmes reçus, entrée JET `FEC_EXPORTED` dans la même
    /// transaction (un export en échec n'écrit rien). Réservé aux responsables. SIREN : 9 chiffres ou vide, contrôle propre à l'iPad (le nom
    /// du fichier en dérive). Charge utile du JET comme l'endpoint : dates telles que reçues (avant inversion, UTC `+00:00`), SIREN tel que
    /// reçu (`""` si vide : le client HTTP envoie toujours `siren=`), `Company` = texte reçu ou `null`.
    public func exportFec(from: Date, to: Date, siren: String, company: String?) async throws -> (fileName: String, data: Data) {
        let manager = try requireManager()
        guard Siren.isValid(siren) else { throw APIError.server(status: 400, message: L10n.string("fiscal.fec_error_invalid_siren")) }
        let fiscal = fiscalRepository, exporter = LocalFecExporter(db: db)
        let now = clock()
        return try db.transaction { () throws -> (fileName: String, data: Data) in
            let result = try exporter.export(from: from, to: to, siren: siren)
            try fiscal.appendJournal(
                eventType: LocalJournalEvents.fecExported,
                payloadJson: FiscalJSON.object([
                    ("StartDateUtc", FiscalJSON.dateTime(from)),
                    ("EndDateUtc", FiscalJSON.dateTime(to)),
                    ("Siren", FiscalJSON.string(siren)),
                    ("Company", company.map(FiscalJSON.string) ?? "null"),
                    ("FileName", FiscalJSON.string(result.fileName)),
                ]),
                terminalId: nil, operatorId: manager.id, now: now
            )
            return (result.fileName, result.data)
        }
    }
```

Textes :
`en.lproj` : `"fiscal.fec_error_invalid_siren" = "Invalid SIREN: 9 digits expected (or leave it empty).";`
`fr.lproj` : `"fiscal.fec_error_invalid_siren" = "SIREN invalide : 9 chiffres attendus (ou laisser vide).";`
`ar.lproj` : `"fiscal.fec_error_invalid_siren" = "رقم SIREN غير صالح: يلزم 9 أرقام (أو اتركه فارغًا).";`

- [ ] **Step 4: Lancer les tests**

Run: `cd ios/Packages/PosKit && swift test`
Expected: PASS (543 tests ; `fecExportUsesContentDispositionFileName` et `realSalesExportBalancedEntriesAndTheExportIsJournaled` inchangés et verts).

- [ ] **Step 5: Vérifier que l'app compile encore**

Run: `cd ios && xcodegen generate && xcodebuild build -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'generic/platform=iOS Simulator' -derivedDataPath build/DerivedData -quiet`
Expected: aucune erreur (l'appel `fiscal.exportFec(from:to:)` de l'écran passe par le store, inchangé).

- [ ] **Step 6: Suivi et commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Core/Siren.swift ios/Packages/PosKit/Sources/PosKit/Networking/PosAPI.swift ios/Packages/PosKit/Sources/PosKit/Networking/HTTPPosAPI.swift ios/Packages/PosKit/Sources/PosKit/Testing/InMemoryPosAPI.swift ios/Packages/PosKit/Sources/PosKit/Local/LocalPosAPI+Fec.swift ios/Packages/PosKit/Sources/PosKit/Resources/en.lproj/Localizable.strings ios/Packages/PosKit/Sources/PosKit/Resources/fr.lproj/Localizable.strings ios/Packages/PosKit/Sources/PosKit/Resources/ar.lproj/Localizable.strings ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFecAPITests.swift ios/Packages/PosKit/Tests/PosKitTests/NetworkingTests.swift docs/plans/ipad-standalone-3d-progress.md
git commit -m "feat(ios): FEC avec SIREN validé et société journalisée ; charge utile FEC_EXPORTED fixée"
```

---

### Task 8: `FiscalStore` : partage d'archive, verdict, plafond, noms de fichiers sûrs, tri

**Files:**
- Create: `ios/Packages/PosKit/Sources/PosKit/Core/SafeFileName.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Networking/PosAPI.swift` (protocole : `archiveFile`)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Networking/HTTPPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Testing/InMemoryPosAPI.swift`
- Modify: `ios/Packages/PosKit/Sources/PosKit/Stores/OperationsStores.swift` (`FiscalStore`, l. 110-347)
- Modify: `ios/Packages/PosKit/Sources/PosKit/Resources/{en,fr,ar}.lproj/Localizable.strings`
- Create: `ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalStoreTests.swift`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/StoreTests.swift`
- Modify: `ios/Packages/PosKit/Tests/PosKitTests/NetworkingTests.swift`

**Interfaces:**
- Consumes: `Siren.normalized` / `Siren.isValid` et `HTTPPosAPI.attachmentName` (Task 7) ; `LocalPosAPI.archiveFile(id:)` (Task 5, déjà conforme à la nouvelle exigence).
- Produces:
  - Protocole : `func archiveFile(id: UUID) async throws -> URL`.
  - `enum SafeFileName { static func sanitized(_ name: String, fallback: String) -> String }` (interne).
  - `FiscalStore` : `public private(set) var exportedArchiveFile: URL?`, `public private(set) var archiveVerification: ArchiveVerificationResult?`, `public var maxArchiveFileBytes: Int` (défaut `64 << 20`), `@discardableResult public func shareArchive(id: UUID) async -> Bool`, `public func verifyArchiveFile(at url: URL) async -> ArchiveVerificationResult?`, `public func exportFec(from: Date, to: Date, company: String? = nil) async` ; `loadPeriodClosures` trie du plus récent au plus ancien (`createdAtUtc`) ; `executePeriodClosure` rogne la clé.
  - Clés PosKit `fiscal.archive_too_large`, `fiscal.archive_unreadable_file`.

- [ ] **Step 1: Écrire les tests qui échouent**

`ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalStoreTests.swift` :

```swift
import Foundation
import Testing
@testable import PosKit

/// `FiscalStore` branché sur la base locale (horloge de test) : ce que voit l'écran Fiscal en mode autonome.
@MainActor
@Suite("FiscalStore sur la base locale")
struct LocalFiscalStoreTests {
    private func makeModel(clock: LocalTestClock) async throws -> (AppModel, URL) {
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("archives-\(UUID().uuidString)", isDirectory: true)
        let api = try LocalPosAPI(path: ":memory:", clock: { clock.now }, calendar: localTestCalendar, archivesDirectory: folder)
        let defaults = UserDefaults(suiteName: "PosKitTests-\(UUID().uuidString)")!
        let model = AppModel(api: api, settings: TerminalSettings(defaults: defaults, credentialStore: InMemoryCredentialStore(.standalone)))
        model.notifier.autoDismissAfter = nil
        for digit in "1234" { await model.session.appendDigit(String(digit)) }
        return (model, folder)
    }

    @Test func aRunningMonthShowsTheLocalizedReason() async throws {
        let (model, _) = try await makeModel(clock: LocalTestClock("2026-09-20T09:00:00Z"))
        #expect(await model.fiscal.executePeriodClosure(type: .monthly, key: "2026-09") == false)
        #expect(model.notifier.lastMessage == L10n.string("fiscal.period_error_not_ended"))
    }

    @Test func closuresAreListedNewestFirstAndKeysAreTrimmed() async throws {
        let clock = LocalTestClock("2026-11-02T09:00:00Z")
        let (model, _) = try await makeModel(clock: clock)
        #expect(await model.fiscal.executePeriodClosure(type: .monthly, key: " 2026-09 "))
        clock.advance(60)
        #expect(await model.fiscal.executePeriodClosure(type: .monthly, key: "2026-10"))
        await model.fiscal.loadPeriodClosures()
        #expect(model.fiscal.periodClosures.map(\.periodKey) == ["2026-10", "2026-09"])
    }

    @Test func closeArchiveShareExportAndVerifyGreen() async throws {
        let (model, folder) = try await makeModel(clock: LocalTestClock("2026-10-02T09:00:00Z"))
        #expect(await model.fiscal.executePeriodClosure(type: .monthly, key: "2026-09"))
        #expect(await model.fiscal.createArchive(periodClosureId: model.fiscal.periodClosures[0].id))
        let archive = try #require(model.fiscal.archives.first)
        #expect(await model.fiscal.shareArchive(id: archive.id))
        let shared = try #require(model.fiscal.exportedArchiveFile)
        #expect(shared.standardizedFileURL == folder.appendingPathComponent(archive.fileName).standardizedFileURL)
        #expect(await model.fiscal.verifyArchiveFile(at: shared)?.isValid == true)
        #expect(model.fiscal.archiveVerification?.isValid == true)

        await model.fiscal.exportFec(from: Date(timeIntervalSince1970: 0), to: Date(timeIntervalSince1970: 1_791_000_000), company: "Chez Marie")
        #expect(model.fiscal.exportedFile?.lastPathComponent.hasPrefix("000000000FEC") == true)
        #expect(await model.fiscal.verify())
        #expect(model.fiscal.verificationResult?.chains.map(\.chain) == ["receipts", "z_closures", "period_closures", "journal", "archives"])
    }

    @Test func anInvalidSirenIsExplainedAndNoFileIsKept() async throws {
        let (model, _) = try await makeModel(clock: LocalTestClock("2026-10-02T09:00:00Z"))
        model.settings.siren = "12345"
        await model.fiscal.exportFec(from: Date(timeIntervalSince1970: 0), to: Date(timeIntervalSince1970: 1_791_000_000))
        #expect(model.fiscal.exportedFile == nil)
        #expect(model.notifier.lastMessage == L10n.string("fiscal.fec_error_invalid_siren"))
    }
}
```

Ajouter à `OperationsTests` dans `StoreTests.swift` (suite « Cuisine, salle et fiscal », qui contient `fiscalArchivesLoadCreateAndVerify`, `@MainActor`) :

```swift
    @Test func theFecFileNeverLeavesTheTemporaryFolder() async throws {
        let (model, _) = await makeModel()
        model.settings.siren = "../../evil"
        await model.fiscal.exportFec(from: Date(timeIntervalSince1970: 0), to: Date())
        let file = try #require(model.fiscal.exportedFile)
        #expect(file.deletingLastPathComponent().standardizedFileURL == FileManager.default.temporaryDirectory.standardizedFileURL)
        #expect(file.lastPathComponent.hasPrefix("evilFEC"))
    }

    @Test func archiveSharingAndVerdictAreKept() async {
        let (model, api) = await makeModel()
        _ = await model.fiscal.executePeriodClosure(type: .monthly, key: "2026-08")
        _ = await model.fiscal.createArchive(periodClosureId: model.fiscal.periodClosures[0].id)
        let archive = model.fiscal.archives[0]
        #expect(await model.fiscal.shareArchive(id: archive.id))
        #expect(model.fiscal.exportedArchiveFile?.lastPathComponent == SafeFileName.sanitized(archive.fileName, fallback: "archive.zip"))
        #expect(await api.calls.contains("archiveFile"))
        _ = await model.fiscal.verifyArchive(data: Data("CORRUPT".utf8), fileName: archive.fileName)
        #expect(model.fiscal.archiveVerification?.reason == "hash_mismatch")
        #expect(await model.fiscal.shareArchive(id: UUID()) == false)
        #expect(model.fiscal.exportedArchiveFile == nil)
    }

    @Test func anArchiveFileAboveTheCeilingIsRefusedWithoutReadingIt() async throws {
        let (model, api) = await makeModel()
        model.fiscal.maxArchiveFileBytes = 4
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("big-\(UUID().uuidString).zip")
        try Data("12345".utf8).write(to: url)
        #expect(await model.fiscal.verifyArchiveFile(at: url) == nil)
        #expect(await api.calls.contains("verifyArchive") == false)
        #expect(model.notifier.lastMessage == L10n.string("fiscal.archive_too_large"))
    }
```

et, à la fin de `StoreTests.swift`, une suite :

```swift
@Suite("Noms de fichiers sûrs")
struct SafeFileNameTests {
    @Test func onlyTheLastSafeComponentIsKept() {
        #expect(SafeFileName.sanitized("../../evilFEC1.txt", fallback: "f") == "evilFEC1.txt")
        #expect(SafeFileName.sanitized("a\\b/archive-T01-M-2026-09.zip", fallback: "f") == "archive-T01-M-2026-09.zip")
        #expect(SafeFileName.sanitized("FEC du 1er été.txt", fallback: "f") == "FEC_du_1er___t_.txt")
        for unsafe in ["", "..", ".", "a/..", "/"] { #expect(SafeFileName.sanitized(unsafe, fallback: "f") == "f", Comment(rawValue: unsafe)) }
    }
}
```

Ajouter à `HTTPPosAPITests` (`NetworkingTests.swift`) :

```swift
    @Test func archiveFileIsDownloadedUnderASafeName() async throws {
        let id = UUID()
        let api = makeAPI { _ in .init(status: 200, body: "PK", headers: ["Content-Disposition": "attachment; filename=../archive-T01-M-2026-09.zip"]) }
        let url = try await api.archiveFile(id: id)
        #expect(last.httpMethod == "GET")
        #expect(last.url?.path == "/api/fiscal/archives/\(id.uuidString.lowercased())/file")
        #expect(url.lastPathComponent == "archive-T01-M-2026-09.zip")
        #expect(url.deletingLastPathComponent().standardizedFileURL == FileManager.default.temporaryDirectory.standardizedFileURL)
        #expect(try Data(contentsOf: url) == Data("PK".utf8))
    }
```

- [ ] **Step 2: Lancer les tests pour les voir échouer**

Run: `cd ios/Packages/PosKit && swift test --filter 'LocalFiscalStoreTests|OperationsTests|SafeFileNameTests|HTTPPosAPITests'`
Expected: FAIL à la compilation (`archiveFile` absent du protocole, `SafeFileName`, `shareArchive`, `verifyArchiveFile`, `maxArchiveFileBytes` inconnus).


- [ ] **Step 3: Écrire l'implémentation**

`ios/Packages/PosKit/Sources/PosKit/Core/SafeFileName.swift` :

```swift
import Foundation

/// Nom de fichier à écrire dans un dossier local : dernier composant seulement, caractères hors `[A-Za-z0-9_.-]` remplacés par `_`,
/// `fallback` si le résultat est vide ou fait de points. Un nom venu du serveur ou dérivé d'une saisie ne sort jamais du dossier.
enum SafeFileName {
    static func sanitized(_ name: String, fallback: String) -> String {
        let last = name.split(whereSeparator: { $0 == "/" || $0 == "\\" }).last.map(String.init) ?? ""
        let safe = last.replacingOccurrences(of: "[^A-Za-z0-9_.-]", with: "_", options: .regularExpression)
        return safe.isEmpty || safe.allSatisfy({ $0 == "." }) ? fallback : safe
    }
}
```

`PosAPI.swift`, après `func verifyArchive(data: Data, fileName: String) async throws -> ArchiveVerificationResult` :

```swift
    /// Fichier ZIP d'une archive, copié dans un dossier local, pour le partage (`GET /api/fiscal/archives/{id}/file`).
    func archiveFile(id: UUID) async throws -> URL
```

`HTTPPosAPI.swift`, après `verifyArchive` :

```swift
    public func archiveFile(id: UUID) async throws -> URL {
        let identifier = id.uuidString.lowercased()
        let (data, response) = try await send(makeRequest("GET", "fiscal/archives/\(identifier)/file"))
        let name = SafeFileName.sanitized(Self.attachmentName(response) ?? "", fallback: "archive-\(identifier).zip")
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(name)
        try data.write(to: url, options: .atomic)
        return url
    }
```

`InMemoryPosAPI.swift`, après `verifyArchive` :

```swift
    public func archiveFile(id: UUID) async throws -> URL {
        try await step("archiveFile"); try requireManager()
        guard let archive = storedArchives.first(where: { $0.id == id }) else { throw APIError.notFound("Fichier d'archive introuvable.") }
        let url = FileManager.default.temporaryDirectory.appendingPathComponent(SafeFileName.sanitized(archive.fileName, fallback: "archive.zip"))
        try Data("ARCHIVE_DATA".utf8).write(to: url, options: .atomic)
        return url
    }
```

`OperationsStores.swift`, dans `FiscalStore` :

1. Après `public private(set) var lastDuplicateNumber: Int?`, ajouter :

```swift
    /// ZIP d'archive prêt à partager (`ShareLink`).
    public private(set) var exportedArchiveFile: URL?
    /// Dernier verdict de vérification d'un fichier d'archive, affiché sous la liste.
    public private(set) var archiveVerification: ArchiveVerificationResult?
    /// Plafond du fichier choisi pour la vérification, lu entièrement en mémoire.
    /// ponytail: une archive mensuelle de restaurant pèse quelques Mo, 64 Mo couvre une annuelle chargée ; au-delà, lire par morceaux.
    public var maxArchiveFileBytes = 64 << 20
```

2. Remplacer `exportFec(from:to:)` par :

```swift
    /// Export FEC : SIREN des réglages ramené aux chiffres ASCII, société facultative (journalisée seulement), fichier écrit sous un nom sûr
    /// dans le dossier temporaire.
    public func exportFec(from: Date, to: Date, company: String? = nil) async {
        isWorking = true
        exportedFile = nil
        fecStatus = L10n.string("fiscal.fec_generating")
        defer { isWorking = false }
        do {
            let trimmedCompany = company?.trimmingCharacters(in: .whitespacesAndNewlines)
            let (name, data) = try await api.exportFec(
                from: from, to: to, siren: Siren.normalized(settings.siren), company: trimmedCompany?.isEmpty == false ? trimmedCompany : nil
            )
            let url = FileManager.default.temporaryDirectory.appendingPathComponent(SafeFileName.sanitized(name, fallback: "FEC.txt"))
            try data.write(to: url, options: .atomic)
            exportedFile = url
            fecStatus = String(format: L10n.string("fiscal.fec_generated"), url.lastPathComponent)
            notifier.success(String(format: L10n.string("fiscal.fec_ready"), url.lastPathComponent))
        } catch APIError.forbidden, APIError.unauthorized {
            fecStatus = L10n.string("fiscal.fec_forbidden_status")
            notifier.error(L10n.string("fiscal.fec_forbidden"))
        } catch {
            fecStatus = L10n.string("fiscal.fec_error")
            notifier.error(error)
        }
    }
```

3. Dans `loadPeriodClosures`, remplacer `periodClosures = try await api.periodClosures(terminalId: settings.terminalId, periodType: type)` par :

```swift
            // Plus récente d'abord, quel que soit l'ordre du serveur ou de la base locale (l'écran n'affiche que les 5 premières).
            periodClosures = try await api.periodClosures(terminalId: settings.terminalId, periodType: type).sorted { $0.createdAtUtc > $1.createdAtUtc }
```

4. Dans `executePeriodClosure`, remplacer `let closure = try await api.executePeriodClosure(terminalId: settings.terminalId, periodType: type, periodKey: key)` par :

```swift
            let closure = try await api.executePeriodClosure(
                terminalId: settings.terminalId, periodType: type, periodKey: key.trimmingCharacters(in: .whitespacesAndNewlines)
            )
```

5. Dans `verifyArchive(data:fileName:)`, juste après `let res = try await api.verifyArchive(data: data, fileName: fileName)`, ajouter `archiveVerification = res`.

6. Ajouter à la fin de la classe :

```swift
    /// Prépare le ZIP d'une archive pour le partage.
    @discardableResult
    public func shareArchive(id: UUID) async -> Bool {
        do {
            exportedArchiveFile = try await api.archiveFile(id: id)
            return true
        } catch {
            exportedArchiveFile = nil
            notifier.error(error)
            return false
        }
    }

    /// Vérifie un fichier choisi (`.fileImporter`) : taille contrôlée avant lecture, l'accès au fichier reste à la charge de l'appelant.
    public func verifyArchiveFile(at url: URL) async -> ArchiveVerificationResult? {
        let size = (try? url.resourceValues(forKeys: [.fileSizeKey]).fileSize) ?? 0
        guard size <= maxArchiveFileBytes else {
            notifier.error(L10n.string("fiscal.archive_too_large"))
            return nil
        }
        guard let data = try? Data(contentsOf: url) else {
            notifier.error(L10n.string("fiscal.archive_unreadable_file"))
            return nil
        }
        return await verifyArchive(data: data, fileName: url.lastPathComponent)
    }
```

Textes :
`en.lproj` :
```
"fiscal.archive_too_large" = "File too large to verify on this device (over 64 MB).";
"fiscal.archive_unreadable_file" = "Unable to read the selected file.";
```
`fr.lproj` :
```
"fiscal.archive_too_large" = "Fichier trop volumineux pour être vérifié sur cet appareil (plus de 64 Mo).";
"fiscal.archive_unreadable_file" = "Impossible de lire le fichier choisi.";
```
`ar.lproj` :
```
"fiscal.archive_too_large" = "الملف كبير جدًا للتحقق منه على هذا الجهاز (أكثر من 64 ميغابايت).";
"fiscal.archive_unreadable_file" = "تعذّرت قراءة الملف المحدد.";
```

- [ ] **Step 4: Lancer les tests**

Run: `cd ios/Packages/PosKit && swift test`
Expected: PASS (552 tests).

- [ ] **Step 5: Vérifier que l'app compile encore**

Run: `cd ios && xcodebuild build -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'generic/platform=iOS Simulator' -derivedDataPath build/DerivedData -quiet`
Expected: aucune erreur.

- [ ] **Step 6: Suivi et commit**

```bash
git add ios/Packages/PosKit/Sources/PosKit/Core/SafeFileName.swift ios/Packages/PosKit/Sources/PosKit/Networking/PosAPI.swift ios/Packages/PosKit/Sources/PosKit/Networking/HTTPPosAPI.swift ios/Packages/PosKit/Sources/PosKit/Testing/InMemoryPosAPI.swift ios/Packages/PosKit/Sources/PosKit/Stores/OperationsStores.swift ios/Packages/PosKit/Sources/PosKit/Resources/en.lproj/Localizable.strings ios/Packages/PosKit/Sources/PosKit/Resources/fr.lproj/Localizable.strings ios/Packages/PosKit/Sources/PosKit/Resources/ar.lproj/Localizable.strings ios/Packages/PosKit/Tests/PosKitTests/Local/LocalFiscalStoreTests.swift ios/Packages/PosKit/Tests/PosKitTests/StoreTests.swift ios/Packages/PosKit/Tests/PosKitTests/NetworkingTests.swift docs/plans/ipad-standalone-3d-progress.md
git commit -m "feat(ios): partage du ZIP d'archive, verdict conservé, plafond de taille et noms de fichiers sûrs"
```

---

### Task 9: Écran Fiscal autonome : cartes, textes FR/EN/AR, XCUITest

**Files:**
- Create: `ios/RestaurantPOS/Features/Fiscal/FiscalArchiveCards.swift`
- Modify: `ios/RestaurantPOS/Features/Fiscal/FiscalScreen.swift` (l. 1-343 : struct `FiscalScreen` ; `FiscalSlip` inchangé)
- Modify: `ios/RestaurantPOS/Resources/Localizable.xcstrings`
- Modify: `ios/RestaurantPOSUITests/StandaloneUITests.swift`

**Interfaces:**
- Consumes: `FiscalStore` (Task 8 : `exportedArchiveFile`, `archiveVerification`, `shareArchive(id:)`, `verifyArchiveFile(at:)`, `exportFec(from:to:company:)`), `Siren` (Task 7), `Card`, `ActionButton`, `Theme` (design system de l'app).
- Produces: vues `ChainVerificationCard`, `PeriodClosuresCard`, `ArchivesCard`, `FecExportCard` ; identifiants d'accessibilité `fiscal.verificationStatus`, `fiscal.chain.<chaîne>`, `fiscal.archived_<clé>`, `fiscal.shareArchive_<clé>`, `fiscal.archiveShare`, `fiscal.archiveVerification`, `fec.company`, `fec.sirenHint` (en plus des identifiants existants, conservés : `fiscal.verifyChains`, `fiscal.periodKey`, `fiscal.executePeriodClosure`, `fiscal.archivePeriodClosure_<clé>`, `fiscal.verifyArchiveButton`, `fec.siren`, `fec.generate`, `fec.status`, `fec.share`).

- [ ] **Step 1: Écrire le test UI qui échoue**

Ajouter à `StandaloneUITests` :

```swift
    /// Mois précédent dans le fuseau du simulateur, comme la clé proposée par l'écran Fiscal.
    /// ponytail: lancé à minuit pile un 1er du mois, l'app et le test pourraient voir deux mois différents ; relancer suffit.
    private func previousMonthKey() -> String {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .current
        let previous = calendar.date(byAdding: .month, value: -1, to: Date())!
        return String(format: "%04d-%02d", calendar.component(.year, from: previous), calendar.component(.month, from: previous))
    }

    /// Mode autonome (base de démonstration, horloge réelle) : le mois précédent n'a ni reçu ni Z, sa clôture est donc permise.
    func testStandaloneClosureArchiveFecAndGreenVerification() {
        launchStandalone()
        tap("nav.fiscal")
        let key = previousMonthKey()

        tap("fiscal.executePeriodClosure")
        waitToast(containing: "Clôture de période scellée")
        tap("fiscal.executePeriodClosure")
        waitToast(containing: "déjà clôturée") // refus métier lisible

        tap("fiscal.archivePeriodClosure_\(key)")
        waitToast(containing: "Archive générée")
        assertExists("fiscal.archived_\(key)")
        tap("fiscal.shareArchive_\(key)")
        assertExists("fiscal.archiveShare")

        tap("fec.generate")
        waitLabel("fec.status", contains: "généré")
        assertExists("fec.share")

        tap("fiscal.verifyChains")
        waitLabel("fiscal.verificationStatus", contains: "intègres")
        assertExists("fiscal.chain.period_closures")
        assertExists("fiscal.chain.archives")
        screenshot("Fiscal autonome")
    }
```

- [ ] **Step 2: Lancer le test pour le voir échouer**

Run: `cd ios && xcodegen generate && xcodebuild test -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'platform=iOS Simulator,name=iPad Pro 13-inch (M5)' -derivedDataPath build/DerivedData -only-testing:RestaurantPOSUITests/StandaloneUITests/testStandaloneClosureArchiveFecAndGreenVerification | grep -E "Test Case|error:|\*\* TEST"`
Expected: FAIL (`« fiscal.archived_<clé> » devrait être visible` : l'identifiant n'existe pas encore).

- [ ] **Step 3: Ajouter les textes de l'app**

Depuis la racine du dépôt :

```bash
python3 -I - <<'PY'
import json, collections
path = "ios/RestaurantPOS/Resources/Localizable.xcstrings"
with open(path, encoding="utf-8") as f:
    catalog = json.load(f, object_pairs_hook=collections.OrderedDict)
new = {
    "fiscal.chains_valid": ("All fiscal chains are intact", "Toutes les chaînes fiscales sont intègres", "جميع السلاسل المالية سليمة"),
    "fiscal.chains_broken": ("Integrity breach detected", "Rupture d'intégrité détectée", "تم اكتشاف خلل في السلامة"),
    "fiscal.chain_receipts": ("Receipts", "Reçus", "الإيصالات"),
    "fiscal.chain_z_closures": ("Daily closures (Z)", "Clôtures journalières (Z)", "الإقفالات اليومية (Z)"),
    "fiscal.chain_period_closures": ("Period closures", "Clôtures de période", "إقفالات الفترات"),
    "fiscal.chain_journal": ("Technical event log (JET)", "Journal des événements (JET)", "سجل الأحداث التقنية (JET)"),
    "fiscal.chain_archives": ("Archives", "Archives", "الأرشيفات"),
    "fiscal.break_sequence_gap": ("Missing sequence number", "Numéro de séquence manquant", "رقم تسلسلي مفقود"),
    "fiscal.break_previous_hash_mismatch": ("Link to the previous entry broken", "Lien avec l'élément précédent rompu", "انقطاع الارتباط بالعنصر السابق"),
    "fiscal.break_signature_mismatch": ("Signature does not match the data", "Signature non conforme aux données", "التوقيع لا يطابق البيانات"),
    "fiscal.verify_archive_button": ("Verify a file", "Vérifier un fichier", "التحقق من ملف"),
    "fiscal.archive_share": ("Share", "Partager", "مشاركة"),
    "fiscal.archive_result_valid": ("Valid, intact archive", "Archive valide et intègre", "أرشيف صالح وسليم"),
    "fiscal.archive_result_hash_mismatch": ("Altered file (SHA-256 differs)", "Fichier altéré (SHA-256 différent)", "ملف معدَّل (بصمة SHA-256 مختلفة)"),
    "fiscal.archive_result_chain_break": ("Archive chain broken", "Chaîne des archives rompue", "سلسلة الأرشيفات مقطوعة"),
    "fiscal.archive_result_unknown": ("Unknown archive", "Archive inconnue", "أرشيف غير معروف"),
    "fiscal.fec_company": ("Company", "Société", "الشركة"),
    "fiscal.siren_invalid_hint": ("9 digits, or leave empty", "9 chiffres, ou laisser vide", "9 أرقام، أو اتركه فارغًا"),
}
for key, (en, fr, ar) in new.items():
    assert key not in catalog["strings"], key
    catalog["strings"][key] = {
        "extractionState": "manual",
        "localizations": {lang: {"stringUnit": {"state": "translated", "value": value}} for lang, value in (("ar", ar), ("en", en), ("fr", fr))},
    }
with open(path, "w", encoding="utf-8") as f:
    f.write(json.dumps(catalog, ensure_ascii=False, indent=2, separators=(",", ": ")) + "\n")
PY
git diff --stat ios/RestaurantPOS/Resources/Localizable.xcstrings
```

Expected: seules des lignes ajoutées en fin de dictionnaire (`git diff` ne montre aucune ligne supprimée hors la virgule de la dernière entrée).

- [ ] **Step 4: Écrire les cartes**

`ios/RestaurantPOS/Features/Fiscal/FiscalArchiveCards.swift` :

```swift
import SwiftUI
import UniformTypeIdentifiers
import PosKit

// MARK: - Vérification des chaînes

/// Vérification des chaînes fiscales : verdict global, une ligne par chaîne et la rupture détectée.
struct ChainVerificationCard: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let fiscal = model.fiscal
        Card {
            VStack(alignment: .leading, spacing: 12) {
                Label("fiscal.verification_title", systemImage: "checkmark.shield").font(.headline)
                Text("fiscal.verification_subtitle").font(.subheadline).foregroundStyle(Theme.inkMuted)
                ActionButton(title: String(localized: "fiscal.verify_chains_button"), systemImage: "checkmark.seal", isLoading: fiscal.isWorking, kind: .primary) {
                    Task { await fiscal.verify() }
                }
                .accessibilityIdentifier("fiscal.verifyChains")
                if let result = fiscal.verificationResult {
                    VerificationResultView(result: result)
                }
            }
        }
    }
}

private struct VerificationResultView: View {
    let result: FiscalVerificationResult

    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            HStack {
                Image(systemName: result.isValid ? "checkmark.circle.fill" : "exclamationmark.triangle.fill")
                Text(result.isValid ? LocalizedStringKey("fiscal.chains_valid") : LocalizedStringKey("fiscal.chains_broken"))
                    .font(.subheadline.bold())
                    .accessibilityIdentifier("fiscal.verificationStatus")
            }
            .foregroundStyle(result.isValid ? Color.green : Color.red)
            ForEach(result.chains, id: \.self) { chain in
                ChainRow(chain: chain)
            }
        }
        .padding(8)
        .background(Color.secondary.opacity(0.1))
        .clipShape(RoundedRectangle(cornerRadius: 8))
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("fiscal.verificationResult")
    }
}

private struct ChainRow: View {
    let chain: FiscalChainStatus

    var body: some View {
        VStack(alignment: .leading, spacing: 2) {
            HStack {
                Text(Self.title(chain.chain)).font(.caption.bold())
                if let terminal = chain.terminalId {
                    Text(verbatim: "(\(terminal))").font(.caption2).foregroundStyle(Theme.inkMuted)
                }
                Spacer()
                Text(verbatim: "\(chain.checkedCount)").font(.caption)
                Image(systemName: chain.isValid ? "checkmark.circle" : "xmark.circle")
                    .foregroundStyle(chain.isValid ? Color.green : Color.red)
            }
            if let broken = chain.break {
                HStack(spacing: 4) {
                    Text(verbatim: "\(broken.reference) :")
                    Text(Self.kind(broken.kind))
                }
                .font(.caption2)
                .foregroundStyle(Color.red)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("fiscal.chain.\(chain.chain)")
    }

    static func title(_ code: String) -> LocalizedStringKey {
        switch code {
        case "receipts": "fiscal.chain_receipts"
        case "z_closures": "fiscal.chain_z_closures"
        case "period_closures": "fiscal.chain_period_closures"
        case "journal": "fiscal.chain_journal"
        case "archives": "fiscal.chain_archives"
        default: LocalizedStringKey(code)
        }
    }

    static func kind(_ code: String) -> LocalizedStringKey {
        switch code {
        case "sequence_gap": "fiscal.break_sequence_gap"
        case "previous_hash_mismatch": "fiscal.break_previous_hash_mismatch"
        case "signature_mismatch": "fiscal.break_signature_mismatch"
        default: LocalizedStringKey(code)
        }
    }
}

// MARK: - Clôtures de période

/// Clôtures mensuelles et annuelles : sélection du type et de la clé, scellement, historique avec archivage.
struct PeriodClosuresCard: View {
    @Environment(AppModel.self) private var model
    @State private var periodType: FiscalPeriodType = .monthly
    @State private var periodKey = ""

    var body: some View {
        let fiscal = model.fiscal
        Card {
            VStack(alignment: .leading, spacing: 12) {
                Label("fiscal.period_closures_title", systemImage: "calendar.badge.clock").font(.headline)
                Text("fiscal.period_closures_subtitle").font(.subheadline).foregroundStyle(Theme.inkMuted)
                Picker("fiscal.period_type", selection: $periodType) {
                    Text("fiscal.period_monthly").tag(FiscalPeriodType.monthly)
                    Text("fiscal.period_annual").tag(FiscalPeriodType.annual)
                }
                .pickerStyle(.segmented)
                .onChange(of: periodType) { _, newType in periodKey = Self.defaultKey(newType, now: Date()) }
                HStack {
                    Text("fiscal.period_key").font(.caption).foregroundStyle(Theme.inkMuted)
                    Spacer()
                    TextField("YYYY-MM", text: $periodKey)
                        .textFieldStyle(.roundedBorder)
                        .frame(width: 120)
                        .accessibilityIdentifier("fiscal.periodKey")
                }
                ActionButton(title: String(localized: "fiscal.execute_period_closure"), systemImage: "lock.badge.clock", isLoading: fiscal.isWorking, kind: .primary) {
                    Task {
                        if await fiscal.executePeriodClosure(type: periodType, key: periodKey) {
                            periodKey = Self.defaultKey(periodType, now: Date())
                        }
                    }
                }
                .accessibilityIdentifier("fiscal.executePeriodClosure")
                if !fiscal.periodClosures.isEmpty {
                    PeriodClosureList()
                }
            }
        }
        .task {
            if periodKey.isEmpty { periodKey = Self.defaultKey(periodType, now: Date()) }
        }
    }

    /// Mois ou exercice précédent (calendrier grégorien du fuseau de l'appareil, comme la clôture) : la période qu'on clôture d'habitude.
    static func defaultKey(_ type: FiscalPeriodType, now: Date) -> String {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .current
        if type == .annual { return String(calendar.component(.year, from: now) - 1) }
        let previous = calendar.date(byAdding: .month, value: -1, to: now) ?? now
        return String(format: "%04d-%02d", calendar.component(.year, from: previous), calendar.component(.month, from: previous))
    }
}

private struct PeriodClosureList: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let fiscal = model.fiscal
        VStack(alignment: .leading, spacing: 6) {
            Text("fiscal.period_history_title").font(.caption.bold()).foregroundStyle(Theme.inkMuted)
            ForEach(fiscal.periodClosures.prefix(5)) { closure in
                HStack {
                    Text(closure.periodType == .monthly ? LocalizedStringKey("fiscal.period_monthly") : LocalizedStringKey("fiscal.period_annual"))
                        .font(.caption2.bold())
                        .padding(.horizontal, 4)
                        .padding(.vertical, 2)
                        .background(Color.blue.opacity(0.15))
                        .foregroundStyle(Color.blue)
                        .clipShape(RoundedRectangle(cornerRadius: 4))
                    Text(verbatim: closure.periodKey).font(.caption.monospaced())
                    Spacer()
                    Text(verbatim: closure.totalTtc.formatted).font(.caption.bold())
                    if fiscal.archives.contains(where: { $0.periodClosureId == closure.id }) {
                        Label("fiscal.archived", systemImage: "checkmark.circle.fill")
                            .font(.caption2.bold())
                            .foregroundStyle(Color.green)
                            .accessibilityIdentifier("fiscal.archived_\(closure.periodKey)")
                    } else {
                        Button {
                            Task { await fiscal.createArchive(periodClosureId: closure.id) }
                        } label: {
                            Text("fiscal.action_archive")
                                .font(.caption2.bold())
                                .padding(.horizontal, 6)
                                .padding(.vertical, 3)
                                .background(Color.secondary.opacity(0.15))
                                .clipShape(RoundedRectangle(cornerRadius: 4))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("fiscal.archivePeriodClosure_\(closure.periodKey)")
                    }
                }
            }
        }
        .padding(8)
        .background(Color.secondary.opacity(0.08))
        .clipShape(RoundedRectangle(cornerRadius: 8))
    }
}

// MARK: - Archives

/// Archives fiscales : liste, partage du ZIP, vérification d'un fichier choisi et son verdict.
struct ArchivesCard: View {
    @Environment(AppModel.self) private var model
    @State private var showsImporter = false

    var body: some View {
        let fiscal = model.fiscal
        Card {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Label("fiscal.archives_title", systemImage: "archivebox").font(.headline)
                    Spacer()
                    Button { showsImporter = true } label: {
                        Label("fiscal.verify_archive_button", systemImage: "checkmark.shield").font(.caption.bold())
                    }
                    .accessibilityIdentifier("fiscal.verifyArchiveButton")
                }
                Text("fiscal.archives_subtitle").font(.subheadline).foregroundStyle(Theme.inkMuted)
                if let result = fiscal.archiveVerification {
                    ArchiveVerdictRow(result: result)
                }
                if fiscal.archives.isEmpty {
                    Text("common.no_data").font(.caption).foregroundStyle(Theme.inkMuted)
                } else {
                    ArchiveList(archives: Array(fiscal.archives.prefix(5)))
                }
                if let file = fiscal.exportedArchiveFile {
                    ShareLink(item: file) { Label("fiscal.share_file \(file.lastPathComponent)", systemImage: "square.and.arrow.up") }
                        .accessibilityIdentifier("fiscal.archiveShare")
                }
            }
        }
        .fileImporter(isPresented: $showsImporter, allowedContentTypes: [.zip]) { result in
            guard case .success(let url) = result else { return }
            Task {
                let scoped = url.startAccessingSecurityScopedResource()
                defer { if scoped { url.stopAccessingSecurityScopedResource() } }
                _ = await fiscal.verifyArchiveFile(at: url)
            }
        }
    }
}

private struct ArchiveList: View {
    @Environment(AppModel.self) private var model
    let archives: [FiscalArchive]

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            ForEach(archives) { archive in
                HStack {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(verbatim: archive.fileName).font(.caption.monospaced().bold())
                        Text(verbatim: "\(archive.periodKey) • \(ByteCountFormatter.string(fromByteCount: Int64(archive.fileSizeBytes), countStyle: .file))")
                            .font(.caption2)
                            .foregroundStyle(Theme.inkMuted)
                    }
                    Spacer()
                    Text(verbatim: "#\(archive.archiveSequence)").font(.caption2.monospaced()).foregroundStyle(Theme.inkMuted)
                    Button {
                        Task { await model.fiscal.shareArchive(id: archive.id) }
                    } label: {
                        Label("fiscal.archive_share", systemImage: "square.and.arrow.up").labelStyle(.iconOnly)
                    }
                    .accessibilityIdentifier("fiscal.shareArchive_\(archive.periodKey)")
                }
                .padding(.vertical, 2)
            }
        }
        .padding(8)
        .background(Color.secondary.opacity(0.08))
        .clipShape(RoundedRectangle(cornerRadius: 8))
    }
}

private struct ArchiveVerdictRow: View {
    let result: ArchiveVerificationResult

    var body: some View {
        Label(Self.text(result), systemImage: result.isValid ? "checkmark.seal.fill" : "exclamationmark.triangle.fill")
            .font(.caption.bold())
            .foregroundStyle(result.isValid ? Color.green : Color.red)
            .accessibilityIdentifier("fiscal.archiveVerification")
    }

    static func text(_ result: ArchiveVerificationResult) -> LocalizedStringKey {
        if result.isValid { return "fiscal.archive_result_valid" }
        switch result.reason {
        case "hash_mismatch": return "fiscal.archive_result_hash_mismatch"
        case "chain_break": return "fiscal.archive_result_chain_break"
        default: return "fiscal.archive_result_unknown"
        }
    }
}

// MARK: - FEC

/// Export FEC : dates, SIREN (9 chiffres ou vide), société (journalisée), partage du fichier.
struct FecExportCard: View {
    @Environment(AppModel.self) private var model
    @State private var from = Calendar.current.date(from: Calendar.current.dateComponents([.year, .month], from: Date())) ?? Date()
    @State private var to = Date()
    @State private var company = ""

    var body: some View {
        let fiscal = model.fiscal
        let sirenIsValid = Siren.isValid(Siren.normalized(model.settings.siren))
        Card {
            VStack(alignment: .leading, spacing: 12) {
                Label("fiscal.fec_export_title", systemImage: "square.and.arrow.up").font(.headline)
                DatePicker("fiscal.fec_from", selection: $from, displayedComponents: .date)
                DatePicker("fiscal.fec_to", selection: $to, in: from..., displayedComponents: .date)
                HStack {
                    Text("fiscal.siren_label")
                    TextField("123456789", text: Binding(get: { model.settings.siren }, set: { model.settings.siren = $0 }))
                        .keyboardType(.numberPad)
                        .textFieldStyle(.roundedBorder)
                        .accessibilityIdentifier("fec.siren")
                }
                if !sirenIsValid {
                    Text("fiscal.siren_invalid_hint").font(.caption).foregroundStyle(Color.red).accessibilityIdentifier("fec.sirenHint")
                }
                HStack {
                    Text("fiscal.fec_company")
                    TextField("", text: $company).textFieldStyle(.roundedBorder).accessibilityIdentifier("fec.company")
                }
                ActionButton(title: String(localized: "fiscal.generate_fec_button"), systemImage: "doc.badge.gearshape", isLoading: fiscal.isWorking, kind: .tonal) {
                    let end = Calendar.current.date(bySettingHour: 23, minute: 59, second: 59, of: to) ?? to
                    Task { await fiscal.exportFec(from: Calendar.current.startOfDay(for: from), to: end, company: company) }
                }
                .disabled(!sirenIsValid)
                .accessibilityIdentifier("fec.generate")
                if let status = fiscal.fecStatus {
                    Text(status).font(.footnote).foregroundStyle(Theme.inkMuted).accessibilityIdentifier("fec.status")
                }
                if let file = fiscal.exportedFile {
                    ShareLink(item: file) { Label("fiscal.share_file \(file.lastPathComponent)", systemImage: "square.and.arrow.up") }
                        .accessibilityIdentifier("fec.share")
                }
            }
        }
        .task(id: model.settingsStore.settings?.companyName) {
            if company.isEmpty { company = model.settingsStore.settings?.companyName ?? "" }
        }
    }
}
```

- [ ] **Step 5: Réduire `FiscalScreen` aux rapports et à l'assemblage**

Dans `FiscalScreen.swift`, remplacer tout le bloc de `import SwiftUI` jusqu'à l'accolade fermante de `struct FiscalScreen` (avant `/// Ticket de rapport au format « imprimante thermique ».`) par :

```swift
import SwiftUI
import PosKit

/// Section Fiscal (NF525) : rapports X et Z, vérification des chaînes, clôtures de période, archives, duplicata, export FEC.
/// Les cartes autres que les rapports et la réimpression sont dans `FiscalArchiveCards.swift`.
struct FiscalScreen: View {
    @Environment(AppModel.self) private var model
    @State private var confirmsZ = false
    @State private var receiptIdentifier = ""

    var body: some View {
        let fiscal = model.fiscal
        HStack(alignment: .top, spacing: 24) {
            ScrollView {
                if let report = fiscal.report {
                    FiscalSlip(report: report, isSealed: fiscal.isSealed, duplicateNumber: fiscal.lastDuplicateNumber)
                        .padding(.vertical, 24)
                } else {
                    ProgressView().padding(60)
                }
            }
            .frame(maxWidth: .infinity)

            ScrollView(showsIndicators: false) {
                VStack(alignment: .leading, spacing: 16) {
                    reportsCard
                    ChainVerificationCard()
                    PeriodClosuresCard()
                    ArchivesCard()
                    reprintCard
                    FecExportCard()
                    Spacer()
                }
            }
            .frame(width: 360)
        }
        .padding(24)
        .task {
            await fiscal.loadLatest()
            await fiscal.loadPeriodClosures()
            await fiscal.loadArchives()
            await model.settingsStore.load()
        }
        .confirmationDialog("fiscal.confirm_z_dialog_title", isPresented: $confirmsZ, titleVisibility: .visible) {
            Button("fiscal.confirm_z_button", role: .destructive) { Task { await fiscal.executeZ() } }
                .accessibilityIdentifier("fiscal.confirmZ")
        } message: {
            Text("fiscal.confirm_z_message")
        }
    }

    @ViewBuilder private var reportsCard: some View {
        let fiscal = model.fiscal
        Card {
            VStack(alignment: .leading, spacing: 12) {
                HStack {
                    Label("fiscal.reports_title", systemImage: "doc.text").font(.headline)
                    Spacer()
                    if let cert = model.settingsStore.settings?.certificateNumber, !cert.isEmpty {
                        Label(String(localized: "fiscal.cert_badge \(cert)"), systemImage: "checkmark.seal.fill")
                            .font(.caption.bold())
                            .foregroundStyle(Color.green)
                            .accessibilityIdentifier("fiscal.certificateBadge")
                    } else {
                        Label("fiscal.cert_pending", systemImage: "hourglass")
                            .font(.caption.bold())
                            .foregroundStyle(Color.orange)
                            .accessibilityIdentifier("fiscal.certificateBadge")
                    }
                }
                Text("fiscal.x_z_explanation")
                    .font(.subheadline).foregroundStyle(Theme.inkMuted)
                ActionButton(title: String(localized: "fiscal.preview_x_button"), systemImage: "eye", kind: .tonal) {
                    Task { await fiscal.previewX() }
                }
                .accessibilityIdentifier("fiscal.previewX")
                if fiscal.report != nil {
                    if fiscal.isSealed {
                        ActionButton(title: String(localized: "fiscal.reprint_z"), systemImage: "printer", kind: .tonal) {
                            Task { await fiscal.reprintZ() }
                        }
                        .accessibilityIdentifier("fiscal.reprintZ")
                    } else {
                        ActionButton(title: String(localized: "fiscal.print_x_report"), systemImage: "printer", kind: .tonal) {
                            Task { await fiscal.printX() }
                        }
                        .accessibilityIdentifier("fiscal.printX")
                    }
                }
                ActionButton(title: String(localized: "fiscal.execute_z_button"), systemImage: "lock.doc", isLoading: fiscal.isWorking, kind: .danger) {
                    confirmsZ = true
                }
                .accessibilityIdentifier("fiscal.executeZ")
            }
        }
    }

    @ViewBuilder private var reprintCard: some View {
        let fiscal = model.fiscal
        Card {
            VStack(alignment: .leading, spacing: 12) {
                Label("fiscal.reprint_receipt_title", systemImage: "doc.text.magnifyingglass").font(.headline)
                Text("fiscal.reprint_receipt_subtitle")
                    .font(.subheadline).foregroundStyle(Theme.inkMuted)
                HStack {
                    TextField("fiscal.reprint_receipt_placeholder", text: $receiptIdentifier)
                        .textFieldStyle(.roundedBorder)
                        .accessibilityIdentifier("fiscal.reprintReceiptInput")
                    ActionButton(title: String(localized: "fiscal.reprint_receipt_btn"), systemImage: "printer", isLoading: fiscal.isWorking, kind: .primary) {
                        Task {
                            let trimmed = receiptIdentifier.trimmingCharacters(in: .whitespacesAndNewlines)
                            if !trimmed.isEmpty {
                                await fiscal.reprintReceipt(receiptIdentifier: trimmed)
                            }
                        }
                    }
                    .accessibilityIdentifier("fiscal.reprintReceiptBtn")
                }
            }
        }
    }
}
```

- [ ] **Step 6: Compiler, lancer le test UI**

Run: `cd ios && xcodegen generate && xcodebuild test -project RestaurantPOS.xcodeproj -scheme RestaurantPOS -destination 'platform=iOS Simulator,name=iPad Pro 13-inch (M5)' -derivedDataPath build/DerivedData -only-testing:RestaurantPOSUITests/StandaloneUITests/testStandaloneClosureArchiveFecAndGreenVerification -only-testing:RestaurantPOSUITests/OperationsUITests/testFecExport -only-testing:RestaurantPOSUITests/OperationsUITests/testFiscalXAndZReports | grep -E "Test Case|error:|\*\* TEST"`
Expected: `** TEST SUCCEEDED **` (le nouveau test et les deux tests fiscaux du mode serveur).
Run: `cd ios/Packages/PosKit && swift test --filter LocalizationTests`
Expected: PASS (`everyAppCatalogKeyIsTranslated` couvre les 18 nouvelles clés en `en`, `fr`, `ar`).

- [ ] **Step 7: Suivi et commit**

Relever dans le suivi le nombre de tests UI lancés (3) et leur résultat.

```bash
git add ios/RestaurantPOS/Features/Fiscal/FiscalArchiveCards.swift ios/RestaurantPOS/Features/Fiscal/FiscalScreen.swift ios/RestaurantPOS/Resources/Localizable.xcstrings ios/RestaurantPOSUITests/StandaloneUITests.swift docs/plans/ipad-standalone-3d-progress.md
git commit -m "feat(ios): section Fiscal autonome (chaînes lisibles, partage d'archive, verdict, société, SIREN) et test UI"
```

---

### Task 10: Documentation et vérification finale

**Files:**
- Modify: `CLAUDE.md` (paragraphe « Parité fiscale .NET/Swift »)
- Modify: `ios/README.md` (l. 107 et 109)
- Modify: `docs/fiscal.md` (§ 5.1)
- Modify: `docs/plans/ipad-standalone-3d-progress.md`, `docs/plans/ipad-standalone-3-progress.md`

**Interfaces:**
- Consumes: tout le lot.
- Produces: documentation à jour ; aucun code.

- [ ] **Step 1: `CLAUDE.md`**

Remplacer :

```
FEC fait (3c) ; vérification des chaînes et interface restent à venir (3d).
```

par :

```
FEC fait (3c). Lot 3d fait : `verifyAllChains` couvre aussi `period_closures` (par poste, chaque type chaîné depuis la genèse) et `archives` (globale), avec les codes et références de `FiscalChainVerificationService` (`M-<clé>`, `A-<clé>`, `ARC-<n>`), et une rupture est journalisée `CHAIN_BREAK_DETECTED` comme le .NET ; `LocalBackupService.restore` refuse aussi une sauvegarde dont ces chaînes sont rompues. La section Fiscal du mode autonome (`FiscalScreen` et `FiscalArchiveCards.swift`) clôture, archive, partage le ZIP (`archiveFile(id:)`, désormais dans `PosAPI`), vérifie un fichier et exporte le FEC ; les refus de clôture et d'archive sont localisés FR/EN/AR (le .NET renvoie un code et un message anglais). Le SIREN du FEC est contrôlé sur l'iPad seulement (9 chiffres ou vide) ; le JET `FEC_EXPORTED` garde le SIREN reçu (`""` si vide) et `Company` (`null` si absente). Sous-projet 3 terminé. Règle de suivi : toute modification de `ExecutePeriodClosureAsync`, `FiscalArchiveService`, `FecExportService`, `FiscalChainVerificationService` ou de `FiscalHashing` impose de régénérer les fixtures concernées (`period_closure_reference.json`, `archive_reference.json`, `fec_reference.json`) et de reporter le changement dans `Local/`. Points à faire valider par l'expert-comptable : dates d'écriture du FEC au jour UTC (une vente à 00 h 30 heure de Paris est datée de la veille) ; compte 580000 avec deux libellés (titres-restaurant et moyen inconnu) ; facturation chambre (411000) présente seulement dans le FEC iPad ; compte 468600 des pourboires.
```

Remplacer :

```
Limite connue : après restauration d'une sauvegarde `.posbak` (qui n'inclut pas les fichiers d'archives) ou si le déplacement du fichier échoue après le commit, le fichier d'archive est irrécupérable sur l'iPad à ce jour (`createArchive` renvoie 409 une fois la ligne écrite ; suivi au lot 3d : reconstruction du ZIP depuis la ligne, avec contrôle du SHA).
```

par :

```
Fichier d'archive absent (restauration d'une sauvegarde `.posbak`, qui n'inclut pas les archives, ou déplacement raté après le commit) : `archiveFile(id:)` renomme le `<id>.tmp` s'il a l'empreinte signée, sinon reconstruit le ZIP depuis la base (mêmes identifiant et horodatage, ZIP déterministe) et ne le sert que si son SHA-256 est celui de la ligne ; sinon 404 « non reconstructible » (données modifiées depuis l'archivage).
```

- [ ] **Step 2: `ios/README.md` et `docs/fiscal.md`**

`ios/README.md`, l. 107 : remplacer la phrase `Les autres méthodes (fiscal, clôtures, appairage `pair`) répondent `501` (« Non disponible en mode autonome ») jusqu'aux sous-projets fiscaux ; le choix Serveur/Autonome est proposé au premier lancement (plan 1e).` par `Les sous-projets 2 et 3 ajoutent le fiscal NF525 (reçus chaînés, JET, X/Z, clôtures de période, archives, FEC, vérification des chaînes) ; les méthodes non portées (dont l'appairage `pair`) répondent `501` (« Non disponible en mode autonome ») ; le choix Serveur/Autonome est proposé au premier lancement (plan 1e).`
L. 109 : remplacer `un bandeau rappelle que le mode n'est pas fiscal et la section Fiscal est masquée.` par `la section Fiscal (rapports X/Z, vérification des chaînes, clôtures de période, archives et leur partage, FEC) est réservée aux responsables.`

`docs/fiscal.md` § 5.1 : remplacer `(`POST /api/fiscal/verify-chains`)` par `(`POST /api/fiscal/verify`)`, et `- Chaîne du journal technique JET (`jet`).` par `- Chaîne du journal technique JET (`journal`).` ; ajouter après la liste des codes :

```
   Une rupture est journalisée (`CHAIN_BREAK_DETECTED`, première chaîne rompue). Le mode autonome de l'iPad fait la même vérification sur sa base (`LocalFiscalRepository.verifyAllChains`, mêmes chaînes, codes et références).
```

- [ ] **Step 3: Vérification complète**

Run: `cd ios/Packages/PosKit && swift test`
Expected: PASS (≈ 552 tests ; relever le nombre exact et le nombre de suites).
Run: `dotnet test RestaurantPos.slnx`
Expected: PASS, 766 (aucun changement .NET dans ce lot).
Run: `cd ios && ./scripts/test.sh ui`
Expected: `✔ Rapport` (toute la suite UI, ~8 min) ; relever le nombre de tests.

- [ ] **Step 4: Suivis**

`docs/plans/ipad-standalone-3d-progress.md` : toutes les lignes à « fait » avec les comptes, puis ajouter :

```markdown
Vérification finale (tâche 10) : `dotnet test RestaurantPos.slnx` <n>/<n>, `swift test` <n>/<n>, tests UI <n>/<n>.

Reports explicites (hors 3d) : en mode serveur, les refus de clôture restent le message anglais du .NET (le code n'est pas transmis par `APIError`) ; mineurs 3b (couverture de la fixture : avril, borne annuelle ; chemin `tooLarge` du ZIP ; unicité V9 sans contrainte précise ; ordre des règlements `rowid` vs `Id` .NET ; clause legacy `POS_MAIN_TERM` ; ZIP corrompu et manifeste non-GUID non testés) ; mineurs 3c (remise de commande et destination absentes de la fixture ; fin de ligne ; tests de format sans montant négatif ; `parseIntObject` et clés dupliquées ; parseur de taux ; reçu HT=0/TVA=0 et plusieurs règlements avec pourboire) ; pas de bouton « vérifier la dernière archive » (le fichier passerait par `archiveFile`, qui journalise `ARCHIVE_EXPORTED`) ; chaîne `receipts` vide listée `T01` sur l'iPad, absente côté .NET ; ordre des types de clôture (mensuelle puis annuelle) au lieu de l'ordre d'apparition du .NET, sans effet sauf double rupture ; impression des clôtures (sous-projet 4).

Risques pour l'expert-comptable : dates d'écriture au jour UTC ; 580000 à deux libellés ; 411000 (chambre) seulement dans le FEC iPad ; 468600 (pourboires).
```

`docs/plans/ipad-standalone-3-progress.md` : ajouter une ligne au tableau

```markdown
| Lots 3a-3d : clôtures de période, archives, FEC, vérification et interface (`ipad-standalone-3{a,b,c,d}-progress.md`) | fait | <n> | <n> | <n> | aucun |
```

et remplacer la ligne `Reste : FEC et export par partage, écrans d'annulation de reçu et de restauration, duplicata imprimés (sous-projet 4), attestation NF525.` par `Reste : écrans d'annulation de reçu et de restauration (sous-projet 5), impression des clôtures et duplicata (sous-projet 4), attestation NF525.`

- [ ] **Step 5: Commit**

```bash
git add CLAUDE.md ios/README.md docs/fiscal.md docs/plans/ipad-standalone-3d-progress.md docs/plans/ipad-standalone-3-progress.md
git commit -m "docs: lot 3d fait (vérification des chaînes, section Fiscal autonome), sous-projet 3 terminé"
```

---

## Couverture du spec et des reports (auto-revue)

| Exigence | Tâche |
|---|---|
| `verifyAllChains` + `PeriodClosures` (par poste et type) et `FiscalArchives` (globale), mêmes codes que le .NET | 1 (+ 2 pour `CHAIN_BREAK_DETECTED`) |
| Preuve : falsification SQL contournant un trigger sur une copie de base | 1 (`tamperableCopy` via `backup(to:)`), 2 |
| Liste et création des clôtures, erreurs métier lisibles | 3 (textes), 8 (store), 9 (écran) |
| Liste et création des archives, vérification (`valid` / `hash_mismatch` / `chain_break` / `unknown_archive`) | 4, 8, 9 |
| Export FEC (dates, SIREN, société) et `ShareLink` archive + FEC | 7, 8, 9 |
| Chaînes FR / EN / AR | 3, 5, 7, 8 (PosKit), 9 (app) |
| XCUITest `-UITestLocal` : clôture, archive, FEC, vérification verte | 9 (le mois précédent n'a ni reçu ni Z dans la base de démonstration : clôture possible avec l'horloge réelle, sans argument de lancement ; les erreurs et l'ordre sont testés au niveau store avec horloge de test, tâche 8) |
| Reports 3b : `corruptChain` → `APIError` ; voisin illisible ; récupération du ZIP ; plafond de taille | 4, 4, 5, 8 |
| Reports 3c : filtre SQL ; SIREN ; nom de fichier sûr ; export en échec sans JET ; `Siren` `""`/`null` et dates ; `ORDER BY SequenceNumber` | 6, 7, 8, 7, 7, 6 |
| `CLAUDE.md` (3d fait, sous-projet 3, « irrécupérable », règle de suivi, risques expert-comptable), suivi 3d | 10 (suivi tenu à chaque tâche) |
