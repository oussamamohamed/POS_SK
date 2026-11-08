using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

public static class StockMovementKinds
{
    public const string Sale = "sale";
    public const string SaleReversal = "saleReversal";
    public const string Receipt = "receipt";
    public const string Count = "count";
    public const string Loss = "loss";
}

/// <summary>Mouvement de stock, en ajout seulement (déclencheurs SQL : aucune modification ni suppression). Le stock d'un ingrédient est la somme de ses mouvements.</summary>
public class StockMovement
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid IngredientId { get; set; }
    public required string Kind { get; set; }
    /// <summary>Signé, en millièmes d'unité de base : négatif pour une sortie.</summary>
    public long QuantityMilli { get; set; }
    /// <summary>Prix d'une réception, ou valorisation d'un écart d'inventaire (milli-centimes par unité de base).</summary>
    public long? UnitCostMilliCents { get; set; }
    public Guid? OrderId { get; set; }
    public Guid? OperatorId { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Quantité ajoutée (ou retirée si négative) pour chaque vente d'un produit avec cette option de modificateur.</summary>
public class ModifierOptionConsumption
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public Guid OptionId { get; set; }
    public Guid IngredientId { get; set; }
    public long DeltaQuantityMilli { get; set; }
}
