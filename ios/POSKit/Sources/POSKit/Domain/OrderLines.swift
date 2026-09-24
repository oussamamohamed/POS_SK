import Foundation

/// Ligne saisie sur l'iPad mais pas encore enregistrée sur le serveur.
public struct PendingLine: Identifiable, Hashable, Sendable {
    public let id: UUID
    public var product: Product
    public var quantity: Int
    public var modifiers: [ModifierOption]
    public var note: String?
    public var course: CourseType

    public init(
        id: UUID = UUID(),
        product: Product,
        quantity: Int = 1,
        modifiers: [ModifierOption] = [],
        note: String? = nil,
        course: CourseType = .direct
    ) {
        self.id = id
        self.product = product
        self.quantity = quantity
        self.modifiers = modifiers
        let trimmed = note?.trimmingCharacters(in: .whitespacesAndNewlines)
        self.note = (trimmed?.isEmpty ?? true) ? nil : trimmed
        self.course = course
    }

    public var modifiersExtra: Money { modifiers.reduce(.zero) { $0 + $1.extraPrice } }
    public var unitTotal: Money { product.price + modifiersExtra }
    public var lineTotal: Money { unitTotal * quantity }

    /// Libellés transmis au serveur et affichés en cuisine.
    public var modifierLabels: [String] {
        modifiers.map(\.name) + (note.map { ["Note : \($0)"] } ?? [])
    }

    /// Deux saisies identiques (même article, options, note et envoi) sont cumulées.
    public func canMerge(with other: PendingLine) -> Bool {
        product.id == other.product.id
            && modifiers.map(\.id) == other.modifiers.map(\.id)
            && note == other.note
            && course == other.course
    }

    public var input: OrderItemInput {
        OrderItemInput(
            productId: product.id,
            productName: product.name,
            quantity: quantity,
            unitPrice: product.price,
            taxRatePercent: product.taxRatePercent,
            preparationStationId: product.preparationStationId,
            modifiers: modifierLabels,
            course: course,
            modifiersPriceExtra: modifiersExtra
        )
    }
}

/// Sélection des modificateurs d'un article (cuissons, suppléments…), avec les règles
/// min/max/obligatoire définies en back-office.
public struct ModifierSelection: Hashable, Sendable {
    public let product: Product
    public private(set) var selectedByGroup: [UUID: [UUID]]
    public var note: String = ""

    public init(product: Product) {
        self.product = product
        var initial: [UUID: [UUID]] = [:]
        for group in product.modifierGroups {
            let defaults = group.options.filter(\.isDefault).map(\.id)
            initial[group.id] = Array(defaults.prefix(group.allowedSelections))
        }
        selectedByGroup = initial
    }

    public func isSelected(_ option: ModifierOption, in group: ModifierGroup) -> Bool {
        selectedByGroup[group.id, default: []].contains(option.id)
    }

    public func selectionCount(in group: ModifierGroup) -> Int {
        selectedByGroup[group.id, default: []].count
    }

    /// Bascule une option. Renvoie `false` si le maximum du groupe est déjà atteint.
    @discardableResult
    public mutating func toggle(_ option: ModifierOption, in group: ModifierGroup) -> Bool {
        var current = selectedByGroup[group.id, default: []]
        if let index = current.firstIndex(of: option.id) {
            // Un choix unique obligatoire ne peut pas être vidé : on garde la sélection.
            if group.isSingleChoice && group.requiredSelections > 0 { return true }
            current.remove(at: index)
        } else if group.isSingleChoice {
            current = [option.id]
        } else if current.count < group.allowedSelections {
            current.append(option.id)
        } else {
            return false
        }
        selectedByGroup[group.id] = current
        return true
    }

    /// Groupes dont le minimum n'est pas atteint.
    public var missingGroups: [ModifierGroup] {
        product.modifierGroups.filter { selectionCount(in: $0) < $0.requiredSelections }
    }

    public var isValid: Bool { missingGroups.isEmpty }

    /// Options retenues, dans l'ordre d'affichage des groupes puis des options.
    public var selectedOptions: [ModifierOption] {
        product.modifierGroups.flatMap { group in
            group.options.filter { isSelected($0, in: group) }
        }
    }

    public var extraPrice: Money { selectedOptions.reduce(.zero) { $0 + $1.extraPrice } }
    public var unitTotal: Money { product.price + extraPrice }
}
