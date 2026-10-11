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

    private static long Cents(JsonElement euros) => (long)(euros.GetDecimal() * 100m);
}
