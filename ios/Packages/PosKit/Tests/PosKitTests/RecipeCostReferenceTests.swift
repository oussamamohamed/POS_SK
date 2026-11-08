import Foundation
import Testing
@testable import PosKit

/// Rejoue `recipe_cost_reference.json`, généré par le .NET (`WRITE_RECIPE_FIXTURE=1 dotnet test … --filter RecipeCostReferenceFixtureTests`).
@Suite struct RecipeCostReferenceTests {
    struct Ing: Decodable {
        let id: UUID, name: String, isPreparation: Bool, yieldMilli: Int64?, unitCostMilliCents: Int64, allergenMask: Int, isActive: Bool
    }
    struct Expected: Decodable {
        let cents: Int64?, allergenMask: Int?, error: String?
        let preparationUnitCosts: [String: Int64]
    }
    struct Case: Decodable {
        let name: String
        let ingredients: [Ing]
        let preparationLines: [String: [RecipeLine]]
        let lines: [RecipeLine]
        let expected: Expected
    }

    @Test func replaysEveryScenario() throws {
        let url = try #require(Bundle.module.url(forResource: "recipe_cost_reference", withExtension: "json", subdirectory: "Fixtures"))
        let cases = try JSONDecoder().decode([Case].self, from: Data(contentsOf: url))
        #expect(cases.count > 30)
        for c in cases {
            let ingredients = Dictionary(uniqueKeysWithValues: c.ingredients.map {
                ($0.id, Ingredient(id: $0.id, name: $0.name, unitCostMilliCents: $0.unitCostMilliCents, isPreparation: $0.isPreparation,
                                   yieldMilli: $0.yieldMilli, allergenMask: $0.allergenMask, isActive: $0.isActive))
            })
            let preps = Dictionary(uniqueKeysWithValues: c.preparationLines.map { (UUID(uuidString: $0.key)!, $0.value) })
            do {
                let result = try RecipeCostCalculator.compute(lines: c.lines, ingredients: ingredients, preparationLines: preps)
                #expect(result.cents == c.expected.cents, "\(c.name) : centimes")
                #expect(result.allergenMask == c.expected.allergenMask, "\(c.name) : allergènes")
                for (key, expected) in c.expected.preparationUnitCosts {
                    let unit = try RecipeCostCalculator.preparationUnitCost(UUID(uuidString: key)!, ingredients: ingredients, preparationLines: preps)
                    #expect(unit == expected, "\(c.name) : prix de préparation")
                }
            } catch let error as RecipeError {
                #expect(error.code == c.expected.error, "\(c.name) : refus")
            }
        }
    }
}
