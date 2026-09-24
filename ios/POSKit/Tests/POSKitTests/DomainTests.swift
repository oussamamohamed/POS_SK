import Foundation
import Testing
@testable import POSKit

@Suite("Montants")
struct MoneyTests {
    @Test func roundsHalfAwayFromZero() {
        #expect(Money(euros: Decimal(string: "12.345")!).cents == 1235)
        #expect(Money(euros: Decimal(string: "-12.345")!).cents == -1235)
        #expect(Money(euros: Decimal(string: "0.1")! + Decimal(string: "0.2")!).cents == 30)
        #expect(Money(euros: 19.99).cents == 1999)
    }

    @Test func formatsInFrench() {
        #expect(Money(cents: 1250).formatted == "12,50\u{00A0}€")
        #expect(Money(cents: 5).formatted == "0,05\u{00A0}€")
        #expect(Money(cents: 123_456_789).formatted == "1\u{202F}234\u{202F}567,89\u{00A0}€")
        #expect(Money(cents: -750).formatted == "-7,50\u{00A0}€")
    }

    @Test func decodesDecimalNumbersObjectsAndStrings() throws {
        let decoder = POSJSON.decoder()
        #expect(try decoder.decode(Money.self, from: Data("48.5".utf8)).cents == 4850)
        #expect(try decoder.decode(Money.self, from: Data("0".utf8)).cents == 0)
        #expect(try decoder.decode(Money.self, from: Data(#"{"amountInCents":1250,"currency":"EUR"}"#.utf8)).cents == 1250)
        #expect(try decoder.decode(Money.self, from: Data(#""16.17""#.utf8)).cents == 1617)
    }

    @Test func encodesAsEuroDecimal() throws {
        let data = try POSJSON.encoder().encode(["amount": Money(cents: 1617)])
        #expect(String(decoding: data, as: UTF8.self) == #"{"amount":16.17}"#)
    }

    @Test func appliesPercentageDiscount() {
        #expect(Money(cents: 1000).applyingDiscount(percent: 15).cents == 850)
        #expect(Money(cents: 999).applyingDiscount(percent: 50).cents == 500)
        #expect(Money(cents: 999).applyingDiscount(percent: 0).cents == 999)
    }
}

@Suite("Dates .NET")
struct DotNetDateTests {
    @Test func parsesSevenFractionDigitsWithOffset() throws {
        let date = try #require(DotNetDate.parse("2026-09-24T10:41:21.5056374+00:00"))
        #expect(abs(date.timeIntervalSince1970 - 1_790_246_481.5056374) < 0.001)
    }

    @Test func parsesZuluOffsetsAndMissingZone() throws {
        let zulu = try #require(DotNetDate.parse("2026-09-24T10:00:00Z"))
        let paris = try #require(DotNetDate.parse("2026-09-24T12:00:00+02:00"))
        let naive = try #require(DotNetDate.parse("2026-09-24T10:00:00"))
        #expect(zulu == paris)
        #expect(zulu == naive)
    }

    @Test func parsesDotNetMinValue() {
        #expect(DotNetDate.parse("0001-01-01T00:00:00+00:00") != nil)
    }

    @Test func rejectsGarbage() {
        #expect(DotNetDate.parse("hier") == nil)
        #expect(DotNetDate.parse("2026-09-24T10:00:00+02:00 extra") == nil)
    }

    @Test func roundTripsThroughFormat() throws {
        let date = Date(timeIntervalSince1970: 1_790_246_481.250)
        let text = DotNetDate.format(date)
        #expect(text == "2026-09-24T10:41:21.250Z")
        #expect(DotNetDate.parse(text) == date)
    }
}

@Suite("Calculs TVA, partage et saisie")
struct CalculationTests {
    @Test func taxTotalsMatchTheServerRoundingPerLine() {
        // Valeurs réelles renvoyées par l'API : 2 burgers (22 € avec suppléments, TVA 10 %) + 1 boisson à 20 %.
        let totals = TaxCalculator.totals([
            (ttc: Money(cents: 4400), ratePercent: 10),
            (ttc: Money(cents: 450), ratePercent: 20),
        ])
        #expect(totals.ttc.cents == 4850)
        #expect(totals.ht.cents == 4375)
        #expect(totals.vat.cents == 475)
        #expect(totals.vatByRate[10]?.cents == 400)
        #expect(totals.vatByRate[20]?.cents == 75)
    }

    @Test func xReportHtMatchesServer() {
        // Rapport X capturé : 61,00 € TTC → 55,11 € HT.
        let totals = TaxCalculator.totals([
            (ttc: Money(cents: 4400), ratePercent: 10),
            (ttc: Money(cents: 450), ratePercent: 20),
            (ttc: Money(cents: 1250), ratePercent: 10),
        ])
        #expect(totals.ht.cents == 5511)
    }

    @Test func rateLabels() {
        #expect(TaxCalculator.rateLabel("10.0") == "10 %")
        #expect(TaxCalculator.rateLabel("5.5") == "5,5 %")
        #expect(TaxCalculator.rateLabel(Decimal(20)) == "20 %")
    }

    @Test(arguments: [(4850, 3), (1000, 3), (1, 2), (99_999, 7), (4850, 1)])
    func equalSplitIsExactToTheCent(totalCents: Int, guests: Int) {
        let parts = SplitCalculator.equalParts(of: Money(cents: Int64(totalCents)), count: guests)
        #expect(parts.count == guests)
        #expect(parts.reduce(0) { $0 + $1.cents } == Int64(totalCents))
        let spread = (parts.map(\.cents).max() ?? 0) - (parts.map(\.cents).min() ?? 0)
        #expect(spread <= 1)
        #expect(parts == parts.sorted(by: >))
    }

    @Test func splitOfNothingIsEmpty() {
        #expect(SplitCalculator.equalParts(of: .zero, count: 3).isEmpty)
        #expect(SplitCalculator.equalParts(of: Money(cents: 100), count: 0).isEmpty)
    }

    @Test func cashSuggestionsStartWithExactAmount() {
        #expect(CashSuggestions.amounts(for: Money(cents: 3233)).map(\.cents) == [3233, 3500, 4000, 5000])
        #expect(CashSuggestions.amounts(for: Money(cents: 2000)).map(\.cents) == [2000, 5000, 10000])
        #expect(CashSuggestions.amounts(for: .zero).isEmpty)
    }

    @Test func amountEntryTypesFromTheRight() {
        var entry = AmountEntry()
        entry.append(0)
        #expect(entry.isEmpty)
        [2, 0].forEach { entry.append($0) }
        entry.appendDoubleZero()
        #expect(entry.amount.cents == 2000)
        entry.backspace()
        #expect(entry.amount.cents == 200)
        entry.set(Money(cents: 1617))
        #expect(entry.digits == "1617")
        entry.clear()
        #expect(entry.amount == .zero)
    }

    @Test func amountEntryIsBounded() {
        var entry = AmountEntry(maxDigits: 3)
        [9, 9, 9, 9].forEach { entry.append($0) }
        #expect(entry.amount.cents == 999)
    }

    @Test func pinEntryAutoSubmitsAtConfiguredLength() {
        var pin = PinEntry()
        #expect(pin.append(1) == false)
        #expect(pin.append(2) == false)
        #expect(pin.append(3) == false)
        #expect(pin.append(4) == true)
        #expect(pin.code == "1234")

        var longPin = PinEntry()
        let results = [1, 2, 3, 4, 5, 6, 7].map { longPin.append($0, autoSubmitLength: 6) }
        #expect(results == [false, false, false, false, false, true, false])
        #expect(longPin.code == "123456")
        longPin.backspace()
        #expect(longPin.isComplete)
    }
}

@Suite("Modificateurs")
struct ModifierSelectionTests {
    let burger = InMemoryPOSBackend.defaultProducts.first { $0.name.hasPrefix("Burger") }!
    var cuisson: ModifierGroup { burger.modifierGroups[0] }
    var supplements: ModifierGroup { burger.modifierGroups[1] }

    @Test func preselectsDefaults() {
        let selection = ModifierSelection(product: burger)
        #expect(selection.selectedOptions.map(\.name) == ["À point"])
        #expect(selection.isValid)
    }

    @Test func singleChoiceReplacesAndCannotBeEmptiedWhenMandatory() {
        var selection = ModifierSelection(product: burger)
        selection.toggle(cuisson.options[1], in: cuisson)
        #expect(selection.selectedOptions.map(\.name) == ["Saignant"])
        selection.toggle(cuisson.options[1], in: cuisson)
        #expect(selection.selectedOptions.map(\.name) == ["Saignant"])
    }

    @Test func multiChoiceRespectsMaximumAndAddsPrice() {
        var selection = ModifierSelection(product: burger)
        let accepted = supplements.options.map { selection.toggle($0, in: supplements) }
        #expect(accepted == [true, true, true])
        #expect(selection.extraPrice.cents == 370)
        #expect(selection.unitTotal.cents == 2320)
        selection.toggle(supplements.options[1], in: supplements)
        #expect(selection.extraPrice.cents == 270)
    }

    @Test func maximumIsEnforced() {
        let group = ModifierGroup(groupName: "Sauces", maxSelections: 1, isSingleChoice: false, options: [
            ModifierOption(name: "A"), ModifierOption(name: "B"),
        ])
        var selection = ModifierSelection(product: Product(name: "Frites", categoryId: "X", price: Money(cents: 400), modifierGroups: [group]))
        let first = selection.toggle(group.options[0], in: group)
        let second = selection.toggle(group.options[1], in: group)
        #expect(first)
        #expect(!second)
        #expect(selection.selectedOptions.map(\.name) == ["A"])
    }

    @Test func mandatoryGroupWithoutDefaultIsInvalid() {
        let group = ModifierGroup(groupName: "Cuisson", minSelections: 1, isMandatory: true, options: [ModifierOption(name: "Saignant")])
        var selection = ModifierSelection(product: Product(name: "Steak", categoryId: "X", price: Money(cents: 1500), modifierGroups: [group]))
        #expect(!selection.isValid)
        #expect(selection.missingGroups.map(\.groupName) == ["Cuisson"])
        selection.toggle(group.options[0], in: group)
        #expect(selection.isValid)
    }

    @Test func pendingLineBuildsServerPayload() {
        let bacon = supplements.options[0]
        let line = PendingLine(product: burger, quantity: 2, modifiers: [cuisson.options[1], bacon], note: "  sans oignons ", course: .suite)
        let input = line.input
        #expect(input.unitPrice.cents == 1950)
        #expect(input.modifiersPriceExtra.cents == 150)
        #expect(input.modifiers == ["Saignant", "Bacon", "Note : sans oignons"])
        #expect(input.course == .suite)
        #expect(line.lineTotal.cents == 4200)
    }
}
