import XCTest

/// Spec 023, lot C : réception, perte et historique au pavé numérique (`-UITestMode`, puis mode autonome).
final class StockMovementsUITests: PosUITestCase {
    private func fill(_ id: String, _ text: String) {
        tap(id)
        element(id).typeText(text)
    }

    private func createIngredient(_ name: String) {
        tap("nav.admin")
        tap("admin.stock")
        tap("stock.add")
        fill("ingredient.name", name)
        fill("ingredient.price", "10")
        tap("ingredient.save")
        assertExists("stock.\(name)")
    }

    private func operate(_ name: String, _ item: String, digits: String) {
        tap("stock.menu.\(name)")
        let entry = app.buttons[item].firstMatch
        XCTAssertTrue(entry.waitForExistence(timeout: 5), "entrée de menu « \(item) » introuvable")
        entry.tap()
        for digit in digits { tap("stockop.key.\(digit)") }
        tap("stockop.save")
    }

    private func runScenario() {
        createIngredient("Farine")
        operate("Farine", "Réception", digits: "100")
        waitLabel("stock.level.Farine", contains: "100")
        operate("Farine", "Perte", digits: "20")
        waitLabel("stock.level.Farine", contains: "80")

        tap("stock.menu.Farine")
        app.buttons["Historique"].firstMatch.tap()
        assertExists("stock.movement.receipt")
        assertExists("stock.movement.loss")
    }

    func testReceiptLossAndHistory() {
        launch()
        runScenario()
    }

    func testStandaloneModeKeepsTheSameFlow() {
        launchStandalone()
        runScenario()
    }
}
