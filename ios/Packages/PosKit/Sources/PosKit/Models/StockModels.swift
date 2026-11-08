import Foundation

/// Matière première ou préparation (miroir de `Ingredient`, spec 023). Quantités en millièmes d'unité de base
/// (g, ml, piece), prix en milli-centimes par unité de base.
public struct Ingredient: Codable, Hashable, Sendable, Identifiable {
    public var id: UUID
    public var name: String
    public var unit: String
    public var unitCostMilliCents: Int64
    public var isPreparation: Bool
    public var yieldMilli: Int64?
    public var allergenMask: Int
    public var lowStockMilli: Int64?
    public var stockMilli: Int64?
    public var isActive: Bool

    public init(id: UUID = UUID(), name: String, unit: String = "g", unitCostMilliCents: Int64 = 0, isPreparation: Bool = false,
                yieldMilli: Int64? = nil, allergenMask: Int = 0, lowStockMilli: Int64? = nil, stockMilli: Int64? = nil, isActive: Bool = true) {
        self.id = id
        self.name = name
        self.unit = unit
        self.unitCostMilliCents = unitCostMilliCents
        self.isPreparation = isPreparation
        self.yieldMilli = yieldMilli
        self.allergenMask = allergenMask
        self.lowStockMilli = lowStockMilli
        self.stockMilli = stockMilli
        self.isActive = isActive
    }

    /// Unités de base acceptées (`g`, `ml`, `piece`).
    public static let units = ["g", "ml", "piece"]
}

/// Corps d'une création ou d'une modification d'ingrédient.
public struct IngredientDraft: Codable, Hashable, Sendable {
    public var name: String
    public var unit: String
    public var unitCostMilliCents: Int64
    public var isPreparation: Bool
    public var yieldMilli: Int64?
    public var allergenMask: Int
    public var lowStockMilli: Int64?
    public var isActive: Bool

    public init(name: String, unit: String = "g", unitCostMilliCents: Int64 = 0, isPreparation: Bool = false, yieldMilli: Int64? = nil,
                allergenMask: Int = 0, lowStockMilli: Int64? = nil, isActive: Bool = true) {
        self.name = name
        self.unit = unit
        self.unitCostMilliCents = unitCostMilliCents
        self.isPreparation = isPreparation
        self.yieldMilli = yieldMilli
        self.allergenMask = allergenMask
        self.lowStockMilli = lowStockMilli
        self.isActive = isActive
    }
}

/// Une ligne de fiche technique : un ingrédient et sa quantité.
public struct RecipeLine: Codable, Hashable, Sendable {
    public var ingredientId: UUID
    public var quantityMilli: Int64

    public init(ingredientId: UUID, quantityMilli: Int64) {
        self.ingredientId = ingredientId
        self.quantityMilli = quantityMilli
    }
}

/// Fiche technique d'un produit avec son coût de revient (centimes) et ses allergènes déduits.
public struct Recipe: Codable, Hashable, Sendable {
    public var lines: [RecipeLine]
    public var costCents: Int64
    public var allergenMask: Int

    public init(lines: [RecipeLine], costCents: Int64, allergenMask: Int) {
        self.lines = lines
        self.costCents = costCents
        self.allergenMask = allergenMask
    }
}

/// Refus d'une fiche ou d'un ingrédient ; `code` est celui renvoyé par l'API.
public enum RecipeError: Error, Equatable, Sendable {
    case cycle, depth, ingredientInactive, ingredientUnknown, invalid
    case ingredientInvalid, nameTaken, hasMovements
    case inUse(products: [String])

    public var code: String {
        switch self {
        case .cycle: "recipe_cycle"
        case .depth: "recipe_depth"
        case .ingredientInactive: "ingredient_inactive"
        case .ingredientUnknown: "ingredient_unknown"
        case .invalid: "recipe_invalid"
        case .ingredientInvalid: "ingredient_invalid"
        case .nameTaken: "ingredient_name_taken"
        case .hasMovements: "ingredient_has_movements"
        case .inUse: "ingredient_in_use"
        }
    }

    public init?(code: String, products: [String] = []) {
        switch code {
        case "recipe_cycle": self = .cycle
        case "recipe_depth": self = .depth
        case "ingredient_inactive": self = .ingredientInactive
        case "ingredient_unknown": self = .ingredientUnknown
        case "recipe_invalid": self = .invalid
        case "ingredient_invalid": self = .ingredientInvalid
        case "ingredient_name_taken": self = .nameTaken
        case "ingredient_has_movements": self = .hasMovements
        case "ingredient_in_use": self = .inUse(products: products)
        default: return nil
        }
    }
}

/// Conversions entre la saisie (€ par kg, par l ou par pièce ; quantités en g, ml ou pièces) et les entiers du serveur.
public extension Ingredient {
    /// Milli-centimes par unité de base pour un prix saisi en euros par kg / l / pièce.
    static func milliCents(fromPrice price: Decimal, unit: String) -> Int64 {
        var value = price * (unit == "piece" ? 100_000 : 100), rounded = Decimal()
        NSDecimalRound(&rounded, &value, 0, .plain)
        return NSDecimalNumber(decimal: rounded).int64Value
    }

    static func price(fromMilliCents milli: Int64, unit: String) -> Decimal {
        Decimal(milli) / (unit == "piece" ? 100_000 : 100)
    }

    static func milli(fromQuantity quantity: Decimal) -> Int64 {
        var value = quantity * 1000, rounded = Decimal()
        NSDecimalRound(&rounded, &value, 0, .plain)
        return NSDecimalNumber(decimal: rounded).int64Value
    }

    static func quantity(fromMilli milli: Int64) -> Decimal { Decimal(milli) / 1000 }
}

/// Un mouvement de stock (ajout seulement) : `sale`, `saleReversal`, `receipt`, `count`, `loss`. Quantité signée, en millièmes d'unité de base.
public struct StockMovement: Codable, Hashable, Sendable, Identifiable {
    public var id: UUID
    public var ingredientId: UUID
    public var kind: String
    public var quantityMilli: Int64
    public var unitCostMilliCents: Int64?
    public var orderId: UUID?
    public var operatorId: UUID?
    public var note: String?
    public var createdAtUtc: Date

    public init(id: UUID = UUID(), ingredientId: UUID, kind: String, quantityMilli: Int64, unitCostMilliCents: Int64? = nil, orderId: UUID? = nil,
                operatorId: UUID? = nil, note: String? = nil, createdAtUtc: Date = Date()) {
        self.id = id
        self.ingredientId = ingredientId
        self.kind = kind
        self.quantityMilli = quantityMilli
        self.unitCostMilliCents = unitCostMilliCents
        self.orderId = orderId
        self.operatorId = operatorId
        self.note = note
        self.createdAtUtc = createdAtUtc
    }
}

/// Résultat d'un inventaire : écart (comptage moins stock théorique) et sa valeur en centimes.
public struct CountResult: Codable, Hashable, Sendable {
    public var varianceMilli: Int64
    public var valueCents: Int64

    public init(varianceMilli: Int64, valueCents: Int64) {
        self.varianceMilli = varianceMilli
        self.valueCents = valueCents
    }
}

public struct LowStockAlert: Codable, Hashable, Sendable, Identifiable {
    public var ingredientId: UUID
    public var name: String
    public var unit: String
    public var stockMilli: Int64
    public var lowStockMilli: Int64
    public var id: UUID { ingredientId }

    public init(ingredientId: UUID, name: String, unit: String, stockMilli: Int64, lowStockMilli: Int64) {
        self.ingredientId = ingredientId
        self.name = name
        self.unit = unit
        self.stockMilli = stockMilli
        self.lowStockMilli = lowStockMilli
    }
}

public struct CountVarianceAlert: Codable, Hashable, Sendable {
    public var ingredientId: UUID
    public var name: String
    public var unit: String
    public var varianceMilli: Int64
    public var valueCents: Int64
    public var createdAtUtc: Date
}

public struct StockAlerts: Codable, Hashable, Sendable {
    public var lowStock: [LowStockAlert]
    public var recentCounts: [CountVarianceAlert]

    public init(lowStock: [LowStockAlert] = [], recentCounts: [CountVarianceAlert] = []) {
        self.lowStock = lowStock
        self.recentCounts = recentCounts
    }
}

/// Réglages du stock et des allergènes (hors `/api/settings`, dont le corps est signé dans le journal fiscal).
public struct StockSettings: Codable, Hashable, Sendable {
    public var autoUnavailableOnStockOut: Bool
    public var printAllergensOnKitchenTicket: Bool
    public var allergenDisclaimer: String?

    public init(autoUnavailableOnStockOut: Bool = false, printAllergensOnKitchenTicket: Bool = false, allergenDisclaimer: String? = nil) {
        self.autoUnavailableOnStockOut = autoUnavailableOnStockOut
        self.printAllergensOnKitchenTicket = printAllergensOnKitchenTicket
        self.allergenDisclaimer = allergenDisclaimer
    }
}

public enum StockMovementKind {
    public static let sale = "sale", saleReversal = "saleReversal", receipt = "receipt", count = "count", loss = "loss"
}

/// Quantité ajoutée (ou retirée si négative) pour chaque portion vendue avec une option de modificateur.
public struct ModifierConsumptionItem: Codable, Hashable, Sendable {
    public var ingredientId: UUID
    public var deltaQuantityMilli: Int64

    public init(ingredientId: UUID, deltaQuantityMilli: Int64) {
        self.ingredientId = ingredientId
        self.deltaQuantityMilli = deltaQuantityMilli
    }
}
