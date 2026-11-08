import Foundation
import Testing
@testable import PosKit

/// Rejoue `cash_reference.json`, généré par le .NET (`WRITE_CASH_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter CashReferenceFixtureTests`).
@Suite struct CashMathTests {
    struct Tender: Decodable { let method: String, amount: Int, tendered: Int }
    struct Receipt: Decodable { let credit: Bool, tenders: [Tender] }
    struct Movement: Decodable { let kind: String, amount: Int, reason: String }
    struct CashCase: Decodable {
        let name: String, openingFloat: Int, receipts: [Receipt], movements: [Movement], counted: Int, threshold: Int, note: String?
        let expectedCents: Int?, varianceCents: Int?, error: String?
    }
    struct Sale: Decodable { let `operator`: String, credit: Bool, totalCents: Int, discountType: String?, discountValue: Decimal, subtotalCents: Int, compCents: Int, lineDiscountPercent: Decimal? }
    struct AlertCase: Decodable { let name: String, ruleType: String, threshold: Int, sales: [Sale], expectedAlerts: [String] }
    struct File: Decodable { let cashCases: [CashCase], alertCases: [AlertCase] }

    private func load() throws -> File {
        let url = try #require(Bundle.module.url(forResource: "cash_reference", withExtension: "json", subdirectory: "Fixtures"))
        let decoder = JSONDecoder()
        return try decoder.decode(File.self, from: Data(contentsOf: url))
    }

    @Test func cashScenariosGiveTheSameExpectedAndVariance() throws {
        for c in try load().cashCases {
            let cash = c.receipts.flatMap(\.tenders).filter { $0.method == "Cash" }.map(\.amount)
            let movements = c.movements.map { CashMovement(kind: $0.kind, amountCents: $0.amount, reason: $0.reason, operatorId: UUID()) }
            let expected = CashMath.expected(openingFloat: c.openingFloat, cashTenders: cash, movements: movements)
            let variance = CashMath.variance(counted: c.counted, expected: expected)
            if c.error != nil {
                // Le refus (note obligatoire) tient à l'écart et au seuil, pas au calcul : on vérifie qu'il dépasse bien le seuil sans note.
                #expect(c.note == nil && c.threshold > 0 && abs(variance) > c.threshold, "\(c.name) : refus attendu")
            } else {
                #expect(expected == c.expectedCents && variance == c.varianceCents, "\(c.name) : attendu \(expected) / écart \(variance)")
            }
        }
    }

    @Test func discountAndCompRulesMatchTheServer() throws {
        for c in try load().alertCases where c.ruleType == AlertRuleType.discountPercent || c.ruleType == AlertRuleType.compsTotal {
            var raised = Set<String>()
            var compsByOperator: [String: Int] = [:]
            for s in c.sales {
                let type = s.discountType.flatMap { ["Percentage": DiscountType.percentage, "FixedAmount": .fixedAmount, "Comp": .comp][$0] }
                if c.ruleType == AlertRuleType.discountPercent, AlertRules.discountPercent(type: type, value: s.discountValue, subtotalCents: s.subtotalCents, maxLinePercent: s.lineDiscountPercent ?? 0) > c.threshold { raised.insert(s.operator) }
                let cents = AlertRules.compCents(lines: [(unitPrice: s.subtotalCents, modifiersExtra: 0, quantity: 1, isComp: s.compCents > 0)], discountType: type)
                compsByOperator[s.operator, default: 0] += cents
            }
            if c.ruleType == AlertRuleType.compsTotal { raised = Set(compsByOperator.filter { $0.value > c.threshold }.keys) }
            let expectedOperators = Set(c.expectedAlerts.compactMap { $0.split(separator: "|").dropFirst().first.map(String.init) })
            #expect(raised == expectedOperators, "\(c.name) : \(raised) au lieu de \(expectedOperators)")
        }
    }

    @Test func denominationsAreSummedExactly() {
        #expect(CashMath.total([CashDenomination(valueCents: 10_000, quantity: 1), CashDenomination(valueCents: 2_000, quantity: 1), CashDenomination(valueCents: 5, quantity: 3)]) == 12_015)
    }
}
