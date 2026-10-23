import Foundation
import Testing
@testable import PosKit

@Suite("Schéma V10 : file d'impression et transport des imprimantes")
struct LocalPrintQueueSchemaTests {
    private func columns(_ db: SQLiteDatabase, _ table: String) throws -> [String] {
        try db.query("PRAGMA table_info(\(table))").compactMap { $0.string("name") }
    }

    @Test func aFreshDatabaseMigratesToV10WithThe14PrintJobColumns() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        #expect(LocalMigrator.steps.count >= 10)
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        let expected = ["Id", "PrinterId", "Kind", "DocumentJson", "OpenCashDrawer", "Status", "Attempts", "NextAttemptAtUtc", "DeadlineAtUtc",
                        "LastError", "CreatedAtUtc", "SentAtUtc", "DuplicateOfDocumentId", "DuplicateNumber"]
        #expect(try columns(db, "PrintJobs") == expected)
        let printerColumns = try columns(db, "PrinterConfigurations")
        #expect(printerColumns.contains("TransportKind") && printerColumns.contains("DeviceIdentifier"))
    }

    @Test func upgradingAV9DatabaseKeepsItsPrintersAsLan() throws {
        let older = try SQLiteDatabase(path: ":memory:")
        for script in LocalMigrator.steps.prefix(9) { try older.exec(script) }
        try older.exec("PRAGMA user_version = 9")
        try older.run("""
        INSERT INTO PrinterConfigurations (Id, Name, IpAddress, Port, PaperWidthMm, OpenCashDrawerOnReceipt, TextMode, AssignedStationIds, IsActive,
            CreatedAtUtc, UpdatedAtUtc)
        VALUES ('P1', 'Cuisine', '10.0.0.5', 9100, 80, 0, 0, '[]', 1, '2026-10-01T10:00:00.000Z', '2026-10-01T10:00:00.000Z')
        """)
        try LocalMigrator.migrate(older)
        #expect(try older.userVersion() == LocalMigrator.steps.count)
        let row = try #require(try older.query("SELECT * FROM PrinterConfigurations").first)
        #expect(row.string("TransportKind") == "lan" && row.string("DeviceIdentifier") == nil && row.string("Name") == "Cuisine")
        #expect(try older.query("SELECT 1 AS x FROM PrintJobs").isEmpty)
    }
}
