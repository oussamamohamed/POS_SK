import XCTest

/// Spec 023, lot B : ingrédients et fiche technique (`-UITestMode`, puis mode autonome).
final class StockUITests: PosUITestCase {
    private func fill(_ id: String, _ text: String) {
        tap(id)
        element(id).typeText(text)
    }

    /// La fiche est sous les allergènes dans l'éditeur : on fait défiler le formulaire jusqu'à l'élément.
    private func reveal(_ id: String) {
        for _ in 0..<6 where !element(id).exists || !element(id).isHittable { app.swipeUp() }
    }

    private func addIngredient(_ name: String, price: String) {
        tap("stock.add")
        fill("ingredient.name", name)
        fill("ingredient.price", price)
        tap("ingredient.save")
        assertExists("stock.\(name)")
    }

    func testCreateIngredientsAndRecipeShowsLiveCost() {
        launch()
        tap("nav.admin")
        tap("admin.stock")
        addIngredient("Farine", price: "12.5")   // 1,25 c/g
        addIngredient("Tomate", price: "3")      // 0,30 c/g

        tap("admin.catalog")
        tap("catalog.product.Burger Gourmet Rossini")
        for (name, qty) in [("Farine", "150"), ("Tomate", "100")] {
            reveal("recipe.pick")
            tap("recipe.pick")
            app.buttons[name].firstMatch.tap()
            reveal("recipe.qty")
            fill("recipe.qty", qty)
            reveal("recipe.add")
            tap("recipe.add")
        }
        // 187,5 + 30 = 217,5 centimes : 2,18 € (demi supérieur).
        reveal("recipe.cost")
        let cost = element("recipe.cost")
        XCTAssertTrue(cost.waitForExistence(timeout: 5))
        XCTAssertTrue(cost.label.contains("2,18"), "coût affiché : \(cost.label)")
        tap("product.save")
    }

    func testStandaloneModeCreatesAnIngredient() {
        launchStandalone()
        tap("nav.admin")
        tap("admin.stock")
        addIngredient("Beurre", price: "9")
    }

    func testVoidReceiptWithTheRestockCheckbox() {
        launch()
        tap("nav.takings")
        tap("takings.void.T01-000003")
        tap("void.restock")
        tap("void.confirm")
        waitToast(containing: "T01-000003")
        // Le reçu est annulé : plus de bouton « Annuler » sur sa ligne, et un avoir apparaît.
        assertNotExists("takings.void.T01-000003")
        assertNotExists("void.confirm")
    }

    func testModifierOptionConsumptionIsSavedWithTheProduct() {
        launch()
        tap("nav.admin")
        tap("admin.stock")
        tap("stock.add")
        fill("ingredient.name", "Cheddar")
        fill("ingredient.price", "20")
        tap("ingredient.save")
        assertExists("stock.Cheddar")

        tap("admin.catalog")
        tap("catalog.product.Burger Gourmet Rossini")
        reveal("option.pick.Double Cheddar Fondu")
        tap("option.pick.Double Cheddar Fondu")
        app.buttons["Cheddar"].firstMatch.tap()
        fill("option.qty.Double Cheddar Fondu", "30")
        tap("option.add.Double Cheddar Fondu")
        let added = app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS '+30'")).firstMatch
        XCTAssertTrue(added.exists, "la ligne +30 doit apparaître avant l'enregistrement")
        tap("product.save")
        assertNotExists("product.save", timeout: 5)

        // Rouvrir le produit : l'ajustement est relu depuis le serveur.
        Thread.sleep(forTimeInterval: 1) // laisse la feuille précédente finir de se fermer
        tap("catalog.product.Burger Gourmet Rossini")
        assertExists("product.save", timeout: 10)
        reveal("option.pick.Double Cheddar Fondu")
        let saved = app.descendants(matching: .any).matching(NSPredicate(format: "label CONTAINS '+30'")).firstMatch
        XCTAssertTrue(saved.waitForExistence(timeout: 5), "l'ajustement +30 doit être relu")
    }
}
