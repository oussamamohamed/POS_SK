using System.Globalization;
using System.Text.Json;
using FluentAssertions;

namespace RestaurantPos.Api.Tests.DaySimulation;

/// <summary>Compare une réponse X / Z / dernière Z (montants en euros, clés de TVA = taux, clés de règlement = nom du moyen) au grand livre.</summary>
public static class DayAssert
{
    public static void SummaryEquals(JsonElement summary, TerminalLedger expected, string because)
    {
        ArgumentNullException.ThrowIfNull(expected);
        Cents(summary.GetProperty("totalSalesTtc")).Should().Be(expected.TtcCents, $"{because} : TTC");
        Cents(summary.GetProperty("totalSalesHt")).Should().Be(expected.HtCents, $"{because} : HT");
        summary.GetProperty("receiptCount").GetInt32().Should().Be(expected.ReceiptCount, $"{because} : nombre de reçus");

        var vat = summary.GetProperty("vatBreakdown").EnumerateObject()
            .ToDictionary(p => decimal.Parse(p.Name, NumberStyles.Number, CultureInfo.InvariantCulture), p => Cents(p.Value));
        vat.Should().BeEquivalentTo(expected.VatByRate, $"{because} : TVA par taux");

        var tenders = summary.GetProperty("paymentTotals").EnumerateObject().ToDictionary(p => p.Name, p => Cents(p.Value));
        tenders.Should().BeEquivalentTo(expected.TenderByMethod, $"{because} : moyens de paiement");
    }

    /// <summary>Deux rapports (par exemple un X et la Z qui le suit) portent les mêmes chiffres : TTC, HT, reçus, TVA, moyens de paiement et grand total perpétuel.</summary>
    public static void SummariesEqual(JsonElement actual, JsonElement expected, string because)
    {
        Cents(actual.GetProperty("totalSalesTtc")).Should().Be(Cents(expected.GetProperty("totalSalesTtc")), $"{because} : TTC");
        Cents(actual.GetProperty("totalSalesHt")).Should().Be(Cents(expected.GetProperty("totalSalesHt")), $"{because} : HT");
        actual.GetProperty("receiptCount").GetInt32().Should().Be(expected.GetProperty("receiptCount").GetInt32(), $"{because} : nombre de reçus");
        Cents(actual.GetProperty("perpetualGrandTotal")).Should().Be(Cents(expected.GetProperty("perpetualGrandTotal")), $"{because} : grand total perpétuel");
        Map(actual.GetProperty("vatBreakdown")).Should().BeEquivalentTo(Map(expected.GetProperty("vatBreakdown")), $"{because} : TVA par taux");
        Map(actual.GetProperty("paymentTotals")).Should().BeEquivalentTo(Map(expected.GetProperty("paymentTotals")), $"{because} : moyens de paiement");
    }

    /// <summary>Rapport d'une période sans vente : tout à zéro, ni TVA ni règlement.</summary>
    public static void IsEmpty(JsonElement summary, string because)
    {
        Cents(summary.GetProperty("totalSalesTtc")).Should().Be(0, $"{because} : TTC");
        Cents(summary.GetProperty("totalSalesHt")).Should().Be(0, $"{because} : HT");
        summary.GetProperty("receiptCount").GetInt32().Should().Be(0, $"{because} : nombre de reçus");
        Map(summary.GetProperty("vatBreakdown")).Should().BeEmpty($"{because} : TVA");
        Map(summary.GetProperty("paymentTotals")).Should().BeEmpty($"{because} : moyens de paiement");
    }

    public static long Cents(JsonElement euros) => (long)(euros.GetDecimal() * 100m);

    private static Dictionary<string, long> Map(JsonElement obj) => obj.EnumerateObject().ToDictionary(p => p.Name, p => Cents(p.Value));
}
