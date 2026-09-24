import Foundation

/// Catégorie de la carte (`GET /api/catalog/categories`).
public struct MenuCategory: Identifiable, Hashable, Codable, Sendable {
    public var id: String
    public var name: String
    public var iconName: String?
    public var colorHex: String?
    public var displayOrder: Int
    public var isActive: Bool

    public init(id: String, name: String, iconName: String? = nil, colorHex: String? = nil, displayOrder: Int = 0, isActive: Bool = true) {
        self.id = id
        self.name = name
        self.iconName = iconName
        self.colorHex = colorHex
        self.displayOrder = displayOrder
        self.isActive = isActive
    }

    enum CodingKeys: String, CodingKey { case id, name, iconName, colorHex, displayOrder, isActive }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(String.self, forKey: .id)
        name = try c.decode(String.self, forKey: .name)
        iconName = try c.decodeIfPresent(String.self, forKey: .iconName)
        colorHex = try c.decodeIfPresent(String.self, forKey: .colorHex)
        displayOrder = try c.decodeIfPresent(Int.self, forKey: .displayOrder) ?? 0
        isActive = try c.decodeIfPresent(Bool.self, forKey: .isActive) ?? true
    }
}

/// Article vendable (`GET /api/catalog/products`), avec ses groupes de modificateurs.
public struct Product: Identifiable, Hashable, Codable, Sendable {
    public var id: UUID
    public var name: String
    public var categoryId: String
    public var description: String?
    public var price: Money
    public var taxRatePercent: Decimal
    public var colorHex: String?
    public var displayOrder: Int
    public var isQuickKey: Bool
    public var preparationStationId: String?
    public var isAvailable: Bool
    public var isActive: Bool
    public var modifierGroups: [ModifierGroup]

    public init(
        id: UUID = UUID(),
        name: String,
        categoryId: String,
        description: String? = nil,
        price: Money,
        taxRatePercent: Decimal = 10,
        colorHex: String? = nil,
        displayOrder: Int = 0,
        isQuickKey: Bool = false,
        preparationStationId: String? = nil,
        isAvailable: Bool = true,
        isActive: Bool = true,
        modifierGroups: [ModifierGroup] = []
    ) {
        self.id = id
        self.name = name
        self.categoryId = categoryId
        self.description = description
        self.price = price
        self.taxRatePercent = taxRatePercent
        self.colorHex = colorHex
        self.displayOrder = displayOrder
        self.isQuickKey = isQuickKey
        self.preparationStationId = preparationStationId
        self.isAvailable = isAvailable
        self.isActive = isActive
        self.modifierGroups = modifierGroups
    }

    public var hasModifiers: Bool { !modifierGroups.isEmpty }
    public var isSellable: Bool { isActive && isAvailable }

    enum CodingKeys: String, CodingKey {
        case id, name, categoryId, description, price, taxRatePercent, colorHex, displayOrder
        case isQuickKey, preparationStationId, isAvailable, isActive, modifierGroups
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decode(UUID.self, forKey: .id)
        name = try c.decode(String.self, forKey: .name)
        categoryId = try c.decodeIfPresent(String.self, forKey: .categoryId) ?? ""
        description = try c.decodeIfPresent(String.self, forKey: .description)
        price = try c.decode(Money.self, forKey: .price)
        taxRatePercent = try c.decodeIfPresent(Decimal.self, forKey: .taxRatePercent) ?? 10
        colorHex = try c.decodeIfPresent(String.self, forKey: .colorHex)
        displayOrder = try c.decodeIfPresent(Int.self, forKey: .displayOrder) ?? 0
        isQuickKey = try c.decodeIfPresent(Bool.self, forKey: .isQuickKey) ?? false
        preparationStationId = try c.decodeIfPresent(String.self, forKey: .preparationStationId)
        isAvailable = try c.decodeIfPresent(Bool.self, forKey: .isAvailable) ?? true
        isActive = try c.decodeIfPresent(Bool.self, forKey: .isActive) ?? true
        modifierGroups = try c.decodeIfPresent([ModifierGroup].self, forKey: .modifierGroups) ?? []
    }
}

public struct ModifierGroup: Identifiable, Hashable, Codable, Sendable {
    public var id: UUID
    public var groupName: String
    public var minSelections: Int
    public var maxSelections: Int
    public var isMandatory: Bool
    public var isSingleChoice: Bool
    public var options: [ModifierOption]

    public init(
        id: UUID = UUID(),
        groupName: String,
        minSelections: Int = 0,
        maxSelections: Int = 1,
        isMandatory: Bool = false,
        isSingleChoice: Bool = true,
        options: [ModifierOption]
    ) {
        self.id = id
        self.groupName = groupName
        self.minSelections = minSelections
        self.maxSelections = maxSelections
        self.isMandatory = isMandatory
        self.isSingleChoice = isSingleChoice
        self.options = options
    }

    /// Nombre minimal de choix réellement exigé (un groupe obligatoire exige au moins 1 choix).
    public var requiredSelections: Int { max(minSelections, isMandatory ? 1 : 0) }

    /// Nombre maximal de choix (un groupe à choix unique est plafonné à 1).
    public var allowedSelections: Int {
        if isSingleChoice { return 1 }
        return maxSelections > 0 ? maxSelections : options.count
    }
}

public struct ModifierOption: Identifiable, Hashable, Codable, Sendable {
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
