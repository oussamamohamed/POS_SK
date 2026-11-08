import Foundation
import Testing
@testable import PosKit

@Suite("Migration iPad V16 : caisse et alertes")
struct LocalCashMigrationTests {
    @Test func migratesFromV15KeepsDataAndInstallsTheConstraints() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try db.transaction {
            for (i, script) in LocalMigrator.steps.prefix(15).enumerated() {
                try db.exec(script)
                try db.exec("PRAGMA user_version = \(i + 1)")
            }
        }
        try db.run("INSERT INTO RestaurantSettings (Id, ReceiptLanguage, KitchenTicketLanguage, CompanyName, AddressLines, Siret, VatNumber, FiscalYearStartMonth, FiscalYearStartDay, UpdatedAtUtc) VALUES (1, 'fr', 'fr', 'X', '', '', '', 1, 1, '2026-01-01T00:00:00.000Z')")

        try LocalMigrator.migrate(db)

        #expect(try db.userVersion() == LocalMigrator.steps.count && LocalMigrator.steps.count >= 16)
        // Colonnes ajoutées sur les réglages existants, seuils par défaut désactivés.
        let settings = try #require(try db.query("SELECT TrendThresholdPercent, CashVarianceNoteRequired FROM RestaurantSettings").first)
        #expect(settings.int("TrendThresholdPercent") == 15 && settings.bool("CashVarianceNoteRequired"))
        let rules = try db.query("SELECT Type, Threshold, Enabled FROM AlertRules ORDER BY Type")
        #expect(rules.count == 4 && rules.allSatisfy { !$0.bool("Enabled") })
        #expect(try db.query("SELECT Threshold FROM AlertRules WHERE Type = 'voids_count'").first?.int("Threshold") == 3)

        // Déclencheurs : ajout seulement, une seule session sans comptage par poste, un seul comptage par session.
        try db.run("INSERT INTO CashSessions (Id, TerminalId, OpenedAtUtc, OpenedByOperatorId, OpeningFloatCents) VALUES ('S1', 'T01', '2026-01-01T00:00:00.000Z', 'O', 100)")
        #expect(throws: SQLiteError.self) { try db.run("UPDATE CashSessions SET OpeningFloatCents = 0") }
        #expect(throws: SQLiteError.self) { try db.run("INSERT INTO CashSessions (Id, TerminalId, OpenedAtUtc, OpenedByOperatorId, OpeningFloatCents) VALUES ('S2', 'T01', '2026-01-01T00:00:00.000Z', 'O', 0)") }
        try db.run("INSERT INTO CashCounts (Id, SessionId, ClosedAtUtc, ClosedByOperatorId, CountedCents, ExpectedCents, VarianceCents) VALUES ('C1', 'S1', '2026-01-01T01:00:00.000Z', 'O', 100, 100, 0)")
        #expect(throws: SQLiteError.self) { try db.run("INSERT INTO CashCounts (Id, SessionId, ClosedAtUtc, ClosedByOperatorId, CountedCents, ExpectedCents, VarianceCents) VALUES ('C2', 'S1', '2026-01-01T02:00:00.000Z', 'O', 1, 1, 0)") }
        try db.run("INSERT INTO CashSessions (Id, TerminalId, OpenedAtUtc, OpenedByOperatorId, OpeningFloatCents) VALUES ('S3', 'T01', '2026-01-01T03:00:00.000Z', 'O', 0)") // la précédente est comptée
    }
}
