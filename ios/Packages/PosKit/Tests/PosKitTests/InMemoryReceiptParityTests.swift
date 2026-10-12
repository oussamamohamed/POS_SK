import Foundation
import Testing
@testable import PosKit

/// Le double de test `InMemoryPosAPI` (aussi utilisé par `-UITestMode`) répartit la TVA d'un reçu comme `FiscalReceiptMath`
/// (plus fort reste par taux, pourboire exclu du reçu), c'est-à-dire comme le .NET et le mode autonome.
@Suite("InMemoryPosAPI : reçus alignés sur FiscalReceiptMath")
struct InMemoryReceiptParityTests {
    private func line(_ name: String, cents: Int, rate: Decimal) -> OrderItemInput {
        OrderItemInput(
            productId: UUID(), productName: name, quantity: 1, unitPrice: Money(cents: cents), taxRatePercent: rate,
            preparationStationId: nil, modifiers: [], course: .direct, modifiersPriceExtra: .zero,
            taxRateTakeawayPercent: nil, isHappyHourApplied: false, originalUnitPrice: nil, appliedHappyHourScheduleId: nil
        )
    }

    private func request(_ order: ActiveOrder, paid: Int, tip: Int = 0) -> PaymentRequest {
        let amount = Money(cents: paid + tip)
        return PaymentRequest(
            orderId: order.orderId, tableNumber: order.tableNumber, operatorId: nil, terminalId: "T01",
            tenders: [TenderInput(method: .creditCard, amount: amount, tendered: amount, changeGiven: .zero)], tipAmount: Money(cents: tip)
        )
    }

    /// Paiements successifs (le dernier avec pourboire) : le rapport X cumule exactement les reçus calculés par `FiscalReceiptMath`.
    private func check(payments: [Int], tip: Int, lines: [OrderItemInput]) async throws {
        let api = InMemoryPosAPI()
        _ = try await api.login(pin: "1234")
        try await api.openTable(number: "T7", covers: 2, operatorId: nil, waiterName: "Test")
        let order = try await api.addItems(table: "T7", items: lines)

        var prior = 0, expectedTtc = 0, expectedHt = 0
        var expectedVat: [String: Int] = [:]
        for (index, paid) in payments.enumerated() {
            let isLast = index == payments.count - 1
            let receiptTip = isLast ? tip : 0
            let amounts = FiscalReceiptMath.amounts(order: order, totalPaidCents: paid + receiptTip, tipCents: tip, priorFiscalTtcCents: prior)
            prior += amounts.ttcCents
            expectedTtc += amounts.ttcCents
            expectedHt += amounts.htCents
            let vat = try #require(JSONSerialization.jsonObject(with: Data(amounts.taxBreakdownJson.utf8)) as? [String: Int])
            for (rate, cents) in vat { expectedVat[rate, default: 0] += cents }
            // Le double accepte le pourboire sur n'importe quel paiement : on le pose sur le dernier seulement, comme le serveur.
            _ = try await api.pay(request(order, paid: paid, tip: receiptTip))
        }

        let report = try await api.xReport(terminalId: "T01")
        #expect(report.totalSalesTtc.cents == expectedTtc)
        #expect(report.totalSalesHt.cents == expectedHt)
        #expect(report.vatBreakdown.mapValues(\.cents) == expectedVat)
        #expect(report.receiptCount == payments.count)
        // Reçus soldant la commande : somme exacte du TTC de la commande, pourboire exclu ; TTC - HT = TVA.
        let orderTtc = OrderMath.totals(lines: order.lines.map(CartLine.init(serverLine:)), destination: order.destination, discount: order.globalDiscount).totalTtc.cents
        #expect(expectedTtc == orderTtc)
        #expect(report.totalSalesTtc.cents - report.totalSalesHt.cents == report.vatBreakdown.values.reduce(0) { $0 + $1.cents })
    }

    @Test func partialPaymentsAtTwoRates() async throws {
        try await check(payments: [700, 800], tip: 0, lines: [line("Pizza", cents: 1000, rate: 10), line("Pain", cents: 500, rate: 5.5)])
    }

    @Test func partialPaymentsWithTipOnTheFinalOne() async throws {
        try await check(payments: [700, 800], tip: 150, lines: [line("Pizza", cents: 1000, rate: 10), line("Pain", cents: 500, rate: 5.5)])
    }

    @Test func threeWaySplitAtThreeRates() async throws {
        try await check(
            payments: [880, 880, 880], tip: 90,
            lines: [line("Plat", cents: 1000, rate: 10), line("Boisson", cents: 1250, rate: 20), line("Pain", cents: 390, rate: 5.5)]
        )
    }

    @Test func singlePaymentWithTipExcludesTipFromTheReceipt() async throws {
        try await check(payments: [1500], tip: 200, lines: [line("Pizza", cents: 1000, rate: 10), line("Pain", cents: 500, rate: 5.5)])
    }
}
