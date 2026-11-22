using System.Numerics;

namespace RestaurantPos.Api.Tests.DaySimulation;

public sealed class TerminalLedger
{
    public long TtcCents { get; set; }
    public long HtCents { get; set; }
    public int ReceiptCount { get; set; }
    public SortedDictionary<decimal, long> VatByRate { get; } = [];
    public SortedDictionary<decimal, long> HtByRate { get; } = [];
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
/// <item>Annulations et corrections de ligne : aucun reçu, aucun effet fiscal propre ; elles changent seulement les lignes (donc le total)
/// sur lesquelles portent les règlements suivants. Une étape refusée ne change rien. Commande rouverte : le premier règlement porte sur la
/// commande d'avant, son reçu est annulé (miroir), les règlements suivants portent sur la commande d'après.</item>
/// </list>
/// </summary>
public static class DayLedgerOracle
{
    /// <param name="withReceiptVoids">faux : ventes d'origine seulement (ni reçu d'annulation, ni avoir d'une commande rouverte).</param>
    public static DayLedger Compute(IEnumerable<DayOperation> operations, IReadOnlyDictionary<string, string> terminalIds, bool withReceiptVoids = true)
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
            Apply(op, ledger.ByTerminal[terminalIds[op.Terminal!]], withReceiptVoids);
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

            foreach (var (rate, ht) in t.HtByRate)
            {
                ledger.Total.HtByRate[rate] = ledger.Total.HtByRate.GetValueOrDefault(rate) + ht;
            }

            foreach (var (method, amount) in t.TenderByMethod)
            {
                ledger.Total.TenderByMethod[method] = ledger.Total.TenderByMethod.GetValueOrDefault(method) + amount;
            }
        }

        return ledger;
    }

    /// <summary>Total TTC final de la commande après remise (borné à zéro) et après toutes ses annulations et corrections acceptées, avant tout règlement ni pourboire.</summary>
    public static long OrderTotalCents(DayOperation op) => TotalCents(op, LinesAfter(op, "paid"));

    /// <summary>Ordre des phases d'annulation et de correction de ligne (<see cref="DayLineVoid.Phase"/>).</summary>
    public static readonly string[] LineVoidPhases = ["draft", "sent", "reopened", "part_paid", "paid"];

    /// <summary>
    /// Lignes de la commande après les annulations et corrections acceptées (<c>Expect</c> = 200) des phases jusqu'à <paramref name="phase"/>
    /// comprise, lignes rajoutées en fin de liste ; une ligne retirée en entier disparaît (le serveur supprime la ligne).
    /// </summary>
    public static List<DayLine> LinesAfter(DayOperation op, string phase)
    {
        ArgumentNullException.ThrowIfNull(op);
        int rank = Array.IndexOf(LineVoidPhases, phase);
        var lines = (op.Lines ?? []).ToList();
        foreach (var step in (op.LineVoids ?? []).Where(v => Array.IndexOf(LineVoidPhases, v.Phase) <= rank))
        {
            if (step.Expect == 200)
            {
                lines[step.Line] = lines[step.Line] with { Quantity = lines[step.Line].Quantity - step.Quantity };
            }

            if (step.ReAdd > 0)
            {
                lines.Add(lines[step.Line] with { Quantity = step.ReAdd, Comp = false }); // même produit, ligne neuve : jamais offerte
            }
        }

        return lines.Where(l => l.Quantity > 0).ToList();
    }

    /// <summary>Total TTC (remise comprise, borné à zéro) de la commande pour ces lignes ; après chaque étape acceptée, la réponse de la route doit le porter.</summary>
    public static long TotalCents(DayOperation op, IReadOnlyList<DayLine> lines)
    {
        ArgumentNullException.ThrowIfNull(op);
        long subtotal = Groups(op, lines).Sum(g => g.Ttc);
        return Math.Max(0, subtotal - DiscountCents(op.Discount, subtotal));
    }

    /// <summary>Total TTC attendu après chaque étape de <c>LineVoids</c> (nul pour une étape refusée).</summary>
    public static List<long?> TotalsAfterSteps(DayOperation op)
    {
        ArgumentNullException.ThrowIfNull(op);
        var steps = op.LineVoids ?? [];
        // la réponse précède le rajout éventuel de l'étape (ReAdd)
        return steps.Select((s, i) => s.Expect == 200
            ? TotalCents(op, LinesAfter(op with { LineVoids = [.. steps.Take(i), s with { ReAdd = 0 }] }, "paid"))
            : (long?)null).ToList();
    }

    /// <summary>Une commande rouverte (étape <c>reopened</c>) : son premier règlement est annulé avant les autres.</summary>
    public static bool IsReopened(DayOperation op) => op?.LineVoids?.Any(v => v.Phase == "reopened") == true;

    /// <summary>Reçus d'annulation de la journée : <c>VoidPaymentIndex</c> et premier reçu d'une commande rouverte.</summary>
    public static int ReceiptVoidCount(IEnumerable<DayOperation> operations) =>
        operations.Count(o => o.Type is "table_order" or "counter_order" && (o.VoidPaymentIndex != null || IsReopened(o)));

    /// <summary>
    /// Annulations acceptées de lignes envoyées (phases <c>sent</c> et <c>reopened</c>) : prix unitaire = prix + suppléments (R9, offert
    /// compris), serveur = celui de la table (R2) ; une vente comptoir n'a pas de serveur assigné. Les corrections (<c>draft</c>) n'en font pas.
    /// </summary>
    public static List<DayExpectedLineVoid> ExpectedLineVoids(IEnumerable<DayOperation> operations)
    {
        var result = new List<DayExpectedLineVoid>();
        foreach (var op in operations.Where(o => o.LineVoids is { Count: > 0 }))
        {
            var lines = (op.Lines ?? []).ToList();
            foreach (var step in op.LineVoids!)
            {
                if (step.Expect == 200 && step.Phase is "sent" or "reopened")
                {
                    var line = lines[step.Line];
                    bool counter = op.Type == "counter_order";
                    result.Add(new DayExpectedLineVoid(op.Terminal!, op.Ref!, counter ? "Comptoir" : op.Table!, line.Name, step.Quantity,
                        line.UnitPriceCents + line.ModifiersExtraCents, step.Reason!.Trim(), counter ? "" : op.Waiter!, !step.Legacy));
                }

                if (step.ReAdd > 0)
                {
                    lines.Add(lines[step.Line] with { Quantity = step.ReAdd, Comp = false });
                }
            }
        }

        return result;
    }

    /// <summary>Articles de bon attendus des lignes envoyées ; aucun pour une ligne jamais envoyée ou ancienne (sans <c>OrderItemId</c>).</summary>
    public static List<DayExpectedTicketLine> TicketLines(DayOperation op)
    {
        ArgumentNullException.ThrowIfNull(op);
        var steps = op.LineVoids ?? [];
        var quantities = (op.Lines ?? []).Select(l => l.Quantity).ToList();
        foreach (var step in steps.Where(s => s.Phase == "draft" && s.Expect == 200))
        {
            quantities[step.Line] -= step.Quantity;
        }

        bool dispatched = op.Type == "table_order" || steps.Any(s => s.Phase != "draft");
        var sent = dispatched ? quantities.ToList() : quantities.Select(_ => 0).ToList();
        var cancelled = sent.Select(_ => 0).ToList();
        var legacy = new HashSet<int>();
        foreach (var step in steps.Where(s => s.Phase != "draft"))
        {
            if (step.Legacy)
            {
                legacy.Add(step.Line);
            }

            if (step.Expect == 200)
            {
                cancelled[step.Line] += step.Quantity;
            }

            if (step.ReAdd > 0)
            {
                sent.Add(step.ReAdd);
                cancelled.Add(0);
            }
        }

        return sent.Select((q, i) => new DayExpectedTicketLine(i, q, cancelled[i])).Where(t => t.Sent > 0 && !legacy.Contains(t.Line)).ToList();
    }

    private static List<(decimal Rate, long Ttc)> Groups(DayOperation op, IReadOnlyList<DayLine> lines)
    {
        bool takeaway = op.Destination == "Takeaway";
        return lines
            .GroupBy(l => takeaway && l.TaxRateTakeawayPercent.HasValue ? l.TaxRateTakeawayPercent.Value : l.TaxRatePercent)
            .OrderBy(g => g.Key)
            .Select(g => (Rate: g.Key, Ttc: g.Sum(l => l.Comp ? 0L : (l.UnitPriceCents + l.ModifiersExtraCents) * l.Quantity)))
            .ToList();
    }

    private sealed record Sale(decimal[] Rates, long Ttc, long Ht, long[] Vat, long[] HtByGroup);

    private static void Apply(DayOperation op, TerminalLedger t, bool withReceiptVoids)
    {
        bool reopened = IsReopened(op);
        var payments = op.Payments ?? [];
        long fiscalPaidSoFar = 0;
        var sales = new List<Sale>();
        for (int p = 0; p < payments.Count; p++)
        {
            // Commande rouverte : le premier règlement porte sur la commande d'avant les annulations « reopened », les suivants sur celle d'après.
            var groups = Groups(op, LinesAfter(op, reopened && p > 0 ? "reopened" : "sent"));
            decimal[] rates = groups.Select(g => g.Rate).ToArray();
            long total = Math.Max(0, groups.Sum(g => g.Ttc) - DiscountCents(op.Discount, groups.Sum(g => g.Ttc)));
            long[] orderShares = Allocate(total, groups.Select(g => g.Ttc).ToArray());

            long paid = payments[p].Sum(x => x.AmountCents);
            bool carriesTip = op.TipCents > 0 && p == payments.Count - 1;
            long fiscal = Math.Clamp(carriesTip ? Math.Min(paid, total - fiscalPaidSoFar) : paid, 0, total);
            fiscalPaidSoFar += fiscal;
            long[] shares = Allocate(fiscal, orderShares); // fiscal == total rend orderShares tel quel
            long ht = 0;
            var vat = new long[groups.Count];
            var htByGroup = new long[groups.Count];
            for (int g = 0; g < groups.Count; g++)
            {
                (long baseHt, long tax) = Split(shares[g], rates[g]);
                ht += baseHt;
                vat[g] = tax;
                htByGroup[g] = baseHt;
            }

            var sale = new Sale(rates, fiscal, ht, vat, htByGroup);
            sales.Add(sale);
            AddReceipt(t, sale, +1);
            foreach (var tender in payments[p])
            {
                t.TenderByMethod[tender.Method] = t.TenderByMethod.GetValueOrDefault(tender.Method) + tender.AmountCents;
            }

            if (reopened && p == 0)
            {
                if (withReceiptVoids)
                {
                    Mirror(t, sale, payments[0]);
                }

                fiscalPaidSoFar = 0; // le reçu annulé ne compte plus dans le restant dû
            }
        }

        if (withReceiptVoids && op.VoidPaymentIndex is { } index)
        {
            Mirror(t, sales[index], payments[index]);
        }
    }

    private static void Mirror(TerminalLedger t, Sale sale, IReadOnlyList<DayTender> tenders)
    {
        AddReceipt(t, sale, -1);
        foreach (var tender in tenders)
        {
            t.TenderByMethod[tender.Method] -= tender.AmountCents;
        }
    }

    private static void AddReceipt(TerminalLedger t, Sale sale, int sign)
    {
        t.TtcCents += sign * sale.Ttc;
        t.HtCents += sign * sale.Ht;
        t.ReceiptCount++;
        for (int g = 0; g < sale.Rates.Length; g++)
        {
            t.VatByRate[sale.Rates[g]] = t.VatByRate.GetValueOrDefault(sale.Rates[g]) + (sign * sale.Vat[g]);
            t.HtByRate[sale.Rates[g]] = t.HtByRate.GetValueOrDefault(sale.Rates[g]) + (sign * sale.HtByGroup[g]);
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
