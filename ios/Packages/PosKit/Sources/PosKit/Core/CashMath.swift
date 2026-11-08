import Foundation

/// Calculs de caisse en centimes entiers (miroir de `CashSessionService` du .NET, rejoués sur `cash_reference.json`).
public enum CashMath {
    /// Espèces attendues : fond + Σ règlements espèces gardés (montant imputé, rendu exclu ; négatif pour un avoir) + entrées − sorties.
    public static func expected(openingFloat: Int, cashTenders: [Int], movements: [CashMovement]) -> Int {
        openingFloat + cashTenders.reduce(0, +) + movements.reduce(0) { $0 + ($1.kind == "in" ? $1.amountCents : -$1.amountCents) }
    }

    public static func variance(counted: Int, expected: Int) -> Int { counted - expected }

    public static func total(_ denominations: [CashDenomination]) -> Int { denominations.reduce(0) { $0 + $1.valueCents * $1.quantity } }
}

/// Règles d'alerte (miroir de `AlertService` du .NET). Un seuil est franchi quand la valeur le **dépasse**.
public enum AlertRules {
    /// Remise de commande en % : le plus fort entre la remise de la note (un montant fixe est ramené en pourcentage, borné à 100 ;
    /// commande offerte = 100) et la plus forte remise de ligne non offerte (`maxLinePercent`, ignorée si la commande est offerte).
    public static func discountPercent(type: DiscountType?, value: Decimal, subtotalCents: Int, maxLinePercent: Decimal = 0) -> Int {
        let line = type == .comp ? 0 : rounded(maxLinePercent)
        guard subtotalCents > 0, let type else { return line }
        let global: Int
        switch type {
        case .percentage: global = rounded(value)
        case .comp: global = 100
        case .fixedAmount: global = min(100, rounded(value * 100 * 100 / Decimal(subtotalCents)))
        }
        return max(global, line)
    }

    /// Valeur (centimes) des offerts d'une commande : lignes offertes, ou toute la commande si elle est offerte.
    public static func compCents(lines: [(unitPrice: Int, modifiersExtra: Int, quantity: Int, isComp: Bool)], discountType: DiscountType?) -> Int {
        let value = { (l: (unitPrice: Int, modifiersExtra: Int, quantity: Int, isComp: Bool)) in (l.unitPrice + l.modifiersExtra) * l.quantity }
        if discountType == .comp { return lines.reduce(0) { $0 + value($1) } }
        return lines.filter(\.isComp).reduce(0) { $0 + value($1) }
    }

    public static func periodKey(terminalId: String, closureSequence: Int) -> String { "\(terminalId):\(closureSequence)" }

    public static func dedupeKey(ruleType: String, operatorId: UUID?, periodKey: String) -> String {
        "\(ruleType)|\(operatorId.map { $0.uuidString.lowercased() } ?? "")|\(periodKey)"
    }

    private static func rounded(_ value: Decimal) -> Int {
        var input = value, output = Decimal()
        NSDecimalRound(&output, &input, 0, .plain)
        return NSDecimalNumber(decimal: output).intValue
    }
}

/// Tendances : delta en pourcentage arrondi à 0,1 (demi éloigné de zéro), miroir de `IInsightsService.DeltaPercent`.
public enum InsightsMath {
    public static let minDays = 3

    public static func deltaPercent(current: Int, reference: Int) -> Decimal {
        guard reference != 0 else { return 0 }
        var input = Decimal(current - reference) * 100 / Decimal(reference), output = Decimal()
        NSDecimalRound(&output, &input, 1, .plain)
        return output
    }
}
