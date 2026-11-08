using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public sealed record CountResult(long VarianceMilli, long ValueCents);

public sealed record LowStockAlert(Guid IngredientId, string Name, string Unit, long StockMilli, long LowStockMilli);

public sealed record CountVarianceAlert(Guid IngredientId, string Name, string Unit, long VarianceMilli, long ValueCents, DateTimeOffset CreatedAtUtc);

public sealed record StockAlerts(IReadOnlyList<LowStockAlert> LowStock, IReadOnlyList<CountVarianceAlert> RecentCounts);

public sealed record StockSettings(bool AutoUnavailableOnStockOut, bool PrintAllergensOnKitchenTicket, string? AllergenDisclaimer);

/// <summary>Stock : réceptions, inventaires, pertes, historique, alertes et rupture automatique. La consommation à la vente est <c>StockConsumption</c> (dans la transaction de règlement).</summary>
public interface IStockService
{
    Task<IReadOnlyDictionary<Guid, long>> LevelsAsync(CancellationToken ct = default);

    /// <summary>Réception (quantité &gt; 0, prix d'achat ≥ 0) : prix moyen pondéré reporté sur l'ingrédient. <c>null</c> si l'ingrédient est inconnu ; <c>IngredientException("ingredient_invalid")</c> sinon.</summary>
    Task<StockMovement?> ReceiveAsync(Guid ingredientId, long quantityMilli, long unitCostMilliCents, string? note, Guid? operatorId, CancellationToken ct = default);

    /// <summary>Inventaire : mouvement égal à l'écart entre le comptage et le stock théorique (aucun mouvement si l'écart est nul).</summary>
    Task<CountResult?> CountAsync(Guid ingredientId, long countedMilli, string? note, Guid? operatorId, CancellationToken ct = default);

    Task<StockMovement?> LossAsync(Guid ingredientId, long quantityMilli, string? note, Guid? operatorId, CancellationToken ct = default);

    Task<IReadOnlyList<StockMovement>> MovementsAsync(Guid? ingredientId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, CancellationToken ct = default);

    Task<StockAlerts> AlertsAsync(CancellationToken ct = default);

    /// <summary>Applique la rupture automatique (option active) et la lève là où le stock est revenu ; renvoie les produits dont la disponibilité a changé.</summary>
    Task<IReadOnlyList<(Guid ProductId, bool IsAvailable)>> RefreshAutoAvailabilityAsync(CancellationToken ct = default);

    Task<StockSettings> GetSettingsAsync(CancellationToken ct = default);
    Task<StockSettings> UpdateSettingsAsync(StockSettings settings, CancellationToken ct = default);

    Task<IReadOnlyList<ModifierOptionConsumption>> ModifierConsumptionAsync(Guid optionId, CancellationToken ct = default);
    /// <summary>Remplace les ajustements d'une option ; <c>null</c> si l'option est inconnue.</summary>
    Task<IReadOnlyList<ModifierOptionConsumption>?> SetModifierConsumptionAsync(Guid optionId, IReadOnlyList<(Guid IngredientId, long DeltaQuantityMilli)> items, CancellationToken ct = default);
}
