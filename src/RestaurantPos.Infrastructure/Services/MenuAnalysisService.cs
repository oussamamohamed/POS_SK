using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public sealed class MenuAnalysisService : IMenuAnalysisService
{
    /// <summary>Un plat est « populaire » s'il pèse au moins 70 % de la part moyenne (règle de Kasavana-Smith).</summary>
    private const decimal PopularityFactor = 0.7m;

    private readonly AppDbContext _dbContext;
    private readonly IRecipeService _recipes;

    public MenuAnalysisService(AppDbContext dbContext, IRecipeService recipes)
    {
        _dbContext = dbContext;
        _recipes = recipes;
    }

    public async Task<MenuAnalysisReportDto> GetMenuAnalysisAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        string? categoryId = null,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var start = fromUtc ?? now.AddDays(-30);
        var end = toUtc ?? now;
        if (start > end)
        {
            (start, end) = (end, start);
        }

        // shortcut: commandes filtrées en mémoire (DateTimeOffset non traduisible par SQLite, comme le tableau de bord) ; à filtrer en SQL au-delà de quelques dizaines de milliers de commandes.
        var receipts = await _dbContext.FiscalReceipts.AsNoTracking()
            .Select(r => new { r.Id, r.OrderId, r.VoidedReceiptId })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var voidedReceiptIds = receipts.Where(r => r.VoidedReceiptId != null).Select(r => r.VoidedReceiptId!.Value).ToHashSet();
        var voidedOrderIds = receipts.Where(r => voidedReceiptIds.Contains(r.Id)).Select(r => r.OrderId).ToHashSet();

        var orders = (await _dbContext.Orders.AsNoTracking().Include(o => o.Items)
                .Where(o => o.Status == OrderStatus.Paid)
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .Where(o => o.CreatedAtUtc >= start && o.CreatedAtUtc <= end
                        && !voidedOrderIds.Contains(o.Id)
                        && o.GlobalDiscountType != DiscountType.Comp)
            .ToList();

        var sold = new Dictionary<Guid, (string Name, int Quantity, decimal RevenueHtCents)>();
        foreach (var order in orders)
        {
            long subtotal = order.Subtotal.AmountInCents;
            decimal scale = subtotal > 0 ? (decimal)order.TotalTtc.AmountInCents / subtotal : 0m;
            foreach (var item in order.Items.Where(i => !i.IsComp && i.Quantity > 0))
            {
                decimal ttcCents = item.CalculateTotalTtc().AmountInCents * scale;
                decimal htCents = ttcCents / (1m + (order.GetEffectiveTaxRate(item) / 100m));
                sold.TryGetValue(item.ProductId, out var acc);
                sold[item.ProductId] = (item.ProductName, acc.Quantity + item.Quantity, acc.RevenueHtCents + htCents);
            }
        }

        var productIds = sold.Keys.ToList();
        var products = (await _dbContext.Products.AsNoTracking()
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(p => p.Id);
        var categories = (await _dbContext.Categories.AsNoTracking().ToListAsync(cancellationToken).ConfigureAwait(false))
            .ToDictionary(c => c.Id, c => c.Name);

        // Filtre de famille : les classes se recalculent sur la sélection (seuils relatifs aux plats restants).
        if (!string.IsNullOrWhiteSpace(categoryId))
        {
            foreach (var id in sold.Keys.Where(id => !products.TryGetValue(id, out var p) || p.CategoryId != categoryId).ToList())
            {
                sold.Remove(id);
            }
        }

        // Le coût de la fiche technique prime sur le coût saisi à la main.
        var recipeCosts = await _recipes.ProductCostsCentsAsync(cancellationToken).ConfigureAwait(false);

        int totalQuantity = sold.Values.Sum(s => s.Quantity);
        decimal threshold = sold.Count > 0 ? PopularityFactor / sold.Count : 0m;

        var rows = sold.Select(kv =>
        {
            products.TryGetValue(kv.Key, out var product);
            decimal? unitCostCents = recipeCosts.TryGetValue(kv.Key, out var recipeCents) ? recipeCents : product?.Cost?.AmountInCents;
            decimal? marginCents = unitCostCents.HasValue ? kv.Value.RevenueHtCents - (unitCostCents.Value * kv.Value.Quantity) : null;
            return (Id: kv.Key, Product: product, kv.Value.Name, kv.Value.Quantity, kv.Value.RevenueHtCents, UnitCostCents: unitCostCents, MarginCents: marginCents);
        }).ToList();

        var costed = rows.Where(r => r.MarginCents.HasValue).ToList();
        int costedQuantity = costed.Sum(r => r.Quantity);
        decimal? averageUnitMarginCents = costedQuantity > 0 ? costed.Sum(r => r.MarginCents!.Value) / costedQuantity : null;

        var items = rows.Select(r =>
        {
            decimal share = totalQuantity > 0 ? (decimal)r.Quantity / totalQuantity : 0m;
            bool popular = share >= threshold;
            decimal? unitMarginCents = r.MarginCents.HasValue ? r.MarginCents.Value / r.Quantity : null;
            bool? profitable = unitMarginCents.HasValue && averageUnitMarginCents.HasValue
                ? unitMarginCents.Value >= averageUnitMarginCents.Value
                : null;
            MenuClass? menuClass = profitable.HasValue
                ? (popular ? (profitable.Value ? MenuClass.Star : MenuClass.Plowhorse) : (profitable.Value ? MenuClass.Puzzle : MenuClass.Dog))
                : null;
            string? categoryName = r.Product is not null && categories.TryGetValue(r.Product.CategoryId, out var cn) ? cn : null;
            return new MenuAnalysisItemDto(
                r.Id,
                r.Product?.Name ?? r.Name,
                categoryName,
                r.Quantity,
                Math.Round(share, 4),
                Euros(r.RevenueHtCents),
                r.UnitCostCents.HasValue ? Euros(r.UnitCostCents.Value) : null,
                unitMarginCents.HasValue ? Euros(unitMarginCents.Value) : null,
                r.MarginCents.HasValue ? Euros(r.MarginCents.Value) : null,
                popular,
                profitable,
                menuClass,
                !r.UnitCostCents.HasValue);
        }).OrderByDescending(i => i.Quantity).ThenBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        return new MenuAnalysisReportDto(
            start,
            end,
            Math.Round(threshold, 4),
            averageUnitMarginCents.HasValue ? Euros(averageUnitMarginCents.Value) : null,
            items.Count(i => i.MissingCost),
            items);
    }

    private static decimal Euros(decimal cents) => Math.Round(cents / 100m, 2, MidpointRounding.AwayFromZero);
}
