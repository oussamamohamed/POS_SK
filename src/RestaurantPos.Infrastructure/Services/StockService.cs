using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Recipes;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public sealed class StockService(AppDbContext db, Microsoft.Extensions.Caching.Memory.IMemoryCache? cache = null) : IStockService
{
    private static readonly TimeSpan RecentCounts = TimeSpan.FromDays(7);

    public async Task<IReadOnlyDictionary<Guid, long>> LevelsAsync(CancellationToken ct = default) =>
        (await db.StockMovements.AsNoTracking().Select(m => new { m.IngredientId, m.QuantityMilli }).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(m => m.IngredientId).ToDictionary(g => g.Key, g => g.Sum(m => m.QuantityMilli));

    private async Task<long> LevelAsync(Guid ingredientId, CancellationToken ct) =>
        (await db.StockMovements.Where(m => m.IngredientId == ingredientId).Select(m => m.QuantityMilli).ToListAsync(ct).ConfigureAwait(false)).Sum();

    private async Task<Ingredient?> TrackedAsync(Guid id, CancellationToken ct)
    {
        var ingredient = await db.Ingredients.FirstOrDefaultAsync(i => i.Id == id, ct).ConfigureAwait(false);
        if (ingredient is { IsPreparation: true }) throw new IngredientException("ingredient_invalid"); // seuls les ingrédients bruts sont suivis
        return ingredient;
    }

    public async Task<StockMovement?> ReceiveAsync(Guid ingredientId, long quantityMilli, long unitCostMilliCents, string? note, Guid? operatorId, CancellationToken ct = default)
    {
        if (quantityMilli <= 0 || unitCostMilliCents < 0) throw new IngredientException("ingredient_invalid");
        return await db.ExecuteInTransactionAsync(async txCt =>
        {
            var ingredient = await TrackedAsync(ingredientId, txCt).ConfigureAwait(false);
            if (ingredient is null) return null;
            var stock = await LevelAsync(ingredientId, txCt).ConfigureAwait(false);
            // Prix moyen pondéré ; un stock nul ou négatif repart du prix de la réception.
            ingredient.UnitCostMilliCents = stock > 0
                ? (long)(((Int128)stock * ingredient.UnitCostMilliCents + (Int128)quantityMilli * unitCostMilliCents + (stock + quantityMilli) / 2) / (stock + quantityMilli))
                : unitCostMilliCents;
            var movement = new StockMovement { IngredientId = ingredientId, Kind = StockMovementKinds.Receipt, QuantityMilli = quantityMilli, UnitCostMilliCents = unitCostMilliCents, Note = note, OperatorId = operatorId };
            db.StockMovements.Add(movement);
            await db.SaveChangesAsync(txCt).ConfigureAwait(false);
            return movement;
        }, System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);
    }

    public async Task<CountResult?> CountAsync(Guid ingredientId, long countedMilli, string? note, Guid? operatorId, CancellationToken ct = default)
    {
        if (countedMilli < 0) throw new IngredientException("ingredient_invalid");
        return await db.ExecuteInTransactionAsync(async txCt =>
        {
            var ingredient = await TrackedAsync(ingredientId, txCt).ConfigureAwait(false);
            if (ingredient is null) return null;
            var variance = countedMilli - await LevelAsync(ingredientId, txCt).ConfigureAwait(false);
            if (variance != 0)
            {
                db.StockMovements.Add(new StockMovement { IngredientId = ingredientId, Kind = StockMovementKinds.Count, QuantityMilli = variance, UnitCostMilliCents = ingredient.UnitCostMilliCents, Note = note, OperatorId = operatorId });
                await db.SaveChangesAsync(txCt).ConfigureAwait(false);
            }
            return new CountResult(variance, ValueCents(variance, ingredient.UnitCostMilliCents));
        }, System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);
    }

    public async Task<StockMovement?> LossAsync(Guid ingredientId, long quantityMilli, string? note, Guid? operatorId, CancellationToken ct = default)
    {
        if (quantityMilli <= 0) throw new IngredientException("ingredient_invalid");
        return await db.ExecuteInTransactionAsync(async txCt =>
        {
            var ingredient = await TrackedAsync(ingredientId, txCt).ConfigureAwait(false);
            if (ingredient is null) return null;
            var movement = new StockMovement { IngredientId = ingredientId, Kind = StockMovementKinds.Loss, QuantityMilli = -quantityMilli, UnitCostMilliCents = ingredient.UnitCostMilliCents, Note = note, OperatorId = operatorId };
            db.StockMovements.Add(movement);
            await db.SaveChangesAsync(txCt).ConfigureAwait(false);
            return movement;
        }, System.Data.IsolationLevel.Serializable, ct).ConfigureAwait(false);
    }

    /// <summary>Valeur (centimes, demi supérieur en valeur absolue) d'une quantité à un prix en milli-centimes.</summary>
    private static long ValueCents(long quantityMilli, long unitCostMilliCents)
    {
        var product = (Int128)quantityMilli * unitCostMilliCents;
        var magnitude = ((product < 0 ? -product : product) + 500_000) / 1_000_000;
        return (long)(product < 0 ? -magnitude : magnitude);
    }

    public async Task<IReadOnlyList<StockMovement>> MovementsAsync(Guid? ingredientId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default)
    {
        var all = await db.StockMovements.AsNoTracking().Where(m => ingredientId == null || m.IngredientId == ingredientId).ToListAsync(ct).ConfigureAwait(false);
        // DateTimeOffset non traduisible par SQLite : filtre en mémoire.
        return all.Where(m => (fromUtc is null || m.CreatedAtUtc >= fromUtc) && (toUtc is null || m.CreatedAtUtc <= toUtc)).OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id).ToList();
    }

    public async Task<StockAlerts> AlertsAsync(CancellationToken ct = default)
    {
        var levels = await LevelsAsync(ct).ConfigureAwait(false);
        var ingredients = await db.Ingredients.AsNoTracking().Where(i => !i.IsPreparation && i.IsActive).ToListAsync(ct).ConfigureAwait(false);
        var low = ingredients.Where(i => i.LowStockMilli.HasValue && levels.GetValueOrDefault(i.Id) <= i.LowStockMilli.Value)
            .Select(i => new LowStockAlert(i.Id, i.Name, i.Unit, levels.GetValueOrDefault(i.Id), i.LowStockMilli!.Value))
            .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        var since = DateTimeOffset.UtcNow - RecentCounts;
        var byId = ingredients.ToDictionary(i => i.Id);
        var counts = (await db.StockMovements.AsNoTracking().Where(m => m.Kind == StockMovementKinds.Count).ToListAsync(ct).ConfigureAwait(false))
            .Where(m => m.CreatedAtUtc >= since && byId.ContainsKey(m.IngredientId))
            .OrderByDescending(m => m.CreatedAtUtc)
            .Select(m => new CountVarianceAlert(m.IngredientId, byId[m.IngredientId].Name, byId[m.IngredientId].Unit, m.QuantityMilli, ValueCents(m.QuantityMilli, m.UnitCostMilliCents ?? 0), m.CreatedAtUtc))
            .ToList();
        return new StockAlerts(low, counts);
    }

    public async Task<IReadOnlyList<(Guid ProductId, bool IsAvailable)>> RefreshAutoAvailabilityAsync(CancellationToken ct = default)
    {
        var settings = await db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        var enabled = settings?.AutoUnavailableOnStockOut == true;
        var changes = new List<(Guid, bool)>();

        var auto = await db.Products.Where(p => p.AutoUnavailable).ToListAsync(ct).ConfigureAwait(false);
        if (!enabled && auto.Count == 0) return changes;

        var recipeLines = (await db.RecipeLines.AsNoTracking().Where(l => l.OwnerKind == RecipeLine.OwnerProduct).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.OwnerId).ToDictionary(g => g.Key, g => g.ToList());
        if (recipeLines.Count == 0 && auto.Count == 0) return changes;

        var ingredients = (await db.Ingredients.AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).ToDictionary(i => i.Id);
        var preps = (await db.RecipeLines.AsNoTracking().Where(l => l.OwnerKind == RecipeLine.OwnerIngredient).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.OwnerId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<RecipeLine>)g.ToList());
        var movements = await db.StockMovements.AsNoTracking().Select(m => new { m.IngredientId, m.QuantityMilli }).ToListAsync(ct).ConfigureAwait(false);
        var levels = movements.GroupBy(m => m.IngredientId).ToDictionary(g => g.Key, g => g.Sum(m => m.QuantityMilli));

        // Épuisé : suivi (au moins un mouvement) et stock nul ou négatif. Un ingrédient jamais reçu n'éteint rien.
        bool SoldOut(Guid productId) => recipeLines.TryGetValue(productId, out var lines)
            && RecipeExpander.ToRaw(lines, 1, ingredients, preps).Keys.Any(i => levels.TryGetValue(i, out var level) && level <= 0);

        var candidates = await db.Products.Where(p => p.IsAvailable || p.AutoUnavailable).ToListAsync(ct).ConfigureAwait(false);
        foreach (var product in candidates)
        {
            var soldOut = enabled && SoldOut(product.Id);
            if (product.IsAvailable && soldOut) { product.IsAvailable = false; product.AutoUnavailable = true; changes.Add((product.Id, false)); }
            else if (!product.IsAvailable && product.AutoUnavailable && !soldOut) { product.IsAvailable = true; product.AutoUnavailable = false; changes.Add((product.Id, true)); }
        }
        if (changes.Count > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            cache?.Remove(BackOfficeCatalogService.CacheKeyProducts); // la disponibilité est servie par le catalogue en cache
        }
        return changes;
    }

    public async Task<StockSettings> GetSettingsAsync(CancellationToken ct = default)
    {
        var s = await db.RestaurantSettings.AsNoTracking().FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return new StockSettings(s?.AutoUnavailableOnStockOut ?? false, s?.PrintAllergensOnKitchenTicket ?? false, s?.AllergenDisclaimer);
    }

    public async Task<StockSettings> UpdateSettingsAsync(StockSettings settings, CancellationToken ct = default)
    {
        var row = await db.RestaurantSettings.FindAsync([RestaurantSettings.SingletonId], ct).ConfigureAwait(false);
        if (row is null)
        {
            row = new RestaurantSettings { ReceiptLanguage = "fr", KitchenTicketLanguage = "fr" };
            db.RestaurantSettings.Add(row);
        }
        row.AutoUnavailableOnStockOut = settings.AutoUnavailableOnStockOut;
        row.PrintAllergensOnKitchenTicket = settings.PrintAllergensOnKitchenTicket;
        row.AllergenDisclaimer = string.IsNullOrWhiteSpace(settings.AllergenDisclaimer) ? null : settings.AllergenDisclaimer.Trim();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return await GetSettingsAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ModifierOptionConsumption>> ModifierConsumptionAsync(Guid optionId, CancellationToken ct = default) =>
        await db.ModifierOptionConsumptions.AsNoTracking().Where(c => c.OptionId == optionId).ToListAsync(ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<ModifierOptionConsumption>?> SetModifierConsumptionAsync(Guid optionId, IReadOnlyList<(Guid IngredientId, long DeltaQuantityMilli)> items, CancellationToken ct = default)
    {
        if (!await db.ModifierOptions.AnyAsync(o => o.Id == optionId, ct).ConfigureAwait(false)) return null;
        var known = (await db.Ingredients.Where(i => !i.IsPreparation).Select(i => i.Id).ToListAsync(ct).ConfigureAwait(false)).ToHashSet();
        if (items.Any(i => i.DeltaQuantityMilli == 0 || !known.Contains(i.IngredientId)) || items.Select(i => i.IngredientId).Distinct().Count() != items.Count)
            throw new IngredientException("ingredient_invalid");
        db.ModifierOptionConsumptions.RemoveRange(db.ModifierOptionConsumptions.Where(c => c.OptionId == optionId));
        var rows = items.Select(i => new ModifierOptionConsumption { OptionId = optionId, IngredientId = i.IngredientId, DeltaQuantityMilli = i.DeltaQuantityMilli }).ToList();
        db.ModifierOptionConsumptions.AddRange(rows);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return rows;
    }
}
