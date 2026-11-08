using System;

namespace RestaurantPos.Domain.Entities;

/// <summary>Réglages du restaurant (ligne unique, Id = 1).</summary>
public class RestaurantSettings
{
    public const int SingletonId = 1;
    public int Id { get; init; } = SingletonId;
    public required string ReceiptLanguage { get; set; }
    public required string KitchenTicketLanguage { get; set; }
    public string CompanyName { get; set; } = "RESTAURANT L'ANTIGRAVITE";
    public string AddressLines { get; set; } = "12 Rue de la Gastronomie\n75001 Paris";
    public string Siret { get; set; } = "88877766600012";
    public string VatNumber { get; set; } = "FR12888777666";
    public string? CertificateNumber { get; set; }
    public int FiscalYearStartMonth { get; set; } = 1;
    public int FiscalYearStartDay { get; set; } = 1;
    /// <summary>Un produit dont un ingrédient suivi est épuisé passe indisponible (<c>Product.AutoUnavailable</c>) jusqu'à la prochaine réception.</summary>
    public bool AutoUnavailableOnStockOut { get; set; }
    public bool PrintAllergensOnKitchenTicket { get; set; }
    public string? AllergenDisclaimer { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

