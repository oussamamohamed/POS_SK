using System;

namespace RestaurantPos.Domain.Entities;

public class TaxRate
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Code { get; set; }
    public decimal RatePercent { get; set; }
    public required string Description { get; set; }
    public bool IsDefault { get; set; }

    public static TaxRate StandardRestaurantVat => new()
    {
        Code = "TVA_10",
        RatePercent = 10.0m,
        Description = "Restauration sur place / Vente à emporter consommable",
        IsDefault = true
    };

    public static TaxRate ReducedVat => new()
    {
        Code = "TVA_5_5",
        RatePercent = 5.5m,
        Description = "Produits alimentaires sous emballage / Eau",
        IsDefault = false
    };

    public static TaxRate StandardAlcoholVat => new()
    {
        Code = "TVA_20",
        RatePercent = 20.0m,
        Description = "Boissons alcoolisées / Prestations standard",
        IsDefault = false
    };
}
