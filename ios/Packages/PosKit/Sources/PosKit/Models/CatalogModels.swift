import Foundation

public struct MenuCategory: Codable, Identifiable, Hashable, Sendable {
    public var id: String
    public var name: String
    public var iconName: String?
    public var colorHex: String?
    public var displayOrder: Int?
    public var isActive: Bool?

    public init(id: String, name: String, iconName: String? = nil, colorHex: String? = nil, displayOrder: Int? = nil, isActive: Bool? = true) {
        self.id = id
        self.name = name
        self.iconName = iconName
        self.colorHex = colorHex
        self.displayOrder = displayOrder
        self.isActive = isActive
    }

    public var symbolName: String {
        switch (iconName ?? name).lowercased() {
        case "salad": "leaf"
        case "meat": "flame"
        case "pizza": "circle.grid.cross"
        case "cake": "birthday.cake"
        case "glass": "wineglass"
        case "coffee": "cup.and.saucer"
        default: "fork.knife"
        }
    }
}

public struct ModifierOption: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID
    public var name: String
    public var extraPrice: Money
    public var isDefault: Bool

    public init(id: UUID = UUID(), name: String, extraPrice: Money = .zero, isDefault: Bool = false) {
        self.id = id
        self.name = name
        self.extraPrice = extraPrice
        self.isDefault = isDefault
    }
}

public struct ModifierGroup: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID
    public var groupName: String
    public var minSelections: Int
    public var maxSelections: Int
    public var isMandatory: Bool
    public var isSingleChoice: Bool
    public var options: [ModifierOption]

    public init(id: UUID = UUID(), groupName: String, minSelections: Int = 0, maxSelections: Int = 0, isMandatory: Bool = false, isSingleChoice: Bool = false, options: [ModifierOption]) {
        self.id = id
        self.groupName = groupName
        self.minSelections = minSelections
        self.maxSelections = maxSelections
        self.isMandatory = isMandatory
        self.isSingleChoice = isSingleChoice
        self.options = options
    }

    public var ruleLabel: String {
        if isSingleChoice { return isMandatory ? "1 choix obligatoire" : "1 choix max" }
        if minSelections > 0 { return "Min. \(minSelections)" }
        if maxSelections > 0 { return "Jusqu'à \(maxSelections)" }
        return "Optionnel"
    }
}

public struct Product: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID
    public var name: String
    public var categoryId: String
    public var description: String?
    public var price: Money
    public var taxRatePercent: Decimal
    public var taxRateTakeawayPercent: Decimal?
    public var colorHex: String?
    public var displayOrder: Int?
    public var isQuickKey: Bool
    public var preparationStationId: String?
    public var isAvailable: Bool?
    public var isActive: Bool?
    public var modifierGroups: [ModifierGroup]

    public init(id: UUID = UUID(), name: String, categoryId: String, description: String? = nil, price: Money, taxRatePercent: Decimal = 10, taxRateTakeawayPercent: Decimal? = nil, colorHex: String? = nil, displayOrder: Int? = nil, isQuickKey: Bool = false, preparationStationId: String? = "HOT_KITCHEN", isAvailable: Bool? = true, isActive: Bool? = true, modifierGroups: [ModifierGroup] = []) {
        self.id = id
        self.name = name
        self.categoryId = categoryId
        self.description = description
        self.price = price
        self.taxRatePercent = taxRatePercent
        self.taxRateTakeawayPercent = taxRateTakeawayPercent
        self.colorHex = colorHex
        self.displayOrder = displayOrder
        self.isQuickKey = isQuickKey
        self.preparationStationId = preparationStationId
        self.isAvailable = isAvailable
        self.isActive = isActive
        self.modifierGroups = modifierGroups
    }

    enum CodingKeys: String, CodingKey {
        case id, name, categoryId, description, price, taxRatePercent, taxRateTakeawayPercent, colorHex, displayOrder
        case isQuickKey, preparationStationId, isAvailable, isActive, modifierGroups
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        name = try c.decode(String.self, forKey: .name)
        categoryId = try c.decodeIfPresent(String.self, forKey: .categoryId) ?? ""
        description = try c.decodeIfPresent(String.self, forKey: .description)
        price = try c.decodeIfPresent(Money.self, forKey: .price) ?? .zero
        taxRatePercent = try c.decodeIfPresent(Decimal.self, forKey: .taxRatePercent) ?? 10
        taxRateTakeawayPercent = try c.decodeIfPresent(Decimal.self, forKey: .taxRateTakeawayPercent)
        colorHex = try c.decodeIfPresent(String.self, forKey: .colorHex)
        displayOrder = try c.decodeIfPresent(Int.self, forKey: .displayOrder)
        isQuickKey = try c.decodeIfPresent(Bool.self, forKey: .isQuickKey) ?? false
        preparationStationId = try c.decodeIfPresent(String.self, forKey: .preparationStationId)
        isAvailable = try c.decodeIfPresent(Bool.self, forKey: .isAvailable)
        isActive = try c.decodeIfPresent(Bool.self, forKey: .isActive)
        modifierGroups = try c.decodeIfPresent([ModifierGroup].self, forKey: .modifierGroups) ?? []
    }

    public var hasModifiers: Bool { !modifierGroups.isEmpty }
    public var station: String { preparationStationId ?? "HOT_KITCHEN" }
}

// MARK: - Grille tactile

public struct ProductSummary: Codable, Hashable, Sendable {
    public var id: UUID
    public var name: String
    public var price: Money
    public var preparationStationId: String?
    public var colorHex: String?
}

public struct GridSlot: Codable, Hashable, Sendable, Identifiable {
    public var id: UUID?
    public var gridLayoutId: UUID?
    public var productId: UUID?
    public var rowIndex: Int
    public var columnIndex: Int
    public var slotIndex: Int?
    public var customLabel: String?
    public var customColorHex: String?
    public var isDisabled: Bool?
    public var product: ProductSummary?

    public init(id: UUID? = UUID(), gridLayoutId: UUID? = nil, productId: UUID?, rowIndex: Int, columnIndex: Int, slotIndex: Int? = nil, customLabel: String? = nil, customColorHex: String? = nil, isDisabled: Bool? = false, product: ProductSummary? = nil) {
        self.id = id
        self.gridLayoutId = gridLayoutId
        self.productId = productId
        self.rowIndex = rowIndex
        self.columnIndex = columnIndex
        self.slotIndex = slotIndex
        self.customLabel = customLabel
        self.customColorHex = customColorHex
        self.isDisabled = isDisabled
        self.product = product
    }

    public var position: GridPosition { GridPosition(row: rowIndex, column: columnIndex) }
}

public struct GridPosition: Hashable, Sendable, Codable {
    public var row: Int
    public var column: Int
    public init(row: Int, column: Int) { self.row = row; self.column = column }
}

public struct TouchGridLayout: Codable, Hashable, Sendable, Identifiable {
    public var id: UUID
    public var categoryId: String
    public var name: String?
    public var columnsCount: Int
    public var rowsCount: Int
    public var pageIndex: Int
    public var totalPages: Int
    public var version: Int?
    public var slots: [GridSlot]

    public init(id: UUID = UUID(), categoryId: String, name: String? = nil, columnsCount: Int = 4, rowsCount: Int = 4, pageIndex: Int = 0, totalPages: Int = 1, version: Int? = 1, slots: [GridSlot] = []) {
        self.id = id
        self.categoryId = categoryId
        self.name = name
        self.columnsCount = columnsCount
        self.rowsCount = rowsCount
        self.pageIndex = pageIndex
        self.totalPages = totalPages
        self.version = version
        self.slots = slots
    }

    public func slot(at position: GridPosition) -> GridSlot? {
        slots.first { $0.rowIndex == position.row && $0.columnIndex == position.column }
    }

    /// Toutes les positions de la matrice, dans l'ordre de lecture.
    public var positions: [GridPosition] {
        (0..<(max(1, rowsCount) * max(1, columnsCount))).map {
            GridPosition(row: $0 / max(1, columnsCount), column: $0 % max(1, columnsCount))
        }
    }
}

public struct UpdateGridSlotItem: Codable, Hashable, Sendable {
    public var rowIndex: Int
    public var columnIndex: Int
    public var productId: UUID?
    public var customLabel: String?
    public var customColorHex: String?

    public init(rowIndex: Int, columnIndex: Int, productId: UUID?, customLabel: String? = nil, customColorHex: String? = nil) {
        self.rowIndex = rowIndex
        self.columnIndex = columnIndex
        self.productId = productId
        self.customLabel = customLabel
        self.customColorHex = customColorHex
    }

    public init(_ slot: GridSlot) {
        self.init(rowIndex: slot.rowIndex, columnIndex: slot.columnIndex, productId: slot.productId, customLabel: slot.customLabel, customColorHex: slot.customColorHex)
    }
}

public struct UpdateGridLayoutRequest: Codable, Hashable, Sendable {
    public var categoryId: String
    public var columnsCount: Int
    public var rowsCount: Int
    public var pageIndex: Int
    public var slots: [UpdateGridSlotItem]

    public init(categoryId: String, columnsCount: Int, rowsCount: Int, pageIndex: Int, slots: [UpdateGridSlotItem]) {
        self.categoryId = categoryId
        self.columnsCount = columnsCount
        self.rowsCount = rowsCount
        self.pageIndex = pageIndex
        self.slots = slots
    }
}
