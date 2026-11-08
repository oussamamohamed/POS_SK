import Foundation
import Observation

/// Back-office : ingrédients, préparations et fiches techniques.
@MainActor @Observable
public final class StockStore {
    public private(set) var ingredients: [Ingredient] = []
    public private(set) var settings = StockSettings()
    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func load() async {
        do { ingredients = try await api.ingredients() } catch { notifier.error(error) }
    }

    public func ingredient(id: UUID) -> Ingredient? { ingredients.first { $0.id == id } }

    public func save(id: UUID?, draft: IngredientDraft) async -> Bool {
        do {
            if let id { _ = try await api.updateIngredient(id: id, draft) } else { _ = try await api.createIngredient(draft) }
            notifier.success(String(format: L10n.string("stock.saved"), draft.name))
            await load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func delete(_ ingredient: Ingredient) async {
        do {
            try await api.deleteIngredient(id: ingredient.id)
            notifier.info(String(format: L10n.string("stock.deleted"), ingredient.name))
            await load()
        } catch {
            notifier.error(error)
        }
    }

    public func recipe(productId: UUID) async -> Recipe? {
        do { return try await api.recipe(productId: productId) } catch { notifier.error(error); return nil }
    }

    public func saveRecipe(productId: UUID, lines: [RecipeLine]) async -> Bool {
        do { _ = try await api.setRecipe(productId: productId, lines: lines); return true } catch { notifier.error(error); return false }
    }

    /// Coût affiché en direct pendant l'édition : Σ quantité × prix, arrondi au demi supérieur (même formule que le serveur ;
    /// le prix d'une préparation est celui que le serveur a enregistré).
    public func liveCostCents(_ lines: [RecipeLine]) -> Int64 {
        let sum = lines.reduce(Decimal(0)) { $0 + Decimal($1.quantityMilli) * Decimal(ingredient(id: $1.ingredientId)?.unitCostMilliCents ?? 0) }
        var value = sum / 1_000_000, rounded = Decimal()
        NSDecimalRound(&rounded, &value, 0, .plain)
        return NSDecimalNumber(decimal: rounded).int64Value
    }

    // MARK: Mouvements

    public func loadSettings() async {
        if let loaded = try? await api.stockSettings() { settings = loaded }
    }

    public func setAutoUnavailable(_ on: Bool) async { await updateSettings { $0.autoUnavailableOnStockOut = on } }

    public func updateSettings(_ change: (inout StockSettings) -> Void) async {
        var updated = settings
        change(&updated)
        do { settings = try await api.saveStockSettings(updated) } catch { notifier.error(error) }
    }

    /// Réception : quantité en unités saisies (g, ml, pièces), prix en euros par kg, l ou pièce.
    public func receive(_ ingredient: Ingredient, quantity: Decimal, price: Decimal, note: String?) async -> Bool {
        await run { _ = try await self.api.receiveStock(ingredientId: ingredient.id, quantityMilli: Ingredient.milli(fromQuantity: quantity),
                                                         unitCostMilliCents: Ingredient.milliCents(fromPrice: price, unit: ingredient.unit), note: note) }
    }

    public func count(_ ingredient: Ingredient, counted: Decimal, note: String?) async -> Bool {
        do {
            let result = try await api.countStock(ingredientId: ingredient.id, countedMilli: Ingredient.milli(fromQuantity: counted), note: note)
            notifier.info(String(format: L10n.string("stock.count_result"), "\(Ingredient.quantity(fromMilli: result.varianceMilli))", L10n.string("stock.unit_short_\(ingredient.unit)"),
                                 Money(cents: Int(result.valueCents)).formatted))
            await load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func loss(_ ingredient: Ingredient, quantity: Decimal, note: String?) async -> Bool {
        await run { _ = try await self.api.recordStockLoss(ingredientId: ingredient.id, quantityMilli: Ingredient.milli(fromQuantity: quantity), note: note) }
    }

    public func movements(of ingredient: Ingredient) async -> [StockMovement] {
        (try? await api.stockMovements(ingredientId: ingredient.id, from: nil, to: nil)) ?? []
    }

    public func consumption(of optionId: UUID) async -> [ModifierConsumptionItem] {
        (try? await api.modifierConsumption(optionId: optionId)) ?? []
    }

    public func saveConsumption(optionId: UUID, items: [ModifierConsumptionItem]) async -> Bool {
        do { _ = try await api.setModifierConsumption(optionId: optionId, items: items); return true } catch { notifier.error(error); return false }
    }

    private func run(_ operation: () async throws -> Void) async -> Bool {
        do {
            try await operation()
            notifier.success(L10n.string("stock.op_saved"))
            await load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }
}
