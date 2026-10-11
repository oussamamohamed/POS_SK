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
}
