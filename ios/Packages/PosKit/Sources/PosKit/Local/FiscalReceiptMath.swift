import Foundation

struct FiscalReceiptAmounts: Equatable {
    let ttcCents: Int
    let htCents: Int
    let taxBreakdownJson: String
    /// HT par taux en centimes, mêmes clés et même ordre que `taxBreakdownJson`. Colonne `HtBreakdownJson`, NON signée (hors hash).
    var htBreakdownJson: String? = nil
}

/// Montants d'un reçu : même règle que `ReceiptTaxCalculator` (.NET). La TVA et le HT viennent de la ventilation de la commande par
/// taux effectif (`OrderMath.totals`) ; un règlement partiel répartit le montant fiscal sur les groupes au plus fort reste.
/// Le HT par taux (`HtBreakdownJson`) vient de la même répartition : Σ HT par taux = HT du reçu.
/// Corriger une de ces règles se fait des deux côtés (voir `CLAUDE.md`).
enum FiscalReceiptMath {
    /// - Parameters:
    ///   - totalPaidCents: somme des règlements de ce reçu (pourboire compris, rendu monnaie exclu).
    ///   - tipCents: pourboire enregistré sur la commande.
    ///   - priorFiscalTtcCents: TTC des reçus antérieurs de la commande, hors annulations et hors reçus annulés.
    static func amounts(order: ActiveOrder, totalPaidCents: Int, tipCents: Int, priorFiscalTtcCents: Int) -> FiscalReceiptAmounts {
        let cart = order.lines.map(CartLine.init(serverLine:))
        let totals = OrderMath.totals(lines: cart, destination: order.destination, discount: order.globalDiscount)
        let orderTtc = totals.totalTtc.cents
        let hasTip = tipCents > 0
        // Montant fiscal du reçu : jamais le pourboire ; avec pourboire il est borné au TTC restant après les reçus antérieurs.
        let fiscalPaid = hasTip ? min(totalPaidCents, max(0, orderTtc - priorFiscalTtcCents)) : totalPaidCents
        let paid = min(max(fiscalPaid, 0), orderTtc)
        let orderShares = totals.vatLines.map { $0.ttc.cents }
        let shares = paid == orderTtc ? orderShares : OrderMath.allocate(total: paid, weights: orderShares)

        var htCents = 0
        var entries: [String] = []
        var htEntries: [String] = []
        for (line, share) in zip(totals.vatLines, shares) {
            let parts = OrderMath.split(ttc: Money(cents: share), ratePercent: line.ratePercent)
            htCents += parts.ht.cents
            entries.append("\"\(line.ratePercent)\":\(parts.vat.cents)")
            htEntries.append("\"\(line.ratePercent)\":\(parts.ht.cents)")
        }
        return FiscalReceiptAmounts(
            ttcCents: paid, htCents: htCents, taxBreakdownJson: "{" + entries.joined(separator: ",") + "}",
            htBreakdownJson: "{" + htEntries.joined(separator: ",") + "}"
        )
    }

    /// Annulation : toutes les valeurs du JSON de TVA (ou de HT par taux) changent de signe, clés et ordre inchangés.
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
}
