using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class HappyHourPricingServiceTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public void CalculatePriceForRule_ProductFixedPriceTakesPrecedenceOverCategoryPercentage()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var categoryId = Guid.NewGuid().ToString();
        var standardPrice = Money.FromEuros(8.00m);

        var rules = new List<HappyHourPriceRule>
        {
            // Category has 25% discount (8.00 -> 6.00)
            new()
            {
                TargetType = HappyHourTargetType.Category,
                TargetId = categoryId,
                TargetName = categoryId,
                PricingMode = HappyHourPricingMode.PercentageDiscount,
                DiscountPercent = 25.0m
            },
            // Product has FixedPrice of 5.00
            new()
            {
                TargetType = HappyHourTargetType.Product,
                TargetId = productId.ToString(),
                TargetName = "Bière Spéciale",
                PricingMode = HappyHourPricingMode.FixedPrice,
                FixedPrice = Money.FromEuros(5.00m)
            }
        };

        // Act
        var result = HappyHourPricingService.CalculatePriceForRule(productId, categoryId, standardPrice, rules);

        // Assert
        result.IsHappyHour.Should().BeTrue();
        result.EffectivePrice.ToDecimal().Should().Be(5.00m);
        result.OriginalPrice!.Value.ToDecimal().Should().Be(8.00m);
    }

    [Fact]
    public void CalculatePriceForRule_CategoryPercentageDiscount_CalculatedCorrectlyWithZeroRoundingDrift()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var categoryId = "BOISSONS";
        var standardPrice = Money.FromEuros(6.50m); // 650 cents

        var rules = new List<HappyHourPriceRule>
        {
            new()
            {
                TargetType = HappyHourTargetType.Category,
                TargetId = categoryId,
                TargetName = "BOISSONS",
                PricingMode = HappyHourPricingMode.PercentageDiscount,
                DiscountPercent = 20.0m // 6.50 * 0.8 = 5.20 EUR
            }
        };

        // Act
        var result = HappyHourPricingService.CalculatePriceForRule(productId, categoryId, standardPrice, rules);

        // Assert
        result.IsHappyHour.Should().BeTrue();
        result.EffectivePrice.AmountInCents.Should().Be(520);
        result.EffectivePrice.ToDecimal().Should().Be(5.20m);
        result.OriginalPrice!.Value.ToDecimal().Should().Be(6.50m);
    }

    [Fact]
    public void CalculatePriceForRule_WhenNoMatchingRule_ReturnsStandardPrice()
    {
        // Arrange
        var productId = Guid.NewGuid();
        var categoryId = "DESSERT";
        var standardPrice = Money.FromEuros(7.00m);
        var rules = new List<HappyHourPriceRule>();

        // Act
        var result = HappyHourPricingService.CalculatePriceForRule(productId, categoryId, standardPrice, rules);

        // Assert
        result.IsHappyHour.Should().BeFalse();
        result.EffectivePrice.ToDecimal().Should().Be(7.00m);
        result.OriginalPrice.Should().BeNull();
    }

    [Fact]
    public async Task GetCurrentStatusAsync_WhenOverrideActive_ReturnsOverrideDetails()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var authMock = new Mock<IOperatorAuthenticationService>();
        var service = new HappyHourPricingService(db, authMock.Object);

        var overrideSession = new HappyHourOverrideSession
        {
            TerminalId = "POS_MAIN",
            OperatorId = Guid.NewGuid(),
            OperatorName = "Alexandre (FloorManager)",
            OverrideType = HappyHourOverrideType.ForceStart,
            StartsAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(55),
            Reason = "Match de foot",
            IsActive = true
        };
        db.HappyHourOverrideSessions.Add(overrideSession);
        await db.SaveChangesAsync();

        // Act
        var status = await service.GetCurrentStatusAsync("POS_MAIN");

        // Assert
        status.IsActive.Should().BeTrue();
        status.IsOverride.Should().BeTrue();
        status.CurrentWindow.Should().NotBeNull();
        status.CurrentWindow!.RemainingMinutes.Should().BeGreaterThan(0);
        status.OverrideDetails.Should().NotBeNull();
        status.OverrideDetails!.OperatorName.Should().Be("Alexandre (FloorManager)");
        status.OverrideDetails.Reason.Should().Be("Match de foot");
    }

    [Fact]
    public async Task ActivateOverrideAsync_WithManagerPin_CreatesSessionAndAuditEntry()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var authMock = new Mock<IOperatorAuthenticationService>();
        authMock.Setup(a => a.AuthenticatePinAsync("9999", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorAuthenticationResult(true, Guid.NewGuid(), "Sophie (Manager)", UserRole.FloorManager, null));

        var service = new HappyHourPricingService(db, authMock.Object);

        var request = new ActivateOverrideRequest("POS_MAIN", "9999", 30, "Prolongation terrasse");

        // Act
        var response = await service.ActivateOverrideAsync(request);

        // Assert
        response.Success.Should().BeTrue();
        response.AuthorizedBy.Should().Be("Sophie (Manager)");

        var activeSession = await db.HappyHourOverrideSessions.FirstOrDefaultAsync(s => s.TerminalId == "POS_MAIN" && s.IsActive);
        activeSession.Should().NotBeNull();
        activeSession!.Reason.Should().Be("Prolongation terrasse");

        var jet = await db.JournalEntries.FirstOrDefaultAsync(j => j.EventType == "EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED");
        jet.Should().NotBeNull();
        jet!.PayloadJson.Should().Contain("Sophie (Manager)");
    }

    [Fact]
    public async Task ActivateOverrideAsync_WithWaiterPin_FailsWithForbiddenMessage()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var authMock = new Mock<IOperatorAuthenticationService>();
        authMock.Setup(a => a.AuthenticatePinAsync("1234", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperatorAuthenticationResult(true, Guid.NewGuid(), "Serveur Jean", UserRole.Waiter, null));

        var service = new HappyHourPricingService(db, authMock.Object);

        var request = new ActivateOverrideRequest("POS_MAIN", "1234", 30, "Essai serveur");

        // Act
        var response = await service.ActivateOverrideAsync(request);

        // Assert
        response.Success.Should().BeFalse();
        response.Message.Should().Contain("Autorisation insuffisante");

        var activeSession = await db.HappyHourOverrideSessions.FirstOrDefaultAsync(s => s.TerminalId == "POS_MAIN" && s.IsActive);
        activeSession.Should().BeNull();
    }

    [Fact]
    public async Task TableOrder_PriceLocking_ItemsPreserveHappyHourRateAfterScheduleExpiration()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var tableService = new TableManagementService(db);

        db.DiningTables.Add(new DiningTable { TableNumber = "T10", Capacity = 4, Status = TableStatus.Free });
        await db.SaveChangesAsync();

        var beerProductId = Guid.NewGuid();
        var standardPrice = 7.50m;
        var happyHourPrice = 5.00m;
        var scheduleId = Guid.NewGuid();

        // Simulate adding order items while Happy Hour was active
        var items = new List<OrderItemInputDto>
        {
            new(
                beerProductId,
                "Pinte Blonde",
                2,
                happyHourPrice, // Locked price = 5.00 EUR
                20.0m,
                "BAR",
                null,
                CourseType.Direct,
                0m,
                null,
                true,
                IsHappyHourApplied: true,
                OriginalUnitPrice: standardPrice,
                AppliedHappyHourScheduleId: scheduleId
            )
        };

        var activeOrder = await tableService.AddOrUpdateTableOrderItemsAsync("T10", items);

        // Assert that items were persisted with locked Happy Hour price
        activeOrder.Lines.Should().HaveCount(1);
        activeOrder.Lines[0].UnitPrice.Should().Be(happyHourPrice);
        activeOrder.Lines[0].IsHappyHourApplied.Should().BeTrue();
        activeOrder.Lines[0].OriginalUnitPrice.Should().Be(standardPrice);
        activeOrder.TotalTtcAmount.Should().Be(10.00m); // 2 x 5.00

        // Now simulate Happy Hour expiring and a new item added at standard price
        var postHhItems = new List<OrderItemInputDto>
        {
            new(
                beerProductId,
                "Pinte Blonde",
                1,
                standardPrice, // Normal price = 7.50 EUR
                20.0m,
                "BAR",
                null,
                CourseType.Direct,
                0m,
                null,
                true,
                IsHappyHourApplied: false,
                OriginalUnitPrice: null,
                AppliedHappyHourScheduleId: null
            )
        };

        var updatedOrder = await tableService.AddOrUpdateTableOrderItemsAsync("T10", postHhItems);

        // Both lines should be present independently due to distinct unit price & IsHappyHourApplied
        updatedOrder.Lines.Should().HaveCount(2);
        var hhLine = updatedOrder.Lines.First(l => l.IsHappyHourApplied);
        var stdLine = updatedOrder.Lines.First(l => !l.IsHappyHourApplied);

        hhLine.Quantity.Should().Be(2);
        hhLine.UnitPrice.Should().Be(5.00m);

        stdLine.Quantity.Should().Be(1);
        stdLine.UnitPrice.Should().Be(7.50m);

        updatedOrder.TotalTtcAmount.Should().Be(17.50m); // 10.00 + 7.50 = 17.50 EUR
    }
}
