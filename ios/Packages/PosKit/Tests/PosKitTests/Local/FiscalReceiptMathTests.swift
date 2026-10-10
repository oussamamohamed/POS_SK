import Foundation
import Testing
@testable import PosKit

@Suite("Montants fiscaux du reçu : parité avec CheckoutPaymentService (.NET)")
struct FiscalReceiptMathTests {
    struct Reference: Decodable {
        struct Line: Decodable {
            let unitPriceCents: Int
            let quantity: Int
            let taxRatePercent: String
            let takeawayRatePercent: String?
            let discountPercent: String
            let isComp: Bool
            let modifiersExtraCents: Int
        }
        struct Tender: Decodable { let method: Int; let amountCents: Int; let tenderedCents: Int }
        struct Expected: Decodable {
            let totalTtcCents: Int
            let totalHtCents: Int
            let taxBreakdownJson: String
        }
        struct Payment: Decodable { let tenders: [Tender]; let expected: Expected }
        struct Voided: Decodable {
            let paymentIndex: Int
            let totalTtcCents: Int
            let totalHtCents: Int
            let taxBreakdownJson: String
        }
        struct Scenario: Decodable {
            let name: String
            let destination: Int
            let globalDiscountType: Int?
            let globalDiscountValue: String
            let tipCents: Int
            let lines: [Line]
            let payments: [Payment]
            let voided: Voided?
        }
        let taxScenarios: [Scenario]
    }

    static func scenarios() throws -> [Reference.Scenario] {
        try JSONDecoder().decode(Reference.self, from: ContractDecodingTests.fixture("fiscal_reference")).taxScenarios
    }

    static func order(_ scenario: Reference.Scenario) throws -> ActiveOrder {
        let lines = scenario.lines.map { line in
            OrderLine(
                productId: UUID(), productName: "Produit", quantity: line.quantity, unitPrice: Money(cents: line.unitPriceCents),
                taxRatePercent: Decimal(string: line.taxRatePercent)!, isComp: line.isComp,
                discountPercent: Decimal(string: line.discountPercent)!, modifiersPriceExtra: Money(cents: line.modifiersExtraCents),
                taxRateTakeawayPercent: line.takeawayRatePercent.flatMap { Decimal(string: $0) }
            )
        }
        return ActiveOrder(
            tableNumber: "T01", lines: lines, globalDiscountType: scenario.globalDiscountType.flatMap { DiscountType(rawValue: $0) },
            globalDiscountValue: Decimal(string: scenario.globalDiscountValue)!, destination: try #require(OrderDestination(rawValue: scenario.destination))
        )
    }

    @Test func everyScenarioMatchesTheBackend() throws {
        let scenarios = try Self.scenarios()
        #expect(scenarios.count >= 9)
        for scenario in scenarios {
            let order = try Self.order(scenario)
            var priorFiscalTtc = 0
            for (index, payment) in scenario.payments.enumerated() {
                let paid = payment.tenders.reduce(0) { $0 + $1.amountCents }
                let amounts = FiscalReceiptMath.amounts(order: order, totalPaidCents: paid, tipCents: scenario.tipCents, priorFiscalTtcCents: priorFiscalTtc)
                #expect(amounts.ttcCents == payment.expected.totalTtcCents, "\(scenario.name)[\(index)] TTC")
                #expect(amounts.htCents == payment.expected.totalHtCents, "\(scenario.name)[\(index)] HT")
                #expect(amounts.taxBreakdownJson == payment.expected.taxBreakdownJson, "\(scenario.name)[\(index)] TVA")
                priorFiscalTtc += amounts.ttcCents
            }
            if let voided = scenario.voided {
                let original = scenario.payments[voided.paymentIndex].expected
                #expect(-original.totalTtcCents == voided.totalTtcCents && -original.totalHtCents == voided.totalHtCents, "\(scenario.name) annulation")
                #expect(FiscalReceiptMath.negatedTaxJson(original.taxBreakdownJson) == voided.taxBreakdownJson, "\(scenario.name) annulation TVA")
            }
        }
    }

    @Test func negatingAnEmptyOrZeroBreakdownIsStable() {
        #expect(FiscalReceiptMath.negatedTaxJson("{}") == "{}")
        #expect(FiscalReceiptMath.negatedTaxJson("{\"10\":0}") == "{\"10\":0}")
        #expect(FiscalReceiptMath.negatedTaxJson("{\"10\":364,\"5.5\":88}") == "{\"10\":-364,\"5.5\":-88}")
    }
}
