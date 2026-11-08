import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : caisse")
struct LocalCashTests {
    private func sell(_ api: LocalPosAPI, table: String, cents: Int, tendered: Int? = nil) async throws {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger)])
        let order = try #require(try await api.activeOrder(table: table))
        _ = try await api.pay(localPayment(order, table: table, tenders: [localTender(.cash, cents, tendered: tendered)]))
    }

    @Test func expectedIsFloatPlusKeptCashNetOfChangeAndMovements() async throws {
        let api = try await makeLocalAPI()
        let session = try await api.openCashSession(openingFloatCents: 10_000)
        try await sell(api, table: "T1", cents: 1_950, tendered: 2_000)   // 19,50 € avec un billet de 20 € : 50 c rendus
        _ = try await api.addCashMovement(sessionId: session.id, kind: "out", amountCents: 1_200, reason: "Achat de glace")
        _ = try await api.addCashMovement(sessionId: session.id, kind: "in", amountCents: 500, reason: "Appoint")

        let current = try #require(try await api.currentCashSession())

        #expect(current.expectedCents == 10_000 + 1_950 - 1_200 + 500)
    }

    @Test func oneOpenSessionAndIdempotentCountThatFreezesTheVariance() async throws {
        let api = try await makeLocalAPI()
        let session = try await api.openCashSession(openingFloatCents: 5_000)
        await #expect(throws: CashError.sessionAlreadyOpen) { _ = try await api.openCashSession(openingFloatCents: 1) }

        let first = try await api.countCash(sessionId: session.id, countedCents: 4_800, denominations: [CashDenomination(valueCents: 2_000, quantity: 2), CashDenomination(valueCents: 800, quantity: 1)], varianceNote: nil)
        try await sell(api, table: "T1", cents: 1_950)              // vente après le comptage : sans effet sur le comptage figé
        let again = try await api.countCash(sessionId: session.id, countedCents: 1, denominations: [], varianceNote: nil)

        #expect(first == CashCountResult(expectedCents: 5_000, countedCents: 4_800, varianceCents: -200))
        #expect(again == first)
        #expect(try await api.currentCashSession() == nil)
        _ = try await api.openCashSession(openingFloatCents: 0) // la session comptée libère le poste
    }

    @Test func varianceNoteIsRequiredAboveTheActiveThreshold() async throws {
        let api = try await makeLocalAPI()
        _ = try await api.setAlertRule(type: AlertRuleType.cashVariance, threshold: 1_000, enabled: true)
        let session = try await api.openCashSession(openingFloatCents: 10_000)

        await #expect(throws: CashError.varianceNoteRequired) { _ = try await api.countCash(sessionId: session.id, countedCents: 8_000, denominations: [], varianceNote: "  ") }
        let result = try await api.countCash(sessionId: session.id, countedCents: 8_000, denominations: [], varianceNote: "Rendu erroné")
        #expect(result.varianceCents == -2_000 && result.alertCreated)
        #expect(try await api.alerts(state: "open").map(\.ruleType) == [AlertRuleType.cashVariance])
    }

    @Test func movementsNeedAReasonAndAnOpenSession() async throws {
        let api = try await makeLocalAPI()
        let session = try await api.openCashSession(openingFloatCents: 0)
        for (kind, amount, reason) in [("out", 100, "ab"), ("out", 100, String(repeating: "x", count: 201)), ("out", 0, "Motif valable"), ("autre", 100, "Motif valable")] {
            await #expect(throws: CashError.movementInvalid) { _ = try await api.addCashMovement(sessionId: session.id, kind: kind, amountCents: amount, reason: reason) }
        }
        _ = try await api.countCash(sessionId: session.id, countedCents: 0, denominations: [], varianceNote: nil)
        await #expect(throws: CashError.sessionClosed) { _ = try await api.addCashMovement(sessionId: session.id, kind: "out", amountCents: 100, reason: "Motif valable") }
    }

    @Test func cashTablesAreAppendOnlyAndEveryStepIsJournaled() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let session = try await api.openCashSession(openingFloatCents: 1_000)
        _ = try await api.addCashMovement(sessionId: session.id, kind: "out", amountCents: 100, reason: "Achat de glace")
        _ = try await api.countCash(sessionId: session.id, countedCents: 900, denominations: [], varianceNote: nil)

        for sql in ["UPDATE CashSessions SET Id = Id", "DELETE FROM CashSessions", "UPDATE CashMovements SET Id = Id", "DELETE FROM CashMovements", "UPDATE CashCounts SET Id = Id", "DELETE FROM CashCounts"] {
            #expect(throws: SQLiteError.self) { try db.run(sql) }
        }
        let events = Set(try db.query("SELECT EventType FROM JournalEntries").compactMap { $0.string("EventType") })
        #expect(events.isSuperset(of: [LocalJournalEvents.cashSessionOpened, LocalJournalEvents.cashMovement, LocalJournalEvents.cashCounted]))
        #expect(try await api.verifyChains().isValid)
    }

    @Test func aWaiterOnlySeesTheirOwnSessionAndHistoryIsManagerOnly() async throws {
        let api = try await makeLocalAPI()
        _ = try await api.openCashSession(openingFloatCents: 100) // ouverte par le responsable
        _ = try await api.login(pin: "2468")
        await #expect(throws: APIError.self) { _ = try await api.currentCashSession() }
        await #expect(throws: APIError.self) { _ = try await api.cashSessions(from: nil, to: nil) }
    }

    @Test func theZReportCarriesTheCountWithoutTouchingTheSignedContent() async throws {
        let api = try await makeLocalAPI()
        let session = try await api.openCashSession(openingFloatCents: 5_000)
        try await sell(api, table: "T1", cents: 1_950)
        _ = try await api.countCash(sessionId: session.id, countedCents: 6_950, denominations: [], varianceNote: nil)
        let z = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")

        let latest = try #require(try await api.latestClosure(terminalId: "T01"))

        #expect(latest.cash == CashZSummary(floatCents: 5_000, countedCents: 6_950, varianceCents: 0))
        #expect(latest.signatureHash == z.signatureHash)
        #expect(try await api.verifyChains().isValid)
    }

    @Test func theSummaryAddsEveryCountOfThePeriod() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let noon = try await api.openCashSession(openingFloatCents: 5_000)
        _ = try await api.countCash(sessionId: noon.id, countedCents: 4_800, denominations: [], varianceNote: nil)       // −2,00 €
        let evening = try await api.openCashSession(openingFloatCents: 3_000)
        _ = try await api.countCash(sessionId: evening.id, countedCents: 3_100, denominations: [], varianceNote: nil)    // +1,00 €

        let summary = try LocalCashRepository(db: db).summary(terminalId: "T01", from: .distantPast, to: Date().addingTimeInterval(60))

        #expect(summary?.float == 8_000 && summary?.counted == 7_900 && summary?.variance == -100)
    }
}
