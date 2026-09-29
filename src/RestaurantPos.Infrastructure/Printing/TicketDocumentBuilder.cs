using System;
using System.Collections.Generic;
using System.Globalization;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Localization;

namespace RestaurantPos.Infrastructure.Printing;

/// <summary>Construit les tickets dans la langue des tickets du restaurant. Aucun rendu ni envoi imprimante (sous-projet C).</summary>
public static class TicketDocumentBuilder
{
    // ponytail: en-tête restaurant en dur, repris de l'ancien formatter ; à déplacer dans RestaurantSettings quand un écran de saisie existera.
    private static readonly string[] Header =
        ["RESTAURANT L'ANTIGRAVITE", "12 Rue de la Gastronomie", "75001 Paris", "SIRET: 888 777 666 00012", "TVA: FR 12 888777666"];

    public static TicketDocument PickupCoupon(Order order, string pickupNumber, string? buzzer, string language, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(order);
        var (lang, culture) = Resolve(language);
        var lines = new List<TicketLine>();
        AppendPickup(lines, culture, order, pickupNumber, buzzer, nowUtc);
        return new TicketDocument(lang, lang == "ar", lines);
    }

    public static TicketDocument FiscalReceipt(FiscalReceipt receipt, Order order, string pickupNumber, string? buzzer, string language)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(order);
        var (lang, c) = Resolve(language);
        var lines = new List<TicketLine> { new TicketSeparator() };
        foreach (var h in Header) lines.Add(new TicketText(h, TicketAlign.Center));
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
        lines.Add(new TicketSeparator(Cut: true));
        AppendPickup(lines, c, order, pickupNumber, buzzer, receipt.CreatedAtUtc);
        return new TicketDocument(lang, lang == "ar", lines);
    }

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
}
