import Foundation

/// Règles de validation des ingrédients et fiches, communes à `LocalPosAPI` et `InMemoryPosAPI` (miroir de `RecipeService`).
public enum RecipeRules {
    /// Nom nettoyé ; refuse un ingrédient invalide ou un nom déjà pris (comparaison sans casse, accents distincts).
    public static func validate(_ draft: IngredientDraft, otherNames: [String]) throws -> String {
        let name = draft.name.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, name.utf16.count <= 100, Ingredient.units.contains(draft.unit), draft.unitCostMilliCents >= 0,
              !(draft.isPreparation && (draft.yieldMilli ?? 0) <= 0), Allergens.isValid(mask: draft.allergenMask)
        else { throw RecipeError.ingredientInvalid }
        guard !otherNames.contains(where: { $0.uppercased() == name.uppercased() }) else { throw RecipeError.nameTaken }
        return name
    }

    /// Quantités strictement positives, un ingrédient une seule fois.
    public static func checkLines(_ lines: [RecipeLine]) throws {
        guard lines.allSatisfy({ $0.quantityMilli > 0 }), Set(lines.map(\.ingredientId)).count == lines.count else { throw RecipeError.invalid }
    }

    /// Libellé d'un refus dans la langue de l'app.
    public static func message(for error: RecipeError) -> String {
        let text = L10n.string("stock.err.\(error.code)")
        if case .inUse(let products) = error { return String(format: text, products.joined(separator: ", ")) }
        return text
    }
}

extension RecipeError: LocalizedError {
    public var errorDescription: String? { RecipeRules.message(for: self) }
}
