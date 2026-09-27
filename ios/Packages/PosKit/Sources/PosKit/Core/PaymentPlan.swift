import Foundation

/// Calcul du montant à encaisser : pourboire, partage à parts égales et suivi des parts réglées.
public struct PaymentPlan: Hashable, Sendable {
    public enum Tip: Hashable, Sendable {
        case none
        case percent(Int)
        case custom(Money)
    }

    /// Montant dû au moment de l'ouverture de l'encaissement.
    public var due: Money
    public var tip: Tip = .none
    /// Nombre de convives pour un partage égal (`nil` = pas de partage).
    public var splitGuests: Int?
    /// Parts déjà réglées (le montant `due` initial est conservé pour garder des parts stables).
    public var paidParts = 0

    public init(due: Money, tip: Tip = .none, splitGuests: Int? = nil) {
        self.due = due
        self.tip = tip
        self.splitGuests = splitGuests
    }

    public static let tipPercentages = [0, 5, 10, 15]
    public static let splitRange = 2...12

    public var tipAmount: Money {
        guard splitGuests == nil else { return .zero }
        switch tip {
        case .none: return .zero
        case .percent(let p): return OrderMath.tip(on: due, percent: p)
        case .custom(let amount): return amount.clampedAtZero()
        }
    }

    public var totalWithTip: Money { due + tipAmount }

    public var parts: [Money] {
        guard let guests = splitGuests else { return [] }
        return OrderMath.splitEqually(due, parts: guests)
    }

    public var isSplit: Bool { splitGuests != nil }
    public var remainingParts: Int { max(0, (splitGuests ?? 0) - paidParts) }

    /// Montant de la prochaine encaisse : part courante en split, sinon total + pourboire.
    public var amountToCollect: Money {
        if isSplit { return parts[safe: paidParts] ?? .zero }
        return totalWithTip
    }

    public var partLabel: String? {
        guard let guests = splitGuests else { return nil }
        return "Part \(min(paidParts + 1, guests))/\(guests)"
    }

    public mutating func markPartPaid() { paidParts += 1 }

    public mutating func setGuests(_ count: Int?) {
        splitGuests = count.map { min(max($0, Self.splitRange.lowerBound), Self.splitRange.upperBound) }
        paidParts = 0
    }
}
