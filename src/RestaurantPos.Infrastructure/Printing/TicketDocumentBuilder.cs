using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Construit les tickets dans la langue des tickets du restaurant. Le rendu est fait par EscPosRasterRenderer.</summary>
public static class TicketDocumentBuilder
{
    public static TicketDocument PickupCoupon(Order order, string pickupNumber, string? buzzer, string language, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(order);
        var (lang, culture) = Resolve(language);
        var lines = new List<TicketLine>();
        AppendPickup(lines, culture, order, pickupNumber, buzzer, nowUtc);
        return new TicketDocument(lang, lang == "ar", lines);
    }

    public static TicketDocument Receipt(FiscalReceipt receipt, Order order, string language, RestaurantSettingsDto? settings = null, int? duplicateNumber = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(order);
        var (lang, c) = Resolve(language);
        return new TicketDocument(lang, lang == "ar", ReceiptLines(receipt, order, c, settings, duplicateNumber));
    }

    public static TicketDocument ReceiptFallback(FiscalReceipt receipt, string language, RestaurantSettingsDto? settings = null, int? duplicateNumber = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var (lang, c) = Resolve(language);
        var lines = new List<TicketLine>();
        if (duplicateNumber.HasValue)
        {
            lines.Add(new TicketText($"DUPLICATA n°{duplicateNumber.Value}", TicketAlign.Center, Bold: true, Large: true));
            lines.Add(new TicketSeparator());
        }
        lines.Add(new TicketSeparator());
        AppendHeader(lines, settings);
        lines.Add(new TicketSeparator());
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.ticket"), receipt.ReceiptNumber));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.date"), receipt.CreatedAtUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.terminal"), receipt.TerminalId));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.total_ttc"), Amount(receipt.TotalTtcAmount.AmountInCents)));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.total_ht"), Amount(receipt.TotalHtAmount.AmountInCents)));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText(Texts.Get(c, "receipt.vat_breakdown"), Bold: true));
        lines.Add(new TicketText(receipt.TaxBreakdownJson));
        lines.Add(new TicketText(Texts.Get(c, "receipt.fiscal_signature"), Bold: true));
        lines.Add(new TicketText(receipt.SignatureHash));
        return new TicketDocument(lang, lang == "ar", lines);
    }

    public static TicketDocument FiscalReceipt(FiscalReceipt receipt, Order order, string pickupNumber, string? buzzer, string language, RestaurantSettingsDto? settings = null, int? duplicateNumber = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(order);
        var (lang, c) = Resolve(language);
        var lines = ReceiptLines(receipt, order, c, settings, duplicateNumber);
        lines.Add(new TicketSeparator(Cut: true));
        AppendPickup(lines, c, order, pickupNumber, buzzer, receipt.CreatedAtUtc);
        return new TicketDocument(lang, lang == "ar", lines);
    }

    public static TicketDocument XReport(FiscalSummaryDto summary, ReportPrintData data, string language, DateTimeOffset nowUtc, RestaurantSettingsDto? settings = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(data);
        var (lang, c) = Resolve(language);
        var lines = new List<TicketLine>
        {
            new TicketText(Texts.Get(c, "report.x_title"), TicketAlign.Center, Large: true),
            new TicketSeparator(),
            new TicketColumns(Texts.Get(c, "receipt.terminal"), summary.TerminalId),
            new TicketColumns(Texts.Get(c, "report.period_start"), LocalDate(summary.PeriodStartUtc)),
            new TicketColumns(Texts.Get(c, "report.period_end"), LocalDate(summary.PeriodEndUtc)),
            new TicketColumns(Texts.Get(c, "report.printed_at"), LocalDate(nowUtc))
        };
        AppendReportTotals(lines, c, summary.ReceiptCount, summary.TotalSalesTtcCents, summary.TotalSalesHtCents, summary.VatBreakdownCents, summary.PaymentTotalsCents, summary.PerpetualGrandTotalCents);
        AppendReportDetails(lines, c, data);
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText($"Logiciel: RestaurantPOS v{GetSoftwareVersion()}", TicketAlign.Center));
        if (!string.IsNullOrWhiteSpace(settings?.CertificateNumber))
            lines.Add(new TicketText($"Certificat: {settings.CertificateNumber}", TicketAlign.Center));
        return new TicketDocument(lang, lang == "ar", lines);
    }

    public static TicketDocument ZClosure(DailyFiscalClosureDto closure, ReportPrintData data, string language, RestaurantSettingsDto? settings = null, int? duplicateNumber = null)
    {
        ArgumentNullException.ThrowIfNull(closure);
        ArgumentNullException.ThrowIfNull(data);
        var (lang, c) = Resolve(language);
        var lines = new List<TicketLine>();
        if (duplicateNumber.HasValue)
        {
            lines.Add(new TicketText($"DUPLICATA n°{duplicateNumber.Value}", TicketAlign.Center, Bold: true, Large: true));
            lines.Add(new TicketSeparator());
        }
        lines.Add(new TicketText(Texts.Get(c, "report.z_title", ("sequence", closure.ClosureSequence)), TicketAlign.Center, Large: true));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.terminal"), closure.TerminalId));
        lines.Add(new TicketColumns(Texts.Get(c, "report.period_start"), LocalDate(closure.PeriodStartUtc)));
        lines.Add(new TicketColumns(Texts.Get(c, "report.closed_at"), LocalDate(closure.ClosedAtUtc)));
        lines.Add(new TicketColumns(Texts.Get(c, "report.manager"), closure.SealedByUserName));
        AppendReportTotals(lines, c, closure.ReceiptCount, closure.TotalSalesTtcCents, closure.TotalSalesHtCents, closure.VatBreakdownCents, closure.PaymentTotalsCents, closure.PerpetualGrandTotalCents);
        AppendReportDetails(lines, c, data);
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText(Texts.Get(c, "report.signature"), Bold: true));
        lines.Add(new TicketText(closure.SignatureHash));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText($"Logiciel: RestaurantPOS v{GetSoftwareVersion()}", TicketAlign.Center));
        if (!string.IsNullOrWhiteSpace(settings?.CertificateNumber))
            lines.Add(new TicketText($"Certificat: {settings.CertificateNumber}", TicketAlign.Center));
        return new TicketDocument(lang, lang == "ar", lines);
    }

    public static TicketDocument PeriodClosure(PeriodClosureDto closure, ReportPrintData data, string language, RestaurantSettingsDto? settings = null)
    {
        ArgumentNullException.ThrowIfNull(closure);
        ArgumentNullException.ThrowIfNull(data);
        var (lang, c) = Resolve(language);
        var title = closure.PeriodType == FiscalPeriodType.Monthly
            ? $"CLOTURE MENSUELLE {closure.PeriodKey} - n°{closure.ClosureSequence}"
            : $"CLOTURE ANNUELLE {closure.PeriodKey} - n°{closure.ClosureSequence}";

        var lines = new List<TicketLine>
        {
            new TicketText(title, TicketAlign.Center, Large: true),
            new TicketSeparator(),
            new TicketColumns(Texts.Get(c, "receipt.terminal"), closure.TerminalId),
            new TicketColumns(Texts.Get(c, "report.period_start"), LocalDate(closure.PeriodStartUtc)),
            new TicketColumns(Texts.Get(c, "report.closed_at"), LocalDate(closure.PeriodEndUtc)),
            new TicketColumns(Texts.Get(c, "report.manager"), closure.SealedByUserName)
        };

        var vatDict = new Dictionary<decimal, long>();
        if (!string.IsNullOrWhiteSpace(closure.TaxesSummaryJson))
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, long>>(closure.TaxesSummaryJson);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        if (decimal.TryParse(kvp.Key, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal r))
                            vatDict[r] = kvp.Value;
                    }
                }
            }
            catch { }
        }

        var tenderDict = new Dictionary<PaymentMethod, long>();
        if (!string.IsNullOrWhiteSpace(closure.TenderTotalsJson))
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, long>>(closure.TenderTotalsJson);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        if (Enum.TryParse<PaymentMethod>(kvp.Key, out var m))
                            tenderDict[m] = kvp.Value;
                    }
                }
            }
            catch { }
        }

        AppendReportTotals(lines, c, closure.DailyClosureCount, closure.TotalTtcCents, closure.TotalHtCents, vatDict, tenderDict, closure.PerpetualGrandTotalCents);
        AppendReportDetails(lines, c, data);
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText(Texts.Get(c, "report.signature"), Bold: true));
        lines.Add(new TicketText(closure.SignatureHash));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText($"Logiciel: RestaurantPOS v{GetSoftwareVersion()}", TicketAlign.Center));
        if (!string.IsNullOrWhiteSpace(settings?.CertificateNumber))
            lines.Add(new TicketText($"Certificat: {settings.CertificateNumber}", TicketAlign.Center));
        return new TicketDocument(lang, lang == "ar", lines);
    }

    private static void AppendReportTotals(List<TicketLine> lines, CultureInfo c, int receiptCount, long ttc, long ht,
        IReadOnlyDictionary<decimal, long> vat, IReadOnlyDictionary<PaymentMethod, long> payments, long grandTotal)
    {
        lines.Add(new TicketSeparator());
        lines.Add(new TicketColumns(Texts.Get(c, "report.receipt_count"), receiptCount.ToString(CultureInfo.InvariantCulture)));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.total_ttc"), Amount(ttc)));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.total_ht"), Amount(ht)));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText(Texts.Get(c, "receipt.vat_breakdown"), Bold: true));
        foreach (var (rate, cents) in vat.OrderBy(v => v.Key))
            lines.Add(new TicketColumns($"{rate.ToString("0.##", CultureInfo.InvariantCulture)} %", Amount(cents)));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText(Texts.Get(c, "report.payments"), Bold: true));
        foreach (var (method, cents) in payments.OrderBy(p => p.Key))
            lines.Add(new TicketColumns(PaymentLabel(c, method), Amount(cents)));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketColumns(Texts.Get(c, "report.grand_total"), Amount(grandTotal)));
    }

    private static void AppendReportDetails(List<TicketLine> lines, CultureInfo c, ReportPrintData data)
    {
        if (data.Categories.Count > 0)
        {
            lines.Add(new TicketSeparator());
            lines.Add(new TicketText(Texts.Get(c, "report.items_sold"), TicketAlign.Center, Bold: true));
            if (data.HasGlobalDiscount) lines.Add(new TicketText(Texts.Get(c, "report.items_before_discount")));
            foreach (var group in data.Categories)
            {
                lines.Add(new TicketText(group.CategoryName ?? Texts.Get(c, "report.other_category"), Bold: true));
                foreach (var item in group.Items)
                    lines.Add(new TicketColumns($"{item.Quantity}x {item.ProductName}", Amount(item.TotalTtcCents)));
                lines.Add(new TicketColumns(Texts.Get(c, "report.subtotal"), Amount(group.SubtotalTtcCents)));
            }
        }
        if (data.TotalTipsCents > 0)
        {
            lines.Add(new TicketSeparator());
            lines.Add(new TicketText(Texts.Get(c, "report.tips_by_server"), TicketAlign.Center, Bold: true));
            foreach (var tip in data.Tips)
                lines.Add(new TicketColumns(tip.ServerName ?? Texts.Get(c, "report.unknown_server"), Amount(tip.TipCents)));
            lines.Add(new TicketColumns(Texts.Get(c, "report.tips_total"), Amount(data.TotalTipsCents)));
        }
    }

    private static string PaymentLabel(CultureInfo c, PaymentMethod method) => Texts.Get(c, method switch
    {
        PaymentMethod.Cash => "admin.payment_method_cash",
        PaymentMethod.CreditCard => "admin.payment_method_credit_card",
        PaymentMethod.MealVoucher => "admin.payment_method_meal_voucher",
        PaymentMethod.GiftCard => "admin.payment_method_gift_card",
        PaymentMethod.RoomCharge => "admin.payment_method_room_charge",
        _ => "admin.payment_method_other"
    });

    // Heure locale du serveur (restaurant) ; DateTimeOffset.MinValue = premier rapport sans clôture précédente.
    private static string LocalDate(DateTimeOffset utc) =>
        utc == DateTimeOffset.MinValue ? "-" : utc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);

    private static List<TicketLine> ReceiptLines(FiscalReceipt receipt, Order order, CultureInfo c, RestaurantSettingsDto? settings, int? duplicateNumber = null)
    {
        var lines = new List<TicketLine>();
        if (duplicateNumber.HasValue)
        {
            lines.Add(new TicketText($"DUPLICATA n°{duplicateNumber.Value}", TicketAlign.Center, Bold: true, Large: true));
            lines.Add(new TicketSeparator());
        }
        lines.Add(new TicketSeparator());
        AppendHeader(lines, settings);
        lines.Add(new TicketSeparator());
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.ticket"), receipt.ReceiptNumber));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.date"), receipt.CreatedAtUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.terminal"), receipt.TerminalId));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.mode"), Mode(c, order)));
        lines.Add(new TicketSeparator());
        foreach (var item in order.Items)
            lines.Add(new TicketColumns($"{item.Quantity}x {item.ProductName}", Amount(item.CalculateTotalTtc().AmountInCents)));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.total_ttc"), Amount(receipt.TotalTtcAmount.AmountInCents)));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.total_ht"), Amount(receipt.TotalHtAmount.AmountInCents)));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText(Texts.Get(c, "receipt.vat_breakdown"), Bold: true));
        lines.Add(new TicketText(receipt.TaxBreakdownJson));
        lines.Add(new TicketText(Texts.Get(c, "receipt.fiscal_signature"), Bold: true));
        lines.Add(new TicketText(receipt.SignatureHash));
        return lines;
    }

    private static void AppendHeader(List<TicketLine> lines, RestaurantSettingsDto? settings)
    {
        var companyName = string.IsNullOrWhiteSpace(settings?.CompanyName)
            ? "RESTAURANT L'ANTIGRAVITE"
            : settings.CompanyName;
        lines.Add(new TicketText(companyName, TicketAlign.Center, Bold: true));

        var address = string.IsNullOrWhiteSpace(settings?.AddressLines)
            ? "12 Rue de la Gastronomie\n75001 Paris"
            : settings.AddressLines;
        foreach (var line in address.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            lines.Add(new TicketText(line, TicketAlign.Center));
        }

        var siret = string.IsNullOrWhiteSpace(settings?.Siret)
            ? "88877766600012"
            : settings.Siret;
        lines.Add(new TicketText($"SIRET: {siret}", TicketAlign.Center));

        var vat = string.IsNullOrWhiteSpace(settings?.VatNumber)
            ? "FR12888777666"
            : settings.VatNumber;
        lines.Add(new TicketText($"TVA: {vat}", TicketAlign.Center));

        lines.Add(new TicketText($"Logiciel: RestaurantPOS v{GetSoftwareVersion()}", TicketAlign.Center));

        if (!string.IsNullOrWhiteSpace(settings?.CertificateNumber))
        {
            lines.Add(new TicketText($"Certificat: {settings.CertificateNumber}", TicketAlign.Center));
        }
    }

    private static string GetSoftwareVersion()
    {
        var assembly = typeof(TicketDocumentBuilder).Assembly;
        var infoVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(infoVersion))
        {
            var plusIndex = infoVersion.IndexOf('+');
            return plusIndex > 0 ? infoVersion[..plusIndex] : infoVersion;
        }
        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    public static TicketDocument KitchenTicket(KitchenTicket ticket, Order order, string language)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        ArgumentNullException.ThrowIfNull(order);
        var (lang, c) = Resolve(language);
        var lines = new List<TicketLine>
        {
            new TicketText(order.Destination == OrderDestination.Takeaway
                ? Texts.Get(c, "kitchen_ticket.takeaway")
                : Texts.Get(c, "kitchen_ticket.table", ("table", order.TableNumber)), TicketAlign.Center, Large: true)
        };
        if (!string.IsNullOrWhiteSpace(order.PickupNumber))
            lines.Add(new TicketText(Texts.Get(c, "kitchen_ticket.pickup", ("number", order.PickupNumber)), TicketAlign.Center, Large: true));
        if (!string.IsNullOrWhiteSpace(order.PickupBuzzer))
            lines.Add(new TicketText(Texts.Get(c, "kitchen_ticket.buzzer", ("buzzer", order.PickupBuzzer)), TicketAlign.Center, Bold: true));
        lines.Add(new TicketColumns(Texts.Get(c, "kitchen_ticket.server"), ticket.ServerName));
        lines.Add(new TicketColumns(Texts.Get(c, "kitchen_ticket.covers"), ticket.CoversCount.ToString(CultureInfo.InvariantCulture)));
        lines.Add(new TicketColumns(Texts.Get(c, "kitchen_ticket.time"), ticket.DispatchedAtUtc.ToLocalTime().ToString("HH:mm", CultureInfo.InvariantCulture)));
        lines.Add(new TicketSeparator());
        foreach (var item in ticket.Items)
        {
            lines.Add(new TicketText($"{item.Quantity}x {item.ProductName}", Large: true));
            if (!string.IsNullOrWhiteSpace(item.ModifiersSummary)) lines.Add(new TicketText("+ " + item.ModifiersSummary));
            if (!string.IsNullOrWhiteSpace(item.KitchenComment))
                lines.Add(new TicketText(Texts.Get(c, "kitchen_ticket.comment", ("comment", item.KitchenComment)), Bold: true));
            // ponytail: service retrouvé par ProductId (KitchenTicketItem ne le stocke pas) ; deux lignes du même article à des services différents affichent le premier. Stocker Course sur KitchenTicketItem si besoin.
            var course = order.Items.FirstOrDefault(i => i.ProductId == item.ProductId)?.Course ?? CourseType.Direct;
            lines.Add(new TicketText(Texts.Get(c, CourseKey(course))));
            lines.Add(new TicketSeparator());
        }
        return new TicketDocument(lang, lang == "ar", lines);
    }

    private static string CourseKey(CourseType course) => course switch
    {
        CourseType.Suite => "kitchen_ticket.course_suite",
        CourseType.Dessert => "kitchen_ticket.course_dessert",
        CourseType.OnDemand => "kitchen_ticket.course_on_demand",
        _ => "kitchen_ticket.course_direct"
    };

    private static void AppendPickup(List<TicketLine> lines, CultureInfo c, Order order, string pickupNumber, string? buzzer, DateTimeOffset nowUtc)
    {
        lines.Add(new TicketText(Texts.Get(c, "receipt.pickup_title"), TicketAlign.Center, Bold: true));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.number"), pickupNumber));
        if (!string.IsNullOrWhiteSpace(buzzer)) lines.Add(new TicketColumns(Texts.Get(c, "receipt.buzzer"), buzzer));
        lines.Add(new TicketSeparator());
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.date"), nowUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)));
        lines.Add(new TicketColumns(Texts.Get(c, "receipt.mode"), Mode(c, order)));
        lines.Add(new TicketText(Texts.Get(c, "receipt.items_count", ("count", order.Items.Count))));
        lines.Add(new TicketSeparator());
        foreach (var item in order.Items)
        {
            lines.Add(new TicketText($"{item.Quantity}x {item.ProductName}"));
            if (item.SelectedModifiers.Count > 0) lines.Add(new TicketText("+ " + string.Join(", ", item.SelectedModifiers)));
        }
        lines.Add(new TicketSeparator());
        lines.Add(new TicketText(Texts.Get(c, "receipt.keep_until_pickup"), TicketAlign.Center));
    }

    public static TicketDocument TestPage(PrinterConfiguration printer, string language, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(printer);
        var (lang, c) = Resolve(language);
        List<TicketLine> lines =
        [
            new TicketText(Texts.Get(c, "receipt.test_title"), TicketAlign.Center, Bold: true, Large: true),
            new TicketSeparator(),
            new TicketColumns(Texts.Get(c, "receipt.test_printer"), printer.Name),
            new TicketColumns(Texts.Get(c, "receipt.test_address"), $"{printer.IpAddress}:{printer.Port}"),
            new TicketColumns(Texts.Get(c, "receipt.test_paper"), $"{printer.PaperWidthMm} mm"),
            new TicketColumns(Texts.Get(c, "receipt.date"), nowUtc.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)),
            new TicketSeparator(),
            // Échantillon fixe : vérifie accents et liaison arabe quelle que soit la langue.
            new TicketText("àâçéèêëîïôùû ÀÉÈ — مرحبا بكم — 0123456789", TicketAlign.Center)
        ];
        return new TicketDocument(lang, lang == "ar", lines);
    }

    private static (string Lang, CultureInfo Culture) Resolve(string language)
    {
        var lang = Array.IndexOf(Texts.SupportedLanguages, language) >= 0 ? language : "en";
        return (lang, CultureInfo.GetCultureInfo(lang));
    }

    private static string Mode(CultureInfo c, Order order) =>
        Texts.Get(c, order.Destination == OrderDestination.Takeaway ? "receipt.mode_takeaway" : "receipt.mode_eat_in");

    // Devise en dur jusqu'au sous-projet B.
    private static string Amount(long cents) => (cents / 100m).ToString("0.00", CultureInfo.InvariantCulture);

    public static TicketDocument WithDuplicateNotice(TicketDocument doc, int duplicateNumber)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var lines = doc.Lines.Where(l => l is not TicketText t || !t.Text.Contains("DUPLICATA", StringComparison.OrdinalIgnoreCase)).ToList();
        lines.Insert(0, new TicketText($"DUPLICATA n°{duplicateNumber}", TicketAlign.Center, Bold: true, Large: true));
        return new TicketDocument(doc.Language, doc.RightToLeft, lines);
    }
}
