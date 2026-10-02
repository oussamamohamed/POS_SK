using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FluentAssertions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Localization;
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

    private static ReportPrintData SampleReportData(bool withTips = true) => new(
        [new ReportCategoryGroup("Desserts", [new ReportItemLine("Tiramisu", 3, 2100)], 2100), new ReportCategoryGroup(null, [new ReportItemLine("Vente Comptoir", 1, 500)], 500)],
        withTips ? [new ReportTipLine("Alex", 150), new ReportTipLine(null, 100)] : [],
        withTips ? 250 : 0,
        HasGlobalDiscount: false);

    private static FiscalSummaryDto SampleSummary() => new("POS_MAIN_TERM", DateTimeOffset.MinValue, new DateTimeOffset(2026, 9, 30, 20, 0, 0, TimeSpan.Zero),
        2600, 2364, 2, new Dictionary<decimal, long> { [10m] = 236 }, new Dictionary<PaymentMethod, long> { [PaymentMethod.Cash] = 2600 }, 99_000);

    [Theory]
    [InlineData("en", "X REPORT", "ITEMS SOLD", "Other", "Unknown")]
    [InlineData("fr", "RAPPORT X", "ARTICLES VENDUS", "Autres", "Inconnu")]
    [InlineData("ar", "تقرير X", "الأصناف المباعة", "أخرى", "غير معروف")]
    public void XReport_ContainsTotalsItemsAndTips_Localized(string lang, string title, string items, string other, string unknown)
    {
        var doc = TicketDocumentBuilder.XReport(SampleSummary(), SampleReportData(), lang, DateTimeOffset.UnixEpoch);
        var text = AllText(doc);
        doc.Lines[0].Should().BeOfType<TicketText>().Which.Text.Should().Be(title);
        text.Should().Contain(items).And.Contain("3x Tiramisu | 21.00").And.Contain(other).And.Contain(unknown)
            .And.Contain(Texts.Get(CultureInfo.GetCultureInfo(lang), "admin.payment_method_cash"))
            .And.Contain("26.00").And.Contain("990.00").And.Contain(" | -\n").And.NotContain("—");
    }

    [Fact]
    public void XReport_NoTips_OmitsTipsSection() =>
        AllText(TicketDocumentBuilder.XReport(SampleSummary(), SampleReportData(withTips: false), "fr", DateTimeOffset.UnixEpoch))
            .Should().NotContain("POURBOIRES");

    [Fact]
    public void ZClosure_HasSequenceManagerAndSignature()
    {
        var closure = new DailyFiscalClosureDto(Guid.NewGuid(), "POS_MAIN_TERM", 12, 2600, 2364, 2, new Dictionary<decimal, long> { [10m] = 236 },
            new Dictionary<PaymentMethod, long> { [PaymentMethod.CreditCard] = 2600 }, 99_000, "abc123", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(-10), "Alexandre");
        var text = AllText(TicketDocumentBuilder.ZClosure(closure, SampleReportData(), "fr"));
        text.Should().StartWith("CLÔTURE Z n° 12").And.Contain("Alexandre").And.Contain("abc123").And.Contain("ARTICLES VENDUS");
    }

    [Fact]
    public void Receipt_WithoutCertificate_ShowsSettingsAndVersionOnly()
    {
        var settings = new RestaurantSettingsDto(
            ReceiptLanguage: "fr",
            KitchenTicketLanguage: "fr",
            CompanyName: "Bistro Parisien",
            AddressLines: "1 rue de la Paix\n75002 Paris",
            Siret: "12345678901234",
            VatNumber: "FR99123456789",
            CertificateNumber: null,
            FiscalYearStartMonth: 1,
            FiscalYearStartDay: 1);

        var doc = TicketDocumentBuilder.Receipt(SampleReceipt(), SampleOrder(), "fr", settings);
        var text = AllText(doc);

        text.Should().Contain("Bistro Parisien")
            .And.Contain("1 rue de la Paix")
            .And.Contain("75002 Paris")
            .And.Contain("SIRET: 12345678901234")
            .And.Contain("TVA: FR99123456789")
            .And.Contain("Logiciel: RestaurantPOS v")
            .And.NotContain("Certificat:");
    }

    [Fact]
    public void Receipt_WithCertificate_PrintsCertificateInHeader()
    {
        var settings = new RestaurantSettingsDto(
            ReceiptLanguage: "fr",
            KitchenTicketLanguage: "fr",
            CompanyName: "Bistro Parisien",
            AddressLines: "1 rue de la Paix\n75002 Paris",
            Siret: "12345678901234",
            VatNumber: "FR99123456789",
            CertificateNumber: "INFOCERT-2026-9999",
            FiscalYearStartMonth: 1,
            FiscalYearStartDay: 1);

        var doc = TicketDocumentBuilder.Receipt(SampleReceipt(), SampleOrder(), "fr", settings);
        var text = AllText(doc);

        text.Should().Contain("Certificat: INFOCERT-2026-9999")
            .And.Contain("Logiciel: RestaurantPOS v");
    }
}
