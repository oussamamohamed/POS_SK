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
}
