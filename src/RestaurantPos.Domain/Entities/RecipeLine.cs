using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

/// <summary>Un composant d'une fiche technique (d'un produit ou d'une préparation).</summary>
public class RecipeLine
{
    public const string OwnerProduct = "product";
    public const string OwnerIngredient = "ingredient";

    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string OwnerKind { get; set; }
    public Guid OwnerId { get; set; }
    public Guid IngredientId { get; set; }
    /// <summary>Quantité (&gt; 0) en millièmes d'unité de base.</summary>
    public long QuantityMilli { get; set; }
}
