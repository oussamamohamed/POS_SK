using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Recipes;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

/// <summary>
/// Consommation du stock à l'encaissement soldé (miroir Swift : LocalStockRepository+Consumption). Appelée dans la transaction du
/// règlement, avant son <c>SaveChanges</c> ; ne lève jamais : une panne de calcul laisse la vente intacte et le stock inchangé.
/// Un seul mouvement <c>sale</c> par (commande, ingrédient), donc idempotent ; les lignes offertes ou remisées sont consommées.
/// </summary>
public static class StockConsumption
{
    public static async Task RecordSaleAsync(AppDbContext db, Order order, Guid? operatorId, CancellationToken ct)
    {
        List<StockMovement> movements;
        try
        {
            if (await db.StockMovements.AnyAsync(m => m.OrderId == order.Id && m.Kind == StockMovementKinds.Sale, ct).ConfigureAwait(false)) return;
            movements = await ComputeAsync(db, order, operatorId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return;
        }
        db.StockMovements.AddRange(movements);
    }

    /// <summary>Restitue la vente d'une commande annulée par avoir (seulement si le responsable l'a demandé).</summary>
    public static async Task RestoreSaleAsync(AppDbContext db, Guid orderId, Guid? operatorId, CancellationToken ct)
    {
        try
        {
            var existing = await db.StockMovements.AsNoTracking().Where(m => m.OrderId == orderId).ToListAsync(ct).ConfigureAwait(false);
            if (existing.Any(m => m.Kind == StockMovementKinds.SaleReversal)) return;
            foreach (var sale in existing.Where(m => m.Kind == StockMovementKinds.Sale))
            {
                db.StockMovements.Add(new StockMovement
                {
                    IngredientId = sale.IngredientId,
                    Kind = StockMovementKinds.SaleReversal,
                    QuantityMilli = -sale.QuantityMilli,
                    OrderId = orderId,
                    OperatorId = operatorId,
                });
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // ne bloque pas l'avoir
        }
    }

    private static async Task<List<StockMovement>> ComputeAsync(AppDbContext db, Order order, Guid? operatorId, CancellationToken ct)
    {
        var productIds = order.Items.Where(i => i.Quantity > 0).Select(i => i.ProductId).Distinct().ToList();
        var productLines = (await db.RecipeLines.AsNoTracking().Where(l => l.OwnerKind == RecipeLine.OwnerProduct && productIds.Contains(l.OwnerId)).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.OwnerId).ToDictionary(g => g.Key, g => g.ToList());
        var ingredients = (await db.Ingredients.AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).ToDictionary(i => i.Id);
        var preps = (await db.RecipeLines.AsNoTracking().Where(l => l.OwnerKind == RecipeLine.OwnerIngredient).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.OwnerId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<RecipeLine>)g.ToList());

        var groups = await db.ModifierGroups.AsNoTracking().Include(g => g.Options).Where(g => productIds.Contains(g.ProductId)).ToListAsync(ct).ConfigureAwait(false);
        var consumptions = await db.ModifierOptionConsumptions.AsNoTracking().ToListAsync(ct).ConfigureAwait(false);

        var total = new Dictionary<Guid, long>();
        foreach (var item in order.Items.Where(i => i.Quantity > 0))
        {
            if (productLines.TryGetValue(item.ProductId, out var lines))
            {
                foreach (var (id, qty) in RecipeExpander.ToRaw(lines, item.Quantity, ingredients, preps)) total[id] = total.GetValueOrDefault(id) + qty;
            }
            foreach (var name in item.SelectedModifiers)
            {
                foreach (var option in groups.Where(g => g.ProductId == item.ProductId).SelectMany(g => g.Options).Where(o => o.Name == name))
                {
                    foreach (var c in consumptions.Where(c => c.OptionId == option.Id)) total[c.IngredientId] = total.GetValueOrDefault(c.IngredientId) + c.DeltaQuantityMilli * item.Quantity;
                }
            }
        }

        return total.Where(t => t.Value != 0 && ingredients.ContainsKey(t.Key)).OrderBy(t => t.Key)
            .Select(t => new StockMovement { IngredientId = t.Key, Kind = StockMovementKinds.Sale, QuantityMilli = -t.Value, OrderId = order.Id, OperatorId = operatorId })
            .ToList();
    }
}
