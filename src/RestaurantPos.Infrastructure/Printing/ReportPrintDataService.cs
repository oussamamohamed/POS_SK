using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Printing;

public sealed record ReportItemLine(string ProductName, int Quantity, long TotalTtcCents);
/// <param name="CategoryName">null = articles sans famille connue (« Autres »).</param>
public sealed record ReportCategoryGroup(string? CategoryName, IReadOnlyList<ReportItemLine> Items, long SubtotalTtcCents);
/// <param name="ServerName">null = serveur inconnu.</param>
public sealed record ReportTipLine(string? ServerName, long TipCents);
/// <summary>Remises (lignes et note), offerts, et annulations (avoirs) de la période, en centimes. Informatif.</summary>
public sealed record ReportActivity(long DiscountsCents, long CompsCents, int VoidCount, long VoidCents);
/// <param name="Activity">null = non calculé (aucun bloc imprimé).</param>
/// <param name="Cash">Dernier comptage de caisse de la période ; null = aucun (aucun bloc imprimé).</param>
public sealed record ReportPrintData(IReadOnlyList<ReportCategoryGroup> Categories, IReadOnlyList<ReportTipLine> Tips, long TotalTipsCents, bool HasGlobalDiscount, ReportActivity? Activity = null, RestaurantPos.Application.Common.Interfaces.CashZSummary? Cash = null);

/// <summary>Articles vendus et pourboires d'une période de rapport. Informatif : n'entre ni dans les totaux fiscaux ni dans la signature Z.</summary>
public sealed class ReportPrintDataService
{
    private static readonly StringComparer French = StringComparer.Create(CultureInfo.GetCultureInfo("fr"), ignoreCase: true);
    private readonly AppDbContext _db;

    private readonly RestaurantPos.Application.Common.Interfaces.ICashSessionService? _cash;

    public ReportPrintDataService(AppDbContext db, RestaurantPos.Application.Common.Interfaces.ICashSessionService? cash = null)
    {
        _db = db;
        _cash = cash;
    }

    public async Task<ReportPrintData> BuildAsync(string terminalId, DateTimeOffset periodStartUtc, DateTimeOffset periodEndUtc, CancellationToken ct = default)
    {
        // Même périmètre que GenerateXReportAsync : le terminal principal couvre tous les terminaux.
        var isMainTerminal = string.IsNullOrWhiteSpace(terminalId) || terminalId == "POS_MAIN_TERM";
        var receipts = await _db.FiscalReceipts.AsNoTracking()
            .Where(r => isMainTerminal || r.TerminalId == terminalId)
            .Select(r => new { r.Id, r.OrderId, r.VoidedReceiptId, r.TotalTtcAmount, r.CreatedAtUtc }) // cinq colonnes : pas les JSON ni les signatures de tout l'historique
            .ToListAsync(ct).ConfigureAwait(false);

        var voidedIds = receipts
            .Where(r => r.VoidedReceiptId != null)
            .Select(r => r.VoidedReceiptId!.Value)
            .ToHashSet();

        var orderIds = receipts
            .Where(r => r.VoidedReceiptId == null && !voidedIds.Contains(r.Id) && r.TotalTtcAmount.AmountInCents > 0 && r.CreatedAtUtc >= periodStartUtc && r.CreatedAtUtc <= periodEndUtc)
            .Select(r => r.OrderId).Distinct().ToList();

        var orders = (await _db.Orders.AsNoTracking().Include(o => o.Items)
                .Where(o => orderIds.Contains(o.Id)).ToListAsync(ct).ConfigureAwait(false))
            .Where(o => o.Status != OrderStatus.Cancelled).ToList();

        var productCategory = await _db.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, p => p.CategoryId, ct).ConfigureAwait(false);
        var categoryName = await _db.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, ct).ConfigureAwait(false);
        var userName = await _db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, ct).ConfigureAwait(false);

        var categories = orders.SelectMany(o => o.Items)
            .GroupBy(i => productCategory.TryGetValue(i.ProductId, out var cat) && categoryName.TryGetValue(cat, out var name) ? name : null)
            .Select(g => new ReportCategoryGroup(
                g.Key,
                g.GroupBy(i => i.ProductName)
                    .Select(p => new ReportItemLine(p.Key, p.Sum(i => i.Quantity), p.Sum(LineTtc)))
                    .OrderBy(l => l.ProductName, French).ToList(),
                g.Sum(LineTtc)))
            .OrderBy(g => g.CategoryName is null).ThenBy(g => g.CategoryName, French)
            .ToList();

        var tips = orders.Where(o => o.TipAmount.AmountInCents > 0)
            .GroupBy(o => o.OperatorId)
            .Select(g => new ReportTipLine(userName.TryGetValue(g.Key, out var name) ? name : null, g.Sum(o => o.TipAmount.AmountInCents)))
            .OrderBy(t => t.ServerName is null).ThenBy(t => t.ServerName, French)
            .ToList();

        // Mêmes formules que le tableau de bord des encaissements (remises, offerts) ; annulations = avoirs signés dans la période.
        long discounts = 0, comps = 0;
        foreach (var order in orders)
        {
            var (orderDiscounts, orderComps) = OrderDiscounts.Of(order);
            discounts += orderDiscounts;
            comps += orderComps;
        }
        var credits = receipts.Where(r => r.VoidedReceiptId != null && r.CreatedAtUtc >= periodStartUtc && r.CreatedAtUtc <= periodEndUtc).ToList();
        var activity = new ReportActivity(discounts, comps, credits.Count, Math.Abs(credits.Sum(r => r.TotalTtcAmount.AmountInCents)));
        var cash = _cash is null ? null : await _cash.CashForPeriodAsync(terminalId, periodStartUtc, periodEndUtc, ct).ConfigureAwait(false);

        return new ReportPrintData(categories, tips, tips.Sum(t => t.TipCents), orders.Any(o => o.GlobalDiscountType is not null), activity, cash);
    }

    private static long LineTtc(OrderItem item) => item.IsComp ? 0 : item.CalculateTotalTtc().AmountInCents;
}
