using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class NF525FiscalAuditService : INF525FiscalAuditService
{
    public const string GenesisHash = "GENESIS_0000000000000000000000000000000000000000000000000000000000000000";
    private static readonly string[] MainTerminalIds = ["", "POS_MAIN_TERM", "POS01"];
    private readonly AppDbContext _dbContext;

    public NF525FiscalAuditService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string ComputeReceiptHashSignature(
        string previousHash,
        string terminalId,
        long sequenceNumber,
        long amountCents,
        DateTimeOffset timestampUtc,
        string taxBreakdownJson)
    {
        string rawData = $"{previousHash}|{terminalId}|{sequenceNumber}|{amountCents}|{timestampUtc:O}|{taxBreakdownJson}";
        byte[] bytes = Encoding.UTF8.GetBytes(rawData);
        byte[] hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }

    public async Task<FiscalSummaryDto> GenerateXReportAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        var isMainTerminal = string.IsNullOrWhiteSpace(terminalId) || terminalId == "POS_MAIN_TERM";

        var closures = await _dbContext.DailyFiscalClosures
            .Where(c => isMainTerminal ? (c.TerminalId == terminalId || c.TerminalId == "POS_MAIN_TERM" || c.TerminalId == "POS01") : c.TerminalId == terminalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lastClosure = closures
            .Where(c => c.TotalSalesTtc.AmountInCents > 0)
            .OrderByDescending(c => c.ClosureSequence)
            .FirstOrDefault()
            ?? closures.OrderByDescending(c => c.ClosureSequence).FirstOrDefault();

        var periodStart = lastClosure?.PeriodEndUtc ?? DateTimeOffset.MinValue;
        var periodEnd = DateTimeOffset.UtcNow;

        var allReceipts = await _dbContext.FiscalReceipts
            .Include(r => r.Tenders)
            .Where(r => isMainTerminal || r.TerminalId == terminalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var receipts = allReceipts
            .Where(r => r.CreatedAtUtc >= periodStart && r.CreatedAtUtc <= periodEnd)
            .ToList();

        // If no receipts found starting from lastClosure, but there are unclosed receipts:
        if (receipts.Count == 0 && allReceipts.Count > 0 && (lastClosure == null || allReceipts.Any(r => r.CreatedAtUtc > lastClosure.PeriodEndUtc)))
        {
            periodStart = lastClosure?.PeriodEndUtc ?? DateTimeOffset.MinValue;
            receipts = allReceipts.Where(r => r.CreatedAtUtc >= periodStart && r.CreatedAtUtc <= periodEnd).ToList();
        }

        long totalTtc = receipts.Sum(r => r.TotalTtcAmount.AmountInCents);
        long totalHt = receipts.Sum(r => r.TotalHtAmount.AmountInCents);

        var vatMap = new Dictionary<decimal, long>();
        var tenderMap = new Dictionary<PaymentMethod, long>();

        foreach (var receipt in receipts)
        {
            // B1 FIX: Deserialize and aggregate VAT breakdown from each receipt's stored JSON
            if (!string.IsNullOrWhiteSpace(receipt.TaxBreakdownJson) && receipt.TaxBreakdownJson != "{}")
            {
                var receiptVat = JsonSerializer.Deserialize<Dictionary<string, long>>(receipt.TaxBreakdownJson);
                if (receiptVat is not null)
                {
                    foreach (var kvp in receiptVat)
                    {
                        if (decimal.TryParse(kvp.Key, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rate))
                        {
                            vatMap[rate] = vatMap.TryGetValue(rate, out long existing) ? existing + kvp.Value : kvp.Value;
                        }
                    }
                }
            }

            foreach (var tender in receipt.Tenders)
            {
                tenderMap[tender.Method] = tenderMap.TryGetValue(tender.Method, out long current)
                    ? current + tender.Amount.AmountInCents
                    : tender.Amount.AmountInCents;
            }
        }

        long perpetualTotal = (lastClosure?.PerpetualGrandTotalCents ?? 0) + totalTtc;

        return new FiscalSummaryDto(
            terminalId,
            periodStart,
            periodEnd,
            totalTtc,
            totalHt,
            receipts.Count,
            vatMap,
            tenderMap,
            perpetualTotal
        );
    }

    public async Task<DailyFiscalClosureDto> ExecuteDailyZClosureAsync(
        string terminalId,
        Guid managerId,
        string managerName,
        CancellationToken cancellationToken = default)
    {
        // SERIALIZABLE: Z-closure sequence must be unique per terminal.
        return await _dbContext.ExecuteInTransactionAsync(
            async ct =>
            {
                var pendingHeldCount = await _dbContext.HeldOrders
                    .CountAsync(h => h.TerminalId == terminalId && !h.IsRecalled && !h.IsVoided, ct)
                    .ConfigureAwait(false);

                if (pendingHeldCount > 0)
                {
                    throw new InvalidOperationException($"Clôture Z impossible : {pendingHeldCount} commande(s) en attente subsistent sur la caisse {terminalId}. Veuillez les rappeler ou les annuler avec un code PIN superviseur avant la clôture.");
                }

                var xSummary = await GenerateXReportAsync(terminalId, ct).ConfigureAwait(false);

                var lastClosure = await _dbContext.DailyFiscalClosures
                    .Where(c => c.TerminalId == terminalId)
                    .OrderByDescending(c => c.ClosureSequence)
                    .FirstOrDefaultAsync(ct)
                    .ConfigureAwait(false);

                long nextSequence = (lastClosure?.ClosureSequence ?? 0) + 1;
                string prevHash = lastClosure?.SignatureHash ?? GenesisHash;

                string sig = ComputeReceiptHashSignature(
                    prevHash,
                    terminalId,
                    nextSequence,
                    xSummary.TotalSalesTtcCents,
                    xSummary.PeriodEndUtc,
                    JsonSerializer.Serialize(xSummary.VatBreakdownCents)
                );

                var closure = new DailyFiscalClosure
                {
                    Id = UuidV7.NewGuid(),
                    TerminalId = terminalId,
                    ClosureSequence = nextSequence,
                    PeriodStartUtc = xSummary.PeriodStartUtc,
                    PeriodEndUtc = xSummary.PeriodEndUtc,
                    TotalSalesTtc = Money.FromCents(xSummary.TotalSalesTtcCents),
                    TotalSalesHt = Money.FromCents(xSummary.TotalSalesHtCents),
                    TaxesSummaryJson = JsonSerializer.Serialize(xSummary.VatBreakdownCents),
                    TenderTotalsJson = JsonSerializer.Serialize(xSummary.PaymentTotalsCents),
                    PerpetualGrandTotalCents = xSummary.PerpetualGrandTotalCents,
                    PreviousSignatureHash = prevHash,
                    SignatureHash = sig,
                    SealedByUserId = managerId,
                    SealedByUserName = managerName
                };

                _dbContext.DailyFiscalClosures.Add(closure);
                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

                return new DailyFiscalClosureDto(
                    closure.Id,
                    closure.TerminalId,
                    closure.ClosureSequence,
                    closure.TotalSalesTtc.AmountInCents,
                    closure.TotalSalesHt.AmountInCents,
                    xSummary.ReceiptCount,
                    xSummary.VatBreakdownCents,
                    xSummary.PaymentTotalsCents,
                    closure.PerpetualGrandTotalCents,
                    closure.SignatureHash,
                    closure.PeriodEndUtc,
                    closure.PeriodStartUtc,
                    closure.SealedByUserName
                );
            },
            System.Data.IsolationLevel.Serializable,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<DailyFiscalClosureDto?> GetLatestZClosureAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        var isMainTerminal = string.IsNullOrWhiteSpace(terminalId) || terminalId == "POS_MAIN_TERM";
        var closures = await _dbContext.DailyFiscalClosures
            .Where(c => isMainTerminal ? (c.TerminalId == terminalId || c.TerminalId == "POS_MAIN_TERM" || c.TerminalId == "POS01") : c.TerminalId == terminalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var closure = closures.OrderByDescending(c => c.ClosureSequence).FirstOrDefault();
        if (closure is null) return null;

        var vatMap = new Dictionary<decimal, long>();
        if (!string.IsNullOrWhiteSpace(closure.TaxesSummaryJson))
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, long>>(closure.TaxesSummaryJson);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        if (decimal.TryParse(kvp.Key, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal rate))
                        {
                            vatMap[rate] = kvp.Value;
                        }
                    }
                }
            }
            catch { }
        }

        var tenderMap = new Dictionary<PaymentMethod, long>();
        if (!string.IsNullOrWhiteSpace(closure.TenderTotalsJson))
        {
            try
            {
                var dict = JsonSerializer.Deserialize<Dictionary<string, long>>(closure.TenderTotalsJson);
                if (dict != null)
                {
                    foreach (var kvp in dict)
                    {
                        if (Enum.TryParse<PaymentMethod>(kvp.Key, out var method))
                        {
                            tenderMap[method] = kvp.Value;
                        }
                    }
                }
            }
            catch { }
        }

        var receipts = await _dbContext.FiscalReceipts
            .Where(r => isMainTerminal || r.TerminalId == terminalId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        int count = receipts.Count(r => r.CreatedAtUtc >= closure.PeriodStartUtc && r.CreatedAtUtc <= closure.PeriodEndUtc);

        return new DailyFiscalClosureDto(
            closure.Id,
            closure.TerminalId,
            closure.ClosureSequence,
            closure.TotalSalesTtc.AmountInCents,
            closure.TotalSalesHt.AmountInCents,
            count,
            vatMap,
            tenderMap,
            closure.PerpetualGrandTotalCents,
            closure.SignatureHash,
            closure.PeriodEndUtc,
            closure.PeriodStartUtc,
            closure.SealedByUserName ?? string.Empty
        );
    }

    public async Task<AuditValidationResult> ValidateAuditChainIntegrityAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        var receipts = await _dbContext.FiscalReceipts
            .Where(r => r.TerminalId == terminalId)
            .OrderBy(r => r.SequenceNumber)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (receipts.Count == 0)
        {
            return new AuditValidationResult(true, 0, null, null);
        }

        string expectedPrevHash = GenesisHash;

        for (int i = 0; i < receipts.Count; i++)
        {
            var r = receipts[i];

            if (r.PreviousSignatureHash != expectedPrevHash)
            {
                return new AuditValidationResult(
                    false,
                    i,
                    r.ReceiptNumber,
                    $"Chaîne de hachage rompue au reçu {r.ReceiptNumber}: le hachage précédent stocké ne correspond pas au hachage calculé."
                );
            }

            string computedSig = ComputeReceiptHashSignature(
                r.PreviousSignatureHash,
                r.TerminalId,
                r.SequenceNumber,
                r.TotalTtcAmount.AmountInCents,
                r.CreatedAtUtc,
                r.TaxBreakdownJson
            );

            if (r.SignatureHash != computedSig)
            {
                return new AuditValidationResult(
                    false,
                    i,
                    r.ReceiptNumber,
                    $"Signature invalide au reçu {r.ReceiptNumber}: les données du reçu ont été altérées."
                );
            }

            expectedPrevHash = r.SignatureHash;
        }

        return new AuditValidationResult(true, receipts.Count, null, null);
    }

    public async Task<IReadOnlyList<OpenOrderDto>> FindOpenOrdersAsync(CancellationToken cancellationToken = default)
    {
        var orders = (await _dbContext.Orders.AsNoTracking().Include(o => o.Items)
                .Where(o => o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(o => o.Items.Count > 0)
            .ToList();
        if (orders.Count == 0) return [];

        var ids = orders.Select(o => o.Id).ToList();
        var held = await _dbContext.HeldOrders.AsNoTracking().Where(h => ids.Contains(h.OrderId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var paid = (await _dbContext.FiscalReceipts.AsNoTracking().Include(r => r.Tenders)
                .Where(r => ids.Contains(r.OrderId) && !r.IsVoid).ToListAsync(cancellationToken).ConfigureAwait(false))
            .GroupBy(r => r.OrderId)
            .ToDictionary(g => g.Key, g => g.SelectMany(r => r.Tenders).Sum(t => t.Amount.AmountInCents));

        var result = new List<OpenOrderDto>();
        foreach (var order in orders)
        {
            var hold = held.Where(h => h.OrderId == order.Id).OrderByDescending(h => h.HeldAtUtc).FirstOrDefault();
            if (hold is { IsVoided: true }) continue;   // panier annulé au code superviseur : la commande reste Open mais n'est plus à encaisser
            var remaining = order.TotalTtc.AmountInCents + order.TipAmount.AmountInCents - paid.GetValueOrDefault(order.Id);
            if (remaining <= 0) continue;               // commande entièrement offerte : rien à encaisser
            var label = hold is { IsRecalled: false } && !string.IsNullOrWhiteSpace(hold.CustomerLabel) ? hold.CustomerLabel : order.TableNumber;
            result.Add(new OpenOrderDto(order.Id, label, remaining));
        }
        return result.OrderBy(o => o.Label, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public async Task<bool> IsInClosedPeriodAsync(string terminalId, DateTimeOffset createdAtUtc, CancellationToken cancellationToken = default)
    {
        var term = terminalId ?? string.Empty;
        // La Z du terminal principal couvre les reçus de tous les terminaux (même périmètre que GenerateXReportAsync).
        var ends = (await _dbContext.DailyFiscalClosures.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(c => c.TerminalId == term || MainTerminalIds.Contains(c.TerminalId))
            .Select(c => c.PeriodEndUtc)
            .ToList();
        return ends.Count > 0 && createdAtUtc <= ends.Max();
    }
}
