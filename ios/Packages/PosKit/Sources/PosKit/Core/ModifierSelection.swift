import Foundation

/// État de sélection des options d'un article (cuissons, suppléments…).
/// Logique pure, testée unitairement ; la vue se contente de l'afficher.
public struct ModifierSelection: Hashable, Sendable {
    public enum ToggleError: Error, Equatable, Sendable {
        case maximumReached(group: String, max: Int)
    }

    public enum ValidationError: Error, Equatable, Sendable, LocalizedError {
        case missingSelection(group: String, minimum: Int)

        public var errorDescription: String? {
            switch self {
            case let .missingSelection(group, minimum):
                "Sélectionnez au moins \(minimum) option(s) pour « \(group) »."
            }
        }
    }

    public let product: Product
    public private(set) var selectedOptionIds: Set<UUID>
    public var kitchenComment: String = ""

    public init(product: Product) {
        self.product = product
        self.selectedOptionIds = Set(product.modifierGroups.flatMap { $0.options.filter(\.isDefault).map(\.id) })
    }

    public func isSelected(_ option: ModifierOption) -> Bool { selectedOptionIds.contains(option.id) }

    public func selectedCount(in group: ModifierGroup) -> Int {
        group.options.filter { selectedOptionIds.contains($0.id) }.count
    }

    public mutating func toggle(_ option: ModifierOption, in group: ModifierGroup) throws(ToggleError) {
        let wasSelected = selectedOptionIds.contains(option.id)
        if group.isSingleChoice {
            group.options.forEach { selectedOptionIds.remove($0.id) }
            if !wasSelected || group.isMandatory {
                selectedOptionIds.insert(option.id)
            }
            return
        }
        if wasSelected {
            selectedOptionIds.remove(option.id)
            return
        }
        if group.maxSelections > 0, selectedCount(in: group) >= group.maxSelections {
            throw .maximumReached(group: group.groupName, max: group.maxSelections)
        }
        selectedOptionIds.insert(option.id)
    }

    public func validate() throws(ValidationError) {
        for group in product.modifierGroups {
            let minimum = group.isMandatory ? max(group.minSelections, 1) : group.minSelections
            if minimum > 0, selectedCount(in: group) < minimum {
                throw .missingSelection(group: group.groupName, minimum: minimum)
            }
        }
    }

    public var selectedOptions: [ModifierOption] {
        product.modifierGroups.flatMap { $0.options.filter { selectedOptionIds.contains($0.id) } }
    }

    public var extraTotal: Money { selectedOptions.reduce(.zero) { $0 + $1.extraPrice } }

    /// Libellés envoyés en cuisine : « Double Cheddar (+2,50 €) ».
    public var modifierLabels: [String] {
        selectedOptions.map { $0.extraPrice.cents > 0 ? "\($0.name) (+\($0.extraPrice.formatted))" : $0.name }
    }

    public func effectiveUnitPrice(base: Money) -> Money { base + extraTotal }
}
