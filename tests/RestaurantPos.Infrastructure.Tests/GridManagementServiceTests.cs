using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class GridManagementServiceTests
{
    private static readonly Guid MargheritaId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReginaId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var db = new AppDbContext(options);

        // Seed sample category and products
        var cat = new Category { Id = "PIZZAS", Name = "Pizzas", ColorHex = "#E74C3C" };
        db.Categories.Add(cat);

        db.Products.Add(new Product
        {
            Id = MargheritaId,
            Name = "Pizza Margherita",
            CategoryId = "PIZZAS",
            Price = Money.FromDecimal(12.50m, "EUR"),
            PreparationStationId = "HOT_PIZZA",
            IsActive = true
        });

        db.Products.Add(new Product
        {
            Id = ReginaId,
            Name = "Pizza Regina",
            CategoryId = "PIZZAS",
            Price = Money.FromDecimal(14.00m, "EUR"),
            PreparationStationId = "HOT_PIZZA",
            IsActive = true
        });

        db.SaveChanges();
        return db;
    }

    [Fact]
    public async Task GetLayoutByCategory_ShouldAutoCreate_Default4x4Layout()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var service = new GridManagementService(db);

        // Act
        var layout = await service.GetLayoutByCategoryAsync("PIZZAS", 0);

        // Assert
        Assert.NotNull(layout);
        Assert.Equal("PIZZAS", layout.CategoryId);
        Assert.Equal(4, layout.ColumnsCount);
        Assert.Equal(4, layout.RowsCount);
        Assert.Equal(0, layout.PageIndex);
        Assert.Equal(1, layout.TotalPages);
        Assert.Equal(16, layout.Slots.Count);
        Assert.Equal(MargheritaId, layout.Slots[0].ProductId);
        Assert.Equal("Pizza Margherita", layout.Slots[0].Product?.Name);
        Assert.Equal(ReginaId, layout.Slots[1].ProductId);
        Assert.Null(layout.Slots[2].ProductId); // Empty slot
    }

    [Fact]
    public async Task GetLayoutByCategory_WithDenseProducts_ShouldAutoCreate_MultiplePages()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        // Add 20 more products (total 22 > 16)
        for (int i = 3; i <= 22; i++)
        {
            db.Products.Add(new Product
            {
                Id = Guid.NewGuid(),
                Name = $"Pizza {i}",
                CategoryId = "PIZZAS",
                Price = Money.FromDecimal(15.00m, "EUR"),
                PreparationStationId = "HOT_PIZZA",
                IsActive = true
            });
        }
        db.SaveChanges();

        var service = new GridManagementService(db);

        // Act
        var page0 = await service.GetLayoutByCategoryAsync("PIZZAS", 0);
        var page1 = await service.GetLayoutByCategoryAsync("PIZZAS", 1);
        var allPages = await service.GetAllPagesByCategoryAsync("PIZZAS");

        // Assert
        Assert.NotNull(page0);
        Assert.NotNull(page1);
        Assert.Equal(2, page0.TotalPages);
        Assert.Equal(0, page0.PageIndex);
        Assert.Equal(1, page1.PageIndex);
        Assert.Equal(2, allPages.Count);
    }

    [Fact]
    public async Task SwapSlots_ShouldSwapPositions_Atomically()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var service = new GridManagementService(db);
        var initial = await service.GetLayoutByCategoryAsync("PIZZAS");
        Assert.NotNull(initial);

        var request = new SwapGridSlotsRequest(
            initial.Id,
            SourceRow: 0,
            SourceCol: 0, // Pizza Margherita
            TargetRow: 1,
            TargetCol: 2  // Empty slot initially
        );

        // Act
        var updated = await service.SwapSlotsAsync(request);

        // Assert
        Assert.NotNull(updated);
        Assert.True(updated.Version > initial.Version);

        var sourceSlot = updated.Slots.First(s => s.RowIndex == 0 && s.ColumnIndex == 0);
        var targetSlot = updated.Slots.First(s => s.RowIndex == 1 && s.ColumnIndex == 2);

        Assert.Null(sourceSlot.ProductId);
        Assert.Equal(MargheritaId, targetSlot.ProductId);
    }

    [Fact]
    public async Task SaveLayout_ShouldPersist_CustomLabelsAndColors()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var service = new GridManagementService(db);

        var updateReq = new UpdateGridLayoutRequest(
            "PIZZAS",
            ColumnsCount: 4,
            RowsCount: 4,
            PageIndex: 0,
            Slots: new List<UpdateGridSlotItem>
            {
                new(0, 0, MargheritaId, "MARGHERITA VIP", "#FF0000"),
                new(0, 1, ReginaId, null, null)
            }
        );

        // Act
        var saved = await service.SaveLayoutAsync(updateReq);

        // Assert
        Assert.NotNull(saved);
        var vipSlot = saved.Slots.First(s => s.RowIndex == 0 && s.ColumnIndex == 0);
        Assert.Equal(MargheritaId, vipSlot.ProductId);
        Assert.Equal("MARGHERITA VIP", vipSlot.CustomLabel);
        Assert.Equal("#FF0000", vipSlot.CustomColorHex);
    }

    [Fact]
    public async Task UpdateDimensions_ShouldResizeGrid_AndPreserveAssignedProducts()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var service = new GridManagementService(db);
        var initial = await service.GetLayoutByCategoryAsync("PIZZAS", 0);
        Assert.NotNull(initial);

        var resizeReq = new UpdateGridDimensionsRequest(
            CategoryId: "PIZZAS",
            ColumnsCount: 5,
            RowsCount: 5,
            ApplyToAllCategories: false
        );

        // Act
        var updatedList = await service.UpdateDimensionsAsync(resizeReq);

        // Assert
        Assert.NotEmpty(updatedList);
        var updated = updatedList.First(l => l.CategoryId == "PIZZAS");
        Assert.Equal(5, updated.ColumnsCount);
        Assert.Equal(5, updated.RowsCount);
        Assert.Equal(25, updated.Slots.Count);
        Assert.Equal(MargheritaId, updated.Slots[0].ProductId);
        Assert.Equal(ReginaId, updated.Slots[1].ProductId);
    }

    [Fact]
    public async Task UpdateDimensions_WithApplyToAll_ShouldUpdateAllCategories()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var cat2 = new Category { Id = "DRINKS", Name = "Boissons", ColorHex = "#3498DB" };
        db.Categories.Add(cat2);
        db.SaveChanges();

        var service = new GridManagementService(db);
        await service.GetLayoutByCategoryAsync("PIZZAS", 0);
        await service.GetLayoutByCategoryAsync("DRINKS", 0);

        var resizeReq = new UpdateGridDimensionsRequest(
            CategoryId: "PIZZAS",
            ColumnsCount: 6,
            RowsCount: 4,
            ApplyToAllCategories: true
        );

        // Act
        var updatedList = await service.UpdateDimensionsAsync(resizeReq);

        // Assert
        Assert.Equal(2, updatedList.Count);
        foreach (var l in updatedList)
        {
            Assert.Equal(6, l.ColumnsCount);
            Assert.Equal(4, l.RowsCount);
            Assert.Equal(24, l.Slots.Count);
        }
    }

    [Fact]
    public async Task GetLayoutByCategory_ForALL_ShouldAggregateAllProducts_AndSupportPagination()
    {
        // Arrange
        using var db = CreateInMemoryDbContext();
        var cat2 = new Category { Id = "DRINKS", Name = "Boissons", ColorHex = "#3498DB" };
        db.Categories.Add(cat2);
        for (int i = 0; i < 20; i++)
        {
            db.Products.Add(new Product
            {
                Name = $"Item {i}",
                Price = Money.FromDecimal(5.00m, "EUR"),
                CategoryId = i < 10 ? "PIZZAS" : "DRINKS",
                IsActive = true
            });
        }
        db.SaveChanges();

        var service = new GridManagementService(db);

        // Act
        var page0 = await service.GetLayoutByCategoryAsync("ALL", 0);
        var page1 = await service.GetLayoutByCategoryAsync("ALL", 1);

        // Assert
        Assert.NotNull(page0);
        Assert.NotNull(page1);
        Assert.Equal("ALL", page0.CategoryId);
        Assert.Equal("ALL", page1.CategoryId);
        Assert.Equal(2, page0.TotalPages);
        Assert.Equal(2, page1.TotalPages);
        Assert.Equal(16, page0.Slots.Count);
        Assert.Equal(16, page1.Slots.Count);
    }
}
