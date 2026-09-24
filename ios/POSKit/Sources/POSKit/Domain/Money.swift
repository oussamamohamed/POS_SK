import Foundation

/// Montant monétaire stocké en centimes (entier) pour éviter toute erreur d'arrondi.
///
/// L'API .NET sérialise les montants en euros décimaux (`12.5`) : `Money` se décode et
/// s'encode donc comme un nombre JSON décimal, mais tous les calculs se font en centimes.
public struct Money: Hashable, Comparable, Sendable {
    public var cents: Int64

    public init(cents: Int64) {
        self.cents = cents
    }

    /// Crée un montant à partir d'euros décimaux, arrondi au centime (demi à l'écart de zéro).
    public init(euros: Decimal) {
        let negative = euros < 0
        var scaled = (negative ? -euros : euros) * 100
        var rounded = Decimal()
        NSDecimalRound(&rounded, &scaled, 0, .plain)
        let value = NSDecimalNumber(decimal: rounded).int64Value
        self.cents = negative ? -value : value
    }

    public init(euros: Double) {
        self.init(euros: Decimal(string: String(format: "%.4f", euros)) ?? Decimal(euros))
    }

    public static let zero = Money(cents: 0)

    public var decimal: Decimal { Decimal(cents) / 100 }
    public var isZero: Bool { cents == 0 }
    public var isPositive: Bool { cents > 0 }

    public static func + (lhs: Money, rhs: Money) -> Money { Money(cents: lhs.cents + rhs.cents) }
    public static func - (lhs: Money, rhs: Money) -> Money { Money(cents: lhs.cents - rhs.cents) }
    public static func * (lhs: Money, rhs: Int) -> Money { Money(cents: lhs.cents * Int64(rhs)) }
    public static func += (lhs: inout Money, rhs: Money) { lhs.cents += rhs.cents }
    public static func -= (lhs: inout Money, rhs: Money) { lhs.cents -= rhs.cents }
    public static prefix func - (value: Money) -> Money { Money(cents: -value.cents) }
    public static func < (lhs: Money, rhs: Money) -> Bool { lhs.cents < rhs.cents }

    public static func max(_ lhs: Money, _ rhs: Money) -> Money { lhs >= rhs ? lhs : rhs }
    public static func min(_ lhs: Money, _ rhs: Money) -> Money { lhs <= rhs ? lhs : rhs }

    /// Applique un pourcentage de remise (ex. 10 pour -10 %), arrondi au centime.
    public func applyingDiscount(percent: Decimal) -> Money {
        guard percent > 0 else { return self }
        let factor = (100 - Swift.min(percent, 100)) / 100
        return Money(euros: decimal * factor)
    }

    /// Format français : `1 234,50 €` (espace fine insécable comme séparateur de milliers).
    public var formatted: String {
        let negative = cents < 0
        let absolute = negative ? -cents : cents
        let units = absolute / 100
        let fraction = absolute % 100
        var digits = String(units)
        var grouped = ""
        while digits.count > 3 {
            grouped = "\u{202F}" + String(digits.suffix(3)) + grouped
            digits = String(digits.dropLast(3))
        }
        grouped = digits + grouped
        let fractionText = fraction < 10 ? "0\(fraction)" : "\(fraction)"
        return "\(negative ? "-" : "")\(grouped),\(fractionText)\u{00A0}€"
    }
}

extension Money: Codable {
    public init(from decoder: Decoder) throws {
        let container = try decoder.singleValueContainer()
        if let value = try? container.decode(Decimal.self) {
            self.init(euros: value)
        } else if let text = try? container.decode(String.self),
                  let value = Decimal(string: text, locale: Locale(identifier: "en_US_POSIX")) {
            self.init(euros: value)
        } else {
            let cents = try container.decode(CentsAmount.self)
            self.init(cents: cents.amountInCents)
        }
    }

    public func encode(to encoder: Encoder) throws {
        var container = encoder.singleValueContainer()
        try container.encode(decimal)
    }
}

extension Money: CustomStringConvertible {
    public var description: String { formatted }
}

/// Représentation brute du value object `Money` .NET (`{ "amountInCents": 1250, "currency": "EUR" }`).
struct CentsAmount: Codable, Sendable {
    var amountInCents: Int64
    var currency: String?
}
