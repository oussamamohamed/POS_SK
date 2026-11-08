import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : tendances et résumé")
struct LocalInsightsTests {
    @Test func insightsNeedThreeDaysOfData() async throws {
        let api = try await makeLocalAPI()
        let insights = try await api.insights(tzOffsetMinutes: 0)
        #expect(insights.insufficientData && insights.cards.isEmpty)
    }

    @Test func noSummaryBeforeTheFirstZ() async throws {
        let api = try await makeLocalAPI()
        #expect(try await api.dailySummary(terminalId: "T01", tzOffsetMinutes: 0) == nil)
    }

    @Test func summaryTotalsEqualTheZAndCarryTipsCashAndAlerts() async throws {
        let api = try await makeLocalAPI()
        let session = try await api.openCashSession(openingFloatCents: 5_000)
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)])
        let order = try #require(try await api.activeOrder(table: "T1"))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1_950)]))
        _ = try await api.countCash(sessionId: session.id, countedCents: 6_900, denominations: [], varianceNote: "Écart")
        let z = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")

        let summary = try #require(try await api.dailySummary(terminalId: "T01", tzOffsetMinutes: 0))

        #expect(summary.totalSalesTtcCents == z.totalSalesTtc.cents)
        #expect(summary.receiptCount == z.receiptCount && summary.closureSequence == z.closureSequence ?? -1)
        #expect(summary.cash == DailySummaryCash(floatCents: 5_000, countedCents: 6_900, varianceCents: -50))
        #expect(summary.shareText.contains("Z \(z.closureSequence ?? -1)"))
    }
}
