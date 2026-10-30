using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public sealed class FinancialDashboardService : IFinancialDashboardService
{
    private readonly AppDbContext _dbContext;

    public FinancialDashboardService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<TakingsReportDto> GetTakingsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        int tzOffsetMinutes = 0,
        DateTimeOffset? nowUtc = null,
        string? compare = null,
        CancellationToken cancellationToken = default)
    {
        var now = nowUtc ?? DateTimeOffset.UtcNow;
        var span = toUtc - fromUtc;
        bool byHour = span <= TimeSpan.FromHours(25);
        string cmp = compare is "previous" or "lastWeek" ? compare : (byHour ? "lastWeek" : "previous");
        var shift = cmp == "previous" ? span : TimeSpan.FromDays(7);
        var offset = TimeSpan.FromMinutes(tzOffsetMinutes);
        var day = TimeSpan.FromDays(1);
        // Une heure de tolérance : une semaine locale qui traverse un changement d'heure mesure 7 jours ± 1 h.
        int buckets = byHour ? 24 : Math.Clamp((int)Math.Ceiling((span - TimeSpan.FromHours(1)) / day), 1, 92);

        var today = await GetFinancialDashboardAsync(new FinancialDashboardFilterDto(fromUtc, toUtc), cancellationToken).ConfigureAwait(false);

        var receipts = await _dbContext.FiscalReceipts.Include(r => r.Tenders).AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var voidedIds = receipts.Where(r => r.VoidedReceiptId != null).Select(r => r.VoidedReceiptId!.Value).ToHashSet();
        var valid = receipts.Where(r => r.VoidedReceiptId == null && !voidedIds.Contains(r.Id)).ToList();

        decimal[] Series(DateTimeOffset from, DateTimeOffset to)
        {
            var values = new decimal[buckets];
            foreach (var r in valid.Where(r => r.CreatedAtUtc >= from && r.CreatedAtUtc < to))
            {
                int i = byHour ? (r.CreatedAtUtc + offset).Hour : Math.Min(buckets - 1, (int)((r.CreatedAtUtc - from) / day));
                values[i] += r.TotalTtcAmount.AmountInCents / 100.0m;
            }
            return values;
        }

        var current = Series(fromUtc, toUtc);
        var reference = Series(fromUtc - shift, toUtc - shift);
        decimal compareByNow = valid
            .Where(r => r.CreatedAtUtc >= fromUtc - shift && r.CreatedAtUtc < toUtc - shift && r.CreatedAtUtc <= now - shift)
            .Sum(r => r.TotalTtcAmount.AmountInCents) / 100.0m;
        var series = Enumerable.Range(0, buckets).Select(i => new TakingsPointDto(
            byHour ? i.ToString(System.Globalization.CultureInfo.InvariantCulture) : (fromUtc + offset + (day * i)).ToString("MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            current[i], reference[i])).ToList();

        // Commandes réglées de la période, hors commandes annulées par avoir.
        var voidedOrderIds = receipts.Where(r => voidedIds.Contains(r.Id)).Select(r => r.OrderId).ToHashSet();
        var orders = (await _dbContext.Orders.AsNoTracking().Include(o => o.Items)
                .Where(o => o.Status == OrderStatus.Paid).ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(o => o.CreatedAtUtc >= fromUtc && o.CreatedAtUtc < toUtc && !voidedOrderIds.Contains(o.Id))
            .ToList();
        var products = await _dbContext.Products.AsNoTracking().ToDictionaryAsync(p => p.Id, cancellationToken).ConfigureAwait(false);
        var categoryNames = await _dbContext.Categories.AsNoTracking().ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken).ConfigureAwait(false);
        var users = await _dbContext.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, cancellationToken).ConfigureAwait(false);
        var tables = await _dbContext.DiningTables.AsNoTracking().ToDictionaryAsync(t => t.TableNumber, StringComparer.OrdinalIgnoreCase, cancellationToken).ConfigureAwait(false);

        long discounts = 0, comps = 0, tips = 0;
        var tipsByServer = new Dictionary<string, long>();
        var byCategory = new Dictionary<string, (int Quantity, decimal Sales)>();
        var byProduct = new Dictionary<Guid, (string Name, int Quantity, decimal Sales)>();
        foreach (var order in orders)
        {
            long subtotal = order.Subtotal.AmountInCents, orderTotal = order.TotalTtc.AmountInCents;
            decimal scale = subtotal > 0 ? (decimal)orderTotal / subtotal : 0m;
            if (order.GlobalDiscountType == DiscountType.Comp)
            {
                comps += subtotal;
            }
            else
            {
                discounts += subtotal - orderTotal;
                foreach (var item in order.Items)
                {
                    long baseCents = (item.UnitPrice.AmountInCents + item.ModifiersPriceExtra.AmountInCents) * item.Quantity;
                    if (item.IsComp) comps += baseCents;
                    else if (item.DiscountPercent > 0) discounts += baseCents - item.CalculateTotalTtc().AmountInCents;
                }
            }

            tips += order.TipAmount.AmountInCents;
            string server = users.TryGetValue(order.OperatorId, out var user) ? user.Name
                : tables.TryGetValue(order.TableNumber, out var table) && !string.IsNullOrWhiteSpace(table.AssignedWaiterName) ? table.AssignedWaiterName! : "Serveur Inconnu";
            tipsByServer[server] = tipsByServer.GetValueOrDefault(server) + order.TipAmount.AmountInCents;

            if (order.GlobalDiscountType == DiscountType.Comp) continue;
            foreach (var item in order.Items.Where(i => !i.IsComp && i.Quantity > 0))
            {
                decimal sales = item.CalculateTotalTtc().AmountInCents * scale / 100m;
                string category = products.TryGetValue(item.ProductId, out var product) && categoryNames.TryGetValue(product.CategoryId, out var name) ? name : "—";
                var c = byCategory.GetValueOrDefault(category);
                byCategory[category] = (c.Quantity + item.Quantity, c.Sales + sales);
                var p = byProduct.GetValueOrDefault(item.ProductId);
                byProduct[item.ProductId] = (item.ProductName, p.Quantity + item.Quantity, p.Sales + sales);
            }
        }

        decimal totalSales = byCategory.Values.Sum(v => v.Sales);
        var categories = byCategory.OrderByDescending(kv => kv.Value.Sales)
            .Select(kv => new CategoryTakingDto(kv.Key, kv.Value.Quantity, Math.Round(kv.Value.Sales, 2), totalSales > 0 ? Math.Round(kv.Value.Sales / totalSales, 4) : 0)).ToList();
        var topProducts = byProduct.OrderByDescending(kv => kv.Value.Sales).Take(5)
            .Select(kv => new TopProductSaleDto(kv.Key, kv.Value.Name, kv.Value.Quantity, Math.Round(kv.Value.Sales, 2),
                totalSales > 0 ? Math.Round(kv.Value.Sales / totalSales * 100m, 1, MidpointRounding.AwayFromZero) : 0)).ToList();

        // TVA : montants par taux des reçus valides (TaxBreakdownJson), base HT si connue (HtBreakdownJson), sinon déduite du taux.
        var vat = new Dictionary<decimal, (long Base, long Vat)>();
        foreach (var r in valid.Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc))
        {
            var vatByRate = ParseRates(r.TaxBreakdownJson);
            var htByRate = r.HtBreakdownJson is null ? null : ParseRates(r.HtBreakdownJson);
            foreach (var (rate, vatCents) in vatByRate)
            {
                long baseCents = htByRate is not null && htByRate.TryGetValue(rate, out var ht) ? ht : rate > 0 ? (long)Math.Round(vatCents * 100m / rate, MidpointRounding.AwayFromZero) : 0;
                var v = vat.GetValueOrDefault(rate);
                vat[rate] = (v.Base + baseCents, v.Vat + vatCents);
            }
        }

        var credits = receipts.Where(r => r.VoidedReceiptId != null && r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc).ToList();
        decimal total = today.Kpis.TotalSalesTtc;
        int covers = today.Kpis.TotalCoversCount;
        return new TakingsReportDto(
            fromUtc, toUtc, byHour ? "hour" : "day", cmp, total, reference.Sum(), compareByNow, covers, today.Kpis.TotalOrdersCount,
            covers > 0 ? Math.Round(total / covers, 2, MidpointRounding.AwayFromZero) : 0,
            series, today.PaymentMethods, today.StaffPerformance,
            tips / 100.0m, tipsByServer.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Select(kv => new ServerTipDto(kv.Key, kv.Value / 100.0m)).ToList(),
            discounts / 100.0m, comps / 100.0m, credits.Count, Math.Abs(credits.Sum(r => r.TotalTtcAmount.AmountInCents)) / 100.0m,
            categories, topProducts,
            vat.OrderBy(kv => kv.Key).Select(kv => new VatLineDto(kv.Key, kv.Value.Base / 100.0m, kv.Value.Vat / 100.0m)).ToList());
    }

    private static Dictionary<decimal, long> ParseRates(string? json)
    {
        var result = new Dictionary<decimal, long>();
        if (string.IsNullOrWhiteSpace(json) || json == "{}") return result;
        try
        {
            foreach (var (key, value) in System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, long>>(json) ?? [])
            {
                if (decimal.TryParse(key, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var rate)) result[rate] = value;
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Ventilation illisible : ce reçu n'apporte rien au détail de TVA.
        }
        return result;
    }

    public async Task<IReadOnlyList<TakingsReceiptDto>> GetReceiptsAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken cancellationToken = default)
    {
        var all = await _dbContext.FiscalReceipts.Include(r => r.Tenders).AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false);
        var voidedIds = all.Where(r => r.VoidedReceiptId != null).Select(r => r.VoidedReceiptId!.Value).ToHashSet();
        var inRange = all.Where(r => r.CreatedAtUtc >= fromUtc && r.CreatedAtUtc < toUtc).OrderByDescending(r => r.CreatedAtUtc).Take(300).ToList();
        var orderIds = inRange.Select(r => r.OrderId).ToList();
        var orders = (await _dbContext.Orders.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false)).Where(o => orderIds.Contains(o.Id)).ToDictionary(o => o.Id);
        var users = await _dbContext.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken).ConfigureAwait(false);
        return inRange.Select(r =>
        {
            orders.TryGetValue(r.OrderId, out var order);
            string server = order is not null && users.TryGetValue(order.OperatorId, out var name) ? name : "Serveur Inconnu";
            return new TakingsReceiptDto(
                r.Id, r.ReceiptNumber, r.CreatedAtUtc, order?.TableNumber, server,
                r.Tenders.Select(t => GetPaymentMethodName(t.Method)).Distinct().ToList(),
                r.TotalTtcAmount.AmountInCents / 100.0m, r.VoidedReceiptId != null, voidedIds.Contains(r.Id));
        }).ToList();
    }

    public async Task<IReadOnlyList<JournalEntryDto>> GetJournalAsync(DateTimeOffset fromUtc, CancellationToken cancellationToken = default)
    {
        var entries = (await _dbContext.JournalEntries.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(e => e.OccurredAtUtc >= fromUtc).OrderByDescending(e => e.OccurredAtUtc).ThenByDescending(e => e.LocalSequence).Take(300).ToList();
        var users = await _dbContext.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name, cancellationToken).ConfigureAwait(false);
        return entries.Select(e => new JournalEntryDto(
            e.Id, e.OccurredAtUtc, e.EventType, e.TerminalId,
            e.OperatorId is { } id && users.TryGetValue(id, out var name) ? name : null, e.PayloadJson)).ToList();
    }

    public async Task<FinancialDashboardReportDto> GetFinancialDashboardAsync(
        FinancialDashboardFilterDto filter,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var startUtc = filter.FromUtc ?? now.Date;
        var endUtc = filter.ToUtc ?? now;

        if (startUtc > endUtc)
        {
            (startUtc, endUtc) = (endUtc, startUtc);
        }

        // 1. Fetch Fiscal Receipts with Tenders
        var allReceipts = await _dbContext.FiscalReceipts
            .Include(r => r.Tenders)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var voidedReceiptIds = allReceipts
            .Where(r => r.VoidedReceiptId != null)
            .Select(r => r.VoidedReceiptId!.Value)
            .ToHashSet();

        var receipts = allReceipts
            .Where(r => r.CreatedAtUtc >= startUtc && r.CreatedAtUtc <= endUtc && r.VoidedReceiptId == null && !voidedReceiptIds.Contains(r.Id))
            .ToList();

        // 2. Fetch Orders and related data in memory
        var allOrders = await _dbContext.Orders
            .Include(o => o.Items)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var orders = allOrders
            .Where(o => o.CreatedAtUtc >= startUtc && o.CreatedAtUtc <= endUtc && (o.Status == OrderStatus.Paid || receipts.Any(r => r.OrderId == o.Id)))
            .ToList();

        // 3. Fetch Dining Tables and Users for mapping
        var tables = await _dbContext.DiningTables
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var tableMap = tables.ToDictionary(t => t.TableNumber, StringComparer.OrdinalIgnoreCase);

        var users = await _dbContext.Users
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var userMap = users.ToDictionary(u => u.Id);

        // 4. Calculate Global KPIs
        decimal totalSalesTtc = receipts.Count > 0
            ? receipts.Sum(r => r.TotalTtcAmount.AmountInCents) / 100.0m
            : orders.Sum(o => o.TotalTtc.AmountInCents) / 100.0m;

        decimal totalSalesHt = receipts.Count > 0
            ? receipts.Sum(r => r.TotalHtAmount.AmountInCents) / 100.0m
            : orders.Sum(o => o.TotalHt.AmountInCents) / 100.0m;

        int totalOrdersCount = receipts.Count > 0 ? receipts.Count : orders.Count;

        int totalCoversCount = 0;
        if (totalOrdersCount > 0)
        {
            totalCoversCount = orders.Sum(o =>
            {
                if (tableMap.TryGetValue(o.TableNumber, out var table) && table.CoversCount > 0)
                {
                    return table.CoversCount;
                }
                return 1;
            });

            if (totalCoversCount == 0)
            {
                totalCoversCount = totalOrdersCount;
            }
        }

        decimal averageOrderTtc = totalOrdersCount > 0
            ? Math.Round(totalSalesTtc / totalOrdersCount, 2, MidpointRounding.AwayFromZero)
            : 0;

        decimal averageCoverTtc = totalCoversCount > 0
            ? Math.Round(totalSalesTtc / totalCoversCount, 2, MidpointRounding.AwayFromZero)
            : 0;

        var kpis = new FinancialKpisDto(
            TotalSalesTtc: totalSalesTtc,
            TotalSalesHt: totalSalesHt,
            AverageOrderTtc: averageOrderTtc,
            AverageCoverTtc: averageCoverTtc,
            TotalOrdersCount: totalOrdersCount,
            TotalCoversCount: totalCoversCount
        );

        // 5. Service Breakdown (Midi vs Soir vs Hors Service)
        var serviceGroups = orders.GroupBy(o =>
        {
            int hour = o.CreatedAtUtc.Hour; // Or local hour
            if (hour >= 11 && hour < 16)
            {
                return "Service Midi (11h-16h)";
            }
            if (hour >= 18 && hour <= 23)
            {
                return "Service Soir (18h-24h)";
            }
            return "Continu / Hors Service";
        }).ToList();

        var services = new List<ServiceBreakdownDto>();
        foreach (var grp in serviceGroups)
        {
            decimal serviceTtc = grp.Sum(o => o.TotalTtc.AmountInCents) / 100.0m;
            decimal serviceHt = grp.Sum(o => o.TotalHt.AmountInCents) / 100.0m;
            int serviceOrders = grp.Count();
            int serviceCovers = grp.Sum(o => tableMap.TryGetValue(o.TableNumber, out var t) && t.CoversCount > 0 ? t.CoversCount : 1);
            decimal avgCover = serviceCovers > 0 ? Math.Round(serviceTtc / serviceCovers, 2, MidpointRounding.AwayFromZero) : 0;

            services.Add(new ServiceBreakdownDto(
                ServiceName: grp.Key,
                SalesTtc: serviceTtc,
                SalesHt: serviceHt,
                OrdersCount: serviceOrders,
                CoversCount: serviceCovers,
                AverageCoverTtc: avgCover
            ));
        }

        // If no orders classified, provide default empty buckets
        if (services.Count == 0)
        {
            services.Add(new ServiceBreakdownDto("Service Midi (11h-16h)", 0, 0, 0, 0, 0));
            services.Add(new ServiceBreakdownDto("Service Soir (18h-24h)", 0, 0, 0, 0, 0));
        }

        // 6. Top Products Sales
        var allItems = orders.SelectMany(o => o.Items).ToList();
        var productGroups = allItems
            .GroupBy(i => new { i.ProductId, i.ProductName })
            .Select(g =>
            {
                int quantity = g.Sum(x => x.Quantity);
                decimal ttc = g.Sum(x => x.CalculateTotalTtc().AmountInCents) / 100.0m;
                return new
                {
                    g.Key.ProductId,
                    g.Key.ProductName,
                    Quantity = quantity,
                    SalesTtc = ttc
                };
            })
            .OrderByDescending(p => p.SalesTtc)
            .Take(10)
            .ToList();

        var topProducts = productGroups.Select(p => new TopProductSaleDto(
            ProductId: p.ProductId,
            ProductName: p.ProductName,
            QuantitySold: p.Quantity,
            TotalSalesTtc: p.SalesTtc,
            PercentageOfTotal: totalSalesTtc > 0 ? Math.Round((p.SalesTtc / totalSalesTtc) * 100.0m, 1, MidpointRounding.AwayFromZero) : 0
        )).ToList();

        // 7. Staff Productivity
        var staffGroups = orders.GroupBy(o => o.OperatorId).ToList();
        var staffPerformance = new List<ServerProductivityDto>();

        foreach (var grp in staffGroups)
        {
            string serverName = "Serveur Inconnu";
            if (grp.Key != Guid.Empty && userMap.TryGetValue(grp.Key, out var user))
            {
                serverName = user.Name;
            }
            else
            {
                // Try to lookup waiter name from table
                var firstOrder = grp.FirstOrDefault();
                if (firstOrder != null && tableMap.TryGetValue(firstOrder.TableNumber, out var table) && !string.IsNullOrWhiteSpace(table.AssignedWaiterName))
                {
                    serverName = table.AssignedWaiterName;
                }
            }

            decimal staffTtc = grp.Sum(o => o.TotalTtc.AmountInCents) / 100.0m;
            int tablesCount = grp.Select(o => o.TableNumber).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            decimal avgTable = tablesCount > 0 ? Math.Round(staffTtc / tablesCount, 2, MidpointRounding.AwayFromZero) : 0;

            staffPerformance.Add(new ServerProductivityDto(
                ServerId: grp.Key != Guid.Empty ? grp.Key : null,
                ServerName: serverName,
                TablesServedCount: tablesCount,
                TotalSalesTtc: staffTtc,
                AverageTableTtc: avgTable
            ));
        }

        staffPerformance = staffPerformance.OrderByDescending(s => s.TotalSalesTtc).ToList();
        if (totalOrdersCount == 0)
        {
            staffPerformance.Clear();
        }

        // 8. Payment Methods Summary
        var allTenders = receipts.SelectMany(r => r.Tenders).ToList();
        var tenderGroups = allTenders.GroupBy(t => t.Method).ToList();
        decimal totalTendersAmount = allTenders.Sum(t => t.Amount.AmountInCents) / 100.0m;

        var paymentMethods = new List<PaymentMethodSummaryDto>();
        foreach (var grp in tenderGroups)
        {
            decimal grpAmount = grp.Sum(t => t.Amount.AmountInCents) / 100.0m;
            int count = grp.Count();
            string methodName = GetPaymentMethodName(grp.Key);
            decimal pct = totalTendersAmount > 0 ? Math.Round((grpAmount / totalTendersAmount) * 100.0m, 1, MidpointRounding.AwayFromZero) : 0;

            paymentMethods.Add(new PaymentMethodSummaryDto(
                Method: grp.Key,
                MethodName: methodName,
                TotalAmount: grpAmount,
                TransactionsCount: count,
                PercentageOfTotal: pct
            ));
        }

        paymentMethods = paymentMethods.OrderByDescending(p => p.TotalAmount).ToList();

        return new FinancialDashboardReportDto(
            Kpis: kpis,
            Services: services,
            TopProducts: topProducts,
            StaffPerformance: staffPerformance,
            PaymentMethods: paymentMethods,
            PeriodStartUtc: startUtc,
            PeriodEndUtc: endUtc
        );
    }

    private static string GetPaymentMethodName(PaymentMethod method)
    {
        return method switch
        {
            PaymentMethod.CreditCard => Texts.T("admin.payment_method_credit_card"),
            PaymentMethod.Cash => Texts.T("admin.payment_method_cash"),
            PaymentMethod.MealVoucher => Texts.T("admin.payment_method_meal_voucher"),
            PaymentMethod.RoomCharge => Texts.T("admin.payment_method_room_charge"),
            PaymentMethod.GiftCard => Texts.T("admin.payment_method_gift_card"),
            _ => Texts.T("admin.payment_method_other")
        };
    }
}
