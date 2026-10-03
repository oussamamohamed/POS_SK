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
    private readonly AppDbContext _dbContext;
    private readonly IFiscalJournal _fiscalJournal;

    public NF525FiscalAuditService(AppDbContext dbContext, IFiscalJournal? fiscalJournal = null)
    {
        _dbContext = dbContext;
        _fiscalJournal = fiscalJournal ?? new FiscalJournalService(dbContext);
    }

    public string ComputeReceiptHashSignature(
        string previousHash,
        string terminalId,
        long sequenceNumber,
        long amountCents,
        DateTimeOffset timestampUtc,
        string taxBreakdownJson)
    {
        return FiscalHashing.ComputeReceiptHash(
            previousHash,
            terminalId,
            sequenceNumber,
            amountCents,
            timestampUtc,
            taxBreakdownJson);
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

                await _fiscalJournal.AppendAsync(
                    JournalEventTypes.ZClosure,
                    new
                    {
                        ClosureId = closure.Id,
                        TerminalId = terminalId,
                        ClosureSequence = nextSequence,
                        TotalSalesTtcCents = xSummary.TotalSalesTtcCents,
                        PerpetualGrandTotalCents = xSummary.PerpetualGrandTotalCents,
                        SignatureHash = sig
                    },
                    terminalId: terminalId,
                    operatorId: managerId,
                    cancellationToken: ct
                ).ConfigureAwait(false);

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
        var voidedIds = await _dbContext.FiscalReceipts.AsNoTracking()
            .Where(r => ids.Contains(r.OrderId) && r.VoidedReceiptId != null)
            .Select(r => r.VoidedReceiptId!.Value)
            .ToListAsync(cancellationToken).ConfigureAwait(false);
        var paid = (await _dbContext.FiscalReceipts.AsNoTracking().Include(r => r.Tenders)
                .Where(r => ids.Contains(r.OrderId) && r.VoidedReceiptId == null && !voidedIds.Contains(r.Id)).ToListAsync(cancellationToken).ConfigureAwait(false))
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
        // Chaque terminal a sa propre clôture : seule sa Z compte.
        var ends = (await _dbContext.DailyFiscalClosures.AsNoTracking().Where(c => c.TerminalId == term).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Select(c => c.PeriodEndUtc)
            .ToList();
        return ends.Count > 0 && createdAtUtc <= ends.Max();
    }

    public async Task<PeriodClosureDto> ExecutePeriodClosureAsync(
        string terminalId,
        FiscalPeriodType periodType,
        string periodKey,
        Guid managerId,
        string managerName,
        DateTimeOffset? utcNow = null,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExecuteInTransactionAsync(async _ =>
        {
            var term = string.IsNullOrWhiteSpace(terminalId) ? "POS_MAIN_TERM" : terminalId;
            // Même périmètre que la Z et GenerateXReportAsync : le terminal principal couvre les reçus de tous les terminaux.
            var isMainTerminal = term == "POS_MAIN_TERM";
            var now = utcNow ?? DateTimeOffset.UtcNow;

            DateTime periodStartLocal;
            DateTime periodEndLocal;
            DateTimeOffset periodStartUtc;
            DateTimeOffset periodEndUtc;

            if (periodType == FiscalPeriodType.Monthly)
            {
                if (!DateTime.TryParseExact(periodKey, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedMonth))
                {
                    throw new ArgumentException("Invalid period key format. Expected yyyy-MM.");
                }
                periodStartLocal = new DateTime(parsedMonth.Year, parsedMonth.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
                periodEndLocal = periodStartLocal.AddMonths(1);
                periodStartUtc = new DateTimeOffset(periodStartLocal, TimeZoneInfo.Local.GetUtcOffset(periodStartLocal)).ToUniversalTime();
                periodEndUtc = new DateTimeOffset(periodEndLocal, TimeZoneInfo.Local.GetUtcOffset(periodEndLocal)).ToUniversalTime();
            }
            else if (periodType == FiscalPeriodType.Annual)
            {
                if (!int.TryParse(periodKey, out int fiscalYear))
                {
                    throw new ArgumentException("Invalid period key format. Expected yyyy.");
                }
                var settings = await _dbContext.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
                int fiscalMonth = settings?.FiscalYearStartMonth ?? 1;
                int fiscalDay = settings?.FiscalYearStartDay ?? 1;
                periodStartLocal = new DateTime(fiscalYear, fiscalMonth, fiscalDay, 0, 0, 0, DateTimeKind.Unspecified);
                periodEndLocal = periodStartLocal.AddYears(1);
                periodStartUtc = new DateTimeOffset(periodStartLocal, TimeZoneInfo.Local.GetUtcOffset(periodStartLocal)).ToUniversalTime();
                periodEndUtc = new DateTimeOffset(periodEndLocal, TimeZoneInfo.Local.GetUtcOffset(periodEndLocal)).ToUniversalTime();
            }
            else
            {
                throw new ArgumentOutOfRangeException(nameof(periodType));
            }

            if (now < periodEndUtc)
            {
                throw new PeriodClosureException("period_not_ended", "The fiscal period has not ended yet.");
            }

            var alreadyClosed = await _dbContext.PeriodClosures
                .AnyAsync(p => p.TerminalId == term && p.PeriodType == periodType && p.PeriodKey == periodKey, cancellationToken)
                .ConfigureAwait(false);
            if (alreadyClosed)
            {
                throw new PeriodClosureException("period_already_closed", "This fiscal period is already closed.");
            }

            long totalTtcCents;
            long totalHtCents;
            int dailyClosureCount;
            long perpetualGrandTotalCents;
            var vatDict = new Dictionary<string, long>();
            var tenderDict = new Dictionary<string, long>();

            if (periodType == FiscalPeriodType.Monthly)
            {
                var allDailyClosures = await _dbContext.DailyFiscalClosures
                    .AsNoTracking()
                    .Where(c => c.TerminalId == term)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var periodDailyClosures = allDailyClosures
                    // Un Z appartient au mois où sa période commence (un Z lancé après minuit clôture le jour précédent).
                    .Where(c => c.PeriodStartUtc >= periodStartUtc && c.PeriodStartUtc < periodEndUtc)
                    .OrderBy(c => c.ClosureSequence)
                    .ToList();

                var periodReceipts = await _dbContext.FiscalReceipts
                    .AsNoTracking()
                    .Where(r => (isMainTerminal || r.TerminalId == term) && r.CreatedAtUtc >= periodStartUtc && r.CreatedAtUtc < periodEndUtc)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var missingDays = new SortedSet<string>();
                foreach (var receipt in periodReceipts)
                {
                    bool covered = periodDailyClosures.Any(c => c.PeriodEndUtc >= receipt.CreatedAtUtc);
                    if (!covered)
                    {
                        var localReceiptTime = TimeZoneInfo.ConvertTime(receipt.CreatedAtUtc, TimeZoneInfo.Local);
                        missingDays.Add(localReceiptTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    }
                }

                if (missingDays.Count > 0)
                {
                    throw new PeriodClosureException("missing_daily_closures", "Missing daily closures for days with receipts.", days: missingDays.ToList());
                }

                totalTtcCents = periodDailyClosures.Sum(c => c.TotalSalesTtc.AmountInCents);
                totalHtCents = periodDailyClosures.Sum(c => c.TotalSalesHt.AmountInCents);
                dailyClosureCount = periodDailyClosures.Count;

                if (periodDailyClosures.Count > 0)
                {
                    perpetualGrandTotalCents = periodDailyClosures.Last().PerpetualGrandTotalCents;
                }
                else
                {
                    var lastPrior = allDailyClosures.Where(c => c.PeriodEndUtc < periodStartUtc).OrderByDescending(c => c.PeriodEndUtc).FirstOrDefault();
                    perpetualGrandTotalCents = lastPrior?.PerpetualGrandTotalCents ?? 0;
                }

                foreach (var c in periodDailyClosures)
                {
                    MergeJsonBreakdown(c.TaxesSummaryJson, vatDict);
                    MergeJsonBreakdown(c.TenderTotalsJson, tenderDict);
                }
            }
            else
            {
                var expectedMonths = new List<string>();
                for (int i = 0; i < 12; i++)
                {
                    expectedMonths.Add(periodStartLocal.AddMonths(i).ToString("yyyy-MM", CultureInfo.InvariantCulture));
                }

                var existingMonthlyClosures = await _dbContext.PeriodClosures
                    .AsNoTracking()
                    .Where(p => p.TerminalId == term && p.PeriodType == FiscalPeriodType.Monthly)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                var closedMonthKeys = existingMonthlyClosures.Select(p => p.PeriodKey).ToHashSet();
                var missingMonths = expectedMonths.Where(m => !closedMonthKeys.Contains(m)).ToList();
                if (missingMonths.Count > 0)
                {
                    throw new PeriodClosureException("missing_monthly_closures", "Missing monthly closures for fiscal year.", months: missingMonths);
                }

                var fiscalYearMonthlyClosures = existingMonthlyClosures
                    .Where(p => expectedMonths.Contains(p.PeriodKey))
                    .OrderBy(p => p.PeriodKey)
                    .ToList();

                totalTtcCents = fiscalYearMonthlyClosures.Sum(c => c.TotalTtcAmount.AmountInCents);
                totalHtCents = fiscalYearMonthlyClosures.Sum(c => c.TotalHtAmount.AmountInCents);
                dailyClosureCount = fiscalYearMonthlyClosures.Sum(c => c.DailyClosureCount);
                perpetualGrandTotalCents = fiscalYearMonthlyClosures.Last().PerpetualGrandTotalCents;

                foreach (var c in fiscalYearMonthlyClosures)
                {
                    MergeJsonBreakdown(c.TaxesSummaryJson, vatDict);
                    MergeJsonBreakdown(c.TenderTotalsJson, tenderDict);
                }
            }

            var lastClosureOfSameType = await _dbContext.PeriodClosures
                .Where(p => p.TerminalId == term && p.PeriodType == periodType)
                .OrderByDescending(p => p.ClosureSequence)
                .FirstOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);

            long nextSequence = (lastClosureOfSameType?.ClosureSequence ?? 0) + 1;
            string prevHash = lastClosureOfSameType?.SignatureHash ?? GenesisHash;

            string taxesJson = JsonSerializer.Serialize(vatDict);
            string tendersJson = JsonSerializer.Serialize(tenderDict);

            string signatureHash = FiscalHashing.ComputePeriodClosureHash(
                prevHash,
                term,
                (int)periodType,
                periodKey,
                totalTtcCents,
                totalHtCents,
                taxesJson,
                tendersJson,
                perpetualGrandTotalCents,
                periodEndUtc);

            var periodClosure = new FiscalPeriodClosure
            {
                Id = UuidV7.NewGuid(),
                TerminalId = term,
                PeriodType = periodType,
                PeriodKey = periodKey,
                ClosureSequence = nextSequence,
                PeriodStartUtc = periodStartUtc,
                PeriodEndUtc = periodEndUtc,
                TotalTtcAmount = Money.FromCents(totalTtcCents),
                TotalHtAmount = Money.FromCents(totalHtCents),
                TaxesSummaryJson = taxesJson,
                TenderTotalsJson = tendersJson,
                PerpetualGrandTotalCents = perpetualGrandTotalCents,
                DailyClosureCount = dailyClosureCount,
                PreviousSignatureHash = prevHash,
                SignatureHash = signatureHash,
                SealedByUserId = managerId,
                SealedByUserName = managerName,
                CreatedAtUtc = now
            };

            _dbContext.PeriodClosures.Add(periodClosure);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            await _fiscalJournal.AppendAsync(
                JournalEventTypes.PeriodClosure,
                new
                {
                    PeriodClosureId = periodClosure.Id,
                    TerminalId = term,
                    PeriodType = periodType == FiscalPeriodType.Monthly ? "monthly" : "annual",
                    PeriodKey = periodKey,
                    ClosureSequence = nextSequence,
                    TotalTtcCents = totalTtcCents,
                    PerpetualGrandTotalCents = perpetualGrandTotalCents,
                    SignatureHash = signatureHash
                },
                terminalId: term,
                operatorId: managerId,
                cancellationToken: cancellationToken
            ).ConfigureAwait(false);

            return new PeriodClosureDto(
                periodClosure.Id,
                periodClosure.TerminalId,
                periodClosure.PeriodType,
                periodClosure.PeriodKey,
                periodClosure.ClosureSequence,
                periodClosure.PeriodStartUtc,
                periodClosure.PeriodEndUtc,
                periodClosure.TotalTtcAmount.AmountInCents,
                periodClosure.TotalHtAmount.AmountInCents,
                periodClosure.TaxesSummaryJson,
                periodClosure.TenderTotalsJson,
                periodClosure.PerpetualGrandTotalCents,
                periodClosure.DailyClosureCount,
                periodClosure.PreviousSignatureHash,
                periodClosure.SignatureHash,
                periodClosure.SealedByUserId,
                periodClosure.SealedByUserName,
                periodClosure.CreatedAtUtc);
        }, System.Data.IsolationLevel.Serializable, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PeriodClosureDto>> GetPeriodClosuresAsync(
        string? terminalId = null,
        FiscalPeriodType? periodType = null,
        CancellationToken cancellationToken = default)
    {
        var query = _dbContext.PeriodClosures.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(terminalId))
        {
            query = query.Where(p => p.TerminalId == terminalId);
        }
        if (periodType.HasValue)
        {
            query = query.Where(p => p.PeriodType == periodType.Value);
        }

        var list = await query
            .OrderByDescending(p => p.PeriodKey)
            .ThenByDescending(p => p.ClosureSequence)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return list.Select(p => new PeriodClosureDto(
            p.Id,
            p.TerminalId,
            p.PeriodType,
            p.PeriodKey,
            p.ClosureSequence,
            p.PeriodStartUtc,
            p.PeriodEndUtc,
            p.TotalTtcAmount.AmountInCents,
            p.TotalHtAmount.AmountInCents,
            p.TaxesSummaryJson,
            p.TenderTotalsJson,
            p.PerpetualGrandTotalCents,
            p.DailyClosureCount,
            p.PreviousSignatureHash,
            p.SignatureHash,
            p.SealedByUserId,
            p.SealedByUserName,
            p.CreatedAtUtc)).ToList();
    }

    private static void MergeJsonBreakdown(string? json, Dictionary<string, long> target)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, long>>(json);
            if (dict == null) return;
            foreach (var kvp in dict)
            {
                target[kvp.Key] = target.GetValueOrDefault(kvp.Key) + kvp.Value;
            }
        }
        catch
        {
            // Ignore malformed JSON in legacy data
        }
    }
}
