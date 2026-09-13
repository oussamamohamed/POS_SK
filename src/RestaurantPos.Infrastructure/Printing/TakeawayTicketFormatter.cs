using System;
using System.Globalization;
using System.Text;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;

namespace RestaurantPos.Infrastructure.Printing;

public static class TakeawayTicketFormatter
{
    public static string FormatCompactPickupCoupon(Order order, string pickupNumber, string? buzzer = null)
    {
        ArgumentNullException.ThrowIfNull(order);

        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        sb.AppendLine("========================================");
        sb.AppendLine("           BON DE RETRAIT COMMANDE      ");
        sb.AppendLine("========================================");
        sb.AppendLine();
        sb.AppendLine(culture, $"        NUMERO :  {pickupNumber}        ");
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(buzzer))
        {
            sb.AppendLine(culture, $"        BIPEUR :  {buzzer}              ");
        }
        sb.AppendLine("----------------------------------------");
        sb.AppendLine(culture, $"Date  : {DateTimeOffset.UtcNow:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine(culture, $"Mode  : {(order.Destination == OrderDestination.Takeaway ? "A EMPORTER" : "SUR PLACE")}");
        sb.AppendLine(culture, $"Articles : {order.Items.Count} ligne(s)");
        sb.AppendLine("----------------------------------------");
        foreach (var item in order.Items)
        {
            sb.AppendLine(culture, $"{item.Quantity}x {item.ProductName}");
            if (item.SelectedModifiers.Count > 0)
            {
                sb.AppendLine(culture, $"   + {string.Join(", ", item.SelectedModifiers)}");
            }
        }
        sb.AppendLine("========================================");
        sb.AppendLine("  CONSERVEZ CE BON JUSQU'AU RETRAIT     ");
        sb.AppendLine("========================================");

        return sb.ToString();
    }

    public static string FormatCombinedAgecReceipt(FiscalReceipt receipt, Order order, string pickupNumber, string? buzzer = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(order);

        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

        // 1. Fiscal Legal Receipt
        sb.AppendLine("========================================");
        sb.AppendLine("          RESTAURANT L'ANTIGRAVITE      ");
        sb.AppendLine("           12 Rue de la Gastronomie     ");
        sb.AppendLine("               75001 Paris              ");
        sb.AppendLine("         SIRET: 888 777 666 00012       ");
        sb.AppendLine("           TVA: FR 12 888777666         ");
        sb.AppendLine("========================================");
        sb.AppendLine(culture, $"Ticket: {receipt.ReceiptNumber}");
        sb.AppendLine(culture, $"Date  : {receipt.CreatedAtUtc:dd/MM/yyyy HH:mm:ss}");
        sb.AppendLine(culture, $"Caisse: {receipt.TerminalId}");
        sb.AppendLine(culture, $"Mode  : {(order.Destination == OrderDestination.Takeaway ? "A EMPORTER" : "SUR PLACE")}");
        sb.AppendLine("----------------------------------------");
        foreach (var item in order.Items)
        {
            decimal lineTotal = item.CalculateTotalTtc().ToDecimal();
            sb.AppendLine(culture, $"{item.Quantity}x {item.ProductName,-24} {lineTotal,8:F2} E");
        }
        sb.AppendLine("----------------------------------------");
        sb.AppendLine(culture, $"TOTAL TTC : {receipt.TotalTtcAmount.AmountInCents / 100.0,28:F2} EUR");
        sb.AppendLine(culture, $"TOTAL HT  : {receipt.TotalHtAmount.AmountInCents / 100.0,28:F2} EUR");
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("VENTILATION TVA (NF525):");
        sb.AppendLine(receipt.TaxBreakdownJson);
        sb.AppendLine("----------------------------------------");
        sb.AppendLine("SIGNATURE FISCALE NF525:");
        sb.AppendLine(receipt.SignatureHash);
        sb.AppendLine("========================================");

        // Perforated Separation Line
        sb.AppendLine();
        sb.AppendLine("- - - - - - - COUPER ICI - - - - - - - -");
        sb.AppendLine();

        // 2. Detachable Pickup Coupon
        sb.Append(FormatCompactPickupCoupon(order, pickupNumber, buzzer));

        return sb.ToString();
    }
}
