import XCTest

/// Spec 024, lot A : ouverture de caisse, sortie, comptage par coupures et écart conservé (`-UITestMode`, puis mode autonome).
final class CashUITests: PosUITestCase {
    private func keys(_ prefix: String, _ digits: String) {
        for digit in digits { tap("\(prefix).key.\(digit)") }
    }

    private func scenario() {
        tap("nav.cash")
        tap("cash.open")
        keys("cash", "100")
        tap("cash.save")
        waitLabel("cash.expected", contains: "100")

        tap("cash.out")
        keys("cash", "12")
        tap("cash.reason")
        element("cash.reason").typeText("Achat de glace")
        tap("cash.save")
        waitLabel("cash.expected", contains: "88")

        tap("cash.count")
        for denomination in [5_000, 2_000, 1_000, 500, 100] { tap("cash.plus.\(denomination)") }   // 50 + 20 + 10 + 5 + 1 = 86 €
        waitLabel("cash.countedTotal", contains: "86")
        tap("cash.countConfirm")
        waitToast(containing: "écart")
        assertExists("cash.open") // la session comptée libère le poste
    }

    func testOpenOutCountAndTheVarianceIsKept() {
        launch()
        scenario()
        assertExists("cash.history.-200") // 88 attendus, 86 comptés : écart de −2 €
    }

    func testStandaloneModeKeepsTheSameFlow() {
        launchStandalone()
        scenario()
        assertExists("cash.history.-200")
    }
}
