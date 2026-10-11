using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Domain.ValueObjects;

public sealed record ReceiptTax(long TtcCents, long HtCents, IReadOnlyList<KeyValuePair<decimal, long>> VatByRate);

/// <summary>
/// Montants fiscaux d'un reçu : une seule source, <see cref="Order.CalculateTaxBreakdown"/>. Un reçu qui encaisse tout le TTC de la
/// commande reprend ses parts ; un reçu partiel répartit son montant sur les parts de la commande (plus fort reste), sans rejouer l'historique.
/// Pour chaque reçu : TTC − HT = Σ TVA, exactement.
/// </summary>
public static class ReceiptTaxCalculator
{
    public static ReceiptTax Compute(Order order, long fiscalPaidCents)
    {
        ArgumentNullException.ThrowIfNull(order);
        var breakdown = order.CalculateTaxBreakdown();
        long orderTtc = order.CalculateTotalTtc().AmountInCents;
        long paid = Math.Clamp(fiscalPaidCents, 0, orderTtc);
        long[] orderShares = breakdown.Select(b => b.TaxableBaseCents + b.TaxAmountCents).ToArray();
        long[] shares = paid == orderTtc ? orderShares : TaxAllocation.Allocate(paid, orderShares);

        long ht = 0;
        var vat = new List<KeyValuePair<decimal, long>>(breakdown.Count);
        for (int i = 0; i < breakdown.Count; i++)
        {
            var part = TaxBreakdownItem.Calculate(breakdown[i].TaxRatePercent, shares[i]);
            ht += part.TaxableBaseCents;
            vat.Add(new KeyValuePair<decimal, long>(breakdown[i].TaxRatePercent, part.TaxAmountCents));
        }

        return new ReceiptTax(paid, ht, vat);
    }
}
