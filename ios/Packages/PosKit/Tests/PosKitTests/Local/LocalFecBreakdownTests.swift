import Foundation
import Testing
@testable import PosKit

@Suite("FEC : ventilation HT / TVA (miroir de ParseVatBreakdown)")
struct LocalFecBreakdownTests {
    private func decimal(_ text: String) -> Decimal { Decimal(string: text, locale: Locale(identifier: "en_US_POSIX"))! }
    private let twoLowRatesVat = "{\"2.1\": 21, \"5.5\": 52}"

    @Test func feasibleHtGivesTheRangeConsistentWithTheVat() {
        #expect(LocalFecBreakdown.feasibleHt(rate: 10, vatCents: 100) == FecHtRange(lo: 995, hi: 1005, est: 1000))
        #expect(LocalFecBreakdown.feasibleHt(rate: 20, vatCents: 200) == FecHtRange(lo: 998, hi: 1003, est: 1000))
        #expect(LocalFecBreakdown.feasibleHt(rate: decimal("5.5"), vatCents: 52).est == 945)
    }

    @Test func coherentHtBreakdownIsTakenAsIs() {
        let items = LocalFecBreakdown.items(ttcCents: 2000, htCents: 1927, taxBreakdownJson: twoLowRatesVat, htBreakdownJson: "{\"2.1\":979,\"5.5\":948}")
        #expect(items == [FecVatItem(rate: decimal("2.1"), htCents: 979, vatCents: 21), FecVatItem(rate: decimal("5.5"), htCents: 948, vatCents: 52)])
    }

    @Test func voidHtBreakdownGivesAbsoluteValues() {
        let items = LocalFecBreakdown.items(ttcCents: -2000, htCents: -1927, taxBreakdownJson: "{\"2.1\": -21, \"5.5\": -52}", htBreakdownJson: "{\"2.1\":-979,\"5.5\":-948}")
        #expect(items == [FecVatItem(rate: decimal("2.1"), htCents: 979, vatCents: 21), FecVatItem(rate: decimal("5.5"), htCents: 948, vatCents: 52)])
    }

    @Test func zeroRateGroupKeepsItsHt() {
        let exact = LocalFecBreakdown.items(ttcCents: 1700, htCents: 1500, taxBreakdownJson: "{\"0\": 0, \"20\": 200}", htBreakdownJson: "{\"0\":500,\"20\":1000}")
        let deduced = LocalFecBreakdown.items(ttcCents: 1700, htCents: 1500, taxBreakdownJson: "{\"0\": 0, \"20\": 200}", htBreakdownJson: nil)
        let expected = [FecVatItem(rate: 0, htCents: 500, vatCents: 0), FecVatItem(rate: 20, htCents: 1000, vatCents: 200)]
        #expect(exact == expected)
        #expect(deduced == expected)
    }

    @Test func legacyReceiptDeducesHtAndTheLowestRateAbsorbsTheRest() {
        let items = LocalFecBreakdown.items(ttcCents: 2000, htCents: 1927, taxBreakdownJson: twoLowRatesVat, htBreakdownJson: nil)
        #expect(items == [FecVatItem(rate: decimal("2.1"), htCents: 982, vatCents: 21), FecVatItem(rate: decimal("5.5"), htCents: 945, vatCents: 52)])
    }

    @Test func reducedRateRoundingStaysOnTheAbsorber() {
        let items = LocalFecBreakdown.items(ttcCents: 2100, htCents: 1979, taxBreakdownJson: "{\"2.1\": 21, \"10\": 100}", htBreakdownJson: nil)
        #expect(items == [FecVatItem(rate: decimal("2.1"), htCents: 979, vatCents: 21), FecVatItem(rate: 10, htCents: 1000, vatCents: 100)])
    }

    @Test(arguments: [
        "pas du json",
        "{\"2.1\":1500,\"5.5\":427}", // somme conservée, ventilation hors plage faisable
        "{\"2.1\":979,\"5.5\":900}",  // somme fausse
        "{\"2.1\":979,\"10\":948}",   // clés différentes de la TVA signée
        "{}",
        "   ",
    ])
    func unusableHtBreakdownFallsBackToTheDeduction(htJson: String) {
        let items = LocalFecBreakdown.items(ttcCents: 2000, htCents: 1927, taxBreakdownJson: twoLowRatesVat, htBreakdownJson: htJson)
        #expect(items.map(\.htCents) == [982, 945])
    }

    @Test func negativeResidualIsTakenBackWithinTheFeasibleRanges() {
        // Reçu historique hors règle : estimations 1000 + 1000 > HT 1800 ; 10 % descend à 995, 20 % à 998, le 0 % absorbe −193.
        let items = LocalFecBreakdown.items(ttcCents: 2100, htCents: 1800, taxBreakdownJson: "{\"0\":0,\"10\":100,\"20\":200}", htBreakdownJson: nil)
        #expect(items == [FecVatItem(rate: 0, htCents: -193, vatCents: 0), FecVatItem(rate: 10, htCents: 995, vatCents: 100), FecVatItem(rate: 20, htCents: 998, vatCents: 200)])
    }

    @Test func duplicateRateKeysAreMergedInTheExactBreakdown() {
        // « 10 » et « 10.0 » sont le même taux decimal côté .NET : fusion, ordre de la première clé.
        let items = LocalFecBreakdown.items(ttcCents: 1100, htCents: 1000, taxBreakdownJson: "{\"10\":100}", htBreakdownJson: "{\"10\":600,\"10.0\":400}")
        #expect(items == [FecVatItem(rate: 10, htCents: 1000, vatCents: 100)])
    }

    @Test func emptyOrUnreadableVatFallsBackToASingleTenPercentLine() {
        for json in ["{}", "", "pas du json", "{\"abc\":100}"] {
            let items = LocalFecBreakdown.items(ttcCents: 1100, htCents: 1000, taxBreakdownJson: json, htBreakdownJson: nil)
            #expect(items == [FecVatItem(rate: 10, htCents: 1000, vatCents: 100)], "\(json)")
        }
    }
}
