using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public sealed record RecipeLineInput(Guid IngredientId, long QuantityMilli);

public sealed record IngredientInput(string Name, string Unit, long UnitCostMilliCents, bool IsPreparation, long? YieldMilli, int AllergenMask, long? LowStockMilli, bool IsActive);

public sealed record RecipeResult(IReadOnlyList<RecipeLineInput> Lines, long CostCents, int AllergenMask);

/// <summary>Refus métier d'un ingrédient (<c>ingredient_in_use</c>, <c>ingredient_name_taken</c>, <c>ingredient_invalid</c>, <c>ingredient_not_found</c>).</summary>
public sealed class IngredientException(string code, IReadOnlyList<string>? products = null) : InvalidOperationException(code)
{
    public string Code { get; } = code;
    public IReadOnlyList<string> Products { get; } = products ?? [];
}

public interface IRecipeService
{
    Task<IReadOnlyList<Ingredient>> ListIngredientsAsync(CancellationToken ct = default);
    Task<Ingredient> CreateIngredientAsync(IngredientInput input, CancellationToken ct = default);
    Task<Ingredient?> UpdateIngredientAsync(Guid id, IngredientInput input, CancellationToken ct = default);
    Task<bool> DeleteIngredientAsync(Guid id, CancellationToken ct = default);

    /// <summary>Fiche d'un produit ; <c>null</c> si le produit n'existe pas.</summary>
    Task<RecipeResult?> GetProductRecipeAsync(Guid productId, CancellationToken ct = default);

    /// <summary>Remplace la fiche (vide = suppression). Lève <c>RecipeException</c> (cycle, profondeur, ingrédient inactif ou inconnu) ; <c>null</c> si le produit n'existe pas.</summary>
    Task<RecipeResult?> SetProductRecipeAsync(Guid productId, IReadOnlyList<RecipeLineInput> lines, CancellationToken ct = default);

    /// <summary>Fiche d'une préparation (coût = prix par unité de base, en milli-centimes) ; <c>null</c> si ce n'est pas une préparation.</summary>
    Task<IReadOnlyList<RecipeLineInput>?> GetPreparationRecipeAsync(Guid ingredientId, CancellationToken ct = default);

    /// <summary>Remplace la fiche d'une préparation et recalcule son prix. Mêmes refus que <see cref="SetProductRecipeAsync"/> ; <c>null</c> si ce n'est pas une préparation.</summary>
    Task<Ingredient?> SetPreparationRecipeAsync(Guid ingredientId, IReadOnlyList<RecipeLineInput> lines, CancellationToken ct = default);

    /// <summary>Coût de revient en centimes de chaque produit ayant une fiche.</summary>
    Task<IReadOnlyDictionary<Guid, long>> ProductCostsCentsAsync(CancellationToken ct = default);
}
