using System.Collections.Generic;
using System.Linq;

namespace RestaurantPos.Domain.Common;

/// <summary>Postes de préparation (liste fixe). RECEIPT désigne les imprimantes de caisse, jamais une ligne de commande.</summary>
public static class PreparationStations
{
    public const string HotKitchen = "HOT_KITCHEN";
    public const string Receipt = "RECEIPT";
    public static readonly IReadOnlyList<string> Kitchen = [HotKitchen, "COLD", "GRILL", "DESSERT", "BAR"];

    public static string? Normalize(string? id) => string.IsNullOrWhiteSpace(id) ? null : id.Trim();

    public static bool IsKitchenStationOrEmpty(string? id) => Normalize(id) is not { } s || Kitchen.Contains(s);

    /// <summary>Ligne → article → famille → cuisine chaude.</summary>
    public static string Resolve(string? item, string? product, string? category) =>
        Normalize(item) ?? Normalize(product) ?? Normalize(category) ?? HotKitchen;
}
