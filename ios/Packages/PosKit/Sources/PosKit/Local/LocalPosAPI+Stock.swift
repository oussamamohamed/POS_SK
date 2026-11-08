import Foundation

extension LocalPosAPI {
    var stockRepository: LocalStockRepository { LocalStockRepository(db: db) }

    public func ingredients() async throws -> [Ingredient] {
        try requireManager()
        let levels = try stockRepository.levels()
        return try stockRepository.ingredients().map { i in
            var copy = i
            copy.stockMilli = i.isPreparation ? nil : levels[i.id, default: 0] // les préparations ne sont pas suivies
            return copy
        }
    }

    public func createIngredient(_ draft: IngredientDraft) async throws -> Ingredient {
        try requireManager()
        return try db.transaction {
            let name = try RecipeRules.validate(draft, otherNames: try stockRepository.ingredients().map(\.name))
            let ingredient = Self.ingredient(Ingredient(name: name), from: draft, name: name)
            try stockRepository.insert(ingredient)
            return ingredient
        }
    }

    public func updateIngredient(id: UUID, _ draft: IngredientDraft) async throws -> Ingredient {
        try requireManager()
        return try db.transaction {
            guard let current = try stockRepository.ingredient(id: id) else { throw APIError.notFound(nil) }
            let name = try RecipeRules.validate(draft, otherNames: try stockRepository.ingredients().filter { $0.id != id }.map(\.name))
            if current.unit != draft.unit, try stockRepository.isUsed(ingredient: id) { throw RecipeError.inUse(products: []) }
            let updated = Self.ingredient(current, from: draft, name: name)
            try stockRepository.update(updated)
            return updated
        }
    }

    public func deleteIngredient(id: UUID) async throws {
        try requireManager()
        try db.transaction {
            guard try stockRepository.ingredient(id: id) != nil else { throw APIError.notFound(nil) }
            // Un ingrédient qui a des mouvements ne se supprime pas : on le désactive pour garder l'historique.
            if try stockRepository.hasMovements(ingredient: id) { throw RecipeError.hasMovements }
            if try stockRepository.isUsed(ingredient: id) { throw RecipeError.inUse(products: try stockRepository.usageNames(ingredient: id)) }
            try stockRepository.delete(id: id)
        }
    }

    public func recipe(productId: UUID) async throws -> Recipe {
        try requireManager()
        guard try stockRepository.productExists(productId) else { throw APIError.notFound(nil) }
        let lines = try stockRepository.lines(kind: LocalStockRepository.productOwner, owner: productId)
        return try recipeResult(lines)
    }

    public func setRecipe(productId: UUID, lines: [RecipeLine]) async throws -> Recipe {
        try requireManager()
        return try db.transaction {
            guard let product = try stockRepository.product(productId) else { throw APIError.notFound(nil) }
            try RecipeRules.checkLines(lines)
            let result = try recipeResult(lines)
            try stockRepository.replaceLines(kind: LocalStockRepository.productOwner, owner: productId, lines)
            // Allergènes déduits de la fiche, sauf saisie manuelle ; une fiche vide ne déclare rien.
            if !lines.isEmpty, !product.overridden { try stockRepository.setProductAllergens(productId, mask: result.allergenMask) }
            return result
        }
    }

    public func preparationRecipe(id: UUID) async throws -> [RecipeLine] {
        try requireManager()
        guard let prep = try stockRepository.ingredient(id: id), prep.isPreparation else { throw APIError.notFound(nil) }
        return try stockRepository.lines(kind: LocalStockRepository.ingredientOwner, owner: id)
    }

    public func setPreparationRecipe(id: UUID, lines: [RecipeLine]) async throws -> Ingredient {
        try requireManager()
        return try db.transaction {
            guard var prep = try stockRepository.ingredient(id: id), prep.isPreparation else { throw APIError.notFound(nil) }
            try RecipeRules.checkLines(lines)
            let all = Dictionary(uniqueKeysWithValues: try stockRepository.ingredients().map { ($0.id, $0) })
            var preps = try stockRepository.preparationLines()
            preps[id] = lines
            prep.unitCostMilliCents = try RecipeCostCalculator.preparationUnitCost(id, ingredients: all, preparationLines: preps)
            try stockRepository.replaceLines(kind: LocalStockRepository.ingredientOwner, owner: id, lines)
            try stockRepository.update(prep)
            return prep
        }
    }

    private func recipeResult(_ lines: [RecipeLine]) throws -> Recipe {
        let all = Dictionary(uniqueKeysWithValues: try stockRepository.ingredients().map { ($0.id, $0) })
        let cost = try RecipeCostCalculator.compute(lines: lines, ingredients: all, preparationLines: try stockRepository.preparationLines())
        return Recipe(lines: lines, costCents: cost.cents, allergenMask: cost.allergenMask)
    }

    /// Champs d'un brouillon appliqués à un ingrédient ; le rendement ne vaut que pour une préparation.
    static func ingredient(_ base: Ingredient, from d: IngredientDraft, name: String) -> Ingredient {
        var i = base
        i.name = name
        i.unit = d.unit
        i.isPreparation = d.isPreparation
        i.yieldMilli = d.isPreparation ? d.yieldMilli : nil
        i.unitCostMilliCents = d.unitCostMilliCents
        i.allergenMask = d.allergenMask
        i.lowStockMilli = d.lowStockMilli
        i.isActive = d.isActive
        return i
    }
}
