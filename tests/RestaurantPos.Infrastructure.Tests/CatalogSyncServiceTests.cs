using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class CatalogSyncServiceTests
{
    [Fact]
    public async Task GetCatalogDeltaAsyncReturnsActiveCatalogAndDeltaFilter()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "SyncTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var syncService = new CatalogSyncService(dbContext);

        var cat = new Category { Id = "CAT-TEST", Name = "Test Category", DisplayOrder = 1, UpdatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2) };
        var prod = new Product { Name = "Item 1", CategoryId = "CAT-TEST", Price = Money.FromDecimal(10m), UpdatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2) };
        var user = new User { Name = "Staff 1", PinHash = "h", PinSalt = "s", IsActive = true, UpdatedAtUtc = DateTimeOffset.UtcNow.AddHours(-2) };

        dbContext.Categories.Add(cat);
        dbContext.Products.Add(prod);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        // Act - Full Sync
        var fullSync = await syncService.GetCatalogDeltaAsync();

        // Assert Full Sync
        fullSync.Categories.Should().HaveCount(1);
        fullSync.Products.Should().HaveCount(1);
        fullSync.Operators.Should().HaveCount(1);

        // Act - Delta Sync for items modified within last 10 minutes (should be empty)
        var deltaSync = await syncService.GetCatalogDeltaAsync(DateTimeOffset.UtcNow.AddMinutes(-10));
        deltaSync.Categories.Should().BeEmpty();
        deltaSync.Products.Should().BeEmpty();
    }
}
