using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Security;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class OperatorAuthenticationServiceTests
{
    [Fact]
    public async Task AuthenticatePinAsyncWithValidPinReturnsSuccessAndRole()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AuthTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var authService = new OperatorAuthenticationService(dbContext);

        string salt = authService.GenerateSalt();
        string pinHash = authService.HashPin("1234", salt);

        var user = new User
        {
            Name = "Alexandre Dupont",
            Role = UserRole.Waiter,
            PinHash = pinHash,
            PinSalt = salt,
            IsActive = true
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await authService.AuthenticatePinAsync("1234");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.OperatorId.Should().Be(user.Id);
        result.OperatorName.Should().Be("Alexandre Dupont");
        result.Role.Should().Be(UserRole.Waiter);
        result.ErrorMessage.Should().BeNull();
    }

    [Fact]
    public async Task AuthenticatePinAsyncWithInvalidPinReturnsFailure()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AuthTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var authService = new OperatorAuthenticationService(dbContext);

        string salt = authService.GenerateSalt();
        string pinHash = authService.HashPin("1234", salt);

        var user = new User
        {
            Name = "Sophie Bernard",
            Role = UserRole.FloorManager,
            PinHash = pinHash,
            PinSalt = salt,
            IsActive = true
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();

        // Act
        var result = await authService.AuthenticatePinAsync("9999");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.OperatorId.Should().BeNull();
        result.ErrorMessage.Should().NotBeNullOrEmpty();
    }
}
