using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

/// <summary>
/// Tendances sans persistance (rien n'est stocké : une tendance se périme en quelques heures) et résumé quotidien. S'appuie sur
/// <see cref="IFinancialDashboardService"/> sans le modifier ; les totaux du résumé sont ceux du dernier Z. Miroir Swift : InsightsMath / LocalPosAPI+Insights.
/// </summary>
public sealed class InsightsService(AppDbContext db, IFinancialDashboardService dashboard, INF525FiscalAuditService fiscal, ICashSessionService cash, IAlertService alerts) : IInsightsService
{
    private const int MinDays = 3;

    public async Task<InsightsDto> GetInsightsAsync(int tzOffsetMinutes, DateTimeOffset? nowUtc = null, CancellationToken ct = default)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var offset = TimeSpan.FromMinutes(tzOffsetMinutes);
        var threshold = (await db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false))?.TrendThresholdPercent ?? 15;

        var receipts = await db.FiscalReceipts.AsNoTracking().Select(r => new { r.Id, r.OrderId, r.VoidedReceiptId, r.TotalTtcAmount, r.CreatedAtUtc }).ToListAsync(ct).ConfigureAwait(false);
        var voidedIds = receipts.Where(r => r.VoidedReceiptId != null).Select(r => r.VoidedReceiptId!.Value).ToHashSet();
        var sales = receipts.Where(r => r.VoidedReceiptId == null && !voidedIds.Contains(r.Id)).ToList();
        if (sales.Select(r => (r.CreatedAtUtc + offset).Date).Distinct().Count() < MinDays) return new InsightsDto([], true);

        var dayStart = new DateTimeOffset((now + offset).Date, TimeSpan.Zero) - offset;
        var elapsed = now - dayStart;
        var week = TimeSpan.FromDays(7);
        var today = await dashboard.GetTakingsAsync(dayStart, dayStart.AddDays(1), tzOffsetMinutes, now, "lastWeek", ct).ConfigureAwait(false);
        var reference = await dashboard.GetTakingsAsync(dayStart - week, dayStart - week + elapsed, tzOffsetMinutes, now - week, "previous", ct).ConfigureAwait(false);

        var orderIds = sales.Select(r => r.OrderId).Distinct().ToList();
        var orders = (await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).Select(o => new { o.Id, o.Destination }).ToListAsync(ct).ConfigureAwait(false)).ToDictionary(o => o.Id, o => o.Destination);

        (long Cents, int Count, int Takeaway) Window(DateTimeOffset from, DateTimeOffset to)
        {
            var inWindow = sales.Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc < to).ToList();
            return (inWindow.Sum(r => r.TotalTtcAmount.AmountInCents), inWindow.Count, inWindow.Count(r => orders.GetValueOrDefault(r.OrderId) == OrderDestination.Takeaway));
        }

        var cur = Window(dayStart, dayStart + elapsed + TimeSpan.FromTicks(1));
        var refw = Window(dayStart - week, dayStart - week + elapsed + TimeSpan.FromTicks(1));
        if (cur.Count == 0) return new InsightsDto([], false); // journée sans vente : aucune carte

        var candidates = new List<(string Metric, long Current, long Reference)>
        {
            ("cumulativeTtc", cur.Cents, refw.Cents),
            ("avgTicket", cur.Cents / cur.Count, refw.Count == 0 ? 0 : refw.Cents / refw.Count),
            ("perCover", (long)Math.Round(today.SpendPerCover * 100m, MidpointRounding.AwayFromZero), (long)Math.Round(reference.SpendPerCover * 100m, MidpointRounding.AwayFromZero)),
            ("takeawayShare", cur.Takeaway * 10_000L / cur.Count, refw.Count == 0 ? 0 : refw.Takeaway * 10_000L / refw.Count),
        };
        var cards = candidates.Where(c => c.Reference > 0 && c.Current >= 0)
            .Select(c => new InsightCardDto(c.Metric, c.Current, c.Reference, IInsightsService.DeltaPercent(c.Current, c.Reference)))
            .Where(c => Math.Abs(c.DeltaPercent) >= threshold).ToList();
        return new InsightsDto(cards, false);
    }

    public async Task<DailySummaryDto?> GetDailySummaryAsync(string terminalId, int tzOffsetMinutes, DateOnly? localDay = null, CancellationToken ct = default)
    {
        var offset = TimeSpan.FromMinutes(tzOffsetMinutes);
        DailyFiscalClosureDto? closure = null;
        if (localDay is null) closure = await fiscal.GetLatestZClosureAsync(terminalId, ct).ConfigureAwait(false);
        else
        {
            var rows = await db.DailyFiscalClosures.AsNoTracking().Where(c => c.TerminalId == terminalId).ToListAsync(ct).ConfigureAwait(false);
            var row = rows.OrderByDescending(c => c.ClosureSequence).FirstOrDefault(c => DateOnly.FromDateTime((c.PeriodEndUtc + offset).DateTime) == localDay);
            if (row is not null && await fiscal.GetLatestZClosureAsync(terminalId, ct).ConfigureAwait(false) is { } latest && latest.ClosureSequence == row.ClosureSequence) closure = latest;
            else if (row is not null) closure = ToDto(row);
        }
        if (closure is null) return null;

        var from = closure.PeriodStartUtc;
        var to = closure.ClosedAtUtc;
        var takings = await dashboard.GetTakingsAsync(from, to.AddTicks(1), tzOffsetMinutes, to, null, ct).ConfigureAwait(false);
        // Pourboires des commandes réglées dans la période du Z (reçus de la période, hors avoirs), par serveur.
        var periodReceipts = (await db.FiscalReceipts.AsNoTracking().Where(r => r.TerminalId == terminalId).ToListAsync(ct).ConfigureAwait(false))
            .Where(r => r.VoidedReceiptId == null && r.CreatedAtUtc >= from && r.CreatedAtUtc <= to).Select(r => r.OrderId).Distinct().ToList();
        var orderRows = await db.Orders.AsNoTracking().Where(o => periodReceipts.Contains(o.Id)).Select(o => new { o.OperatorId, o.TipAmount }).ToListAsync(ct).ConfigureAwait(false);
        var operatorIds = orderRows.Select(o => o.OperatorId).Distinct().ToList();
        var names = await db.Users.AsNoTracking().Where(u => operatorIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name, ct).ConfigureAwait(false);
        var tips = orderRows.GroupBy(o => o.OperatorId).Select(g => new ServerTipSummary(names.GetValueOrDefault(g.Key) ?? "—", g.Sum(o => o.TipAmount.AmountInCents))).Where(t => t.TipsCents > 0).OrderBy(t => t.ServerName, StringComparer.CurrentCultureIgnoreCase).ToList();
        var cashSummary = await cash.CashForPeriodAsync(terminalId, from, to, ct).ConfigureAwait(false);
        var open = (await alerts.ListAsync(Alert.Open, null, null, ct).ConfigureAwait(false)).Where(a => a.TerminalId == terminalId).ToList();
        var average = closure.ReceiptCount > 0 ? closure.TotalSalesTtcCents / closure.ReceiptCount : 0;
        return new DailySummaryDto(terminalId, closure.ClosureSequence, closure.ClosedAtUtc, closure.TotalSalesTtcCents, closure.TotalSalesHtCents, closure.ReceiptCount, takings.Covers, average,
            closure.PaymentTotalsCents.ToDictionary(p => p.Key.ToString(), p => p.Value), tips,
            cashSummary is null ? null : new DailySummaryCash(cashSummary.FloatCents, cashSummary.CountedCents, cashSummary.VarianceCents), open);
    }

    private static DailyFiscalClosureDto ToDto(DailyFiscalClosure row)
    {
        var tenders = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(row.TenderTotalsJson) ?? [];
        var taxes = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(row.TaxesSummaryJson) ?? [];
        return new DailyFiscalClosureDto(row.Id, row.TerminalId, row.ClosureSequence, row.TotalSalesTtc.AmountInCents, row.TotalSalesHt.AmountInCents, 0,
            taxes.ToDictionary(t => decimal.Parse(t.Key, System.Globalization.CultureInfo.InvariantCulture), t => t.Value),
            tenders.Where(t => Enum.TryParse<PaymentMethod>(t.Key, out _)).ToDictionary(t => Enum.Parse<PaymentMethod>(t.Key), t => t.Value),
            row.PerpetualGrandTotalCents, row.SignatureHash, row.PeriodEndUtc, row.PeriodStartUtc, row.SealedByUserName);
    }
}
