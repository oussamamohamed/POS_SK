import Foundation

struct FiscalReceiptAmounts: Equatable {
    let ttcCents: Int
    let htCents: Int
    let taxBreakdownJson: String
}

/// Port exact des montants d'un reçu dans `CheckoutPaymentService.ProcessPaymentTendersAsync` (.NET), **incohérences comprises** :
/// - le JSON de TVA somme `OrderItem.TaxAmount` par `TaxRatePercent` de la ligne (jamais le taux à emporter, jamais la remise globale) ;
/// - le HT vient de `Order.CalculateTaxBreakdown` (taux effectif ; remise globale en pourcentage seulement, pas en montant fixe) ;
/// - pour un règlement partiel, TVA et HT sont mis à l'échelle par `fiscalPaid / orderTtc` avec `Math.Round` sans mode (arrondi au pair).
/// Corriger une de ces règles se fait des deux côtés (voir `CLAUDE.md`).
enum FiscalReceiptMath {
    /// - Parameters:
    ///   - totalPaidCents: somme des règlements de ce reçu (pourboire compris, rendu monnaie exclu).
    ///   - tipCents: pourboire enregistré sur la commande.
    ///   - priorFiscalTtcCents: TTC des reçus antérieurs de la commande, hors annulations et hors reçus annulés.
    static func amounts(order: ActiveOrder, totalPaidCents: Int, tipCents: Int, priorFiscalTtcCents: Int) -> FiscalReceiptAmounts {
        let cart = order.lines.map(CartLine.init(serverLine:))
        let orderTtc = OrderMath.totals(lines: cart, destination: order.destination, discount: order.globalDiscount).totalTtc.cents
        let totalDue = orderTtc + tipCents
        let hasTip = tipCents > 0
        let fiscalPaid = hasTip ? min(totalPaidCents, max(0, orderTtc - priorFiscalTtcCents)) : totalPaidCents
        let isPartial = hasTip ? (priorFiscalTtcCents > 0 || fiscalPaid < orderTtc) : totalPaidCents < totalDue
        let ratio: Decimal = (orderTtc > 0 && isPartial) ? Decimal(fiscalPaid) / Decimal(orderTtc) : 1

        var rates: [Decimal] = [] // ordre d'apparition, comme le `Dictionary` .NET
        var vat: [Decimal: Int] = [:]
        for line in cart {
            let itemVat = OrderMath.split(ttc: OrderMath.lineTotal(line), ratePercent: line.taxRatePercent).vat.cents
            let scaled = ratio == 1 ? itemVat : bankersRound(Decimal(itemVat) * ratio)
            if vat[line.taxRatePercent] == nil { rates.append(line.taxRatePercent) }
            vat[line.taxRatePercent, default: 0] += scaled
        }
        let json = "{" + rates.map { "\"\($0)\":\(vat[$0] ?? 0)" }.joined(separator: ",") + "}"

        let orderHt = htCents(cart, destination: order.destination, discount: order.globalDiscount)
        return FiscalReceiptAmounts(
            ttcCents: ratio == 1 ? orderTtc : fiscalPaid,
            htCents: ratio == 1 ? orderHt : bankersRound(Decimal(orderHt) * ratio),
            taxBreakdownJson: json
        )
    }

    /// Annulation : toutes les valeurs du JSON de TVA changent de signe, clés et ordre inchangés.
    static func negatedTaxJson(_ json: String) -> String {
        guard let regex = try? NSRegularExpression(pattern: "\"([^\"]+)\":(-?[0-9]+)") else { return json }
        let range = NSRange(json.startIndex..., in: json)
        let pairs = regex.matches(in: json, range: range).compactMap { match -> String? in
            guard let keyRange = Range(match.range(at: 1), in: json), let valueRange = Range(match.range(at: 2), in: json),
                  let value = Int(json[valueRange]) else { return nil }
            return "\"\(json[keyRange])\":\(-value)"
        }
        return "{" + pairs.joined(separator: ",") + "}"
    }

    /// `Order.TotalHt` : somme des bases HT par taux effectif.
    private static func htCents(_ cart: [CartLine], destination: OrderDestination, discount: GlobalDiscount?) -> Int {
        var groups: [Decimal: Int] = [:]
        for line in cart {
            groups[OrderMath.effectiveTaxRate(line, destination: destination), default: 0] += OrderMath.lineTotal(line).cents
        }
        return groups.reduce(0) { total, group in
            var groupTtc = group.value
            if let discount, discount.type == .percentage, discount.value > 0 {
                groupTtc = Money.roundAwayFromZero(Decimal(groupTtc) * (1 - discount.value / 100))
            }
            return total + OrderMath.split(ttc: Money(cents: max(0, groupTtc)), ratePercent: group.key).ht.cents
        }
    }

    /// `Math.Round(decimal)` du .NET : arrondi au pair.
    static func bankersRound(_ value: Decimal) -> Int {
        var input = value
        var result = Decimal()
        NSDecimalRound(&result, &input, 0, .bankers)
        return NSDecimalNumber(decimal: result).intValue
    }
}
