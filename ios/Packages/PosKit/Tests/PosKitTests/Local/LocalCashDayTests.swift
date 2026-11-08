import Foundation
import Testing
@testable import PosKit

/// Miroir de `CashDaySimulationTests` (.NET) : même journée scriptée, même oracle (spec 024, SC-001/SC-002/SC-003).
@Suite("LocalPosAPI : journée de caisse")
struct LocalCashDayTests {
    @Test func scriptedDayGivesTheOracleExpectedVarianceOneAlertAndAValidChain() async throws {
        let api = try await makeLocalAPI()
        _ = try await api.setAlertRule(type: AlertRuleType.cashVariance, threshold: 200, enabled: true)
        let session = try await api.openCashSession(openingFloatCents: 10_000)
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        for (index, (method, tendered)) in [(PaymentMethod.cash, 2_000), (.creditCard, 1_950), (.cash, 1_950)].enumerated() {
            let table = "J\(index + 1)"
            try await seatTable(api, table, items: [localInput(burger)])
            let order = try #require(try await api.activeOrder(table: table))
            _ = try await api.pay(localPayment(order, table: table, tenders: [localTender(method, 1_950, tendered: tendered)]))
        }
        _ = try await api.addCashMovement(sessionId: session.id, kind: "out", amountCents: 1_200, reason: "Achat de glace")
        _ = try await api.addCashMovement(sessionId: session.id, kind: "in", amountCents: 500, reason: "Appoint de monnaie")

        #expect(try await api.currentCashSession()?.expectedCents == 13_200)
        await #expect(throws: CashError.varianceNoteRequired) { _ = try await api.countCash(sessionId: session.id, countedCents: 12_750, denominations: [], varianceNote: nil) }
        let counted = try await api.countCash(sessionId: session.id, countedCents: 12_750, denominations: [], varianceNote: "Rendu erroné")
        let again = try await api.countCash(sessionId: session.id, countedCents: 1, denominations: [], varianceNote: nil)
        #expect(counted.varianceCents == -450 && again.varianceCents == -450 && again.countedCents == 12_750)
        #expect(try await api.alerts(state: "open").filter { $0.ruleType == AlertRuleType.cashVariance }.count == 1)

        let z = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
        let summary = try #require(try await api.dailySummary(terminalId: "T01", tzOffsetMinutes: 0))
        #expect(summary.totalSalesTtcCents == 5_850 && summary.totalSalesTtcCents == z.totalSalesTtc.cents)
        #expect(summary.cash?.varianceCents == -450)
        #expect(try await api.verifyChains().isValid)
    }
}
