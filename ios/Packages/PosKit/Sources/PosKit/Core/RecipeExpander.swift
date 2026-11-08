import Foundation

/// Ramène une fiche aux ingrédients bruts (miroir de `RecipeExpander` du .NET). Une préparation de quantité Q donne, pour chaque
/// ligne interne de quantité qi, Q × qi / rendement arrondi au demi supérieur ; la récursion s'arrête à `RecipeCostCalculator.maxDepth`.
/// Les préparations ne sont pas suivies en stock : seuls les ingrédients bruts le sont.
public enum RecipeExpander {
    public static func toRaw(_ lines: [RecipeLine], multiplier: Int64, ingredients: [UUID: Ingredient], preparationLines: [UUID: [RecipeLine]]) -> [UUID: Int64] {
        var raw: [UUID: Int64] = [:]
        for line in lines { add(&raw, line.ingredientId, line.quantityMilli * multiplier, ingredients, preparationLines, 1) }
        return raw
    }

    private static func add(_ raw: inout [UUID: Int64], _ id: UUID, _ quantity: Int64, _ ingredients: [UUID: Ingredient], _ preps: [UUID: [RecipeLine]], _ depth: Int) {
        guard let ingredient = ingredients[id] else { return }
        if !ingredient.isPreparation {
            raw[id, default: 0] += quantity
            return
        }
        guard depth < RecipeCostCalculator.maxDepth, let yield = ingredient.yieldMilli, yield > 0, let inner = preps[id] else { return }
        for line in inner {
            let share = (try? U128.zero.adding(UInt64(max(quantity, 0)), times: UInt64(max(line.quantityMilli, 0))).divided(by: UInt64(yield))).map(Int64.init) ?? 0
            add(&raw, line.ingredientId, share, ingredients, preps, depth + 1)
        }
    }
}
