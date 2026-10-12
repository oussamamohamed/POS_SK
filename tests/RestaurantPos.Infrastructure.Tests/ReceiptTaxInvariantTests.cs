using FluentAssertions;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class ReceiptTaxInvariantTests
{
    public static IEnumerable<object[]> Names() => FiscalScenarios.All.Select(s => new object[] { s.Name });

    [Theory]
    [MemberData(nameof(Names))]
    public async Task EveryScenarioKeepsTheFiscalInvariants(string name)
    {
        var spec = FiscalScenarios.All.Single(s => s.Name == name);
        var outcome = await FiscalScenarios.RunAsync(spec);
        string context = $"{name} (lines: {string.Join("; ", spec.Lines.Select(l => l.ToString()))})";
        var order = outcome.Order;

        // commande
        order.Groups.Sum(g => g.TtcCents).Should().Be(order.TotalTtcCents, context);
        order.Groups.Sum(g => g.BaseHtCents).Should().Be(order.TotalHtCents, context);
        order.Groups.Sum(g => g.VatCents).Should().Be(order.TotalVatCents, context);
        (order.TotalTtcCents - order.TotalHtCents).Should().Be(order.TotalVatCents, context);
        order.Groups.Should().OnlyContain(g => g.BaseHtCents + g.VatCents == g.TtcCents && g.TtcCents >= 0, context);
        order.DiscountCents.Should().Be(order.SubtotalCents - order.TotalTtcCents, context);
        order.Groups.Select(g => g.Rate).Should().BeInAscendingOrder(context);

        // reçus
        foreach (var (receipt, index) in outcome.Payments.Select((r, i) => (r, i)))
        {
            string at = $"{context} payment {index}";
            (receipt.TtcCents - receipt.HtCents).Should().Be(receipt.VatByRate.Sum(v => v.Value), at);
            receipt.VatByRate.Should().OnlyContain(v => v.Value >= 0, at);
            receipt.HtCents.Should().BeGreaterThanOrEqualTo(0, at);
            receipt.VatByRate.Select(v => v.Key).Should().Equal(order.Groups.Select(g => g.Rate), at);

            // HT par taux persisté : mêmes taux, même ordre que la TVA, somme = HT du reçu ; reçu complet = bases HT de la commande
            receipt.HtJson.Should().NotBeNull(at);
            receipt.HtByRate.Select(h => h.Key).Should().Equal(receipt.VatByRate.Select(v => v.Key), at);
            receipt.HtByRate.Sum(h => h.Value).Should().Be(receipt.HtCents, at);
            receipt.HtByRate.Should().OnlyContain(h => h.Value >= 0, at);
            if (outcome.Payments.Count == 1)
            {
                receipt.HtByRate.Select(h => h.Value).Should().Equal(order.Groups.Select(g => g.BaseHtCents), at);
            }

            // HT par taux + TVA par taux = part TTC du taux : cohérent avec la règle de calcul
            for (int g = 0; g < receipt.HtByRate.Count; g++)
            {
                var part = RestaurantPos.Domain.ValueObjects.TaxBreakdownItem.Calculate(receipt.HtByRate[g].Key, receipt.HtByRate[g].Value + receipt.VatByRate[g].Value);
                part.TaxableBaseCents.Should().Be(receipt.HtByRate[g].Value, $"{at} rate {receipt.HtByRate[g].Key}");
            }
        }

        // Σ TTC des reçus = TTC de la commande (commande soldée) ; écart par taux ≤ 1 centime par reçu
        outcome.Payments.Sum(r => r.TtcCents).Should().Be(order.TotalTtcCents, context);
        outcome.Payments[^1].RemainingBalanceCents.Should().Be(0, context);
        for (int g = 0; g < order.Groups.Count; g++)
        {
            long vatAcrossReceipts = outcome.Payments.Sum(r => r.VatByRate[g].Value);
            Math.Abs(vatAcrossReceipts - order.Groups[g].VatCents).Should().BeLessThanOrEqualTo(outcome.Payments.Count, $"{context} rate {order.Groups[g].Rate}");
        }

        // annulation : négation exacte de l'original
        if (outcome.Voided is { } voided && spec.VoidPayment is { } v)
        {
            var original = outcome.Payments[v];
            voided.TtcCents.Should().Be(-original.TtcCents, context);
            voided.HtCents.Should().Be(-original.HtCents, context);
            voided.VatByRate.Select(x => x.Value).Should().Equal(original.VatByRate.Select(x => -x.Value), context);
            voided.HtByRate.Select(x => (x.Key, x.Value)).Should().Equal(original.HtByRate.Select(x => (x.Key, -x.Value)), context);
            voided.HtByRate.Sum(x => x.Value).Should().Be(voided.HtCents, context);
        }
    }

    [Fact]
    public async Task TakeawayAtReducedRate_IsDeclaredUnderThatRate_NeverUnderTheBaseRate()
    {
        var spec = FiscalScenarios.All.Single(s => s.Name == "takeaway-reduced-rate");
        var outcome = await FiscalScenarios.RunAsync(spec);
        outcome.Payments.Single().TaxJson.Should().Be("{\"5.5\":52}");
        outcome.Payments.Single().HtCents.Should().Be(948);
    }

    [Fact]
    public async Task ZeroTotalOrders_GiveAZeroReceipt()
    {
        foreach (var name in new[] { "zero-total-comp", "global-percent-100", "global-fixed-equal-to-total", "global-fixed-above-total" })
        {
            var outcome = await FiscalScenarios.RunAsync(FiscalScenarios.All.Single(s => s.Name == name));
            var receipt = outcome.Payments.Single();
            receipt.TtcCents.Should().Be(0, name);
            receipt.HtCents.Should().Be(0, name);
            receipt.VatByRate.Should().OnlyContain(v => v.Value == 0, name);
        }
    }

    [Fact]
    public void ScenarioNamesAreUnique_AndTheMatrixIsBigEnough()
    {
        FiscalScenarios.All.Select(s => s.Name).Should().OnlyHaveUniqueItems();
        FiscalScenarios.All.Count.Should().BeGreaterThanOrEqualTo(120);
    }
}
