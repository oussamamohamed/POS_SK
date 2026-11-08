using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Recipes;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public sealed class RecipeService(AppDbContext db, Microsoft.Extensions.Caching.Memory.IMemoryCache? cache = null) : IRecipeService
{
    private static readonly string[] Units = ["g", "ml", "piece"];

    public async Task<IReadOnlyList<Ingredient>> ListIngredientsAsync(CancellationToken ct = default) =>
        (await db.Ingredients.AsNoTracking().ToListAsync(ct).ConfigureAwait(false))
            .OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public async Task<Ingredient> CreateIngredientAsync(IngredientInput input, CancellationToken ct = default)
    {
        var ingredient = new Ingredient { Name = input.Name.Trim(), Unit = input.Unit };
        await ApplyAsync(ingredient, input, ct).ConfigureAwait(false);
        db.Ingredients.Add(ingredient);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ingredient;
    }

    public async Task<Ingredient?> UpdateIngredientAsync(Guid id, IngredientInput input, CancellationToken ct = default)
    {
        var ingredient = await db.Ingredients.FirstOrDefaultAsync(i => i.Id == id, ct).ConfigureAwait(false);
        if (ingredient is null) return null;
        await ApplyAsync(ingredient, input, ct).ConfigureAwait(false);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ingredient;
    }

    public async Task<bool> DeleteIngredientAsync(Guid id, CancellationToken ct = default)
    {
        var ingredient = await db.Ingredients.FirstOrDefaultAsync(i => i.Id == id, ct).ConfigureAwait(false);
        if (ingredient is null) return false;

        // Un ingrédient qui a des mouvements ne se supprime pas : on le désactive pour garder l'historique.
        if (await db.StockMovements.AnyAsync(m => m.IngredientId == id, ct).ConfigureAwait(false)) throw new IngredientException("ingredient_has_movements");

        var ownerIds = await db.RecipeLines.Where(l => l.IngredientId == id).Select(l => new { l.OwnerKind, l.OwnerId }).ToListAsync(ct).ConfigureAwait(false);
        if (ownerIds.Count > 0)
        {
            var productIds = ownerIds.Where(o => o.OwnerKind == RecipeLine.OwnerProduct).Select(o => o.OwnerId).ToList();
            var names = await db.Products.Where(p => productIds.Contains(p.Id)).Select(p => p.Name).ToListAsync(ct).ConfigureAwait(false);
            var prepIds = ownerIds.Where(o => o.OwnerKind == RecipeLine.OwnerIngredient).Select(o => o.OwnerId).ToList();
            names.AddRange(await db.Ingredients.Where(i => prepIds.Contains(i.Id)).Select(i => i.Name).ToListAsync(ct).ConfigureAwait(false));
            throw new IngredientException("ingredient_in_use", names.Order().ToList());
        }

        // Sa propre fiche (préparation) disparaît avec elle.
        db.RecipeLines.RemoveRange(db.RecipeLines.Where(l => l.OwnerKind == RecipeLine.OwnerIngredient && l.OwnerId == id));
        db.Ingredients.Remove(ingredient);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<RecipeResult?> GetProductRecipeAsync(Guid productId, CancellationToken ct = default)
    {
        if (!await db.Products.AnyAsync(p => p.Id == productId, ct).ConfigureAwait(false)) return null;
        var (ingredients, preps) = await LoadAsync(ct).ConfigureAwait(false);
        var lines = await db.RecipeLines.AsNoTracking().Where(l => l.OwnerKind == RecipeLine.OwnerProduct && l.OwnerId == productId).ToListAsync(ct).ConfigureAwait(false);
        return ToResult(lines, RecipeCostCalculator.Compute(lines, ingredients, preps));
    }

    public async Task<RecipeResult?> SetProductRecipeAsync(Guid productId, IReadOnlyList<RecipeLineInput> lines, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct).ConfigureAwait(false);
        if (product is null) return null;
        CheckLines(lines);

        var (ingredients, preps) = await LoadAsync(ct).ConfigureAwait(false);
        var newLines = lines.Select(l => new RecipeLine { OwnerKind = RecipeLine.OwnerProduct, OwnerId = productId, IngredientId = l.IngredientId, QuantityMilli = l.QuantityMilli }).ToList();
        var cost = RecipeCostCalculator.Compute(newLines, ingredients, preps);

        db.RecipeLines.RemoveRange(db.RecipeLines.Where(l => l.OwnerKind == RecipeLine.OwnerProduct && l.OwnerId == productId));
        db.RecipeLines.AddRange(newLines);
        // Allergènes déduits de la fiche, sauf saisie manuelle (AllergensOverridden) ; une fiche vide ne déclare rien.
        if (newLines.Count > 0 && !product.AllergensOverridden)
        {
            product.AllergenMask = cost.AllergenMask;
            product.AllergensDeclared = true;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        cache?.Remove(BackOfficeCatalogService.CacheKeyProducts); // les allergènes du produit ont pu changer
        return ToResult(newLines, cost);
    }

    public async Task<IReadOnlyList<RecipeLineInput>?> GetPreparationRecipeAsync(Guid ingredientId, CancellationToken ct = default)
    {
        if (!await db.Ingredients.AnyAsync(i => i.Id == ingredientId && i.IsPreparation, ct).ConfigureAwait(false)) return null;
        var lines = await db.RecipeLines.AsNoTracking().Where(l => l.OwnerKind == RecipeLine.OwnerIngredient && l.OwnerId == ingredientId).ToListAsync(ct).ConfigureAwait(false);
        return lines.Select(l => new RecipeLineInput(l.IngredientId, l.QuantityMilli)).ToList();
    }

    public async Task<Ingredient?> SetPreparationRecipeAsync(Guid ingredientId, IReadOnlyList<RecipeLineInput> lines, CancellationToken ct = default)
    {
        var prep = await db.Ingredients.FirstOrDefaultAsync(i => i.Id == ingredientId && i.IsPreparation, ct).ConfigureAwait(false);
        if (prep is null) return null;
        CheckLines(lines);

        var (ingredients, preps) = await LoadAsync(ct).ConfigureAwait(false);
        var newLines = lines.Select(l => new RecipeLine { OwnerKind = RecipeLine.OwnerIngredient, OwnerId = ingredientId, IngredientId = l.IngredientId, QuantityMilli = l.QuantityMilli }).ToList();
        preps[ingredientId] = newLines;
        var unitCost = RecipeCostCalculator.PreparationUnitCost(ingredientId, ingredients, preps);

        db.RecipeLines.RemoveRange(db.RecipeLines.Where(l => l.OwnerKind == RecipeLine.OwnerIngredient && l.OwnerId == ingredientId));
        db.RecipeLines.AddRange(newLines);
        prep.UnitCostMilliCents = unitCost;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return prep;
    }

    public async Task<IReadOnlyDictionary<Guid, long>> ProductCostsCentsAsync(CancellationToken ct = default)
    {
        var all = await db.RecipeLines.AsNoTracking().Where(l => l.OwnerKind == RecipeLine.OwnerProduct).ToListAsync(ct).ConfigureAwait(false);
        if (all.Count == 0) return new Dictionary<Guid, long>();
        var (ingredients, preps) = await LoadAsync(ct).ConfigureAwait(false);
        var result = new Dictionary<Guid, long>();
        foreach (var group in all.GroupBy(l => l.OwnerId))
        {
            try { result[group.Key] = RecipeCostCalculator.Compute(group.ToList(), ingredients, preps).Cents; }
            catch (RecipeException) { /* fiche devenue invalide (ingrédient désactivé) : le plat retombe sur Product.Cost */ }
        }
        return result;
    }

    private static void CheckLines(IReadOnlyList<RecipeLineInput> lines)
    {
        if (lines.Any(l => l.QuantityMilli <= 0) || lines.Select(l => l.IngredientId).Distinct().Count() != lines.Count)
            throw new RecipeException("recipe_invalid");
    }

    private async Task ApplyAsync(Ingredient ingredient, IngredientInput input, CancellationToken ct)
    {
        var name = input.Name.Trim();
        if (name.Length is 0 or > 100 || !Units.Contains(input.Unit) || input.UnitCostMilliCents < 0
            || (input.IsPreparation && input.YieldMilli is null or <= 0)
            || !Allergens.IsValidMask(input.AllergenMask))
            throw new IngredientException("ingredient_invalid");
        // Unicité sans casse en mémoire (NOCASE de SQLite ignore les accents) ; « Crème » et « creme » restent distincts.
        var names = await db.Ingredients.Where(i => i.Id != ingredient.Id).Select(i => i.Name).ToListAsync(ct).ConfigureAwait(false);
        if (names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
            throw new IngredientException("ingredient_name_taken");
        if (ingredient.Unit != input.Unit && await db.RecipeLines.AnyAsync(l => l.IngredientId == ingredient.Id, ct).ConfigureAwait(false))
            throw new IngredientException("ingredient_in_use");

        ingredient.Name = name;
        ingredient.Unit = input.Unit;
        ingredient.IsPreparation = input.IsPreparation;
        ingredient.YieldMilli = input.IsPreparation ? input.YieldMilli : null;
        ingredient.UnitCostMilliCents = input.UnitCostMilliCents;
        ingredient.AllergenMask = input.AllergenMask;
        ingredient.LowStockMilli = input.LowStockMilli;
        ingredient.IsActive = input.IsActive;
    }

    private async Task<(Dictionary<Guid, Ingredient>, Dictionary<Guid, IReadOnlyCollection<RecipeLine>>)> LoadAsync(CancellationToken ct)
    {
        var ingredients = (await db.Ingredients.AsNoTracking().ToListAsync(ct).ConfigureAwait(false)).ToDictionary(i => i.Id);
        var preps = (await db.RecipeLines.AsNoTracking().Where(l => l.OwnerKind == RecipeLine.OwnerIngredient).ToListAsync(ct).ConfigureAwait(false))
            .GroupBy(l => l.OwnerId).ToDictionary(g => g.Key, g => (IReadOnlyCollection<RecipeLine>)g.ToList());
        return (ingredients, preps);
    }

    private static RecipeResult ToResult(IEnumerable<RecipeLine> lines, RecipeCost cost) =>
        new(lines.Select(l => new RecipeLineInput(l.IngredientId, l.QuantityMilli)).ToList(), cost.Cents, cost.AllergenMask);
}
