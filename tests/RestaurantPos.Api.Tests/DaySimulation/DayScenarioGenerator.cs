using System.Globalization;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>
/// Génère une journée déterministe (même graine, même journée) : 3 postes, 4 serveurs, 40 à 60 opérations aléatoires précédées de quatre cas
/// imposés par la spec (total nul, remise fixe supérieure au total, règlement de 1 centime, règlement fractionné en 5 avec reste).
/// Toutes les commandes sont soldées (ou annulées) avant le premier <c>z_closure</c> ; les annulations de reçu des commandes sont toutes jouées
/// avant la Z. La journée se termine par un X puis une Z par poste, un X de la nouvelle période (qui repart de zéro) et une tentative
/// d'annulation tardive d'un reçu scellé (<c>void_receipt</c>, refusée par l'API).
/// </summary>
public static class DayScenarioGenerator
{
    public const int DefaultSeed = 20261011;

    private const long MealVoucherCapCents = 2500;
    private static readonly string[] TerminalKeys = ["A", "B", "C"];
    private static readonly string[] Waiters = ["waiter1", "waiter2", "waiter3", "waiter4"];
    private static readonly decimal[] Rates = [0m, 2.1m, 5.5m, 10m, 20m];
    private static readonly decimal[] TakeawayRates = [5.5m, 10m];
    private static readonly string[] Methods = ["Cash", "CreditCard", "MealVoucher", "GiftCard"];
    private static readonly decimal[] PercentDiscounts = [10m, 15m, 33.33m];
    private static readonly decimal[] FixedDiscountsEuros = [1m, 2.5m, 5m];

    public static DayScenario Build(int seed)
    {
        var rng = new Random(seed);
        var ops = new List<DayOperation>(MandatoryCases());
        int count = rng.Next(40, 61);
        for (int i = 0; i < count; i++)
        {
            ops.Add(RandomOperation(rng, ops.Count));
            if ((i + 1) % 12 == 0)
            {
                ops.AddRange(TerminalKeys.Select(k => new DayOperation("x_report", k)));
            }
        }

        ops.AddRange(TerminalKeys.Select(k => new DayOperation("x_report", k)));
        ops.AddRange(TerminalKeys.Select(k => new DayOperation("z_closure", k)));
        ops.AddRange(TerminalKeys.Select(k => new DayOperation("x_report", k))); // nouvelle période : repart de zéro
        ops.AddRange(LateVoidAttempts(ops));
        return new DayScenario(seed, TerminalKeys.Select(k => new DayTerminal(k, $"Caisse {k}")).ToList(), ops);
    }

    /// <summary>Le scénario et son grand livre, tels que publiés dans <c>day_scenario.json</c> (identifiants de poste fictifs T01, T02, T03 : l'oracle ne s'en sert que comme clés).</summary>
    public static DayScenarioFile ToFile(DayScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var ids = scenario.Terminals.Select((t, i) => (t.Key, Id: $"T{i + 1:00}")).ToDictionary(x => x.Key, x => x.Id);
        var ledger = DayLedgerOracle.Compute(scenario.Operations, ids);
        return new DayScenarioFile(
            scenario.Seed,
            scenario.Terminals,
            scenario.Operations,
            scenario.Terminals.ToDictionary(t => t.Key, t => Expected(ledger.ByTerminal[ids[t.Key]])),
            Expected(ledger.Total));
    }

    private static DayExpected Expected(TerminalLedger t) => new(
        t.TtcCents,
        t.HtCents,
        t.ReceiptCount,
        t.VatByRate.ToDictionary(kv => kv.Key.ToString(CultureInfo.InvariantCulture), kv => kv.Value),
        t.TenderByMethod.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToDictionary(kv => kv.Key, kv => kv.Value));

    private static List<DayOperation> MandatoryCases()
    {
        static DayOperation Order(string reference, string key, string waiter, string table, int covers, DayLine[] lines, DayOrderDiscount? discount, DayTender[][] payments) =>
            new("table_order", key, waiter, table, covers, "EatIn", lines, discount, 0,
                payments.Select(p => (IReadOnlyList<DayTender>)p.ToList()).ToList(), Ref: reference);

        return
        [
            // total nul : tout est offert
            Order("m0", "A", "waiter1", "1", 2, [new("Offert", 1, 1200, 10m, Comp: true)], null, [[]]), // aucun règlement : l'API refuse un règlement à 0
            // remise fixe de 10 € sur 5,00 € : total borné à zéro
            Order("m1", "B", "waiter2", "2", 1, [new("Café", 2, 250, 20m)], new("FixedAmount", 10m), [[]]),
            // règlement de 1 centime, puis le reste
            Order("m2", "C", "waiter3", "3", 2, [new("Pain", 1, 1000, 5.5m)], null, [[new("Cash", 1)], [new("CreditCard", 999)]]),
            // 10,03 € en cinq règlements dont un reste de division
            Order("m3", "A", "waiter4", "4", 4, [new("Plat", 1, 1003, 10m)], null,
                [[new("Cash", 200, 300)], [new("Cash", 200)], [new("CreditCard", 200)], [new("MealVoucher", 200)], [new("GiftCard", 203)]]),
        ];
    }

    private static DayOperation RandomOperation(Random rng, int index)
    {
        string key = Pick(rng, TerminalKeys);
        string waiter = Pick(rng, Waiters);
        int kind = rng.Next(100);
        if (kind < 5)
        {
            return new DayOperation("hold_then_void", key, waiter, null, 0, "Takeaway", [new DayLine("Café", 1, 200, 10m, 5.5m)]);
        }

        bool table = kind < 55;
        var lines = MakeLines(rng);
        DayOrderDiscount? discount = rng.Next(100) switch
        {
            < 50 => null,
            < 75 => new DayOrderDiscount("Percentage", Pick(rng, PercentDiscounts)),
            _ => new DayOrderDiscount("FixedAmount", Pick(rng, FixedDiscountsEuros)),
        };
        string destination = table ? (rng.Next(100) < 10 ? "Takeaway" : "EatIn") : (rng.Next(2) == 0 ? "Takeaway" : "EatIn");
        var draft = new DayOperation(table ? "table_order" : "counter_order", key, waiter, table ? (1 + rng.Next(12)).ToString(CultureInfo.InvariantCulture) : null,
            table ? 1 + rng.Next(6) : 0, destination, lines, discount, Ref: $"o{index}");
        if (DayLedgerOracle.OrderTotalCents(draft) <= 0)
        {
            draft = draft with { Discount = null }; // une remise qui annule tout est un cas imposé, pas un cas aléatoire
        }

        long total = DayLedgerOracle.OrderTotalCents(draft);
        long tip = rng.Next(100) < 25 ? rng.Next(50, 501) : 0;
        var payments = MakePayments(rng, total, tip);
        return draft with
        {
            TipCents = tip,
            Payments = payments,
            VoidPaymentIndex = rng.Next(100) < 15 ? rng.Next(payments.Count) : null,
            Reprint = rng.Next(100) < 10,
            HoldAndRecall = !table && rng.Next(100) < 10,
        };
    }

    private static List<DayLine> MakeLines(Random rng)
    {
        int count = rng.Next(1, 7);
        var lines = new List<DayLine>();
        for (int i = 0; i < count; i++)
        {
            decimal rate = Pick(rng, Rates);
            decimal? takeaway = rng.Next(100) < 30 ? Pick(rng, TakeawayRates) : null;
            long extra = rng.Next(100) < 20 ? rng.Next(50, 201) : 0;
            bool comp = i > 0 && rng.Next(100) < 8; // la première ligne est payante : le total n'est jamais nul par hasard
            lines.Add(new DayLine($"Article {i + 1}", rng.Next(1, 5), rng.Next(50, 2501), rate, takeaway, extra, comp));
        }

        return lines;
    }

    /// <summary>Découpe le total en 1 à 5 règlements d'au moins 1 centime ; le pourboire s'ajoute au dernier (seul règlement qui solde).</summary>
    private static List<IReadOnlyList<DayTender>> MakePayments(Random rng, long total, long tip)
    {
        int n = rng.Next(100) switch { < 50 => 1, < 75 => 2, < 87 => 3, < 94 => 4, _ => 5 };
        n = (int)Math.Min(n, total);
        var payments = new List<IReadOnlyList<DayTender>>();
        long remaining = total;
        for (int i = 0; i < n; i++)
        {
            long part = i == n - 1 ? remaining : rng.NextInt64(1, remaining - (n - 1 - i) + 1);
            remaining -= part;
            payments.Add(MakeTenders(rng, i == n - 1 ? part + tip : part));
        }

        return payments;
    }

    private static List<DayTender> MakeTenders(Random rng, long amount)
    {
        long first = amount >= 2 && rng.Next(100) < 25 ? rng.NextInt64(1, amount) : amount;
        var tenders = new List<DayTender> { Tender(rng, first) };
        if (first < amount)
        {
            tenders.Add(Tender(rng, amount - first));
        }

        return tenders;
    }

    private static DayTender Tender(Random rng, long amount)
    {
        string method = Pick(rng, Methods);
        if (method == "MealVoucher" && amount > MealVoucherCapCents)
        {
            method = "CreditCard"; // le comptoir refuse un titre-restaurant au-delà du plafond légal de 25 € (400 « exceeds the legal eligible cap »)
        }

        long extra = method == "Cash" && rng.Next(100) < 40 ? rng.Next(1, 2001) : 0; // rendu monnaie : espèces remises en plus
        return new DayTender(method, amount, extra);
    }

    /// <summary>Une annulation tardive par poste ayant un reçu non annulé : après la Z, l'API la refuse.</summary>
    private static IEnumerable<DayOperation> LateVoidAttempts(List<DayOperation> ops)
    {
        foreach (string key in TerminalKeys)
        {
            var target = ops.FirstOrDefault(o => o.Terminal == key && o.Ref is not null && o.VoidPaymentIndex is null && o.Payments is { Count: > 0 }
                && DayLedgerOracle.OrderTotalCents(o) > 0);
            if (target is not null)
            {
                yield return new DayOperation("void_receipt", key, VoidPaymentIndex: 0, Ref: target.Ref);
            }
        }
    }

    private static T Pick<T>(Random rng, T[] values) => values[rng.Next(values.Length)];
}
