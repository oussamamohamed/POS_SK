import Foundation

/// Coût de revient d'une fiche, en entiers (miroir de `RecipeCostCalculator` du .NET, rejoué sur `recipe_cost_reference.json`).
/// Σ quantité × prix dépasse 64 bits pour les très grandes valeurs : la somme est tenue sur 128 bits (`Int128` n'existe pas en iOS 17).
/// Centimes = Σ / 1 000 000, arrondi une seule fois au demi supérieur ; le prix d'une préparation est son coût divisé par son
/// rendement, arrondi au demi supérieur en milli-centimes par unité de base.
public enum RecipeCostCalculator {
    public static let maxDepth = 5
    private static let centsDivisor: UInt64 = 1_000_000

    public struct Result: Equatable, Sendable {
        public let cents: Int64
        public let allergenMask: Int
    }

    public static func compute(lines: [RecipeLine], ingredients: [UUID: Ingredient], preparationLines: [UUID: [RecipeLine]]) throws -> Result {
        var memo: [UUID: (unitCost: Int64, mask: Int)] = [:]
        let (sum, mask) = try total(lines, ingredients, preparationLines, &memo, [], 1)
        return Result(cents: Int64(try sum.divided(by: centsDivisor)), allergenMask: mask)
    }

    /// Prix par unité de base (milli-centimes) d'une préparation, recalculé depuis sa fiche.
    public static func preparationUnitCost(_ id: UUID, ingredients: [UUID: Ingredient], preparationLines: [UUID: [RecipeLine]]) throws -> Int64 {
        guard let prep = ingredients[id] else { throw RecipeError.ingredientUnknown }
        var memo: [UUID: (unitCost: Int64, mask: Int)] = [:]
        let (sum, _) = try total(preparationLines[id] ?? [], ingredients, preparationLines, &memo, [id], 2)
        return try unitCost(of: sum, prep)
    }

    private static func unitCost(of sum: U128, _ prep: Ingredient) throws -> Int64 {
        guard let yield = prep.yieldMilli, yield > 0 else { throw RecipeError.ingredientUnknown }
        return Int64(try sum.divided(by: UInt64(yield)))
    }

    private static func total(_ lines: [RecipeLine], _ ingredients: [UUID: Ingredient], _ preps: [UUID: [RecipeLine]],
                              _ memo: inout [UUID: (unitCost: Int64, mask: Int)], _ path: Set<UUID>, _ depth: Int) throws -> (U128, Int) {
        if depth > maxDepth { throw RecipeError.depth }
        var sum = U128.zero
        var mask = 0
        for line in lines {
            guard let ingredient = ingredients[line.ingredientId] else { throw RecipeError.ingredientUnknown }
            if !ingredient.isActive { throw RecipeError.ingredientInactive }

            let unit: Int64
            if ingredient.isPreparation {
                if path.contains(ingredient.id) { throw RecipeError.cycle }
                if let known = memo[ingredient.id] {
                    unit = known.unitCost
                    mask |= known.mask
                } else {
                    var inner = path
                    inner.insert(ingredient.id)
                    let (innerSum, innerMask) = try total(preps[ingredient.id] ?? [], ingredients, preps, &memo, inner, depth + 1)
                    let known = (unitCost: try unitCost(of: innerSum, ingredient), mask: innerMask)
                    memo[ingredient.id] = known
                    unit = known.unitCost
                    mask |= known.mask
                }
            } else {
                unit = ingredient.unitCostMilliCents
                mask |= ingredient.allergenMask
            }
            sum = sum.adding(UInt64(max(line.quantityMilli, 0)), times: UInt64(max(unit, 0)))
        }
        return (sum, mask)
    }
}

/// Entier non signé sur 128 bits, juste ce qu'il faut : ajouter un produit, diviser avec arrondi au demi supérieur.
struct U128 {
    var high: UInt64
    var low: UInt64
    static let zero = U128(high: 0, low: 0)

    func adding(_ a: UInt64, times b: UInt64) -> U128 {
        let product = a.multipliedFullWidth(by: b)
        let (low, carry) = low.addingReportingOverflow(product.low)
        return U128(high: high &+ product.high &+ (carry ? 1 : 0), low: low)
    }

    /// (self + d/2) / d ; le quotient doit tenir sur 64 bits (comme le `(long)` du .NET).
    func divided(by d: UInt64) throws -> UInt64 {
        let (low, carry) = low.addingReportingOverflow(d / 2)
        let high = high &+ (carry ? 1 : 0)
        guard high < d else { throw RecipeError.invalid }
        return d.dividingFullWidth((high, low)).quotient
    }
}
