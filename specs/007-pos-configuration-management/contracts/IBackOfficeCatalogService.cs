// Contract: IBackOfficeCatalogService
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

/// <summary>
/// Service contract for managing menu catalog, categories, products, and modifier groups.
/// </summary>
public interface IBackOfficeCatalogService
{
    Task<IReadOnlyList<ProductCategory>> GetAllCategoriesAsync(bool includeArchived = false, CancellationToken ct = default);
    Task<ProductCategory> CreateCategoryAsync(string name, string colorHex, int displayOrder, string? iconGlyph, CancellationToken ct = default);
    Task<ProductCategory> UpdateCategoryAsync(Guid categoryId, string name, string colorHex, int displayOrder, string? iconGlyph, bool isActive, CancellationToken ct = default);
    Task<bool> ArchiveCategoryAsync(Guid categoryId, CancellationToken ct = default);

    Task<IReadOnlyList<Product>> GetProductsByCategoryAsync(Guid categoryId, bool includeArchived = false, CancellationToken ct = default);
    Task<Product> CreateProductAsync(Guid categoryId, Guid taxRateId, string name, string? description, decimal price, PreparationStation station, string? colorHex, int displayOrder, CancellationToken ct = default);
    Task<Product> UpdateProductAsync(Guid productId, string name, string? description, decimal price, Guid categoryId, Guid taxRateId, PreparationStation station, bool isActive, bool isQuickKey, CancellationToken ct = default);
    Task<bool> ArchiveProductAsync(Guid productId, CancellationToken ct = default);

    Task<IReadOnlyList<TaxRate>> GetTaxRatesAsync(CancellationToken ct = default);
}
