import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : ingrédients et fiches techniques")
struct LocalStockTests {
    private func draft(_ name: String, price: Int64 = 1000, mask: Int = 0, prep: Bool = false, yield: Int64? = nil) -> IngredientDraft {
        IngredientDraft(name: name, unitCostMilliCents: price, isPreparation: prep, yieldMilli: yield, allergenMask: mask)
    }

    @Test func recipeGivesCostAndFillsTheProductAllergens() async throws {
        let api = try await makeLocalAPI()
        let flour = try await api.createIngredient(draft("Farine", price: 1250, mask: 1))
        let cheese = try await api.createIngredient(draft("Mozza", price: 1800, mask: 64))
        let product = try await localProduct(api, "Burger Gourmet Rossini")

        let recipe = try await api.setRecipe(productId: product.id, lines: [RecipeLine(ingredientId: flour.id, quantityMilli: 150_000),
                                                                            RecipeLine(ingredientId: cheese.id, quantityMilli: 125_000)])

        #expect(recipe.costCents == 413 && recipe.allergenMask == 65) // 187,5 + 225 = 412,5
        let stored = try #require(try await api.products().first { $0.id == product.id })
        #expect(stored.allergensDeclared && stored.allergenMask == 65)
        #expect(try await api.recipe(productId: product.id) == recipe)
        #expect(await api.recipeCostsForTests()[product.id] == 413)
    }

    @Test func manualAllergensSurviveARecipeSave() async throws {
        let api = try await makeLocalAPI()
        let flour = try await api.createIngredient(draft("Farine", mask: 1))
        let product = try await localProduct(api, "Burger Gourmet Rossini")
        try await api.setAllergens(productId: product.id, declared: true, mask: 8, overridden: true)

        _ = try await api.setRecipe(productId: product.id, lines: [RecipeLine(ingredientId: flour.id, quantityMilli: 1000)])

        #expect(try await api.products().first { $0.id == product.id }?.allergenMask == 8)
    }

    @Test func duplicateNameUnitAndInUseAreRefused() async throws {
        let api = try await makeLocalAPI()
        let salt = try await api.createIngredient(draft("Sel"))
        await #expect(throws: RecipeError.nameTaken) { _ = try await api.createIngredient(draft("SEL")) }
        await #expect(throws: RecipeError.ingredientInvalid) { _ = try await api.createIngredient(draft("  ")) }

        let product = try await localProduct(api, "Burger Gourmet Rossini")
        _ = try await api.setRecipe(productId: product.id, lines: [RecipeLine(ingredientId: salt.id, quantityMilli: 1000)])

        await #expect(throws: RecipeError.inUse(products: ["Burger Gourmet Rossini"])) { try await api.deleteIngredient(id: salt.id) }
        await #expect(throws: RecipeError.inUse(products: [])) { _ = try await api.updateIngredient(id: salt.id, IngredientDraft(name: "Sel", unit: "ml")) }
    }

    @Test func preparationRecipeSetsItsPriceAndLoopsAreRefused() async throws {
        let api = try await makeLocalAPI()
        let tomato = try await api.createIngredient(draft("Tomate", price: 300))
        let sauce = try await api.createIngredient(draft("Sauce", prep: true, yield: 500_000))
        let other = try await api.createIngredient(draft("Fond", prep: true, yield: 1000))

        let priced = try await api.setPreparationRecipe(id: sauce.id, lines: [RecipeLine(ingredientId: tomato.id, quantityMilli: 1_000_000)])
        #expect(priced.unitCostMilliCents == 600)

        _ = try await api.setPreparationRecipe(id: other.id, lines: [RecipeLine(ingredientId: sauce.id, quantityMilli: 1000)])
        await #expect(throws: RecipeError.cycle) {
            _ = try await api.setPreparationRecipe(id: sauce.id, lines: [RecipeLine(ingredientId: other.id, quantityMilli: 1000)])
        }
    }

    @Test func waiterCannotReadIngredients() async throws {
        let api = try await makeLocalAPI()
        _ = try await api.login(pin: "2468")
        await #expect(throws: APIError.self) { _ = try await api.ingredients() }
    }
}

extension LocalPosAPI {
    func recipeCostsForTests() -> [UUID: Int] { (try? stockRepository.productCostsCents()) ?? [:] }
}
