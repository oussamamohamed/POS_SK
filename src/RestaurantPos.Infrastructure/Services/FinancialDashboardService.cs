using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public sealed class FinancialDashboardService : IFinancialDashboardService
{
    private readonly AppDbContext _dbContext;

    public FinancialDashboardService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
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

        // 1. Fetch Orders and related data in memory
        var allOrders = await _dbContext.Orders
            .Include(o => o.Items)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var orders = allOrders
            .Where(o => o.CreatedAtUtc >= startUtc && o.CreatedAtUtc <= endUtc && o.Status != OrderStatus.Cancelled)
            .ToList();

        // 2. Fetch Fiscal Receipts with Tenders
        var allReceipts = await _dbContext.FiscalReceipts
            .Include(r => r.Tenders)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var receipts = allReceipts
            .Where(r => r.CreatedAtUtc >= startUtc && r.CreatedAtUtc <= endUtc && !r.IsVoid)
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

        int totalOrdersCount = orders.Count > 0 ? orders.Count : receipts.Count;

        int totalCoversCount = orders.Sum(o =>
        {
            if (tableMap.TryGetValue(o.TableNumber, out var table) && table.CoversCount > 0)
            {
                return table.CoversCount;
            }
            return 1;
        });

        if (totalCoversCount == 0 && totalOrdersCount > 0)
        {
            totalCoversCount = totalOrdersCount;
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
            PaymentMethod.CreditCard => "Carte Bancaire (CB)",
            PaymentMethod.Cash => "Espèces",
            PaymentMethod.MealVoucher => "Titres Restaurant",
            PaymentMethod.RoomCharge => "Note de Chambre (PMS)",
            PaymentMethod.GiftCard => "Carte Cadeau",
            _ => "Autre Règlement"
        };
    }
}
