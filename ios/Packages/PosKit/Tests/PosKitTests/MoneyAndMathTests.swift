import Foundation
import Testing
@testable import PosKit

@Suite("Money")
struct MoneyTests {
    @Test func roundsHalfAwayFromZeroLikeDotNet() {
        #expect(Money(euros: Decimal(string: "0.125")!).cents == 13)
        #expect(Money(euros: Decimal(string: "19.5")!).cents == 1950)
        #expect(Money(euros: Decimal(string: "-0.125")!).cents == -13)
    }

    @Test func formatsInFrench() {
        let text = Money(cents: 1950).formatted
        #expect(text.contains("19,50"))
        #expect(text.contains("€"))
    }

    @Test(arguments: [("12,5", 1250), ("12.50", 1250), ("7", 700), (" 3,99 € ", 399)])
    func parsesUserInput(input: String, cents: Int) {
        #expect(Money.parse(input)?.cents == cents)
    }

    @Test func rejectsInvalidInput() {
        #expect(Money.parse("") == nil)
        #expect(Money.parse("abc") == nil)
    }

    @Test func decodesEurosFromJSONNumbers() throws {
        let values = try JSONDecoder().decode([Money].self, from: Data("[19.5, 0.1, 6, 25.1]".utf8))
        #expect(values.map(\.cents) == [1950, 10, 600, 2510])
    }
}

@Suite("OrderMath — miroir des règles .NET")
struct OrderMathTests {
    func line(_ cents: Int, qty: Int = 1, vat: Decimal = 10, takeaway: Decimal? = nil, extra: Int = 0, comp: Bool = false, discount: Decimal = 0) -> CartLine {
        CartLine(productId: UUID(), name: "Article", unitPrice: Money(cents: cents), taxRatePercent: vat, taxRateTakeawayPercent: takeaway, modifiersExtra: Money(cents: extra), quantity: qty, isComp: comp, discountPercent: discount)
    }

    @Test func lineTotalIncludesOptionsAndQuantity() {
        #expect(OrderMath.lineTotal(line(1950, qty: 2, extra: 250)) == Money(cents: 4400))
    }

    @Test func compedLineIsFree() {
        #expect(OrderMath.lineTotal(line(1950, comp: true)) == .zero)
    }

    @Test func lineDiscountRoundsToCent() {
        // 3 × 3,33 € = 9,99 € ; -15 % = 8,4915 → 8,49 €
        #expect(OrderMath.lineTotal(line(333, qty: 3, discount: 15)) == Money(cents: 849))
    }

    @Test func vatSplitMatchesServer() {
        // Serveur : burger 2 × 19,50 € à 10 % → HT 35,45 € / TVA 3,55 €
        let parts = OrderMath.split(ttc: Money(cents: 3900), ratePercent: 10)
        #expect(parts.ht == Money(cents: 3545))
        #expect(parts.vat == Money(cents: 355))
    }

    @Test func totalsGroupByVatRate() {
        let totals = OrderMath.totals(lines: [line(1950, qty: 2), line(600, vat: 20)], destination: .eatIn, discount: nil)
        #expect(totals.totalTtc == Money(cents: 4500))
        #expect(totals.vatLines.count == 2)
        #expect(totals.totalHt + totals.totalVat == totals.totalTtc)
        #expect(totals.vatLines.first { $0.ratePercent == 20 }?.vat == Money(cents: 100))
    }

    @Test func takeawayUsesReducedVat() {
        let pizza = line(1250, vat: 10, takeaway: 5.5)
        let eatIn = OrderMath.totals(lines: [pizza], destination: .eatIn, discount: nil)
        let takeaway = OrderMath.totals(lines: [pizza], destination: .takeaway, discount: nil)
        #expect(eatIn.totalTtc == takeaway.totalTtc)
        #expect(eatIn.vatLines.first?.ratePercent == 10)
        #expect(takeaway.vatLines.first?.ratePercent == 5.5)
        #expect(takeaway.totalVat < eatIn.totalVat)
    }

    @Test func percentageDiscountMatchesServerTotal() {
        // Fixture serveur : 39,00 € avec -10 % → 35,10 €
        let totals = OrderMath.totals(lines: [line(1950, qty: 2)], destination: .eatIn, discount: GlobalDiscount(type: .percentage, value: 10))
        #expect(totals.totalTtc == Money(cents: 3510))
        #expect(totals.discountAmount == Money(cents: 390))
    }

    @Test func fixedDiscountIsProratedAcrossVatRatesWithoutLosingCents() {
        let totals = OrderMath.totals(lines: [line(1000), line(1000, vat: 20)], destination: .eatIn, discount: GlobalDiscount(type: .fixedAmount, value: Decimal(string: "3.33")!))
        #expect(totals.totalTtc == Money(cents: 1667))
        #expect(totals.vatLines.reduce(Money.zero) { $0 + $1.ttc } == totals.totalTtc)
    }

    @Test func discountNeverGoesNegative() {
        let totals = OrderMath.totals(lines: [line(500)], destination: .eatIn, discount: GlobalDiscount(type: .fixedAmount, value: 50))
        #expect(totals.totalTtc == .zero)
    }

    @Test(arguments: [(10000, 3), (1, 2), (4501, 4), (9999, 7)])
    func equalSplitHasZeroCentGap(totalCents: Int, parts: Int) {
        let split = OrderMath.splitEqually(Money(cents: totalCents), parts: parts)
        #expect(split.count == parts)
        #expect(split.reduce(Money.zero, +).cents == totalCents)
        #expect((split.map(\.cents).max()! - split.map(\.cents).min()!) <= 1)
    }

    @Test func splitGivesRemainderToFirstGuests() {
        #expect(OrderMath.splitEqually(Money(cents: 10000), parts: 3).map(\.cents) == [3334, 3333, 3333])
    }

    @Test func tipAndChange() {
        #expect(OrderMath.tip(on: Money(cents: 4550), percent: 10) == Money(cents: 455))
        #expect(OrderMath.change(tendered: Money(cents: 5000), due: Money(cents: 2510)) == Money(cents: 2490))
        #expect(OrderMath.change(tendered: Money(cents: 1000), due: Money(cents: 2510)) == .zero)
    }

    @Test func suggestsCashAmounts() {
        let suggestions = OrderMath.suggestedCashAmounts(for: Money(cents: 1740))
        #expect(suggestions.first == Money(cents: 1740))
        #expect(suggestions.contains(Money(cents: 1800)))
        #expect(suggestions.contains(Money(cents: 2000)))
        #expect(suggestions.allSatisfy { $0 >= Money(cents: 1740) })
    }
}

@Suite("PaymentPlan")
struct PaymentPlanTests {
    @Test func tipIsAddedToTotal() {
        var plan = PaymentPlan(due: Money(cents: 4000))
        plan.tip = .percent(10)
        #expect(plan.amountToCollect == Money(cents: 4400))
        plan.tip = .custom(Money(cents: 250))
        #expect(plan.amountToCollect == Money(cents: 4250))
    }

    @Test func splitCollectsPartsInOrder() {
        var plan = PaymentPlan(due: Money(cents: 10000))
        plan.setGuests(3)
        plan.tip = .percent(15) // ignoré en partage
        #expect(plan.tipAmount == .zero)
        #expect(plan.amountToCollect == Money(cents: 3334))
        #expect(plan.partLabel == "Part 1/3")
        plan.markPartPaid()
        #expect(plan.amountToCollect == Money(cents: 3333))
        #expect(plan.remainingParts == 2)
    }

    @Test func guestsAreClamped() {
        var plan = PaymentPlan(due: Money(cents: 1000))
        plan.setGuests(1)
        #expect(plan.splitGuests == 2)
        plan.setGuests(99)
        #expect(plan.splitGuests == 12)
        plan.setGuests(nil)
        #expect(plan.isSplit == false)
    }
}

@Suite("ModifierSelection")
struct ModifierSelectionTests {
    let cooking = ModifierGroup(groupName: "Cuisson", minSelections: 1, maxSelections: 1, isMandatory: true, isSingleChoice: true, options: [
        ModifierOption(name: "Saignant"), ModifierOption(name: "À Point", isDefault: true),
    ])
    let extras = ModifierGroup(groupName: "Suppléments", maxSelections: 2, options: [
        ModifierOption(name: "Cheddar", extraPrice: Money(cents: 250)),
        ModifierOption(name: "Bacon", extraPrice: Money(cents: 200)),
        ModifierOption(name: "Œuf", extraPrice: Money(cents: 150)),
    ])
    let sauce = ModifierGroup(groupName: "Sauce", isSingleChoice: true, options: [ModifierOption(name: "Poivre"), ModifierOption(name: "Béarnaise")])

    var product: Product { Product(name: "Burger", categoryId: "C", price: Money(cents: 1950), modifierGroups: [cooking, extras, sauce]) }

    @Test func preselectsDefaults() {
        let selection = ModifierSelection(product: product)
        #expect(selection.modifierLabels == ["À Point"])
    }

    @Test func singleChoiceReplacesAndMandatoryCannotBeCleared() throws {
        var selection = ModifierSelection(product: product)
        try selection.toggle(cooking.options[0], in: cooking)
        #expect(selection.modifierLabels == ["Saignant"])
        try selection.toggle(cooking.options[0], in: cooking)
        #expect(selection.modifierLabels == ["Saignant"])
    }

    @Test func optionalSingleChoiceCanBeCleared() throws {
        var selection = ModifierSelection(product: product)
        try selection.toggle(sauce.options[1], in: sauce)
        try selection.toggle(sauce.options[1], in: sauce)
        #expect(!selection.isSelected(sauce.options[1]))
    }

    @Test func multiChoiceRespectsMaximum() throws {
        var selection = ModifierSelection(product: product)
        try selection.toggle(extras.options[0], in: extras)
        try selection.toggle(extras.options[1], in: extras)
        #expect(throws: ModifierSelection.ToggleError.maximumReached(group: "Suppléments", max: 2)) {
            try selection.toggle(extras.options[2], in: extras)
        }
        #expect(selection.extraTotal == Money(cents: 450))
        #expect(selection.effectiveUnitPrice(base: product.price) == Money(cents: 2400))
        #expect(selection.modifierLabels.contains { $0.hasPrefix("Cheddar (+2,50") })
    }

    @Test func validationRequiresMandatoryGroups() throws {
        let noDefault = ModifierGroup(groupName: "Cuisson", minSelections: 1, maxSelections: 1, isMandatory: true, isSingleChoice: true, options: [ModifierOption(name: "Bleu")])
        let selection = ModifierSelection(product: Product(name: "Steak", categoryId: "C", price: Money(cents: 2000), modifierGroups: [noDefault]))
        #expect(throws: ModifierSelection.ValidationError.missingSelection(group: "Cuisson", minimum: 1)) {
            try selection.validate()
        }
    }
}
