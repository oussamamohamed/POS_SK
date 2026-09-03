using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class BackOfficeCatalogService : IBackOfficeCatalogService
{
    private readonly AppDbContext _dbContext;
    private readonly IMemoryCache _cache;
    private static readonly string CacheKeyCategories = "catalog_categories";
    private static readonly string CacheKeyProducts = "catalog_products";

    public BackOfficeCatalogService(AppDbContext dbContext, IMemoryCache cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    private void InvalidateCache()
    {
        _cache.Remove(CacheKeyCategories);
        _cache.Remove(CacheKeyProducts);
    }

    public async Task<IReadOnlyList<Category>> GetAllCategoriesAsync(bool includeArchived = false, CancellationToken ct = default)
    {
        if (includeArchived)
        {
            return await _dbContext.Categories.AsNoTracking()
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(ct).ConfigureAwait(false);
        }

        return await _cache.GetOrCreateAsync(CacheKeyCategories, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await _dbContext.Categories.AsNoTracking()
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayOrder).ThenBy(c => c.Name).ToListAsync(ct).ConfigureAwait(false);
        }) ?? [];
    }

    public async Task<Category> CreateCategoryAsync(string name, string? colorHex, int displayOrder, string? iconName, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var category = new Category
        {
            Id = "CAT_" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant(),
            Name = name.Trim(),
            ColorHex = colorHex ?? "#4A90E2",
            DisplayOrder = displayOrder,
            IconName = iconName,
            IsActive = true,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.Categories.Add(category);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        InvalidateCache();
        return category;
    }

    public async Task<Category> UpdateCategoryAsync(string categoryId, string name, string? colorHex, int displayOrder, string? iconName, bool isActive, CancellationToken ct = default)
    {
        var category = await _dbContext.Categories.FindAsync([categoryId], ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Catégorie introuvable: {categoryId}");

        category.Name = name.Trim();
        category.ColorHex = colorHex;
        category.DisplayOrder = displayOrder;
        category.IconName = iconName;
        category.IsActive = isActive;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        InvalidateCache();
        return category;
    }

    public async Task<bool> ArchiveCategoryAsync(string categoryId, CancellationToken ct = default)
    {
        var category = await _dbContext.Categories.FindAsync([categoryId], ct).ConfigureAwait(false);
        if (category is null) return false;

        category.IsActive = false;
        category.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        InvalidateCache();
        return true;
    }

    public async Task<IReadOnlyList<Product>> GetProductsByCategoryAsync(string categoryId, bool includeArchived = false, CancellationToken ct = default)
    {
        if (includeArchived || !string.IsNullOrWhiteSpace(categoryId))
        {
            var query = _dbContext.Products.AsNoTracking();
            if (!string.IsNullOrWhiteSpace(categoryId))
            {
                query = query.Where(p => p.CategoryId == categoryId);
            }
            if (!includeArchived)
            {
                query = query.Where(p => p.IsActive);
            }
            return await query.OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name).ToListAsync(ct).ConfigureAwait(false);
        }

        return await _cache.GetOrCreateAsync(CacheKeyProducts, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            return await _dbContext.Products.AsNoTracking()
                .Where(p => p.IsActive)
                .OrderBy(p => p.DisplayOrder).ThenBy(p => p.Name).ToListAsync(ct).ConfigureAwait(false);
        }) ?? [];
    }

    public async Task<Product> CreateProductAsync(string name, string categoryId, decimal price, decimal taxRatePercent, string? description, string? colorHex, int displayOrder, bool isQuickKey, string? stationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(price);

        var product = new Product
        {
            Id = UuidV7.NewGuid(),
            Name = name.Trim(),
            CategoryId = categoryId,
            Price = Money.FromDecimal(price, "EUR"),
            TaxRatePercent = taxRatePercent,
            Description = description,
            ColorHex = colorHex,
            DisplayOrder = displayOrder,
            IsQuickKey = isQuickKey,
            IsAvailable = true,
            IsActive = true,
            PreparationStationId = stationId,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        _dbContext.Products.Add(product);
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        InvalidateCache();
        return product;
    }

    public async Task<Product> UpdateProductAsync(Guid productId, string name, string categoryId, decimal price, decimal taxRatePercent, string? description, string? colorHex, int displayOrder, bool isAvailable, bool isActive, bool isQuickKey, string? stationId, CancellationToken ct = default)
    {
        var product = await _dbContext.Products.FindAsync([productId], ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Article introuvable: {productId}");

        product.Name = name.Trim();
        product.CategoryId = categoryId;
        product.Price = Money.FromDecimal(price, "EUR");
        product.TaxRatePercent = taxRatePercent;
        product.Description = description;
        product.ColorHex = colorHex;
        product.DisplayOrder = displayOrder;
        product.IsAvailable = isAvailable;
        product.IsActive = isActive;
        product.IsQuickKey = isQuickKey;
        product.PreparationStationId = stationId;
        product.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        InvalidateCache();
        return product;
    }

    public async Task<bool> ArchiveProductAsync(Guid productId, CancellationToken ct = default)
    {
        var product = await _dbContext.Products.FindAsync([productId], ct).ConfigureAwait(false);
        if (product is null) return false;

        product.IsActive = false;
        product.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        InvalidateCache();
        return true;
    }
}
