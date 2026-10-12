using System.Text.Json;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Domain.ValueObjects;

public sealed record ReceiptTax(
    long TtcCents,
    long HtCents,
    IReadOnlyList<KeyValuePair<decimal, long>> VatByRate,
    IReadOnlyList<KeyValuePair<decimal, long>> HtByRate);

/// <summary>
/// Montants fiscaux d'un reçu : une seule source, <see cref="Order.CalculateTaxBreakdown"/>. Un reçu qui encaisse tout le TTC de la
/// commande reprend ses parts ; un reçu partiel répartit son montant sur les parts de la commande (plus fort reste), sans rejouer l'historique.
/// Pour chaque reçu : TTC − HT = Σ TVA, exactement, et Σ HT par taux = HT du reçu (le HT par taux est persisté dans la colonne non signée
/// <c>HtBreakdownJson</c>, mêmes clés et même ordre que le JSON de TVA signé).
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
        var htByRate = new List<KeyValuePair<decimal, long>>(breakdown.Count);
        for (int i = 0; i < breakdown.Count; i++)
        {
            var part = TaxBreakdownItem.Calculate(breakdown[i].TaxRatePercent, shares[i]);
            ht += part.TaxableBaseCents;
            vat.Add(new KeyValuePair<decimal, long>(breakdown[i].TaxRatePercent, part.TaxAmountCents));
            htByRate.Add(new KeyValuePair<decimal, long>(breakdown[i].TaxRatePercent, part.TaxableBaseCents));
        }

        return new ReceiptTax(paid, ht, vat, htByRate);
    }

    /// <summary>JSON par taux (<c>{"10":91,"5.5":52}</c>) : même sérialisation pour la TVA signée et le HT non signé.</summary>
    public static string ToJson(IEnumerable<KeyValuePair<decimal, long>> byRate)
    {
        var dict = new Dictionary<decimal, long>();
        foreach (var kvp in byRate)
        {
            dict[kvp.Key] = kvp.Value;
        }

        return JsonSerializer.Serialize(dict);
    }

    /// <summary>Miroir d'un JSON par taux (annulation) : mêmes clés, même ordre, valeurs de signe opposé.</summary>
    public static string Negate(string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, long>>(json) ?? [];
        return JsonSerializer.Serialize(dict.ToDictionary(kvp => kvp.Key, kvp => -kvp.Value));
    }
}
