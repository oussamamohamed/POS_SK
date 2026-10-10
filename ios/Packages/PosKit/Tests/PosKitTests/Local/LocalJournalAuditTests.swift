import Foundation
import Testing
@testable import PosKit

@Suite("JET : dérogations Happy Hour et annulations de mise en attente")
struct LocalJournalAuditTests {
    private func entries(_ db: SQLiteDatabase, _ type: String) throws -> [SQLRow] {
        try db.query("SELECT * FROM JournalEntries WHERE EventType = ? ORDER BY ChainSequence", [.text(type)])
    }

    @Test func happyHourOverrideIsJournaledWithTheBackendPayload() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        _ = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 30, reason: "Dérogation +10 % d'été")
        _ = try await api.stopHappyHourOverride(terminalId: "T01", pin: "1234", reason: "Fin de service")
        let started = try #require(try entries(db, "EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED").first)
        let payload = try #require(started.string("PayloadJson"))
        #expect(payload.hasPrefix("{\"SessionId\":\"") && payload.contains("\"DurationMinutes\":30") && payload.contains("\"Reason\":\"D\\u00E9rogation \\u002B10 % d\\u0027\\u00E9t\\u00E9\""))
        #expect(payload.contains("\"Operator\":") && payload.contains("\"ExpiresAtUtc\":\""))
        #expect(started.string("TerminalId") == "T01" && started.uuid("OperatorId") != nil)
        let stopped = try #require(try entries(db, "EVENT_HAPPY_HOUR_OVERRIDE_STOPPED").first)
        #expect(stopped.string("PayloadJson")?.contains("\"Reason\":\"Fin de service\"") == true)
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
    }

    @Test func aRefusedOverrideWritesNothing() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        await #expect(throws: APIError.self) { try await api.activateHappyHourOverride(terminalId: "T01", pin: "0000", minutes: 30, reason: "x") }
        #expect(try db.query("SELECT COUNT(*) AS n FROM JournalEntries").first?.int("n") == 0)
    }

    @Test func voidingAHeldOrderIsJournaled() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)])
        let order = try #require(try await api.activeOrder(table: "T1"))
        try await api.holdOrder(orderId: order.orderId, terminalId: "T01", label: "Client A")
        let held = try #require(try await api.heldOrders(terminalId: "T01").first)
        try await api.voidHeldOrder(holdId: held.holdId, supervisorPin: "1234", reason: "Client parti", terminalId: "T01")
        let entry = try #require(try entries(db, "EVENT_HELD_ORDER_VOIDED").first)
        let payload = try #require(entry.string("PayloadJson"))
        #expect(payload.contains("\"HoldId\":\"\(FiscalHashing.guidText(held.holdId))\"") && payload.contains("\"Reason\":\"Client parti\"") && payload.contains("\"VoidedBy\":"))
        #expect(entry.string("TerminalId") == "T01")
    }
}
