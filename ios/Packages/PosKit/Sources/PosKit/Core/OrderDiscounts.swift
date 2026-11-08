import Foundation

/// Remises et offerts d'une commande, en centimes : une seule formule pour les encaissements et les rapports imprimés
/// (miroir de `OrderDiscounts.Of` du .NET). Commande offerte : tout le sous-total est « offert ».
/// Sinon remise de commande + remises de ligne, les lignes offertes étant comptées à part.
public enum OrderDiscounts {
    public static func of(_ order: ActiveOrder) -> (discounts: Int, comps: Int) {
        let lines = order.lines.map(CartLine.init(serverLine:))
        let totals = OrderMath.totals(lines: lines, destination: order.destination, discount: order.globalDiscount)
        if order.globalDiscountType == .comp { return (0, totals.subtotalTtc.cents) }
        var discounts = totals.subtotalTtc.cents - totals.totalTtc.cents, comps = 0
        for line in lines {
            let base = line.effectiveUnitPrice.cents * line.quantity
            if line.isComp { comps += base } else if line.discountPercent > 0 { discounts += base - OrderMath.lineTotal(line).cents }
        }
        return (discounts, comps)
    }
}
