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
        struct Tender: Decodable { let amountCents: Int }
        struct Group: Decodable { let rate: String; let ttcCents: Int; let baseHtCents: Int; let vatCents: Int }
        struct OrderBlock: Decodable {
            let subtotalCents: Int
            let discountCents: Int
            let totalTtcCents: Int
            let totalHtCents: Int
            let totalVatCents: Int
            let groups: [Group]
        }
        struct Expected: Decodable {
            let totalTtcCents: Int
            let totalHtCents: Int
            let taxBreakdownJson: String
            let htBreakdownJson: String
        }
        struct Payment: Decodable { let tenders: [Tender]; let expected: Expected }
        struct Voided: Decodable {
            let paymentIndex: Int
            let totalTtcCents: Int
            let totalHtCents: Int
            let taxBreakdownJson: String
            let htBreakdownJson: String
        }
        struct Scenario: Decodable {
            let name: String
            let destination: Int
            let globalDiscountType: Int?
            let globalDiscountValue: String
            let tipCents: Int
            let lines: [Line]
            let order: OrderBlock
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

    /// Somme des valeurs du JSON de TVA (même expression régulière que `negatedTaxJson`).
    static func vatSum(_ json: String) -> Int {
        guard let regex = try? NSRegularExpression(pattern: "\"([^\"]+)\":(-?[0-9]+)") else { return 0 }
        return regex.matches(in: json, range: NSRange(json.startIndex..., in: json)).reduce(0) { sum, match in
            guard let range = Range(match.range(at: 2), in: json), let value = Int(json[range]) else { return sum }
            return sum + value
        }
    }

    @Test func everyOrderTotalsMatchTheBackend() throws {
        for scenario in try Self.scenarios() {
            let order = try Self.order(scenario)
            let totals = OrderMath.totals(lines: order.lines.map(CartLine.init(serverLine:)), destination: order.destination, discount: order.globalDiscount)
            let expected = scenario.order
            #expect(totals.subtotalTtc.cents == expected.subtotalCents, "\(scenario.name) sous-total")
            #expect(totals.discountAmount.cents == expected.discountCents, "\(scenario.name) remise")
            #expect(totals.totalTtc.cents == expected.totalTtcCents, "\(scenario.name) total TTC")
            #expect(totals.totalHt.cents == expected.totalHtCents, "\(scenario.name) HT")
            #expect(totals.totalVat.cents == expected.totalVatCents, "\(scenario.name) TVA")
            #expect(totals.vatLines.count == expected.groups.count, "\(scenario.name) nombre de taux")
            for (line, group) in zip(totals.vatLines, expected.groups) {
                #expect("\(line.ratePercent)" == group.rate, "\(scenario.name) taux")
                #expect(line.ttc.cents == group.ttcCents && line.baseHt.cents == group.baseHtCents && line.vat.cents == group.vatCents, "\(scenario.name) taux \(group.rate)")
            }
        }
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
                #expect(amounts.ttcCents - amounts.htCents == Self.vatSum(amounts.taxBreakdownJson), "\(scenario.name)[\(index)] TTC - HT = TVA")
                #expect(amounts.htBreakdownJson == payment.expected.htBreakdownJson, "\(scenario.name)[\(index)] HT par taux")
                #expect(Self.vatSum(payment.expected.htBreakdownJson) == amounts.htCents, "\(scenario.name)[\(index)] Σ HT par taux = HT")
                priorFiscalTtc += amounts.ttcCents
            }
            if let voided = scenario.voided {
                let original = scenario.payments[voided.paymentIndex].expected
                #expect(-original.totalTtcCents == voided.totalTtcCents && -original.totalHtCents == voided.totalHtCents, "\(scenario.name) annulation")
                #expect(FiscalReceiptMath.negatedTaxJson(original.taxBreakdownJson) == voided.taxBreakdownJson, "\(scenario.name) annulation TVA")
                #expect(FiscalReceiptMath.negatedTaxJson(original.htBreakdownJson) == voided.htBreakdownJson, "\(scenario.name) annulation HT par taux")
            }
        }
    }

    @Test func negatingAnEmptyOrZeroBreakdownIsStable() {
        #expect(FiscalReceiptMath.negatedTaxJson("{}") == "{}")
        #expect(FiscalReceiptMath.negatedTaxJson("{\"10\":0}") == "{\"10\":0}")
        #expect(FiscalReceiptMath.negatedTaxJson("{\"10\":364,\"5.5\":88}") == "{\"10\":-364,\"5.5\":-88}")
    }
}
