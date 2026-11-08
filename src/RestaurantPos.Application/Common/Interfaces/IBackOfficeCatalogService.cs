using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public interface IBackOfficeCatalogService
{
    Task<IReadOnlyList<Category>> GetAllCategoriesAsync(bool includeArchived = false, CancellationToken ct = default);
    Task<Category> CreateCategoryAsync(string name, string? colorHex, int displayOrder, string? iconName, string? preparationStationId = null, CancellationToken ct = default);
    Task<Category> UpdateCategoryAsync(string categoryId, string name, string? colorHex, int displayOrder, string? iconName, bool isActive, string? preparationStationId = null, CancellationToken ct = default);
    Task<bool> ArchiveCategoryAsync(string categoryId, CancellationToken ct = default);

    Task<IReadOnlyList<Product>> GetProductsByCategoryAsync(string categoryId, bool includeArchived = false, CancellationToken ct = default);
    Task<Product> CreateProductAsync(string name, string categoryId, decimal price, decimal taxRatePercent, string? description, string? colorHex, int displayOrder, bool isQuickKey, string? stationId, decimal? cost = null, CancellationToken ct = default);
    Task<Product?> SetProductAvailabilityAsync(Guid productId, bool isAvailable, CancellationToken ct = default);
    Task<Product?> SetProductAllergensAsync(Guid productId, bool declared, int mask, bool overridden, CancellationToken ct = default);
    /// <summary><paramref name="isAvailable"/> nul = disponibilité inchangée (l'éditeur de produit ne doit pas lever une rupture sans le dire).</summary>
    Task<Product> UpdateProductAsync(Guid productId, string name, string categoryId, decimal price, decimal taxRatePercent, string? description, string? colorHex, int displayOrder, bool? isAvailable, bool isActive, bool isQuickKey, string? stationId, decimal? cost = null, bool clearCost = false, CancellationToken ct = default);
    Task<bool> ArchiveProductAsync(Guid productId, CancellationToken ct = default);
}
