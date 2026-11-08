using System;
using System.Collections.Generic;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Domain.Recipes;

/// <summary>
/// Ramène une fiche aux ingrédients bruts (miroir Swift : RecipeExpander.swift). Une préparation de quantité Q donne,
/// pour chaque ligne interne de quantité qi, Q × qi / rendement arrondi au demi supérieur ; la récursion s'arrête à la profondeur
/// <see cref="RecipeCostCalculator.MaxDepth"/>. Les préparations ne sont pas suivies en stock : seuls les ingrédients bruts le sont.
/// </summary>
public static class RecipeExpander
{
    public static Dictionary<Guid, long> ToRaw(
        IEnumerable<RecipeLine> lines,
        long multiplier,
        IReadOnlyDictionary<Guid, Ingredient> ingredients,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<RecipeLine>> preparationLines)
    {
        var raw = new Dictionary<Guid, long>();
        foreach (var line in lines) Add(raw, line.IngredientId, line.QuantityMilli * multiplier, ingredients, preparationLines, 1);
        return raw;
    }

    private static void Add(
        Dictionary<Guid, long> raw, Guid ingredientId, long quantity,
        IReadOnlyDictionary<Guid, Ingredient> ingredients,
        IReadOnlyDictionary<Guid, IReadOnlyCollection<RecipeLine>> preparationLines, int depth)
    {
        if (!ingredients.TryGetValue(ingredientId, out var ingredient)) return;
        if (!ingredient.IsPreparation)
        {
            raw[ingredientId] = raw.GetValueOrDefault(ingredientId) + quantity;
            return;
        }
        var yield = ingredient.YieldMilli.GetValueOrDefault();
        if (depth >= RecipeCostCalculator.MaxDepth || yield <= 0 || !preparationLines.TryGetValue(ingredientId, out var inner)) return;
        foreach (var l in inner)
        {
            var share = (long)(((Int128)quantity * l.QuantityMilli + yield / 2) / yield);
            Add(raw, l.IngredientId, share, ingredients, preparationLines, depth + 1);
        }
    }
}
