using System;
using RestaurantPos.Domain.Common;

namespace RestaurantPos.Domain.Entities;

/// <summary>Matière première ou préparation (sous-recette) d'une fiche technique. Quantités en millièmes d'unité de base, prix en milli-centimes.</summary>
public class Ingredient
{
    public Guid Id { get; init; } = UuidV7.NewGuid();
    public required string Name { get; set; }
    /// <summary><c>g</c>, <c>ml</c> ou <c>piece</c> ; immuable dès qu'un mouvement de stock existe.</summary>
    public required string Unit { get; set; }
    /// <summary>Prix d'achat HT par unité de base, en milli-centimes ; pour une préparation, recalculé depuis sa fiche.</summary>
    public long UnitCostMilliCents { get; set; }
    public bool IsPreparation { get; set; }
    /// <summary>Rendement d'une préparation, en millièmes d'unité de base (&gt; 0).</summary>
    public long? YieldMilli { get; set; }
    public int AllergenMask { get; set; }
    public long? LowStockMilli { get; set; }
    public bool IsActive { get; set; } = true;
}
