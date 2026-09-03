using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public sealed class FecExportService : IFecExportService
{
    private readonly AppDbContext _dbContext;

    public FecExportService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<FecExportResult> GenerateFecAsync(
        FecExportRequest request,
        CancellationToken cancellationToken = default)
    {
        var startUtc = request.StartDateUtc;
        var endUtc = request.EndDateUtc;

        // Ensure chronological interval
        if (startUtc > endUtc)
        {
            (startUtc, endUtc) = (endUtc, startUtc);
        }

        var allReceipts = await _dbContext.FiscalReceipts
            .Include(r => r.Tenders)
            .OrderBy(r => r.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var receipts = allReceipts
            .Where(r => r.CreatedAtUtc >= startUtc && r.CreatedAtUtc <= endUtc)
            .ToList();

        var siren = string.IsNullOrWhiteSpace(request.SirenNumber) ? "000000000" : request.SirenNumber.Trim();
        var dateClosingStr = endUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var fileName = $"{siren}FEC{dateClosingStr}.txt";

        var sb = new StringBuilder();

        // Standard official French FEC Header (18 fields, tab-delimited)
        sb.AppendLine(string.Join("\t",
            "JournalCode",
            "JournalLib",
            "EcritureNum",
            "EcritureDate",
            "CompteNum",
            "CompteLib",
            "CompAuxNum",
            "CompAuxLib",
            "PieceRef",
            "PieceDate",
            "EcritureLib",
            "Debit",
            "Credit",
            "EcritureLet",
            "DateLet",
            "ValidDate",
            "Montantdevise",
            "Idevise"
        ));

        long entryNumber = 1;
        long cumulativeDebitCents = 0;
        long cumulativeCreditCents = 0;
        int totalRecords = 0;

        foreach (var receipt in receipts)
        {
            var ecritureNumStr = entryNumber.ToString("D7", CultureInfo.InvariantCulture);
            var ecritureDateStr = receipt.CreatedAtUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var validDateStr = receipt.CreatedAtUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var pieceRef = receipt.ReceiptNumber;
            var isVoid = receipt.IsVoid;

            // 1. Determine VAT & HT breakdowns
            var vatItems = ParseVatBreakdown(receipt);

            // 2. Generate Sales Lines (Compte 706xxx)
            foreach (var item in vatItems)
            {
                if (item.HtCents <= 0 && item.VatCents <= 0)
                {
                    continue;
                }

                var (salesAccount, salesLibelle) = GetSalesAccountForRate(item.TaxRatePercent);
                var (vatAccount, vatLibelle) = GetVatAccountForRate(item.TaxRatePercent);

                // Sales Line (HT)
                long htDebit = isVoid ? item.HtCents : 0;
                long htCredit = isVoid ? 0 : item.HtCents;

                sb.AppendLine(FormatFecLine(
                    "VT",
                    "Journal des Ventes",
                    ecritureNumStr,
                    ecritureDateStr,
                    salesAccount,
                    salesLibelle,
                    pieceRef,
                    ecritureDateStr,
                    $"Vente {pieceRef} (HT {item.TaxRatePercent:0.#}%)",
                    htDebit,
                    htCredit,
                    validDateStr
                ));
                cumulativeDebitCents += htDebit;
                cumulativeCreditCents += htCredit;
                totalRecords++;

                // VAT Line
                if (item.VatCents > 0)
                {
                    long vatDebit = isVoid ? item.VatCents : 0;
                    long vatCredit = isVoid ? 0 : item.VatCents;

                    sb.AppendLine(FormatFecLine(
                        "VT",
                        "Journal des Ventes",
                        ecritureNumStr,
                        ecritureDateStr,
                        vatAccount,
                        vatLibelle,
                        pieceRef,
                        ecritureDateStr,
                        $"TVA Collectee {item.TaxRatePercent:0.#}% {pieceRef}",
                        vatDebit,
                        vatCredit,
                        validDateStr
                    ));
                    cumulativeDebitCents += vatDebit;
                    cumulativeCreditCents += vatCredit;
                    totalRecords++;
                }
            }

            // 3. Generate Settlement Lines (Payment Tenders)
            if (receipt.Tenders.Count > 0)
            {
                foreach (var tender in receipt.Tenders)
                {
                    var (accountNum, accountLib) = GetPaymentAccount(tender.Method);
                    long amountCents = tender.Amount.AmountInCents;
                    long debitCents = isVoid ? 0 : amountCents;
                    long creditCents = isVoid ? amountCents : 0;

                    sb.AppendLine(FormatFecLine(
                        "VT",
                        "Journal des Ventes",
                        ecritureNumStr,
                        ecritureDateStr,
                        accountNum,
                        accountLib,
                        pieceRef,
                        ecritureDateStr,
                        $"Reglement {tender.Method} {pieceRef}",
                        debitCents,
                        creditCents,
                        validDateStr
                    ));
                    cumulativeDebitCents += debitCents;
                    cumulativeCreditCents += creditCents;
                    totalRecords++;
                }
            }
            else
            {
                // Fallback settlement to cash (530000) if no specific tenders recorded
                long amountCents = receipt.TotalTtcAmount.AmountInCents;
                long debitCents = isVoid ? 0 : amountCents;
                long creditCents = isVoid ? amountCents : 0;

                sb.AppendLine(FormatFecLine(
                    "VT",
                    "Journal des Ventes",
                    ecritureNumStr,
                    ecritureDateStr,
                    "530000",
                    "Caisse Especes",
                    pieceRef,
                    ecritureDateStr,
                    $"Reglement Caisse {pieceRef}",
                    debitCents,
                    creditCents,
                    validDateStr
                ));
                cumulativeDebitCents += debitCents;
                cumulativeCreditCents += creditCents;
                totalRecords++;
            }

            entryNumber++;
        }

        var fileBytes = Encoding.UTF8.GetBytes(sb.ToString());

        return new FecExportResult(
            fileName,
            fileBytes,
            totalRecords,
            cumulativeDebitCents / 100.0m,
            cumulativeCreditCents / 100.0m
        );
    }

    private static string FormatFecLine(
        string journalCode,
        string journalLib,
        string ecritureNum,
        string ecritureDate,
        string compteNum,
        string compteLib,
        string pieceRef,
        string pieceDate,
        string ecritureLib,
        long debitCents,
        long creditCents,
        string validDate)
    {
        return string.Join("\t",
            journalCode,
            journalLib,
            ecritureNum,
            ecritureDate,
            compteNum,
            compteLib,
            string.Empty, // CompAuxNum
            string.Empty, // CompAuxLib
            pieceRef,
            pieceDate,
            ecritureLib,
            (debitCents / 100.0m).ToString("0.00", CultureInfo.InvariantCulture),
            (creditCents / 100.0m).ToString("0.00", CultureInfo.InvariantCulture),
            string.Empty, // EcritureLet
            string.Empty, // DateLet
            validDate,
            string.Empty, // Montantdevise
            "EUR"         // Idevise
        );
    }

    private static (string AccountNum, string AccountLib) GetSalesAccountForRate(decimal taxRate)
    {
        return taxRate switch
        {
            >= 19.5m and <= 20.5m => ("706200", "Ventes Boissons Alcoolisees 20%"),
            >= 9.5m and <= 10.5m => ("706100", "Ventes Restauration Sur Place 10%"),
            >= 5.0m and <= 6.0m => ("706550", "Ventes Restauration A Emporter 5.5%"),
            _ => ("706000", "Ventes Prestations Restauration")
        };
    }

    private static (string AccountNum, string AccountLib) GetVatAccountForRate(decimal taxRate)
    {
        return taxRate switch
        {
            >= 19.5m and <= 20.5m => ("445720", "TVA Collectee 20%"),
            >= 9.5m and <= 10.5m => ("445710", "TVA Collectee 10%"),
            >= 5.0m and <= 6.0m => ("445755", "TVA Collectee 5.5%"),
            _ => ("445700", "TVA Collectee")
        };
    }

    private static (string AccountNum, string AccountLib) GetPaymentAccount(PaymentMethod method)
    {
        return method switch
        {
            PaymentMethod.CreditCard => ("512000", "Banque Cartes Bancaires"),
            PaymentMethod.Cash => ("530000", "Caisse Especes"),
            PaymentMethod.MealVoucher => ("580000", "Titres Restaurant a Encaisser"),
            PaymentMethod.RoomCharge => ("411000", "Clients - Facturation Chambre"),
            PaymentMethod.GiftCard => ("419100", "Clients - Cartes Cadeaux"),
            _ => ("580000", "Virements Internes POS")
        };
    }

    private sealed record VatBreakdownItem(decimal TaxRatePercent, long HtCents, long VatCents);

    private static List<VatBreakdownItem> ParseVatBreakdown(FiscalReceipt receipt)
    {
        var items = new List<VatBreakdownItem>();
        long totalTtcCents = receipt.TotalTtcAmount.AmountInCents;
        long totalHtCents = receipt.TotalHtAmount.AmountInCents;
        long totalVatCents = totalTtcCents - totalHtCents;

        if (!string.IsNullOrWhiteSpace(receipt.TaxBreakdownJson) && receipt.TaxBreakdownJson != "{}")
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, long>>(receipt.TaxBreakdownJson);
                if (dict != null && dict.Count > 0)
                {
                    long allocatedHtCents = 0;
                    var parsedList = new List<(decimal Rate, long VatCents)>();

                    foreach (var kvp in dict)
                    {
                        if (decimal.TryParse(kvp.Key, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
                        {
                            parsedList.Add((rate, kvp.Value));
                        }
                    }

                    for (int i = 0; i < parsedList.Count; i++)
                    {
                        var (rate, vat) = parsedList[i];
                        long ht;
                        if (i == parsedList.Count - 1)
                        {
                            // Last bucket takes remainder to guarantee sum(HtCents) == totalHtCents
                            ht = Math.Max(0, totalHtCents - allocatedHtCents);
                        }
                        else
                        {
                            decimal rateFactor = rate / 100.0m;
                            ht = rateFactor > 0 ? (long)Math.Round(vat / rateFactor, MidpointRounding.AwayFromZero) : 0;
                            allocatedHtCents += ht;
                        }

                        items.Add(new VatBreakdownItem(rate, ht, vat));
                    }

                    return items;
                }
            }
            catch
            {
                // Fallback if JSON parsing fails
            }
        }

        // Default single rate (10%) fallback
        items.Add(new VatBreakdownItem(10.0m, totalHtCents, totalVatCents));
        return items;
    }
}
