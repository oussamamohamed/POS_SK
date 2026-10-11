using System.Numerics;

namespace RestaurantPos.Api.Tests.DaySimulation;

public sealed class TerminalLedger
{
    public long TtcCents { get; set; }
    public long HtCents { get; set; }
    public int ReceiptCount { get; set; }
    public SortedDictionary<decimal, long> VatByRate { get; } = [];
    public Dictionary<string, long> TenderByMethod { get; } = [];
}

public sealed class DayLedger
{
    public Dictionary<string, TerminalLedger> ByTerminal { get; } = [];
    public TerminalLedger Total { get; } = new();
}

/// <summary>
/// Grand livre de la journée, calculé à partir des entrées (lignes, taux, remises, règlements) avec une arithmétique entière propre au test,
/// indépendante du code de production (aucune référence à <c>Order</c>, <c>TaxAllocation</c>, <c>ReceiptTaxCalculator</c>).
/// Règles :
/// <list type="bullet">
/// <item>TTC de ligne = (prix + suppléments) × quantité, 0 si offerte ; groupes par taux effectif (taux à emporter si la commande est
/// à emporter et que la ligne en a un), taux croissants ; un groupe dont toutes les lignes sont offertes reste présent (TVA 0).</item>
/// <item>Remise de commande (pourcentage arrondi demi éloigné de zéro, ou montant fixe), total borné à zéro, répartie sur les groupes au
/// plus fort reste.</item>
/// <item>Montant fiscal d'un règlement = somme de ses montants (pourboire compris), borné au TTC de la commande ; sur le règlement qui porte
/// le pourboire, borné au TTC restant après les reçus précédents. Un reçu partiel répartit son montant sur les parts de la commande au plus
/// fort reste. HT = part / (1 + taux) arrondi au centime (demi éloigné de zéro), TVA = part − HT.</item>
/// <item>Moyens de paiement = montants imputés (pourboire compris, rendu monnaie exclu). Un reçu annulé ajoute un reçu négatif miroir
/// (TTC, HT, TVA et règlements opposés) : il compte dans le nombre de reçus.</item>
/// </list>
/// </summary>
public static class DayLedgerOracle
{
    public static DayLedger Compute(IEnumerable<DayOperation> operations, IReadOnlyDictionary<string, string> terminalIds)
    {
        ArgumentNullException.ThrowIfNull(operations);
        ArgumentNullException.ThrowIfNull(terminalIds);
        var ledger = new DayLedger();
        foreach (string id in terminalIds.Values)
        {
            ledger.ByTerminal[id] = new TerminalLedger(); // un poste sans vente a un grand livre vide, pas une clé absente
        }

        foreach (var op in operations.Where(o => o.Type is "table_order" or "counter_order"))
        {
            Apply(op, ledger.ByTerminal[terminalIds[op.Terminal!]]);
        }

        foreach (var t in ledger.ByTerminal.Values)
        {
            ledger.Total.TtcCents += t.TtcCents;
            ledger.Total.HtCents += t.HtCents;
            ledger.Total.ReceiptCount += t.ReceiptCount;
            foreach (var (rate, vat) in t.VatByRate)
            {
                ledger.Total.VatByRate[rate] = ledger.Total.VatByRate.GetValueOrDefault(rate) + vat;
            }

            foreach (var (method, amount) in t.TenderByMethod)
            {
                ledger.Total.TenderByMethod[method] = ledger.Total.TenderByMethod.GetValueOrDefault(method) + amount;
            }
        }

        return ledger;
    }

    /// <summary>Total TTC de la commande après remise (borné à zéro), avant tout règlement ni pourboire.</summary>
    public static long OrderTotalCents(DayOperation op)
    {
        ArgumentNullException.ThrowIfNull(op);
        long subtotal = Groups(op).Sum(g => g.Ttc);
        return Math.Max(0, subtotal - DiscountCents(op.Discount, subtotal));
    }

    private static List<(decimal Rate, long Ttc)> Groups(DayOperation op)
    {
        bool takeaway = op.Destination == "Takeaway";
        return (op.Lines ?? [])
            .GroupBy(l => takeaway && l.TaxRateTakeawayPercent.HasValue ? l.TaxRateTakeawayPercent.Value : l.TaxRatePercent)
            .OrderBy(g => g.Key)
            .Select(g => (Rate: g.Key, Ttc: g.Sum(l => l.Comp ? 0L : (l.UnitPriceCents + l.ModifiersExtraCents) * l.Quantity)))
            .ToList();
    }

    private static void Apply(DayOperation op, TerminalLedger t)
    {
        var groups = Groups(op);
        decimal[] rates = groups.Select(g => g.Rate).ToArray();
        long total = OrderTotalCents(op);
        long[] orderShares = Allocate(total, groups.Select(g => g.Ttc).ToArray());

        var payments = op.Payments ?? [];
        long fiscalPaidSoFar = 0;
        var sales = new List<(long Ttc, long Ht, long[] Vat)>();
        for (int p = 0; p < payments.Count; p++)
        {
            long paid = payments[p].Sum(x => x.AmountCents);
            bool carriesTip = op.TipCents > 0 && p == payments.Count - 1;
            long fiscal = Math.Clamp(carriesTip ? Math.Min(paid, total - fiscalPaidSoFar) : paid, 0, total);
            fiscalPaidSoFar += fiscal;
            long[] shares = Allocate(fiscal, orderShares); // fiscal == total rend orderShares tel quel
            long ht = 0;
            var vat = new long[groups.Count];
            for (int g = 0; g < groups.Count; g++)
            {
                (long baseHt, long tax) = Split(shares[g], rates[g]);
                ht += baseHt;
                vat[g] = tax;
            }

            sales.Add((fiscal, ht, vat));
            AddReceipt(t, rates, fiscal, ht, vat, +1);
            foreach (var tender in payments[p])
            {
                t.TenderByMethod[tender.Method] = t.TenderByMethod.GetValueOrDefault(tender.Method) + tender.AmountCents;
            }
        }

        if (op.VoidPaymentIndex is { } index)
        {
            var original = sales[index];
            AddReceipt(t, rates, original.Ttc, original.Ht, original.Vat, -1);
            foreach (var tender in payments[index])
            {
                t.TenderByMethod[tender.Method] -= tender.AmountCents;
            }
        }
    }

    private static void AddReceipt(TerminalLedger t, decimal[] rates, long ttc, long ht, long[] vat, int sign)
    {
        t.TtcCents += sign * ttc;
        t.HtCents += sign * ht;
        t.ReceiptCount++;
        for (int g = 0; g < rates.Length; g++)
        {
            t.VatByRate[rates[g]] = t.VatByRate.GetValueOrDefault(rates[g]) + (sign * vat[g]);
        }
    }

    private static long DiscountCents(DayOrderDiscount? discount, long subtotal)
    {
        if (discount is null || discount.Value <= 0)
        {
            return 0;
        }

        if (discount.Type == "Percentage")
        {
            long discounted = (long)Math.Round(subtotal * (1m - (discount.Value / 100m)), MidpointRounding.AwayFromZero);
            return subtotal - Math.Max(0, discounted);
        }

        return Math.Min(subtotal, (long)Math.Round(discount.Value * 100m, MidpointRounding.AwayFromZero));
    }

    /// <summary>HT = TTC / (1 + taux), arrondi au centime (demi éloigné de zéro) ; TVA = TTC − HT.</summary>
    private static (long BaseHt, long Tax) Split(long ttc, decimal rate)
    {
        long ht = (long)Math.Round(ttc / (1m + (rate / 100m)), MidpointRounding.AwayFromZero);
        return (ht, ttc - ht);
    }

    /// <summary>Plus fort reste avec des entiers exacts (BigInteger), ordre des groupes pour départager.</summary>
    private static long[] Allocate(long total, long[] weights)
    {
        var shares = new long[weights.Length];
        BigInteger sum = weights.Aggregate(BigInteger.Zero, (a, w) => a + w);
        if (total <= 0 || sum == 0)
        {
            return shares;
        }

        if (total >= sum)
        {
            return weights.ToArray();
        }

        var rem = new List<(int I, BigInteger R)>();
        long used = 0;
        for (int i = 0; i < weights.Length; i++)
        {
            var product = (BigInteger)total * weights[i];
            shares[i] = (long)(product / sum);
            rem.Add((i, product % sum));
            used += shares[i];
        }

        foreach (var (i, _) in rem.OrderByDescending(x => x.R).ThenBy(x => x.I).Take((int)(total - used)))
        {
            shares[i]++;
        }

        return shares;
    }
}
