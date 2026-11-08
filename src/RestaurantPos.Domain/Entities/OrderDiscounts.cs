namespace RestaurantPos.Domain.Entities;

/// <summary>
/// Remises et offerts d'une commande, en centimes : une seule formule pour le tableau de bord, les rapports imprimés et les alertes
/// (miroir iPad : <c>OrderDiscounts</c> dans PosKit).
/// </summary>
public static class OrderDiscounts
{
    /// <summary>Commande offerte : tout le sous-total est « offert ». Sinon : remise de commande + remises de ligne ; lignes offertes comptées à part.</summary>
    public static (long DiscountsCents, long CompsCents) Of(Order order)
    {
        ArgumentNullException.ThrowIfNull(order);
        long subtotal = order.Subtotal.AmountInCents;
        if (order.GlobalDiscountType == DiscountType.Comp) return (0, subtotal);

        long discounts = subtotal - order.TotalTtc.AmountInCents, comps = 0;
        foreach (var item in order.Items)
        {
            long baseCents = (item.UnitPrice.AmountInCents + item.ModifiersPriceExtra.AmountInCents) * item.Quantity;
            if (item.IsComp) comps += baseCents;
            else if (item.DiscountPercent > 0) discounts += baseCents - item.CalculateTotalTtc().AmountInCents;
        }
        return (discounts, comps);
    }
}
