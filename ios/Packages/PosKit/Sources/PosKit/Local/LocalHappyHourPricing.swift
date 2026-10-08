import Foundation

/// Port de `HappyHourPricingService.CalculatePriceForRule` : prix fixe produit (seulement s'il est plus bas), remise produit,
/// remise catégorie, prix fixe catégorie (seulement s'il est plus bas). Remises arrondies « away from zero », jamais négatives.
enum LocalHappyHourPricing {
    static func price(productId: UUID, categoryId: String, standard: Money, rules: [HappyHourRule]) -> (effective: Money, isHappyHour: Bool) {
        func isProduct(_ rule: HappyHourRule) -> Bool {
            rule.targetType == .product && rule.targetId.caseInsensitiveCompare(productId.uuidString) == .orderedSame
        }
        func isCategory(_ rule: HappyHourRule) -> Bool { rule.targetType == .category && matchesCategory(rule, categoryId) }
        func discounted(_ percent: Decimal) -> Money { standard.multiplied(by: 1 - percent / 100).clampedAtZero() }

        if let fixed = rules.first(where: { isProduct($0) && $0.pricingMode == .fixedPrice && $0.fixedPrice != nil })?.fixedPrice, fixed < standard {
            return (fixed, true)
        }
        if let percent = rules.first(where: { isProduct($0) && $0.pricingMode == .percentageDiscount && ($0.discountPercent ?? 0) > 0 })?.discountPercent {
            return (discounted(percent), true)
        }
        if let percent = rules.first(where: { isCategory($0) && $0.pricingMode == .percentageDiscount && ($0.discountPercent ?? 0) > 0 })?.discountPercent {
            return (discounted(percent), true)
        }
        if let fixed = rules.first(where: { isCategory($0) && $0.pricingMode == .fixedPrice && $0.fixedPrice != nil })?.fixedPrice, fixed < standard {
            return (fixed, true)
        }
        return (standard, false)
    }

    /// Libellé du type de règle, repris tel quel du serveur : toute règle qui n'est pas un prix fixe s'appelle `CategoryDiscount`,
    /// même pour une remise propre à un produit.
    static func ruleType(productId: UUID, categoryId: String, rules: [HappyHourRule]) -> String {
        let rule = rules.first { $0.targetType == .product && $0.targetId.caseInsensitiveCompare(productId.uuidString) == .orderedSame }
            ?? rules.first { $0.targetType == .category && matchesCategory($0, categoryId) }
        return rule?.pricingMode == .fixedPrice ? "FixedPrice" : "CategoryDiscount"
    }

    private static func matchesCategory(_ rule: HappyHourRule, _ categoryId: String) -> Bool {
        rule.targetId.caseInsensitiveCompare(categoryId) == .orderedSame || rule.targetName.caseInsensitiveCompare(categoryId) == .orderedSame
    }
}
