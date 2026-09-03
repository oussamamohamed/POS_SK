using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class BackOfficeCatalogServiceTests
{
    private static (AppDbContext Context, IMemoryCache Cache) CreateContextAndCache()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"PosTest_Catalog_{Guid.NewGuid()}")
            .Options;
        var cache = new MemoryCache(new MemoryCacheOptions());
        return (new AppDbContext(options), cache);
    }

    [Fact]
    public async Task CreateCategory_And_Product_ShouldPersistCorrectly()
    {
        var (context, cache) = CreateContextAndCache();
        using (context)
        {
            var service = new BackOfficeCatalogService(context, cache);

        var category = await service.CreateCategoryAsync("Desserts Maison", "#E67E22", 1, "cake");
        category.Should().NotBeNull();
        category.Name.Should().Be("Desserts Maison");
        category.IsActive.Should().BeTrue();

        var product = await service.CreateProductAsync(
            name: "Tiramisu Spéculos",
            categoryId: category.Id,
            price: 7.50m,
            taxRatePercent: 10.0m,
            description: "Fait maison",
            colorHex: "#D35400",
            displayOrder: 1,
            isQuickKey: true,
            stationId: "DESSERT"
        );

        product.Should().NotBeNull();
        product.Price.AmountInCents.Should().Be(750);
        product.Price.ToDecimal().Should().Be(7.50m);
        product.IsQuickKey.Should().BeTrue();
        product.IsActive.Should().BeTrue();

        var activeProducts = await service.GetProductsByCategoryAsync(category.Id);
        activeProducts.Should().HaveCount(1);
        activeProducts[0].Name.Should().Be("Tiramisu Spéculos");
        }
    }

    [Fact]
    public async Task ArchiveProduct_ShouldMarkInactive_WithoutDeleting()
    {
        var (context, cache) = CreateContextAndCache();
        using (context)
        {
            var service = new BackOfficeCatalogService(context, cache);

            var category = await service.CreateCategoryAsync("Entrées", "#2ECC71", 0, null);
            var product = await service.CreateProductAsync("Salade César", category.Id, 11.00m, 10.0m, null, null, 0, false, "COLD");

            var archiveResult = await service.ArchiveProductAsync(product.Id);
            archiveResult.Should().BeTrue();

            var activeList = await service.GetProductsByCategoryAsync(category.Id, includeArchived: false);
            activeList.Should().BeEmpty();

            var allList = await service.GetProductsByCategoryAsync(category.Id, includeArchived: true);
            allList.Should().HaveCount(1);
            allList[0].IsActive.Should().BeFalse();
        }
    }

    [Fact]
    public async Task UpdateCategory_And_Product_ShouldModifyFieldsCorrectly()
    {
        var (context, cache) = CreateContextAndCache();
        using (context)
        {
            var service = new BackOfficeCatalogService(context, cache);

            var category = await service.CreateCategoryAsync("Plats", "#3498DB", 1, "utensils");
            var updatedCat = await service.UpdateCategoryAsync(category.Id, "Plats & Grillades", "#9B59B6", 2, "fire", true);

            updatedCat.Name.Should().Be("Plats & Grillades");
            updatedCat.ColorHex.Should().Be("#9B59B6");
            updatedCat.DisplayOrder.Should().Be(2);

            var product = await service.CreateProductAsync("Burger Maison", category.Id, 16.50m, 10.0m, null, null, 1, false, "GRILL");
            var updatedProd = await service.UpdateProductAsync(
                product.Id,
                name: "Burger Maison & Frites",
                categoryId: category.Id,
                price: 18.00m,
                taxRatePercent: 10.0m,
                description: "Boeuf Limousin",
                colorHex: "#E74C3C",
                displayOrder: 1,
                isAvailable: true,
                isActive: true,
                isQuickKey: true,
                stationId: "HOT_KITCHEN"
            );

            updatedProd.Name.Should().Be("Burger Maison & Frites");
            updatedProd.Price.ToDecimal().Should().Be(18.00m);
            updatedProd.IsQuickKey.Should().BeTrue();
            updatedProd.PreparationStationId.Should().Be("HOT_KITCHEN");
            updatedProd.Description.Should().Be("Boeuf Limousin");
        }
    }
}
