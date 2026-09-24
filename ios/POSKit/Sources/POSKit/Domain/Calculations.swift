import Foundation

/// Totaux d'une commande. Les prix de la carte sont TTC ; HT et TVA sont déduits ligne par ligne,
/// avec arrondi au centime par ligne, exactement comme `TableManagementService` côté serveur.
public struct OrderTotals: Hashable, Sendable {
    public var ht: Money
    public var vat: Money
    public var ttc: Money
    /// TVA par taux (clé : taux en %).
    public var vatByRate: [Decimal: Money]

    public init(ht: Money = .zero, vat: Money = .zero, ttc: Money = .zero, vatByRate: [Decimal: Money] = [:]) {
        self.ht = ht
        self.vat = vat
        self.ttc = ttc
        self.vatByRate = vatByRate
    }

    public static let zero = OrderTotals()

    public static func + (lhs: OrderTotals, rhs: OrderTotals) -> OrderTotals {
        OrderTotals(
            ht: lhs.ht + rhs.ht,
            vat: lhs.vat + rhs.vat,
            ttc: lhs.ttc + rhs.ttc,
            vatByRate: lhs.vatByRate.merging(rhs.vatByRate, uniquingKeysWith: +)
        )
    }
}

public enum TaxCalculator {
    /// Montant HT contenu dans un montant TTC pour un taux donné (arrondi au centime).
    public static func ht(fromTTC ttc: Money, ratePercent: Decimal) -> Money {
        guard ratePercent > 0 else { return ttc }
        return Money(euros: ttc.decimal / (1 + ratePercent / 100))
    }

    /// Totaux pour une liste de lignes (montant TTC, taux de TVA).
    public static func totals(_ lines: [(ttc: Money, ratePercent: Decimal)]) -> OrderTotals {
        lines.reduce(into: OrderTotals.zero) { result, line in
            let ht = ht(fromTTC: line.ttc, ratePercent: line.ratePercent)
            let vat = line.ttc - ht
            result.ttc += line.ttc
            result.ht += ht
            result.vat += vat
            result.vatByRate[line.ratePercent, default: .zero] += vat
        }
    }

    /// Libellé d'un taux (`10.0` → `10 %`, `5.5` → `5,5 %`).
    public static func rateLabel(_ rate: Decimal) -> String {
        var value = rate
        var rounded = Decimal()
        NSDecimalRound(&rounded, &value, 2, .plain)
        let text = NSDecimalNumber(decimal: rounded).stringValue.replacingOccurrences(of: ".", with: ",")
        return "\(text) %"
    }

    public static func rateLabel(_ raw: String) -> String {
        guard let rate = Decimal(string: raw, locale: Locale(identifier: "en_US_POSIX")) else { return raw }
        return rateLabel(rate)
    }
}

public enum SplitCalculator {
    /// Partage à parts égales au centime près : les centimes restants vont aux premiers convives.
    /// La somme des parts est toujours égale au total (tolérance zéro, exigence NF525).
    public static func equalParts(of total: Money, count: Int) -> [Money] {
        guard count > 0, total.cents > 0 else { return [] }
        let base = total.cents / Int64(count)
        let remainder = Int(total.cents % Int64(count))
        return (0..<count).map { index in Money(cents: base + (index < remainder ? 1 : 0)) }
    }
}

/// Suggestions de billets pour un paiement espèces (montant exact puis arrondis usuels).
public enum CashSuggestions {
    public static func amounts(for due: Money, limit: Int = 4) -> [Money] {
        guard due.cents > 0 else { return [] }
        var result: [Money] = [due]
        for step in [500, 1000, 2000, 5000, 10000] as [Int64] {
            let rounded = Money(cents: ((due.cents + step - 1) / step) * step)
            if !result.contains(rounded) {
                result.append(rounded)
            }
            if result.count >= limit { break }
        }
        return Array(result.prefix(limit))
    }
}

/// Saisie d'un montant façon terminal de paiement : les chiffres entrent par la droite
/// (`2`, `0`, `0`, `0` → 20,00 €). Évite le clavier système qui masquerait l'écran.
public struct AmountEntry: Hashable, Sendable {
    public private(set) var digits: String = ""
    public let maxDigits: Int

    public init(maxDigits: Int = 7) {
        self.maxDigits = maxDigits
    }

    public var isEmpty: Bool { digits.isEmpty }
    public var amount: Money { Money(cents: Int64(digits) ?? 0) }

    public mutating func append(_ digit: Int) {
        guard (0...9).contains(digit), digits.count < maxDigits else { return }
        if digits.isEmpty && digit == 0 { return }
        digits.append(String(digit))
    }

    public mutating func appendDoubleZero() {
        guard !digits.isEmpty else { return }
        append(0)
        append(0)
    }

    public mutating func backspace() {
        if !digits.isEmpty { digits.removeLast() }
    }

    public mutating func clear() {
        digits = ""
    }

    public mutating func set(_ money: Money) {
        digits = money.cents > 0 ? String(money.cents) : ""
    }
}

/// Saisie du code PIN opérateur (4 à 6 chiffres).
public struct PinEntry: Hashable, Sendable {
    public static let minLength = 4
    public static let maxLength = 6

    public private(set) var code: String = ""

    public init() {}

    public var count: Int { code.count }
    public var isComplete: Bool { code.count >= Self.minLength }

    /// Ajoute un chiffre ; renvoie `true` si le code a atteint la longueur de validation automatique.
    @discardableResult
    public mutating func append(_ digit: Int, autoSubmitLength: Int = PinEntry.minLength) -> Bool {
        guard (0...9).contains(digit), code.count < Self.maxLength else { return false }
        code.append(String(digit))
        return code.count == min(max(autoSubmitLength, Self.minLength), Self.maxLength)
    }

    public mutating func backspace() {
        if !code.isEmpty { code.removeLast() }
    }

    public mutating func clear() {
        code = ""
    }
}
