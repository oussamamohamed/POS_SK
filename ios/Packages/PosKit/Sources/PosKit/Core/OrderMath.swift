import Foundation

/// Ligne de ticket côté iPad : soit déjà persistée (`serverLineId != nil`), soit en brouillon local.
///
/// L'API n'expose que l'ajout de lignes (pas de suppression ni de décrément). Les lignes en
/// brouillon restent donc entièrement modifiables en local et ne sont envoyées au serveur qu'au
/// moment d'un « commit » (envoi cuisine, paiement, remise, mise en attente, transfert…).
public struct CartLine: Identifiable, Hashable, Sendable {
    public var id: UUID
    public var serverLineId: UUID?
    public var productId: UUID
    public var name: String
    /// Prix unitaire effectivement facturé (tarif Happy Hour inclus), hors options.
    public var unitPrice: Money
    public var taxRatePercent: Decimal
    public var taxRateTakeawayPercent: Decimal?
    public var station: String
    public var modifiers: [String]
    public var modifiersExtra: Money
    public var kitchenComment: String?
    public var course: CourseType
    public var quantity: Int
    public var isDispatched: Bool
    public var isComp: Bool
    public var discountPercent: Decimal
    public var isHappyHourApplied: Bool
    public var originalUnitPrice: Money?
    public var happyHourScheduleId: UUID?

    public init(id: UUID = UUID(), serverLineId: UUID? = nil, productId: UUID, name: String, unitPrice: Money, taxRatePercent: Decimal, taxRateTakeawayPercent: Decimal? = nil, station: String = "HOT_KITCHEN", modifiers: [String] = [], modifiersExtra: Money = .zero, kitchenComment: String? = nil, course: CourseType = .direct, quantity: Int = 1, isDispatched: Bool = false, isComp: Bool = false, discountPercent: Decimal = 0, isHappyHourApplied: Bool = false, originalUnitPrice: Money? = nil, happyHourScheduleId: UUID? = nil) {
        self.id = id
        self.serverLineId = serverLineId
        self.productId = productId
        self.name = name
        self.unitPrice = unitPrice
        self.taxRatePercent = taxRatePercent
        self.taxRateTakeawayPercent = taxRateTakeawayPercent
        self.station = station
        self.modifiers = modifiers
        self.modifiersExtra = modifiersExtra
        self.kitchenComment = kitchenComment
        self.course = course
        self.quantity = quantity
        self.isDispatched = isDispatched
        self.isComp = isComp
        self.discountPercent = discountPercent
        self.isHappyHourApplied = isHappyHourApplied
        self.originalUnitPrice = originalUnitPrice
        self.happyHourScheduleId = happyHourScheduleId
    }

    public init(serverLine l: OrderLine) {
        self.init(
            id: l.lineId, serverLineId: l.lineId, productId: l.productId, name: l.productName,
            unitPrice: l.unitPrice, taxRatePercent: l.taxRatePercent, taxRateTakeawayPercent: l.taxRateTakeawayPercent,
            station: l.preparationStationId ?? "HOT_KITCHEN", modifiers: l.modifiersSummary, modifiersExtra: l.modifiersPriceExtra,
            course: l.course, quantity: l.quantity, isDispatched: l.isDispatched, isComp: l.isComp,
            discountPercent: l.discountPercent, isHappyHourApplied: l.isHappyHourApplied,
            originalUnitPrice: l.originalUnitPrice, happyHourScheduleId: l.appliedHappyHourScheduleId
        )
    }

    public var isDraft: Bool { serverLineId == nil }
    public var effectiveUnitPrice: Money { unitPrice + modifiersExtra }

    /// Deux lignes brouillon identiques fusionnent (même règle que le serveur).
    public func canMerge(with other: CartLine) -> Bool {
        isDraft && other.isDraft
            && productId == other.productId
            && course == other.course
            && unitPrice == other.unitPrice
            && modifiersExtra == other.modifiersExtra
            && isHappyHourApplied == other.isHappyHourApplied
            && modifiers.sorted() == other.modifiers.sorted()
            && (kitchenComment ?? "") == (other.kitchenComment ?? "")
    }

    /// Payload `OrderItemInputDto`. Le commentaire cuisine voyage dans les modificateurs
    /// (l'API n'a pas de champ dédié) pour apparaître sur le ticket KDS.
    public var asInput: OrderItemInput {
        var mods = modifiers
        if let comment = kitchenComment?.trimmingCharacters(in: .whitespacesAndNewlines), !comment.isEmpty {
            mods.append("💬 \(comment)")
        }
        return OrderItemInput(
            productId: productId, productName: name, quantity: quantity, unitPrice: unitPrice,
            taxRatePercent: taxRatePercent, preparationStationId: station, modifiers: mods, course: course,
            modifiersPriceExtra: modifiersExtra, taxRateTakeawayPercent: taxRateTakeawayPercent,
            isHappyHourApplied: isHappyHourApplied, originalUnitPrice: originalUnitPrice,
            appliedHappyHourScheduleId: happyHourScheduleId
        )
    }
}

public struct VatLine: Hashable, Sendable {
    public var ratePercent: Decimal
    public var baseHt: Money
    public var vat: Money
    public var ttc: Money
}

public struct OrderTotals: Hashable, Sendable {
    public var subtotalTtc: Money
    public var discountAmount: Money
    public var totalTtc: Money
    public var totalHt: Money
    public var totalVat: Money
    public var vatLines: [VatLine]

    public static let zero = OrderTotals(subtotalTtc: .zero, discountAmount: .zero, totalTtc: .zero, totalHt: .zero, totalVat: .zero, vatLines: [])
}

/// Calculs monétaires — miroir de `Order.CalculateTotalTtc` / `TaxBreakdownItem.Calculate` (.NET).
public enum OrderMath {

    public static func lineTotal(_ line: CartLine) -> Money {
        if line.isComp { return .zero }
        let base = line.effectiveUnitPrice * line.quantity
        guard line.discountPercent > 0 else { return base }
        return base.multiplied(by: 1 - line.discountPercent / 100).clampedAtZero()
    }

    public static func effectiveTaxRate(_ line: CartLine, destination: OrderDestination) -> Decimal {
        if destination == .takeaway, let takeaway = line.taxRateTakeawayPercent { return takeaway }
        return line.taxRatePercent
    }

    /// Ventile un TTC en HT + TVA (arrondi au centime, TVA = TTC − HT).
    public static func split(ttc: Money, ratePercent: Decimal) -> (ht: Money, vat: Money) {
        let divisor = 1 + ratePercent / 100
        let ht = Money(cents: Money.roundAwayFromZero(Decimal(ttc.cents) / divisor))
        return (ht, ttc - ht)
    }

    public static func discountedTotal(subtotal: Money, discount: GlobalDiscount?) -> Money {
        guard let discount, discount.value > 0 else { return subtotal }
        switch discount.type {
        case .percentage:
            return subtotal.multiplied(by: 1 - discount.value / 100).clampedAtZero()
        case .fixedAmount:
            return (subtotal - Money(euros: discount.value)).clampedAtZero()
        case .comp:
            return .zero
        }
    }

    public static func totals(lines: [CartLine], destination: OrderDestination, discount: GlobalDiscount?) -> OrderTotals {
        let subtotal = lines.reduce(Money.zero) { $0 + lineTotal($1) }
        let total = discountedTotal(subtotal: subtotal, discount: discount)

        // Regroupement par taux effectif, puis répartition de la remise au prorata du TTC.
        var groups: [Decimal: Money] = [:]
        for line in lines {
            groups[effectiveTaxRate(line, destination: destination), default: .zero] += lineTotal(line)
        }
        let rates = groups.keys.sorted()
        var vatLines: [VatLine] = []
        var allocated = Money.zero
        for (index, rate) in rates.enumerated() {
            let groupTtc = groups[rate]!
            let share: Money
            if index == rates.count - 1 {
                share = total - allocated
            } else if subtotal.cents > 0 {
                share = Money(cents: Money.roundAwayFromZero(Decimal(groupTtc.cents) * Decimal(total.cents) / Decimal(subtotal.cents)))
            } else {
                share = .zero
            }
            allocated += share
            let parts = split(ttc: share, ratePercent: rate)
            vatLines.append(VatLine(ratePercent: rate, baseHt: parts.ht, vat: parts.vat, ttc: share))
        }

        let ht = vatLines.reduce(Money.zero) { $0 + $1.baseHt }
        return OrderTotals(
            subtotalTtc: subtotal,
            discountAmount: subtotal - total,
            totalTtc: total,
            totalHt: ht,
            totalVat: total - ht,
            vatLines: vatLines
        )
    }

    /// Partage à parts égales au centime près : les premiers convives absorbent le reste.
    public static func splitEqually(_ total: Money, parts: Int) -> [Money] {
        guard parts > 0 else { return [] }
        let base = total.cents / parts
        let remainder = total.cents % parts
        return (0..<parts).map { Money(cents: base + ($0 < remainder ? 1 : 0)) }
    }

    public static func tip(on total: Money, percent: Int) -> Money {
        total.multiplied(by: Decimal(percent) / 100)
    }

    public static func change(tendered: Money, due: Money) -> Money {
        (tendered - due).clampedAtZero()
    }

    /// Coupures proposées pour un encaissement espèces rapide (montant exact + billets supérieurs).
    public static func suggestedCashAmounts(for due: Money) -> [Money] {
        guard due.cents > 0 else { return [] }
        let notes = [5, 10, 20, 50, 100, 200].map { Money(cents: $0 * 100) }
        var result = [due]
        for note in notes where note > due && result.count < 5 {
            result.append(note)
        }
        // Arrondi à l'euro supérieur si différent du montant exact.
        let roundedUp = Money(cents: ((due.cents + 99) / 100) * 100)
        if roundedUp != due, !result.contains(roundedUp) {
            result.insert(roundedUp, at: 1)
        }
        return Array(result.prefix(5))
    }
}
