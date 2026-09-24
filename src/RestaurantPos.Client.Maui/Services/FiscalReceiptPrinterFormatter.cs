using System;
using System.Globalization;
using System.Text;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.Services;

public static class FiscalReceiptPrinterFormatter
{
    public static string FormatLegalReceiptText(FiscalReceipt receipt)
    {
        var sb = new StringBuilder();
        var culture = CultureInfo.InvariantCulture;

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
        sb.AppendLine("      MERCI DE VOTRE VISITE !           ");
        sb.AppendLine("========================================");

        return sb.ToString();
    }
}
