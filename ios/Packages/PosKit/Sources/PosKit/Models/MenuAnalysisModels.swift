import Foundation

/// Miroir de `MenuClass` (sérialisé en nombre) : classes de l'ingénierie de menu (Kasavana-Smith).
public enum MenuClass: Int, Codable, Hashable, Sendable, CaseIterable {
    case star = 0, plowhorse, puzzle, dog
}

public struct MenuAnalysisItem: Codable, Identifiable, Hashable, Sendable {
    public var productId: UUID
    public var name: String
    public var categoryName: String?
    public var quantity: Int
    public var share: Decimal
    public var revenueHt: Money
    public var unitCost: Money?
    public var unitMarginHt: Money?
    public var marginHt: Money?
    public var popular: Bool
    public var profitable: Bool?
    /// `nil` = coût manquant : classé sur la seule popularité.
    public var `class`: MenuClass?
    public var missingCost: Bool

    public var id: UUID { productId }

    public init(productId: UUID, name: String, categoryName: String? = nil, quantity: Int, share: Decimal, revenueHt: Money, unitCost: Money? = nil, unitMarginHt: Money? = nil, marginHt: Money? = nil, popular: Bool, profitable: Bool? = nil, class menuClass: MenuClass? = nil, missingCost: Bool) {
        self.productId = productId
        self.name = name
        self.categoryName = categoryName
        self.quantity = quantity
        self.share = share
        self.revenueHt = revenueHt
        self.unitCost = unitCost
        self.unitMarginHt = unitMarginHt
        self.marginHt = marginHt
        self.popular = popular
        self.profitable = profitable
        self.class = menuClass
        self.missingCost = missingCost
    }
}

public struct MenuAnalysisReport: Codable, Hashable, Sendable {
    public var fromUtc: Date
    public var toUtc: Date
    public var popularityThreshold: Decimal
    public var averageUnitMarginHt: Money?
    public var missingCostCount: Int
    public var items: [MenuAnalysisItem]

    public init(fromUtc: Date, toUtc: Date, popularityThreshold: Decimal, averageUnitMarginHt: Money?, missingCostCount: Int, items: [MenuAnalysisItem]) {
        self.fromUtc = fromUtc
        self.toUtc = toUtc
        self.popularityThreshold = popularityThreshold
        self.averageUnitMarginHt = averageUnitMarginHt
        self.missingCostCount = missingCostCount
        self.items = items
    }
}

/// Ventes agrégées d'un plat sur la période : entrée du calcul des classes.
public struct MenuSale: Sendable {
    public var productId: UUID
    public var name: String
    public var categoryName: String?
    public var quantity: Int
    /// Chiffre d'affaires HT en centimes, non arrondi (somme des lignes).
    public var revenueHtCents: Decimal
    public var unitCostCents: Int?

    public init(productId: UUID, name: String, categoryName: String?, quantity: Int, revenueHtCents: Decimal, unitCostCents: Int?) {
        self.productId = productId
        self.name = name
        self.categoryName = categoryName
        self.quantity = quantity
        self.revenueHtCents = revenueHtCents
        self.unitCostCents = unitCostCents
    }
}

/// Calcul des classes, partagé par `InMemoryPosAPI` et `LocalPosAPI` ; miroir de `MenuAnalysisService` (.NET).
public enum MenuAnalysisCalculator {
    /// Un plat est « populaire » s'il pèse au moins 70 % de la part moyenne (règle de Kasavana-Smith).
    // Décimal exact : un littéral `0.7` passerait par un Double (0,6999…).
    static let popularityFactor = Decimal(7) / Decimal(10)

    public static func report(from: Date, to: Date, sales: [MenuSale]) -> MenuAnalysisReport {
        let totalQuantity = sales.reduce(0) { $0 + $1.quantity }
        let threshold: Decimal = sales.isEmpty ? 0 : popularityFactor / Decimal(sales.count)

        func margin(_ s: MenuSale) -> Decimal? { s.unitCostCents.map { s.revenueHtCents - Decimal($0) * Decimal(s.quantity) } }
        let costed = sales.filter { $0.unitCostCents != nil }
        let costedQuantity = costed.reduce(0) { $0 + $1.quantity }
        let averageCents: Decimal? = costedQuantity > 0 ? costed.reduce(Decimal(0)) { $0 + (margin($1) ?? 0) } / Decimal(costedQuantity) : nil

        let items = sales.map { s -> MenuAnalysisItem in
            let share: Decimal = totalQuantity > 0 ? Decimal(s.quantity) / Decimal(totalQuantity) : 0
            let popular = share >= threshold
            let marginCents = margin(s)
            let unitMarginCents = marginCents.map { $0 / Decimal(s.quantity) }
            let profitable: Bool? = (unitMarginCents != nil && averageCents != nil) ? unitMarginCents! >= averageCents! : nil
            let menuClass: MenuClass? = profitable.map { p in popular ? (p ? .star : .plowhorse) : (p ? .puzzle : .dog) }
            return MenuAnalysisItem(
                productId: s.productId, name: s.name, categoryName: s.categoryName, quantity: s.quantity,
                share: round(share, 4), revenueHt: euros(s.revenueHtCents),
                unitCost: s.unitCostCents.map { euros(Decimal($0)) },
                unitMarginHt: unitMarginCents.map(euros), marginHt: marginCents.map(euros),
                popular: popular, profitable: profitable, class: menuClass, missingCost: s.unitCostCents == nil
            )
        }.sorted { a, b in
            a.quantity != b.quantity ? a.quantity > b.quantity : a.name.localizedCaseInsensitiveCompare(b.name) == .orderedAscending
        }

        return MenuAnalysisReport(
            fromUtc: from, toUtc: to, popularityThreshold: round(threshold, 4),
            averageUnitMarginHt: averageCents.map(euros), missingCostCount: items.filter(\.missingCost).count, items: items
        )
    }

    private static func round(_ value: Decimal, _ scale: Int) -> Decimal {
        var input = value, output = Decimal()
        NSDecimalRound(&output, &input, scale, .plain)
        return output
    }

    /// Centimes → euros arrondis au centime, à l'écart de zéro (`Math.Round(cents / 100m, 2, AwayFromZero)`).
    private static func euros(_ cents: Decimal) -> Money { Money(cents: NSDecimalNumber(decimal: round(cents, 0)).intValue) }
}
