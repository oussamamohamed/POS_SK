using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Domain.Recipes;

/// <summary>Refus d'une fiche technique (<see cref="Code"/> : <c>recipe_cycle</c>, <c>recipe_depth</c>, <c>ingredient_inactive</c>, <c>ingredient_unknown</c>).</summary>
public sealed class RecipeException(string code) : InvalidOperationException(code)
{
    public string Code { get; } = code;
}

/// <summary>Coût d'une fiche : centimes arrondis une seule fois, et allergènes déduits des ingrédients.</summary>
public readonly record struct RecipeCost(long Cents, int AllergenMask);

/// <summary>
/// Coût de revient d'une fiche, en entiers (miroir Swift : RecipeCost.swift, rejoué sur recipe_cost_reference.json).
/// Quantités en millièmes d'unité de base, prix en milli-centimes : Σ quantité × prix vaut des milli-millièmes de milli-centime,
/// donc centimes = Σ / 1 000 000, arrondi une seule fois au demi supérieur. Le prix d'une préparation est son coût divisé par
/// son rendement, arrondi au demi supérieur en milli-centimes par unité de base.
/// </summary>
public static class RecipeCostCalculator
{
    public const int MaxDepth = 5;
    private const long CentsDivisor = 1_000_000;

    /// <param name="lines">Lignes de la fiche à chiffrer (un produit ou une préparation).</param>
    /// <param name="ingredients">Tous les ingrédients, par identifiant.</param>
    /// <param name="preparationLines">Fiche de chaque préparation, par identifiant de préparation.</param>
    public static RecipeCost Compute(
        IReadOnlyCollection<RecipeLine> lines,
        IReadOnlyDictionary<Guid, Ingredient> ingredients,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<RecipeLine>> preparationLines)
    {
        var (sum, mask) = Sum(lines, ingredients, preparationLines, [], [], 1);
        return new RecipeCost((long)((sum + CentsDivisor / 2) / CentsDivisor), mask);
    }

    /// <summary>Prix par unité de base (milli-centimes) d'une préparation, recalculé depuis sa fiche.</summary>
    public static long PreparationUnitCost(
        Guid preparationId,
        IReadOnlyDictionary<Guid, Ingredient> ingredients,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<RecipeLine>> preparationLines)
    {
        var prep = ingredients.GetValueOrDefault(preparationId) ?? throw new RecipeException("ingredient_unknown");
        var lines = preparationLines.GetValueOrDefault(preparationId) ?? [];
        var (sum, _) = Sum(lines, ingredients, preparationLines, [], [preparationId], 2);
        return UnitCostOf(sum, prep);
    }

    private static long UnitCostOf(Int128 sum, Ingredient prep)
    {
        var yield = prep.YieldMilli.GetValueOrDefault();
        if (yield <= 0) throw new RecipeException("ingredient_unknown");
        return (long)((sum + yield / 2) / yield);
    }

    private static (Int128 Sum, int Mask) Sum(
        IReadOnlyCollection<RecipeLine> lines,
        IReadOnlyDictionary<Guid, Ingredient> ingredients,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<RecipeLine>> preparationLines,
        Dictionary<Guid, (long UnitCost, int Mask)> memo,
        HashSet<Guid> path,
        int depth)
    {
        if (depth > MaxDepth) throw new RecipeException("recipe_depth");
        Int128 sum = 0;
        var mask = 0;
        foreach (var line in lines)
        {
            var ingredient = ingredients.GetValueOrDefault(line.IngredientId) ?? throw new RecipeException("ingredient_unknown");
            if (!ingredient.IsActive) throw new RecipeException("ingredient_inactive");

            long unitCost;
            if (ingredient.IsPreparation)
            {
                if (path.Contains(ingredient.Id)) throw new RecipeException("recipe_cycle");
                if (!memo.TryGetValue(ingredient.Id, out var known))
                {
                    path.Add(ingredient.Id);
                    var (inner, innerMask) = Sum(preparationLines.GetValueOrDefault(ingredient.Id) ?? [], ingredients, preparationLines, memo, path, depth + 1);
                    path.Remove(ingredient.Id);
                    known = (UnitCostOf(inner, ingredient), innerMask);
                    memo[ingredient.Id] = known;
                }
                unitCost = known.UnitCost;
                mask |= known.Mask;
            }
            else
            {
                unitCost = ingredient.UnitCostMilliCents;
                mask |= ingredient.AllergenMask;
            }
            sum += (Int128)line.QuantityMilli * unitCost;
        }
        return (sum, mask);
    }
}
