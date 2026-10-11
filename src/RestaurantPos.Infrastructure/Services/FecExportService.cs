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
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public sealed class FecExportService : IFecExportService
{
    // Compte 4686 (créditeurs divers) : pas de plan comptable de pourboires existant dans le dépôt, choix le plus simple.
    private const string TipsAccount = "468600";

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
            var isVoid = receipt.VoidedReceiptId != null || receipt.TotalTtcAmount.AmountInCents < 0;

            // 1. Determine VAT & HT breakdowns
            var vatItems = ParseVatBreakdown(receipt);

            // 2. Generate Sales Lines (Compte 706xxx)
            foreach (var item in vatItems)
            {
                long absHt = Math.Abs(item.HtCents);
                long absVat = Math.Abs(item.VatCents);

                if (absHt <= 0 && absVat <= 0)
                {
                    continue;
                }

                var (salesAccount, salesLibelle) = GetSalesAccountForRate(item.TaxRatePercent);
                var (vatAccount, vatLibelle) = GetVatAccountForRate(item.TaxRatePercent);

                // Sales Line (HT)
                long htDebit = isVoid ? absHt : 0;
                long htCredit = isVoid ? 0 : absHt;

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
                if (absVat > 0)
                {
                    long vatDebit = isVoid ? absVat : 0;
                    long vatCredit = isVoid ? 0 : absVat;

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
                    long amountCents = Math.Abs(tender.Amount.AmountInCents);
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

                // Pourboire : encaissé avec le règlement mais hors reçu fiscal (TTC) ; dette envers le personnel, pas un produit.
                // Sans cette contrepartie l'écriture serait déséquilibrée du montant du pourboire.
                long tipCents = receipt.Tenders.Sum(t => Math.Abs(t.Amount.AmountInCents)) - Math.Abs(receipt.TotalTtcAmount.AmountInCents);
                if (tipCents > 0)
                {
                    long tipDebit = isVoid ? tipCents : 0;
                    long tipCredit = isVoid ? 0 : tipCents;

                    sb.AppendLine(FormatFecLine(
                        "VT",
                        "Journal des Ventes",
                        ecritureNumStr,
                        ecritureDateStr,
                        TipsAccount,
                        "Pourboires a reverser",
                        pieceRef,
                        ecritureDateStr,
                        $"Pourboire {pieceRef}",
                        tipDebit,
                        tipCredit,
                        validDateStr
                    ));
                    cumulativeDebitCents += tipDebit;
                    cumulativeCreditCents += tipCredit;
                    totalRecords++;
                }
            }
            else
            {
                // Fallback settlement to cash (530000) if no specific tenders recorded
                long amountCents = Math.Abs(receipt.TotalTtcAmount.AmountInCents);
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

    /// <summary>Montants en valeur absolue : le sens (vente ou annulation) est porté par <c>isVoid</c> à l'écriture.</summary>
    private sealed record VatBreakdownItem(decimal TaxRatePercent, long HtCents, long VatCents);

    /// <summary>
    /// Ventilation par taux du reçu. Reçu récent : <c>HtBreakdownJson</c> (colonne non signée, posée à la création) donne le HT exact
    /// par taux, repris tel quel. Reçu historique (<c>null</c>) : le JSON signé ne porte que la TVA par taux et le HT en est déduit.
    /// Pour un taux t &gt; 0, le HT retenu est la valeur cohérente avec la TVA (<see cref="TaxBreakdownItem.Calculate"/>) la plus proche
    /// de TVA / t ; un taux 0 % (TVA nulle, HT invisible dans le JSON) ou, à défaut, le plus petit taux (le plus sensible à l'arrondi)
    /// absorbe le reste : la somme des HT vaut toujours le HT du reçu. Limite de ce repli seul : à plusieurs taux bas, le HT exact par
    /// taux n'est pas récupérable (écart de quelques centimes entre deux lignes, total exact).
    /// </summary>
    private static List<VatBreakdownItem> ParseVatBreakdown(FiscalReceipt receipt)
    {
        long totalHtCents = Math.Abs(receipt.TotalHtAmount.AmountInCents);
        long totalVatCents = Math.Abs(receipt.TotalTtcAmount.AmountInCents) - totalHtCents;

        var exact = ParseExactBreakdown(receipt);
        if (exact is not null)
        {
            return exact;
        }

        if (!string.IsNullOrWhiteSpace(receipt.TaxBreakdownJson) && receipt.TaxBreakdownJson != "{}")
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, long>>(receipt.TaxBreakdownJson);
                var parsed = new List<(decimal Rate, long Vat)>();
                foreach (var kvp in dict ?? [])
                {
                    if (decimal.TryParse(kvp.Key, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
                    {
                        parsed.Add((rate, Math.Abs(kvp.Value)));
                    }
                }

                if (parsed.Count > 0)
                {
                    int absorber = parsed.FindIndex(p => p.Rate == 0m);
                    if (absorber < 0)
                    {
                        absorber = parsed.IndexOf(parsed.OrderBy(p => p.Rate).First());
                    }

                    var ranges = parsed.Select(p => p.Rate > 0m ? FeasibleHt(p.Rate, p.Vat) : (Lo: 0L, Hi: 0L, Est: 0L)).ToArray();
                    var ht = ranges.Select(r => r.Est).ToArray();
                    ht[absorber] = 0;
                    long residual = totalHtCents - ht.Sum();

                    // Reste négatif (estimations trop hautes) : on le reprend sur les taux les plus ambigus, dans leur plage cohérente.
                    foreach (int i in Enumerable.Range(0, ht.Length).Where(i => i != absorber).OrderBy(i => parsed[i].Rate))
                    {
                        if (residual >= 0)
                        {
                            break;
                        }

                        long shift = Math.Min(-residual, ht[i] - ranges[i].Lo);
                        ht[i] -= shift;
                        residual += shift;
                    }

                    ht[absorber] = residual;

                    return parsed.Select((p, i) => new VatBreakdownItem(p.Rate, ht[i], p.Vat)).ToList();
                }
            }
            catch (JsonException)
            {
                // JSON illisible : repli ci-dessous.
            }
        }

        // Default single rate (10%) fallback
        return [new VatBreakdownItem(10.0m, totalHtCents, totalVatCents)];
    }

    /// <summary>HT par taux persisté, avec la TVA du JSON signé au même taux. <c>null</c> si absent ou illisible (repli par déduction).</summary>
    private static List<VatBreakdownItem>? ParseExactBreakdown(FiscalReceipt receipt)
    {
        if (string.IsNullOrWhiteSpace(receipt.HtBreakdownJson))
        {
            return null;
        }

        try
        {
            var ht = ParseByRate(receipt.HtBreakdownJson);
            var vat = string.IsNullOrWhiteSpace(receipt.TaxBreakdownJson) ? [] : ParseByRate(receipt.TaxBreakdownJson);
            if (ht is null || vat is null || ht.Count == 0 || !IsCoherent(receipt, ht, vat))
            {
                return null;
            }

            var items = ht.Select(h => new VatBreakdownItem(h.Key, Math.Abs(h.Value), Math.Abs(vat.GetValueOrDefault(h.Key)))).ToList();
            items.AddRange(vat.Where(v => !ht.ContainsKey(v.Key)).Select(v => new VatBreakdownItem(v.Key, 0, Math.Abs(v.Value))));
            return items;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// La colonne n'est pas signée : on ne la croit que si elle colle au reçu signé. Mêmes taux que la TVA signée, Σ|HT| = |TTC| - Σ|TVA|,
    /// et, pour chaque taux &gt; 0, HT dans la plage cohérente avec la TVA. Sinon repli par déduction.
    /// </summary>
    private static bool IsCoherent(FiscalReceipt receipt, Dictionary<decimal, long> ht, Dictionary<decimal, long> vat)
    {
        if (!ht.Keys.ToHashSet().SetEquals(vat.Keys)
            || ht.Values.Sum(Math.Abs) != Math.Abs(receipt.TotalTtcAmount.AmountInCents) - vat.Values.Sum(Math.Abs))
        {
            return false;
        }

        return ht.All(h =>
        {
            if (h.Key <= 0m)
            {
                return true;
            }

            var (lo, hi, _) = FeasibleHt(h.Key, Math.Abs(vat[h.Key]));
            long abs = Math.Abs(h.Value);
            return abs >= lo && abs <= hi;
        });
    }

    private static Dictionary<decimal, long>? ParseByRate(string json)
    {
        var dict = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
        var result = new Dictionary<decimal, long>();
        foreach (var kvp in dict ?? [])
        {
            if (!decimal.TryParse(kvp.Key, NumberStyles.Any, CultureInfo.InvariantCulture, out var rate))
            {
                return null;
            }

            result[rate] = result.GetValueOrDefault(rate) + kvp.Value;
        }

        return result;
    }

    /// <summary>
    /// HT possibles pour une TVA donnée au taux donné (TVA = TTC - HT, HT = round(TTC / (1 + t))) : plage [Lo, Hi] et valeur la plus
    /// proche de TVA / t (Est). Plage vide (reçu hors règle actuelle) : estimation seule.
    /// </summary>
    private static (long Lo, long Hi, long Est) FeasibleHt(decimal ratePercent, long vatCents)
    {
        long estimate = (long)Math.Round(vatCents / (ratePercent / 100m), MidpointRounding.AwayFromZero);
        long window = (long)Math.Ceiling(100m / ratePercent) + 2;
        var feasible = new List<long>();
        for (long b = Math.Max(0, estimate - window); b <= estimate + window; b++)
        {
            var c = TaxBreakdownItem.Calculate(ratePercent, b + vatCents);
            if (c.TaxableBaseCents == b && c.TaxAmountCents == vatCents)
            {
                feasible.Add(b);
            }
        }

        return feasible.Count == 0
            ? (estimate, estimate, estimate)
            : (feasible[0], feasible[^1], feasible.OrderBy(b => Math.Abs(b - estimate)).First());
    }
}
