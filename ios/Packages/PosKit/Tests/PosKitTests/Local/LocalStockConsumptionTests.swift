import Foundation
import Testing
@testable import PosKit

/// Rejoue côté iPad les cas de `StockServiceTests` (.NET) : consommation, sous-recettes, modificateurs, prix moyen, inventaire, restitution, rupture automatique.
@Suite("LocalPosAPI : stock et consommation")
struct LocalStockConsumptionTests {
    private func raw(_ api: LocalPosAPI, _ name: String, price: Int64 = 1000, low: Int64? = nil) async throws -> Ingredient {
        try await api.createIngredient(IngredientDraft(name: name, unitCostMilliCents: price, lowStockMilli: low))
    }

    private func sell(_ api: LocalPosAPI, _ product: Product, quantity: Int, table: String, modifiers: [String] = []) async throws {
        try await seatTable(api, table, items: [localInput(product, quantity: quantity, modifiers: modifiers)])
        let order = try #require(try await api.activeOrder(table: table))
        _ = try await api.pay(localPayment(order, table: table, tenders: [localTender(.cash, 1950 * quantity)]))
    }

    private func stock(_ api: LocalPosAPI, _ id: UUID) async throws -> Int64? {
        try await api.ingredients().first { $0.id == id }?.stockMilli
    }

    @Test func saleConsumesRecipeAndSubRecipes() async throws {
        let api = try await makeLocalAPI()
        let beef = try await raw(api, "Boeuf")
        let tomato = try await raw(api, "Tomate", price: 300)
        let sauce = try await api.createIngredient(IngredientDraft(name: "Sauce", isPreparation: true, yieldMilli: 500_000))
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        _ = try await api.setPreparationRecipe(id: sauce.id, lines: [RecipeLine(ingredientId: tomato.id, quantityMilli: 1_000_000)])
        _ = try await api.setRecipe(productId: burger.id, lines: [RecipeLine(ingredientId: beef.id, quantityMilli: 200_000), RecipeLine(ingredientId: sauce.id, quantityMilli: 100_000)])

        try await sell(api, burger, quantity: 3, table: "T1")

        #expect(try await stock(api, beef.id) == -600_000)
        #expect(try await stock(api, tomato.id) == -600_000) // 3 × 100 g de sauce = 3 × 200 g de tomate
    }

    @Test func saleWithoutRecipeWritesNothing() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let beef = try await raw(api, "Boeuf")

        try await sell(api, burger, quantity: 2, table: "T1")

        #expect(try await stock(api, beef.id) == 0)
        #expect(try await api.stockMovements(ingredientId: nil, from: nil, to: nil).isEmpty)
    }

    @Test func receiptsWeightTheAveragePriceAndNegativeStockIsTolerated() async throws {
        let api = try await makeLocalAPI()
        let flour = try await raw(api, "Farine")
        _ = try await api.receiveStock(ingredientId: flour.id, quantityMilli: 10_000_000, unitCostMilliCents: 1000, note: nil)
        _ = try await api.receiveStock(ingredientId: flour.id, quantityMilli: 10_000_000, unitCostMilliCents: 2000, note: nil)
        #expect(try await api.ingredients().first { $0.id == flour.id }?.unitCostMilliCents == 1500)

        _ = try await api.recordStockLoss(ingredientId: flour.id, quantityMilli: 25_000_000, note: "casse")
        #expect(try await stock(api, flour.id) == -5_000_000)
        await #expect(throws: RecipeError.ingredientInvalid) { _ = try await api.receiveStock(ingredientId: flour.id, quantityMilli: 0, unitCostMilliCents: 1, note: nil) }
    }

    @Test func countWritesTheValuedVariance() async throws {
        let api = try await makeLocalAPI()
        let oil = try await raw(api, "Huile", price: 2500)
        _ = try await api.receiveStock(ingredientId: oil.id, quantityMilli: 5_000_000, unitCostMilliCents: 2500, note: nil)

        let result = try await api.countStock(ingredientId: oil.id, countedMilli: 4_200_000, note: "inventaire")

        #expect(result == CountResult(varianceMilli: -800_000, valueCents: -2000))
        #expect(try await stock(api, oil.id) == 4_200_000)
        #expect(try await api.countStock(ingredientId: oil.id, countedMilli: 4_200_000, note: nil).varianceMilli == 0)
    }

    @Test func restockHappensOnlyWhenAsked() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let beef = try await raw(api, "Boeuf")
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        _ = try await api.setRecipe(productId: burger.id, lines: [RecipeLine(ingredientId: beef.id, quantityMilli: 200_000)])
        /// Dernier reçu de vente encore valable (ni avoir, ni déjà annulé).
        func liveReceipt() throws -> UUID {
            try #require(try db.query("SELECT Id FROM FiscalReceipts WHERE VoidedReceiptId IS NULL AND Id NOT IN (SELECT VoidedReceiptId FROM FiscalReceipts WHERE VoidedReceiptId IS NOT NULL) ORDER BY rowid DESC LIMIT 1").first?.uuid("Id"))
        }

        try await sell(api, burger, quantity: 1, table: "T1")
        _ = try await api.voidReceipt(receiptId: try liveReceipt())
        #expect(try await stock(api, beef.id) == -200_000)

        try await sell(api, burger, quantity: 1, table: "T2")
        _ = try await api.voidReceipt(receiptId: try liveReceipt(), restock: true)
        #expect(try await stock(api, beef.id) == -200_000)
        #expect(try await api.stockMovements(ingredientId: beef.id, from: nil, to: nil).filter { $0.kind == StockMovementKind.saleReversal }.count == 1)
    }

    @Test func autoUnavailableFollowsTheStockButNeverLiftsAManualOutage() async throws {
        let api = try await makeLocalAPI()
        let cream = try await raw(api, "Creme")
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let pizza = try await localProduct(api, "Pizza Margherita AOP")
        _ = try await api.setRecipe(productId: burger.id, lines: [RecipeLine(ingredientId: cream.id, quantityMilli: 100_000)])
        _ = try await api.setRecipe(productId: pizza.id, lines: [RecipeLine(ingredientId: cream.id, quantityMilli: 100_000)])
        try await api.setAvailability(productId: pizza.id, isAvailable: false)
        _ = try await api.saveStockSettings(StockSettings(autoUnavailableOnStockOut: true))
        _ = try await api.receiveStock(ingredientId: cream.id, quantityMilli: 100_000, unitCostMilliCents: 1000, note: nil)

        try await sell(api, burger, quantity: 1, table: "T1") // stock à 0
        #expect(try await localProduct(api, "Burger Gourmet Rossini").isOutOfStock)

        _ = try await api.receiveStock(ingredientId: cream.id, quantityMilli: 500_000, unitCostMilliCents: 1000, note: nil)
        #expect(try await !localProduct(api, "Burger Gourmet Rossini").isOutOfStock)
        #expect(try await localProduct(api, "Pizza Margherita AOP").isOutOfStock) // rupture manuelle : jamais levée par le stock
    }

    @Test func lowStockAlertListsIngredientsUnderTheirThreshold() async throws {
        let api = try await makeLocalAPI()
        let salt = try await raw(api, "Sel", low: 1_000_000)
        _ = try await api.receiveStock(ingredientId: salt.id, quantityMilli: 800_000, unitCostMilliCents: 100, note: nil)

        let alerts = try await api.stockAlerts()

        #expect(alerts.lowStock.map(\.ingredientId) == [salt.id] && alerts.lowStock[0].stockMilli == 800_000)
    }

    @Test func stockMovementsAreAppendOnly() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let salt = try await raw(api, "Sel")
        _ = try await api.receiveStock(ingredientId: salt.id, quantityMilli: 1000, unitCostMilliCents: 100, note: nil)

        #expect(throws: SQLiteError.self) { try db.run("UPDATE StockMovements SET QuantityMilli = 0") }
        #expect(throws: SQLiteError.self) { try db.run("DELETE FROM StockMovements") }
        await #expect(throws: RecipeError.hasMovements) { try await api.deleteIngredient(id: salt.id) }
    }

    @Test func waiterCannotTouchStock() async throws {
        let api = try await makeLocalAPI()
        let salt = try await raw(api, "Sel")
        _ = try await api.login(pin: "2468")
        await #expect(throws: APIError.self) { _ = try await api.receiveStock(ingredientId: salt.id, quantityMilli: 1, unitCostMilliCents: 1, note: nil) }
    }

    @Test func modifierOptionConsumptionIsEditableAndConsumedBySales() async throws {
        let api = try await makeLocalAPI()
        let cheese = try await raw(api, "Cheddar")
        let sauce = try await api.createIngredient(IngredientDraft(name: "Sauce", isPreparation: true, yieldMilli: 1000))
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let option = try #require(burger.modifierGroups.flatMap(\.options).first { $0.name == "Double Cheddar Fondu" })

        _ = try await api.setModifierConsumption(optionId: option.id, items: [ModifierConsumptionItem(ingredientId: cheese.id, deltaQuantityMilli: 30_000)])
        #expect(try await api.modifierConsumption(optionId: option.id) == [ModifierConsumptionItem(ingredientId: cheese.id, deltaQuantityMilli: 30_000)])
        await #expect(throws: RecipeError.ingredientInvalid) { _ = try await api.setModifierConsumption(optionId: option.id, items: [ModifierConsumptionItem(ingredientId: sauce.id, deltaQuantityMilli: 1)]) }
        await #expect(throws: RecipeError.ingredientInvalid) { _ = try await api.setModifierConsumption(optionId: option.id, items: [ModifierConsumptionItem(ingredientId: cheese.id, deltaQuantityMilli: 0)]) }

        try await sell(api, burger, quantity: 2, table: "T1", modifiers: ["Double Cheddar Fondu"])
        #expect(try await stock(api, cheese.id) == -60_000)
    }
}
