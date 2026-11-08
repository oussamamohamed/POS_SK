import Foundation

extension LocalPosAPI {
    private func tracked(_ id: UUID) throws -> Ingredient {
        guard let ingredient = try stockRepository.ingredient(id: id) else { throw APIError.notFound(nil) }
        if ingredient.isPreparation { throw RecipeError.ingredientInvalid } // seuls les ingrédients bruts sont suivis
        return ingredient
    }

    public func receiveStock(ingredientId: UUID, quantityMilli: Int64, unitCostMilliCents: Int64, note: String?) async throws -> StockMovement {
        let manager = try requireManager()
        guard quantityMilli > 0, unitCostMilliCents >= 0 else { throw RecipeError.ingredientInvalid }
        return try db.transaction {
            var ingredient = try tracked(ingredientId)
            let stock = try stockRepository.level(of: ingredientId)
            // Prix moyen pondéré ; un stock nul ou négatif repart du prix de la réception.
            if stock > 0 {
                let sum = U128.zero.adding(UInt64(stock), times: UInt64(max(ingredient.unitCostMilliCents, 0))).adding(UInt64(quantityMilli), times: UInt64(unitCostMilliCents))
                ingredient.unitCostMilliCents = Int64(try sum.divided(by: UInt64(stock + quantityMilli)))
            } else {
                ingredient.unitCostMilliCents = unitCostMilliCents
            }
            try stockRepository.update(ingredient)
            let movement = StockMovement(ingredientId: ingredientId, kind: StockMovementKind.receipt, quantityMilli: quantityMilli, unitCostMilliCents: unitCostMilliCents,
                                         operatorId: manager.id, note: note, createdAtUtc: clock())
            try stockRepository.insert(movement)
            try stockRepository.refreshAutoAvailability()
            return movement
        }
    }

    public func countStock(ingredientId: UUID, countedMilli: Int64, note: String?) async throws -> CountResult {
        let manager = try requireManager()
        guard countedMilli >= 0 else { throw RecipeError.ingredientInvalid }
        return try db.transaction {
            let ingredient = try tracked(ingredientId)
            let variance = countedMilli - (try stockRepository.level(of: ingredientId))
            if variance != 0 {
                try stockRepository.insert(StockMovement(ingredientId: ingredientId, kind: StockMovementKind.count, quantityMilli: variance, unitCostMilliCents: ingredient.unitCostMilliCents,
                                                          operatorId: manager.id, note: note, createdAtUtc: clock()))
                try stockRepository.refreshAutoAvailability()
            }
            return CountResult(varianceMilli: variance, valueCents: LocalStockRepository.valueCents(quantityMilli: variance, unitCostMilliCents: ingredient.unitCostMilliCents))
        }
    }

    public func recordStockLoss(ingredientId: UUID, quantityMilli: Int64, note: String?) async throws -> StockMovement {
        let manager = try requireManager()
        guard quantityMilli > 0 else { throw RecipeError.ingredientInvalid }
        return try db.transaction {
            let ingredient = try tracked(ingredientId)
            let movement = StockMovement(ingredientId: ingredientId, kind: StockMovementKind.loss, quantityMilli: -quantityMilli, unitCostMilliCents: ingredient.unitCostMilliCents,
                                         operatorId: manager.id, note: note, createdAtUtc: clock())
            try stockRepository.insert(movement)
            try stockRepository.refreshAutoAvailability()
            return movement
        }
    }

    public func stockMovements(ingredientId: UUID?, from: Date?, to: Date?) async throws -> [StockMovement] {
        try requireManager()
        return try stockRepository.movements(ingredient: ingredientId, from: from, to: to)
    }

    public func stockAlerts() async throws -> StockAlerts {
        try requireManager()
        return try stockRepository.alerts(now: clock())
    }

    public func stockSettings() async throws -> StockSettings {
        try requireManager()
        return try stockRepository.settings()
    }

    public func saveStockSettings(_ settings: StockSettings) async throws -> StockSettings {
        try requireManager()
        return try db.transaction {
            try stockRepository.save(settings)
            try stockRepository.refreshAutoAvailability()
            return try stockRepository.settings()
        }
    }

    public func modifierConsumption(optionId: UUID) async throws -> [ModifierConsumptionItem] {
        try requireManager()
        return try stockRepository.consumption(optionId: optionId)
    }

    public func setModifierConsumption(optionId: UUID, items: [ModifierConsumptionItem]) async throws -> [ModifierConsumptionItem] {
        try requireManager()
        return try db.transaction {
            guard try stockRepository.optionExists(optionId) else { throw APIError.notFound(nil) }
            let raw = Set(try stockRepository.ingredients().filter { !$0.isPreparation }.map(\.id))
            guard items.allSatisfy({ $0.deltaQuantityMilli != 0 && raw.contains($0.ingredientId) }), Set(items.map(\.ingredientId)).count == items.count else {
                throw RecipeError.ingredientInvalid
            }
            try stockRepository.replaceConsumption(optionId: optionId, items)
            return items
        }
    }
}
