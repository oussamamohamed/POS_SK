using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Infrastructure.Tests;

internal sealed record LineSpec(long UnitPriceCents, int Quantity, decimal Rate, decimal? TakeawayRate = null, decimal DiscountPercent = 0, bool IsComp = false, long ModifiersExtraCents = 0);

/// <summary>
/// Un règlement : montant TTC fiscal encaissé (le pourboire est ajouté au dernier), moyen, et espèces remises en plus (rendu monnaie).
/// Un montant négatif (<see cref="Rest"/>) veut dire « tout le reste de la commande » : le test le calcule à partir du total réel.
/// </summary>
internal sealed record PaySpec(long AmountCents, PaymentMethod Method = PaymentMethod.CreditCard, long ExtraTenderedCents = 0)
{
    public static PaySpec Rest(PaymentMethod method = PaymentMethod.CreditCard, long extraTenderedCents = 0) => new(-1, method, extraTenderedCents);
}

internal sealed record ScenarioSpec(
    string Name,
    OrderDestination Destination,
    LineSpec[] Lines,
    PaySpec[] Payments,
    DiscountType? GlobalType = null,
    decimal GlobalValue = 0,
    long TipCents = 0,
    int? VoidPayment = null);

internal sealed record GroupOutcome(decimal Rate, long TtcCents, long BaseHtCents, long VatCents);

internal sealed record OrderOutcome(long SubtotalCents, long DiscountCents, long TotalTtcCents, long TotalHtCents, long TotalVatCents, IReadOnlyList<GroupOutcome> Groups);

/// <summary><c>TenderAmountCents</c> : somme réglée pour ce reçu, pourboire compris sur le dernier (0 pour une annulation).</summary>
internal sealed record ReceiptOutcome(long TtcCents, long HtCents, string TaxJson, IReadOnlyList<KeyValuePair<decimal, long>> VatByRate, long ChangeGivenCents, long RemainingBalanceCents, long TenderAmountCents = 0)
{
    /// <summary>HT par taux persisté sur le reçu (colonne non signée <c>HtBreakdownJson</c>).</summary>
    public string? HtJson { get; init; }

    public IReadOnlyList<KeyValuePair<decimal, long>> HtByRate { get; init; } = [];
}

internal sealed record ScenarioOutcome(ScenarioSpec Spec, OrderOutcome Order, IReadOnlyList<ReceiptOutcome> Payments, ReceiptOutcome? Voided);

internal static class FiscalScenarios
{
    public static string Rate(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    public static Order BuildOrder(ScenarioSpec spec)
    {
        var order = new Order { TableNumber = "T01", Destination = spec.Destination, TipAmount = Money.FromCents(spec.TipCents) };
        foreach (var l in spec.Lines)
        {
            order.Items.Add(new OrderItem
            {
                ProductName = "Produit",
                Quantity = l.Quantity,
                UnitPrice = Money.FromCents(l.UnitPriceCents),
                TaxRatePercent = l.Rate,
                TaxRateTakeawayPercent = l.TakeawayRate,
                DiscountPercent = l.DiscountPercent,
                IsComp = l.IsComp,
                ModifiersPriceExtra = Money.FromCents(l.ModifiersExtraCents),
            });
        }

        if (spec.GlobalType is { } type)
        {
            order.GlobalDiscountType = type;
            order.GlobalDiscountValue = spec.GlobalValue;
        }

        return order;
    }

    public static OrderOutcome DescribeOrder(Order order)
    {
        var groups = order.CalculateTaxBreakdown();
        return new OrderOutcome(
            order.Subtotal.AmountInCents, order.DiscountAmount.AmountInCents, order.TotalTtc.AmountInCents, order.TotalHt.AmountInCents,
            order.TotalTtc.AmountInCents - order.TotalHt.AmountInCents,
            groups.Select(g => new GroupOutcome(g.TaxRatePercent, g.TaxableBaseCents + g.TaxAmountCents, g.TaxableBaseCents, g.TaxAmountCents)).ToList());
    }

    public static async Task<ScenarioOutcome> RunAsync(ScenarioSpec spec)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("FiscalScenario_" + Guid.NewGuid().ToString("N")).Options;
        using var db = new AppDbContext(options);
        var service = new CheckoutPaymentService(db, new NF525FiscalAuditService(db));

        var order = BuildOrder(spec);
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var orderOutcome = DescribeOrder(order);

        var payments = new List<ReceiptOutcome>();
        var receiptIds = new List<Guid>();
        long paidSoFar = 0;
        for (int p = 0; p < spec.Payments.Length; p++)
        {
            var pay = spec.Payments[p];
            long fiscalAmount = pay.AmountCents < 0 ? orderOutcome.TotalTtcCents - paidSoFar : pay.AmountCents;
            paidSoFar += fiscalAmount;
            long amount = fiscalAmount + (p == spec.Payments.Length - 1 ? spec.TipCents : 0);
            var tenders = amount == 0
                ? new List<PaymentTenderRequest>()
                : [new PaymentTenderRequest(pay.Method, amount, pay.Method == PaymentMethod.Cash ? amount + pay.ExtraTenderedCents : amount)];
            var result = await service.ProcessPaymentTendersAsync(order.Id, "T01", tenders);
            result.IsSuccess.Should().BeTrue($"{spec.Name} payment {p}");
            var receipt = await db.FiscalReceipts.SingleAsync(r => r.ReceiptNumber == result.ReceiptNumber);
            receiptIds.Add(receipt.Id);
            payments.Add(ToOutcome(receipt, result.ChangeGivenCents, result.RemainingBalanceCents) with { TenderAmountCents = amount });
        }

        paidSoFar.Should().Be(orderOutcome.TotalTtcCents, $"{spec.Name}: les règlements doivent solder exactement la commande");

        ReceiptOutcome? voided = null;
        if (spec.VoidPayment is { } index)
        {
            (await service.VoidReceiptAsync(receiptIds[index], "T01", Guid.NewGuid())).IsSuccess.Should().BeTrue();
            var voidReceipt = await db.FiscalReceipts.SingleAsync(r => r.VoidedReceiptId == receiptIds[index]);
            voided = ToOutcome(voidReceipt, 0, 0);
        }

        return new ScenarioOutcome(spec, orderOutcome, payments, voided);
    }

    private static ReceiptOutcome ToOutcome(FiscalReceipt receipt, long change, long remaining)
    {
        var vat = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(receipt.TaxBreakdownJson)!
            .Select(kv => new KeyValuePair<decimal, long>(decimal.Parse(kv.Key, CultureInfo.InvariantCulture), kv.Value)).ToList();
        var ht = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(receipt.HtBreakdownJson ?? "{}")!
            .Select(kv => new KeyValuePair<decimal, long>(decimal.Parse(kv.Key, CultureInfo.InvariantCulture), kv.Value)).ToList();
        return new ReceiptOutcome(receipt.TotalTtcAmount.AmountInCents, receipt.TotalHtAmount.AmountInCents, receipt.TaxBreakdownJson, vat, change, remaining)
        {
            HtJson = receipt.HtBreakdownJson,
            HtByRate = ht,
        };
    }

    private static PaySpec Card(long cents) => new(cents);

    private static readonly decimal[] LineDiscounts = [10m, 33.33m, 100m];
    private static readonly decimal[] GlobalPercents = [5m, 10m, 33.33m, 50m, 100m];
    private static readonly int[] PartCounts = [1, 1, 2, 3, 5];

    /// <summary>Cas écrits à la main (plus lisibles à relire) puis jeu aléatoire déterministe.</summary>
    public static IReadOnlyList<ScenarioSpec> All { get; } = [.. Handwritten(), .. Random(20261011, 120)];

    private static IEnumerable<ScenarioSpec> Handwritten()
    {
        // total TTC calculé à la main dans chaque commentaire
        yield return new("simple-eat-in", OrderDestination.EatIn, [new(2000, 2, 10m)], [new(4000, PaymentMethod.Cash, 1000)], VoidPayment: 0); // 4000
        yield return new("multi-rate", OrderDestination.EatIn, [new(1250, 1, 10m), new(890, 2, 20m), new(450, 3, 5.5m)], [Card(4380)]); // 1250+1780+1350
        yield return new("takeaway-reduced-rate", OrderDestination.Takeaway, [new(1000, 1, 10m, 5.5m)], [Card(1000)]);
        yield return new("takeaway-one-reduced-line-among-others", OrderDestination.Takeaway, [new(1000, 1, 10m, 5.5m), new(2000, 1, 20m), new(500, 1, 10m)], [Card(3500)]);
        yield return new("global-percent-discount", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m)], [Card(2700)], DiscountType.Percentage, 10m);
        yield return new("global-percent-33.33", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m), new(777, 1, 5.5m)], [PaySpec.Rest()], DiscountType.Percentage, 33.33m);
        yield return new("global-percent-100", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m)], [new(0)], DiscountType.Percentage, 100m);
        yield return new("global-fixed-discount", OrderDestination.EatIn, [new(1000, 1, 10m), new(2000, 1, 20m)], [new(2700, PaymentMethod.Cash, 300)], DiscountType.FixedAmount, 3m);
        yield return new("global-fixed-equal-to-total", OrderDestination.EatIn, [new(1000, 1, 10m)], [new(0)], DiscountType.FixedAmount, 10m);
        yield return new("global-fixed-above-total", OrderDestination.EatIn, [new(1000, 1, 10m)], [new(0)], DiscountType.FixedAmount, 15m);
        yield return new("split-two-payments", OrderDestination.EatIn, [new(1250, 1, 10m), new(890, 2, 20m)], [Card(1000), new(2030, PaymentMethod.Cash, 500)], VoidPayment: 1);
        yield return new("split-three-equal-with-remainder", OrderDestination.EatIn, [new(1000, 1, 10m), new(1000, 1, 20m), new(1, 1, 5.5m)], [Card(668), Card(667), Card(666)], VoidPayment: 0); // 2001
        yield return new("split-five-with-discount-and-takeaway", OrderDestination.Takeaway, [new(999, 1, 10m, 5.5m), new(1999, 1, 20m), new(1, 1, 0m)], [Card(400), Card(400), Card(400), Card(400), PaySpec.Rest()], DiscountType.Percentage, 33.33m);
        yield return new("tip-single", OrderDestination.EatIn, [new(1250, 1, 10m)], [new(1250, PaymentMethod.Cash, 750)], TipCents: 200);
        yield return new("tip-with-split", OrderDestination.EatIn, [new(1250, 1, 10m), new(2000, 1, 20m)], [Card(1000), Card(2250)], TipCents: 350, VoidPayment: 1);
        yield return new("line-discount-comp-and-modifiers", OrderDestination.EatIn, [new(1000, 2, 10m, DiscountPercent: 10m), new(500, 1, 10m, IsComp: true), new(300, 1, 20m, ModifiersExtraCents: 50)], [Card(2150)]); // 1800 + 0 + 350
        yield return new("zero-total-comp", OrderDestination.EatIn, [new(500, 1, 10m, IsComp: true)], [new(0)]);
        yield return new("one-cent", OrderDestination.EatIn, [new(1, 1, 20m)], [Card(1)]);
        yield return new("many-rates", OrderDestination.EatIn, [new(333, 1, 0m), new(333, 1, 2.1m), new(333, 1, 5.5m), new(333, 1, 10m), new(333, 1, 20m)], [PaySpec.Rest()], DiscountType.FixedAmount, 0.07m);
        yield return new("big-total", OrderDestination.EatIn, [new(9_999_999, 9, 10m), new(8_888_888, 7, 20m)], [PaySpec.Rest()]);
    }

    private static IEnumerable<ScenarioSpec> Random(int seed, int count)
    {
        var random = new System.Random(seed);
        decimal[] rates = [0m, 2.1m, 5.5m, 10m, 20m];
        for (int i = 0; i < count; i++)
        {
            var lines = Enumerable.Range(0, random.Next(1, 7)).Select(_ => new LineSpec(
                random.Next(1, 4000), random.Next(1, 6), rates[random.Next(rates.Length)],
                random.Next(3) == 0 ? rates[random.Next(rates.Length)] : null,
                random.Next(4) == 0 ? LineDiscounts[random.Next(3)] : 0m,
                random.Next(12) == 0,
                random.Next(3) == 0 ? random.Next(0, 300) : 0)).ToArray();
            int kind = random.Next(3);
            DiscountType? type = kind == 0 ? null : kind == 1 ? DiscountType.Percentage : DiscountType.FixedAmount;
            decimal value = kind == 1 ? GlobalPercents[random.Next(5)] : kind == 2 ? random.Next(1, 8000) / 100m : 0m;
            var destination = random.Next(2) == 0 ? OrderDestination.EatIn : OrderDestination.Takeaway;
            long tip = random.Next(4) == 0 ? random.Next(1, 500) : 0;

            // les montants de règlement sont dérivés du total réel de la commande
            var probe = BuildOrder(new ScenarioSpec("probe", destination, lines, [], type, value, tip));
            long total = probe.TotalTtc.AmountInCents;
            int parts = PartCounts[random.Next(5)];
            var amounts = Split(total, parts, random);
            var payments = amounts.Select((a, idx) => new PaySpec(a, idx % 2 == 0 ? PaymentMethod.Cash : PaymentMethod.CreditCard, random.Next(0, 800))).ToArray();
            int? voidIndex = random.Next(4) == 0 && payments.Length > 0 ? random.Next(payments.Length) : null;
            yield return new ScenarioSpec($"random-{seed}-{i}", destination, lines, payments, type, value, tip, voidIndex);
        }
    }

    private static long[] Split(long total, int parts, System.Random random)
    {
        if (parts <= 1 || total < parts)
        {
            return [total];
        }

        var cuts = Enumerable.Range(0, parts - 1).Select(_ => random.NextInt64(1, total)).OrderBy(c => c).ToList();
        var result = new List<long>();
        long previous = 0;
        foreach (long cut in cuts)
        {
            result.Add(cut - previous);
            previous = cut;
        }

        result.Add(total - previous);
        return result.Where(r => r > 0).ToArray();
    }
}
