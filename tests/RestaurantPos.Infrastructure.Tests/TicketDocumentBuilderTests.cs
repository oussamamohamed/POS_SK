using System;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Printing;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class TicketDocumentBuilderTests
{
    private static Order SampleOrder() => new()
    {
        Destination = OrderDestination.Takeaway,
        Items = { new OrderItem { ProductName = "Burger Rossini", Quantity = 2, UnitPrice = Money.FromCents(1250) } }
    };

    private static FiscalReceipt SampleReceipt() => new()
    {
        TerminalId = "T01",
        ReceiptNumber = "T01-000042",
        TotalTtcAmount = Money.FromCents(2500),
        TotalHtAmount = Money.FromCents(2273),
        TaxBreakdownJson = "{\"10\":227}",
        SignatureHash = "abc123"
    };

    private static string AllText(TicketDocument doc) => string.Join("\n", doc.Lines.Select(l => l switch
    {
        TicketText t => t.Text,
        TicketColumns c => c.Label + " | " + c.Value,
        _ => "---"
    }));

    [Theory]
    [InlineData("en", "TAKEAWAY", "PICKUP")]
    [InlineData("fr", "À EMPORTER", "RETRAIT")]
    [InlineData("ar", "سفري", "استلام")]
    public void PickupCoupon_UsesReceiptLanguage(string lang, string mode, string title)
    {
        var doc = TicketDocumentBuilder.PickupCoupon(SampleOrder(), "A12", "7", lang, DateTimeOffset.UnixEpoch);
        var text = AllText(doc);
        text.Should().Contain(mode).And.Contain(title).And.Contain("A12").And.Contain("2x Burger Rossini");
        doc.Language.Should().Be(lang);
    }

    [Fact]
    public void Arabic_IsRightToLeft_OthersNot()
    {
        TicketDocumentBuilder.PickupCoupon(SampleOrder(), "1", null, "ar", DateTimeOffset.UnixEpoch).RightToLeft.Should().BeTrue();
        TicketDocumentBuilder.PickupCoupon(SampleOrder(), "1", null, "fr", DateTimeOffset.UnixEpoch).RightToLeft.Should().BeFalse();
    }

    [Fact]
    public void FiscalReceipt_ContainsTotals_Signature_AndPickupCoupon()
    {
        var doc = TicketDocumentBuilder.FiscalReceipt(SampleReceipt(), SampleOrder(), "A12", null, "en");
        var text = AllText(doc);
        text.Should().Contain("TOTAL INCL. VAT | 25.00").And.Contain("abc123").And.Contain("T01-000042").And.Contain("PICKUP");
        doc.Lines.OfType<TicketSeparator>().Should().Contain(s => s.Cut);
    }

    [Fact]
    public void Amounts_UseWesternDigits_InArabic()
    {
        var text = AllText(TicketDocumentBuilder.FiscalReceipt(SampleReceipt(), SampleOrder(), "A12", null, "ar"));
        text.Should().Contain("25.00").And.NotContainAny("٠", "١", "٢", "٥");
    }

    [Fact]
    public void UnsupportedLanguage_FallsBackToEnglish() =>
        AllText(TicketDocumentBuilder.PickupCoupon(SampleOrder(), "1", null, "de", DateTimeOffset.UnixEpoch)).Should().Contain("TAKEAWAY");

    [Fact]
    public void Nf525Hash_IsIndependentOfCulture()
    {
        var service = new NF525FiscalAuditService(null!);
        string Hash() => service.ComputeReceiptHashSignature(NF525FiscalAuditService.GenesisHash, "T01", 1, 2500,
            new DateTimeOffset(2026, 9, 27, 12, 0, 0, TimeSpan.Zero), "{\"10\":227}");
        var original = (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
            var en = Hash();
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
            var fr = Hash();
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ar");
            Hash().Should().Be(en).And.Be(fr);
        }
        finally
        {
            (CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture) = original;
        }
    }

    [Theory]
    [InlineData("en", "PRINT TEST")]
    [InlineData("fr", "TEST D'IMPRESSION")]
    [InlineData("ar", "اختبار الطباعة")]
    public void TestPage_IsLocalized_AndShowsPrinter(string lang, string title)
    {
        var printer = new PrinterConfiguration { Name = "Cuisine", IpAddress = "10.0.0.5", Port = 9100, PaperWidthMm = 58 };
        var text = AllText(TicketDocumentBuilder.TestPage(printer, lang, DateTimeOffset.UnixEpoch));
        text.Should().Contain(title).And.Contain("Cuisine").And.Contain("10.0.0.5:9100").And.Contain("58 mm");
    }

    private static readonly DateTimeOffset Dispatched = new(2026, 9, 29, 12, 34, 0, TimeSpan.Zero);

    private static KitchenTicket SampleKitchenTicket(Guid productId) => new()
    {
        TableNumber = "stocké, ignoré",
        ServerName = "Julie",
        CoversCount = 4,
        StationId = "HOT_KITCHEN",
        DispatchedAtUtc = Dispatched,
        Items = { new KitchenTicketItem { ProductId = productId, ProductName = "Burger Rossini", Quantity = 2, ModifiersSummary = "Saignant", KitchenComment = "Sans oignon" } }
    };

    [Theory]
    [InlineData("en", "TABLE T05", "Next course", "Covers")]
    [InlineData("fr", "TABLE T05", "Suite", "Couverts")]
    [InlineData("ar", "طاولة T05", "الطبق التالي", "عدد الأشخاص")]
    public void KitchenTicket_EatIn_ShowsTableCourseAndDetails(string lang, string header, string course, string covers)
    {
        var productId = Guid.NewGuid();
        var order = new Order { TableNumber = "T05", Destination = OrderDestination.EatIn, Items = { new OrderItem { ProductId = productId, ProductName = "Burger Rossini", Course = CourseType.Suite } } };
        var doc = TicketDocumentBuilder.KitchenTicket(SampleKitchenTicket(productId), order, lang);
        var text = AllText(doc);

        doc.RightToLeft.Should().Be(lang == "ar");
        doc.Lines[0].Should().BeOfType<TicketText>().Which.Should().Match<TicketText>(t => t.Text == header && t.Large);
        text.Should().Contain("2x Burger Rossini").And.Contain("+ Saignant").And.Contain("Sans oignon")
            .And.Contain(course).And.Contain(covers).And.Contain("Julie")
            .And.Contain(Dispatched.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture))
            .And.NotContain("stocké, ignoré");
    }

    [Theory]
    [InlineData("en", "TAKEAWAY", "Buzzer 7")]
    [InlineData("fr", "À EMPORTER", "Bipeur 7")]
    [InlineData("ar", "سفري", "جهاز النداء 7")]
    public void KitchenTicket_Takeaway_ShowsPickupAndBuzzer(string lang, string header, string buzzer)
    {
        var order = new Order { TableNumber = "Comptoir", Destination = OrderDestination.Takeaway, PickupNumber = "A-12", PickupBuzzer = "7" };
        var text = AllText(TicketDocumentBuilder.KitchenTicket(SampleKitchenTicket(Guid.NewGuid()), order, lang));
        text.Should().StartWith(header).And.Contain("A-12").And.Contain(buzzer);
    }

    [Fact]
    public void Receipt_HasFiscalContent_WithoutPickupCoupon()
    {
        var doc = TicketDocumentBuilder.Receipt(SampleReceipt(), SampleOrder(), "fr");
        var text = AllText(doc);
        text.Should().Contain("T01-000042").And.Contain("abc123").And.NotContain("RETRAIT");
        doc.Lines.OfType<TicketSeparator>().Should().NotContain(s => s.Cut);
    }
}
