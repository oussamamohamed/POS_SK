import Foundation
import Testing
@testable import PosKit

@Suite("Schéma v5 : tables fiscales et immuabilité")
struct LocalFiscalSchemaTests {
    private func makeDB() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return db
    }

    static let insertReceipt = """
    INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
        PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid, VoidedReceiptId)
    VALUES ('R1', 'T01', 'T01-000001', 'O1', 1, 1000, 909, '{}', 'GENESIS', 'SIG', '2026-10-10T09:00:00.000Z', 0, NULL)
    """

    @Test func migrationCreatesTheFiscalTables() throws {
        let db = try makeDB()
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        #expect(LocalMigrator.steps.count >= 5)
        let tables = try db.query("SELECT name FROM sqlite_master WHERE type = 'table'").compactMap { $0.string("name") }
        for expected in ["FiscalReceipts", "PaymentTenders", "JournalEntries"] { #expect(tables.contains(expected)) }
    }

    @Test func upgradingAV4DatabaseKeepsItsData() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        for script in [LocalMigrator.schemaV1, LocalMigrator.schemaV2, LocalMigrator.schemaV3, LocalMigrator.schemaV4] { try db.exec(script) }
        try db.exec("PRAGMA user_version = 4")
        try db.run("INSERT INTO LocalCounters (Name, Day, LastSequence) VALUES ('keep', '', 7)")
        try LocalMigrator.migrate(db)
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        #expect(try db.query("SELECT LastSequence FROM LocalCounters WHERE Name = 'keep'").first?.int("LastSequence") == 7)
        #expect(try db.query("SELECT 1 AS x FROM sqlite_master WHERE name = 'FiscalReceipts'").count == 1)
    }

    @Test func upgradingAV5DatabaseAddsTheNullableHtBreakdownColumn() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        for script in [LocalMigrator.schemaV1, LocalMigrator.schemaV2, LocalMigrator.schemaV3, LocalMigrator.schemaV4, LocalMigrator.schemaV5] { try db.exec(script) }
        try db.exec("PRAGMA user_version = 5")
        try db.run(Self.insertReceipt)
        try LocalMigrator.migrate(db)
        #expect(try db.userVersion() == LocalMigrator.steps.count && LocalMigrator.steps.count >= 6)
        let row = try #require(try db.query("SELECT TotalTtcAmount, HtBreakdownJson FROM FiscalReceipts WHERE Id = 'R1'").first)
        #expect(row.int("TotalTtcAmount") == 1000 && row.string("HtBreakdownJson") == nil)
    }

    @Test func theHtBreakdownIsImmutableLikeTheRestOfTheReceipt() throws {
        let db = try makeDB()
        try db.run(Self.insertReceipt)
        #expect(throws: SQLiteError.self) { try db.run("UPDATE FiscalReceipts SET HtBreakdownJson = '{}' WHERE Id = 'R1'") }
    }

    @Test func receiptSequencesAreUniquePerTerminal() throws {
        let db = try makeDB()
        try db.run(Self.insertReceipt)
        #expect(throws: SQLiteError.self) {
            try db.run(Self.insertReceipt.replacingOccurrences(of: "'R1'", with: "'R2'"))
        }
    }

    @Test func fiscalRowsCannotBeUpdatedOrDeleted() throws {
        let db = try makeDB()
        try db.run(Self.insertReceipt)
        try db.run("INSERT INTO PaymentTenders (Id, FiscalReceiptId, Method, Amount, Tendered, ChangeGiven) VALUES ('P1', 'R1', 0, 1000, 1000, 0)")
        try db.run(
            """
            INSERT INTO JournalEntries (Id, LocalSequence, TerminalId, OccurredAtUtc, IdempotencyKey, EventType, PayloadJson, EntryHash, ChainSequence, PreviousHash, OperatorId)
            VALUES ('J1', 1, 'T01', '2026-10-10T09:00:00.000Z', 'k', 'X', '{}', 'h', 1, 'GENESIS', NULL)
            """
        )
        let attempts: [(String, String)] = [
            ("UPDATE FiscalReceipts SET TotalTtcAmount = 1 WHERE Id = 'R1'", "Fiscal receipts are append-only and immutable"),
            ("DELETE FROM FiscalReceipts WHERE Id = 'R1'", "Fiscal receipts are append-only and immutable"),
            ("UPDATE PaymentTenders SET Amount = 1 WHERE Id = 'P1'", "Payment tenders are immutable"),
            ("DELETE FROM PaymentTenders WHERE Id = 'P1'", "Payment tenders are immutable"),
            ("UPDATE JournalEntries SET PayloadJson = '{\"x\":1}' WHERE Id = 'J1'", "Chained journal entries are immutable"),
            ("DELETE FROM JournalEntries WHERE Id = 'J1'", "Chained journal entries are immutable"),
        ]
        for (sql, message) in attempts {
            do {
                try db.run(sql)
                Issue.record("« \(sql) » aurait dû être refusé")
            } catch let error as SQLiteError {
                #expect(error.message.contains("NF525 INV-3") && error.message.contains(message), "\(sql) : \(error.message)")
            }
        }
        #expect(try db.query("SELECT TotalTtcAmount FROM FiscalReceipts").first?.int("TotalTtcAmount") == 1000)
    }

    private static let insertPeriodClosure = """
    INSERT INTO PeriodClosures (Id, TerminalId, PeriodType, PeriodKey, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalTtcAmount, TotalHtAmount,
        TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, DailyClosureCount, PreviousSignatureHash, SignatureHash, SealedByUserId,
        SealedByUserName, CreatedAtUtc)
    VALUES ('P1', 'T01', 1, '2025-01', 1, '2024-12-31T23:00:00.000Z', '2025-01-31T23:00:00.000Z', 1000, 909, '{}', '{}', 1000, 1, 'GENESIS', 'SIG',
        'U1', 'Chef', '2026-02-01T10:00:00.000Z')
    """

    @Test func migrationV8CreatesThePeriodClosuresTable() throws {
        let db = try makeDB()
        #expect(LocalMigrator.steps.count >= 8)
        #expect(try db.query("SELECT 1 AS x FROM sqlite_master WHERE type = 'table' AND name = 'PeriodClosures'").count == 1)
    }

    @Test func periodClosuresAreAppendOnly() throws {
        let db = try makeDB()
        try db.run(Self.insertPeriodClosure)
        #expect(throws: (any Error).self) { try db.run("UPDATE PeriodClosures SET TotalTtcAmount = 1 WHERE Id = 'P1'") }
        #expect(throws: (any Error).self) { try db.run("DELETE FROM PeriodClosures WHERE Id = 'P1'") }
    }

    @Test func aPeriodCanOnlyBeClosedOncePerTerminalAndType() throws {
        let db = try makeDB()
        try db.run(Self.insertPeriodClosure)
        #expect(throws: (any Error).self) {
            try db.run(Self.insertPeriodClosure.replacingOccurrences(of: "'P1'", with: "'P2'").replacingOccurrences(of: ", 1, '2024-12-31", with: ", 2, '2024-12-31"))
        }
    }

    @Test func upgradingAV7DatabaseAddsThePeriodClosuresTable() throws {
        let older = try SQLiteDatabase(path: ":memory:")
        for script in LocalMigrator.steps.prefix(7) { try older.exec(script) }
        try older.exec("PRAGMA user_version = 7")
        try LocalMigrator.migrate(older)
        #expect(try older.userVersion() == LocalMigrator.steps.count)
        #expect(try older.query("SELECT 1 AS x FROM sqlite_master WHERE name = 'PeriodClosures'").count == 1)
    }

    private static let insertArchive = """
    INSERT INTO FiscalArchives (Id, PeriodClosureId, PeriodType, PeriodKey, FileName, FileSha256, FileSizeBytes, ArchiveSequence,
        PreviousSignatureHash, SignatureHash, CreatedByUserId, CreatedAtUtc)
    VALUES ('A1', 'P1', 1, '2025-01', 'archive-T01-M-2025-01.zip', 'abc', 10, 1, 'GENESIS', 'SIG', 'U1', '2026-02-01T10:00:00.000Z')
    """

    @Test func migrationV9CreatesTheFiscalArchivesTable() throws {
        let db = try makeDB()
        #expect(LocalMigrator.steps.count >= 9)
        #expect(try db.query("SELECT 1 AS x FROM sqlite_master WHERE type = 'table' AND name = 'FiscalArchives'").count == 1)
    }

    @Test func fiscalArchivesAreAppendOnly() throws {
        let db = try makeDB()
        try db.run(Self.insertArchive)
        #expect(throws: (any Error).self) { try db.run("UPDATE FiscalArchives SET FileSha256 = 'x' WHERE Id = 'A1'") }
        #expect(throws: (any Error).self) { try db.run("DELETE FROM FiscalArchives WHERE Id = 'A1'") }
    }

    @Test func oneArchivePerClosureAndOneArchivePerSequenceNumber() throws {
        let db = try makeDB()
        try db.run(Self.insertArchive)
        #expect(throws: (any Error).self) { // même clôture, autre séquence
            try db.run(Self.insertArchive.replacingOccurrences(of: "'A1', 'P1'", with: "'A2', 'P1'").replacingOccurrences(of: ", 10, 1, ", with: ", 10, 2, "))
        }
        #expect(throws: (any Error).self) { // autre clôture, même séquence
            try db.run(Self.insertArchive.replacingOccurrences(of: "'A1', 'P1'", with: "'A3', 'P3'"))
        }
    }

    @Test func upgradingAV8DatabaseAddsTheFiscalArchivesTable() throws {
        let older = try SQLiteDatabase(path: ":memory:")
        for script in LocalMigrator.steps.prefix(8) { try older.exec(script) }
        try older.exec("PRAGMA user_version = 8")
        try LocalMigrator.migrate(older)
        #expect(try older.userVersion() == LocalMigrator.steps.count)
        #expect(try older.query("SELECT 1 AS x FROM sqlite_master WHERE name = 'FiscalArchives'").count == 1)
    }
}
