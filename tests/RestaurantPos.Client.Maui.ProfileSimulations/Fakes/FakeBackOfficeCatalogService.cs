using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Fakes;

/// <summary>
/// In-memory fake for <see cref="IBackOfficeCatalogService"/>.
/// Pre-seeded with two categories and four products.
/// </summary>
public sealed class FakeBackOfficeCatalogService : IBackOfficeCatalogService
{
    private readonly List<Category> _categories =
    [
        new() { Id = "CAT-001", Name = "Entrées",  DisplayOrder = 1, IsActive = true },
        new() { Id = "CAT-002", Name = "Plats",    DisplayOrder = 2, IsActive = true }
    ];

    private readonly List<Product> _products =
    [
        new() { Name = "Salade César",   CategoryId = "CAT-001", Price = Money.FromDecimal(9.50m),  TaxRatePercent = 10m },
        new() { Name = "Soupe du Jour",  CategoryId = "CAT-001", Price = Money.FromDecimal(7.00m),  TaxRatePercent = 10m },
        new() { Name = "Burger Rossini", CategoryId = "CAT-002", Price = Money.FromDecimal(19.50m), TaxRatePercent = 10m },
        new() { Name = "Steak Frites",   CategoryId = "CAT-002", Price = Money.FromDecimal(22.00m), TaxRatePercent = 10m }
    ];

    // --- Categories ---

    public Task<IReadOnlyList<Category>> GetAllCategoriesAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        IReadOnlyList<Category> result = includeArchived
            ? _categories.AsReadOnly()
            : _categories.Where(c => c.IsActive).ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<Category> CreateCategoryAsync(string name, string? colorHex, int displayOrder, string? iconName, CancellationToken ct = default)
    {
        var cat = new Category { Id = $"CAT-{_categories.Count + 1:D3}", Name = name, ColorHex = colorHex, DisplayOrder = displayOrder, IconName = iconName };
        _categories.Add(cat);
        return Task.FromResult(cat);
    }

    public Task<Category> UpdateCategoryAsync(string categoryId, string name, string? colorHex, int displayOrder, string? iconName, bool isActive, CancellationToken ct = default)
    {
        var cat = _categories.First(c => c.Id == categoryId);
        cat.Name = name; cat.ColorHex = colorHex; cat.DisplayOrder = displayOrder; cat.IconName = iconName; cat.IsActive = isActive;
        return Task.FromResult(cat);
    }

    public Task<bool> ArchiveCategoryAsync(string categoryId, CancellationToken ct = default)
    {
        var cat = _categories.FirstOrDefault(c => c.Id == categoryId);
        if (cat is null) return Task.FromResult(false);
        cat.IsActive = false;
        return Task.FromResult(true);
    }

    // --- Products ---

    public Task<IReadOnlyList<Product>> GetProductsByCategoryAsync(string categoryId, bool includeArchived = false, CancellationToken ct = default)
    {
        IReadOnlyList<Product> result = _products
            .Where(p => p.CategoryId == categoryId && (includeArchived || p.IsActive))
            .ToList().AsReadOnly();
        return Task.FromResult(result);
    }

    public Task<Product> CreateProductAsync(string name, string categoryId, decimal price, decimal taxRatePercent, string? description, string? colorHex, int displayOrder, bool isQuickKey, string? stationId, CancellationToken ct = default)
    {
        var product = new Product
        {
            Name = name,
            CategoryId = categoryId,
            Price = Money.FromDecimal(price),
            TaxRatePercent = taxRatePercent,
            Description = description,
            ColorHex = colorHex,
            DisplayOrder = displayOrder,
            IsQuickKey = isQuickKey,
            PreparationStationId = stationId
        };
        _products.Add(product);
        return Task.FromResult(product);
    }

    public Task<Product> UpdateProductAsync(Guid productId, string name, string categoryId, decimal price, decimal taxRatePercent, string? description, string? colorHex, int displayOrder, bool isAvailable, bool isActive, bool isQuickKey, string? stationId, CancellationToken ct = default)
    {
        var p = _products.First(x => x.Id == productId);
        p.Name = name; p.CategoryId = categoryId; p.Price = Money.FromDecimal(price);
        p.TaxRatePercent = taxRatePercent; p.Description = description; p.ColorHex = colorHex;
        p.DisplayOrder = displayOrder; p.IsAvailable = isAvailable; p.IsActive = isActive;
        p.IsQuickKey = isQuickKey; p.PreparationStationId = stationId;
        return Task.FromResult(p);
    }

    public Task<bool> ArchiveProductAsync(Guid productId, CancellationToken ct = default)
    {
        var p = _products.FirstOrDefault(x => x.Id == productId);
        if (p is null) return Task.FromResult(false);
        p.IsActive = false;
        return Task.FromResult(true);
    }
}
