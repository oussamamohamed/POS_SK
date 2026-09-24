import Foundation

/// Montant en centimes d'euro. Toute l'arithmétique se fait en entiers pour
/// garantir « zéro écart de centime » (même règle que `Money` côté .NET).
public struct Money: Hashable, Comparable, Sendable, Codable, CustomStringConvertible {
    public var cents: Int

    public init(cents: Int) { self.cents = cents }

    /// Construit à partir d'euros décimaux, arrondi « away from zero » comme `Money.FromDecimal`.
    public init(euros: Decimal) {
        self.cents = Money.roundAwayFromZero(euros * 100)
    }

    public init(euros: Double) {
        self.init(euros: Decimal(string: String(format: "%.4f", euros)) ?? Decimal(euros))
    }

    public static let zero = Money(cents: 0)

    public var euros: Decimal { Decimal(cents) / 100 }
    public var doubleValue: Double { Double(cents) / 100 }
    public var isZero: Bool { cents == 0 }

    public static func + (a: Money, b: Money) -> Money { Money(cents: a.cents + b.cents) }
    public static func - (a: Money, b: Money) -> Money { Money(cents: a.cents - b.cents) }
    public static func * (a: Money, q: Int) -> Money { Money(cents: a.cents * q) }
    public static func += (a: inout Money, b: Money) { a.cents += b.cents }
    public static func -= (a: inout Money, b: Money) { a.cents -= b.cents }
    public static func < (a: Money, b: Money) -> Bool { a.cents < b.cents }

    /// Multiplie par un facteur décimal avec arrondi « away from zero ».
    public func multiplied(by factor: Decimal) -> Money {
        Money(cents: Money.roundAwayFromZero(Decimal(cents) * factor))
    }

    public func clampedAtZero() -> Money { Money(cents: max(0, cents)) }

    // MARK: Codable — l'API expose les montants en euros décimaux (ex. 19.5)

    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if let decimal = try? container.decode(Decimal.self) {
            self.init(euros: decimal)
        } else {
            self.init(euros: try container.decode(Double.self))
        }
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        try container.encode(euros)
    }

    // MARK: Formatage

    public var description: String { formatted }

    /// « 19,50 € »
    public var formatted: String { Money.formatter.string(from: euros as NSDecimalNumber) ?? "\(euros) €" }

    /// « 19.50 » — utile pour les champs de saisie.
    public var plain: String { String(format: "%.2f", doubleValue) }

    private static let formatter: NumberFormatter = {
        let f = NumberFormatter()
        f.numberStyle = .currency
        f.currencyCode = "EUR"
        f.locale = Locale(identifier: "fr_FR")
        f.minimumFractionDigits = 2
        f.maximumFractionDigits = 2
        return f
    }()

    static func roundAwayFromZero(_ value: Decimal) -> Int {
        var input = value
        var result = Decimal()
        NSDecimalRound(&result, &input, 0, .plain) // .plain = half away from zero
        return NSDecimalNumber(decimal: result).intValue
    }

    /// Parse une saisie utilisateur (« 12,5 », « 12.50 », « 12 ») en montant.
    public static func parse(_ text: String) -> Money? {
        let normalized = text
            .replacingOccurrences(of: ",", with: ".")
            .replacingOccurrences(of: "€", with: "")
            .trimmingCharacters(in: .whitespaces)
        guard !normalized.isEmpty, let decimal = Decimal(string: normalized, locale: Locale(identifier: "en_US_POSIX")) else {
            return nil
        }
        return Money(euros: decimal)
    }
}

/// Montant sérialisé en objet `{ "amountInCents": 600, "currency": "EUR" }` (ex. commandes en attente).
public struct CentsAmount: Codable, Hashable, Sendable {
    public var amountInCents: Int
    public var currency: String?

    public var money: Money { Money(cents: amountInCents) }
}
