import XCTest

/// Spec 023 : filtre d'allergènes et ruptures sur l'écran de vente (`-UITestMode`).
final class AllergensAvailabilityUITests: PosUITestCase {

    func testAllergenFilterHidesMatchingAndUndeclaredDishes() {
        launch()
        tap("nav.order")
        assertExists("product.Burger Gourmet Rossini")
        assertExists("product.Pizza Margherita AOP")

        tap("allergen.filter.open")
        tap("product.allergens.gluten")
        tap("allergen.filter.done")

        // Le burger contient du gluten ; le tiramisu n'est pas renseigné : tous deux sont masqués par prudence.
        XCTAssertFalse(element("product.Burger Gourmet Rossini").waitForExistence(timeout: 2))
        XCTAssertFalse(element("product.Tiramisu Maison").exists)
        assertExists("product.Pizza Margherita AOP")
    }

    func testStockOutModeTogglesAvailabilityWithToast() {
        launch()
        tap("nav.order")
        tap("stockout.mode")
        tap("product.Pizza Margherita AOP")
        waitToast(containing: "en rupture")
        tap("product.Pizza Margherita AOP")
        waitToast(containing: "de nouveau disponible")
    }
}
