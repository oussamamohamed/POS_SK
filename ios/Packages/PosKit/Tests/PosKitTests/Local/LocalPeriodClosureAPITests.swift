import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : clôtures de période")
struct LocalPeriodClosureAPITests {
    private func payOneBurger(_ api: LocalPosAPI, table: String) async throws {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger)])
        let order = try #require(try await api.activeOrder(table: table))
        _ = try await api.pay(localPayment(order, table: table, tenders: [localTender(.creditCard, 1950)]))
    }

    /// API sur fichier pilotée par une horloge de test, avec une seconde connexion pour relire les tables.
    private func makeAPIWithDatabase(pin: String = "1234") async throws -> (LocalPosAPI, SQLiteDatabase, LocalTestClock) {
        let clock = LocalTestClock("2026-09-10T12:00:00Z")
        let path = temporaryDatabasePath()
        let api = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
        _ = try await api.login(pin: pin)
        return (api, try SQLiteDatabase(path: path), clock)
    }

    @Test func aMonthIsClosedFromItsDailyClosuresAndIsListed() async throws {
        let clock = LocalTestClock("2026-09-09T21:00:00Z")
        let api = try await makeLocalAPI(clock: clock)
        // Parité .NET : le premier Z d'un poste commence à l'epoch, donc hors du mois ; seul le second Z est compté.
        _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
        clock.set("2026-09-10T12:00:00Z")
        try await payOneBurger(api, table: "T1")
        clock.set("2026-09-10T21:00:00Z")
        let z = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
        clock.set("2026-10-02T09:00:00Z")

        let closure = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        #expect(closure.closureSequence == 1)
        #expect(closure.totalTtcCents == z.totalSalesTtc.cents)
        #expect(closure.dailyClosureCount == 1)
        #expect(closure.perpetualGrandTotalCents == z.perpetualGrandTotal.cents)
        #expect(closure.previousSignatureHash == FiscalHashing.genesisHash)
        #expect(try await api.periodClosures(terminalId: nil, periodType: nil).map(\.periodKey) == ["2026-09"])
        #expect(try await api.periodClosures(terminalId: "T01", periodType: .annual).isEmpty)
    }

    @Test func theClosureIsJournaledWithTheDotNetPayload() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        clock.set("2026-10-02T09:00:00Z")
        let closure = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        let entry = try #require(try db.query("SELECT PayloadJson FROM JournalEntries WHERE EventType = 'PERIOD_CLOSURE'").first)
        let id = FiscalHashing.guidText(closure.id)
        let signature = try #require(closure.signatureHash)
        #expect(entry.string("PayloadJson") ==
            "{\"PeriodClosureId\":\"\(id)\",\"TerminalId\":\"T01\",\"PeriodType\":\"monthly\",\"PeriodKey\":\"2026-09\",\"ClosureSequence\":1,\"TotalTtcCents\":0,\"PerpetualGrandTotalCents\":0,\"SignatureHash\":\"\(signature)\"}")
    }

    @Test func aMonthStillRunningIsRefusedAndWritesNothing() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        clock.set("2026-09-20T09:00:00Z")
        do {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
            Issue.record("la clôture aurait dû être refusée")
        } catch let APIError.server(status, _) {
            #expect(status == 409)
        }
        #expect(try db.query("SELECT 1 AS x FROM PeriodClosures").isEmpty)
        #expect(try db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'PERIOD_CLOSURE'").isEmpty)
    }

    @Test func aMonthWithAReceiptButNoZClosureListsTheMissingDays() async throws {
        let clock = LocalTestClock("2026-09-10T12:00:00Z")
        let api = try await makeLocalAPI(clock: clock)
        try await payOneBurger(api, table: "T1")
        clock.set("2026-10-02T09:00:00Z")
        do {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
            Issue.record("la clôture aurait dû être refusée")
        } catch let APIError.server(status, message) {
            #expect(status == 409)
            #expect(message?.contains("2026-09-10") == true)
        }
    }

    @Test func aWaiterCannotClosePeriods() async throws {
        let clock = LocalTestClock("2026-10-02T09:00:00Z")
        let api = try await makeLocalAPI(pin: "2468", clock: clock)
        await #expect(throws: APIError.self) {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        }
        await #expect(throws: APIError.self) { _ = try await api.periodClosures(terminalId: nil, periodType: nil) }
    }

    @Test func aDoubleClosureIsRefusedAndAnInvalidKeyIs400() async throws {
        let clock = LocalTestClock("2026-10-02T09:00:00Z")
        let api = try await makeLocalAPI(clock: clock)
        _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        do {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
            Issue.record("le doublon aurait dû être refusé")
        } catch let APIError.server(status, _) { #expect(status == 409) }
        do {
            _ = try await api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-13")
            Issue.record("la clé invalide aurait dû être refusée")
        } catch let APIError.server(status, _) { #expect(status == 400) }
    }
}
