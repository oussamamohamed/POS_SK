using System.IO;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantPos.Client.Maui.Contracts;
using RestaurantPos.Client.Maui.Models;
using RestaurantPos.Client.Maui.Persistence;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class LocalAppDbContextMappingTests
{
    [Fact]
    public async Task ApplyCatalogSyncPayloadAsyncUpdatesLocalSqliteDatabaseAtomically()
    {
        // Arrange
        string tempDir = Path.Combine(Path.GetTempPath(), "LocalDbMapping_" + Guid.NewGuid().ToString("N"));
        var envMock = new Mock<IPlatformEnvironmentService>();
        envMock.Setup(e => e.GetSecureDatabasePath(It.IsAny<string>()))
               .Returns(Path.Combine(tempDir, "test_local_catalog.db"));

        if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

        using var dbContext = new LocalAppDbContext(envMock.Object);
        await dbContext.Database.EnsureCreatedAsync();

        try
        {
            var cat = new Category { Id = "CAT-MAINS", Name = "Plats Chauds", DisplayOrder = 1 };
            var prod = new Product { Name = "Burger Gourmet", CategoryId = "CAT-MAINS", Price = Money.FromDecimal(18.50m) };
            var user = new User { Name = "Jean Dupont", PinHash = "hash123", PinSalt = "salt123", Role = UserRole.Waiter };

            // Act
            await dbContext.ApplyCatalogSyncPayloadAsync([cat], [prod], [user]);

            // Assert
            var savedCat = await dbContext.Categories.FindAsync(cat.Id);
            savedCat.Should().NotBeNull();
            savedCat!.Name.Should().Be("Plats Chauds");

            var savedProd = await dbContext.Products.FindAsync(prod.Id);
            savedProd.Should().NotBeNull();
            savedProd!.Price.AmountInCents.Should().Be(1850);

            var savedUser = await dbContext.Users.FindAsync(user.Id);
            savedUser.Should().NotBeNull();
            savedUser!.Role.Should().Be(UserRole.Waiter);
        }
        finally
        {
            await dbContext.Database.EnsureDeletedAsync();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
