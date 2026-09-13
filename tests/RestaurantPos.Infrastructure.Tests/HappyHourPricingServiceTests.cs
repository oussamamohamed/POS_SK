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

    [Fact]
    public async Task ApplyBatchPriceRulesAsync_ProductsWithFixedPrice_CreatesAndUpsertsRules()
    {
        // Arrange
        using var dbContext = CreateInMemoryDb();
        var authMock = new Mock<IOperatorAuthenticationService>();
        var service = new HappyHourPricingService(dbContext, authMock.Object);

        var schedule = new HappyHourSchedule
        {
            Name = "Soirée Batch",
            DaysOfWeek = [DayOfWeek.Friday],
            StartTime = new TimeOnly(18, 0),
            EndTime = new TimeOnly(21, 0),
            IsActive = true
        };
        dbContext.HappyHourSchedules.Add(schedule);

        var p1 = new Product { Id = Guid.NewGuid(), Name = "Blonde 50cl", CategoryId = "BAR", Price = Money.FromEuros(7.00m) };
        var p2 = new Product { Id = Guid.NewGuid(), Name = "IPA 50cl", CategoryId = "BAR", Price = Money.FromEuros(8.50m) };
        dbContext.Products.AddRange(p1, p2);
        await dbContext.SaveChangesAsync();

        var batchReq = new BatchPriceRulesRequestDto(
            HappyHourTargetType.Product,
            [p1.Id.ToString(), p2.Id.ToString()],
            HappyHourPricingMode.FixedPrice,
            FixedPrice: 5.00m,
            DiscountPercent: null
        );

        // Act
        var result = await service.ApplyBatchPriceRulesAsync(schedule.Id, batchReq);

        // Assert
        result.Success.Should().BeTrue();
        result.AppliedCount.Should().Be(2);

        var refreshedSchedule = await dbContext.HappyHourSchedules.Include(s => s.PriceRules).FirstAsync(s => s.Id == schedule.Id);
        refreshedSchedule.PriceRules.Should().HaveCount(2);
        refreshedSchedule.PriceRules.Should().AllSatisfy(r =>
        {
            r.PricingMode.Should().Be(HappyHourPricingMode.FixedPrice);
            r.FixedPrice!.Value.ToDecimal().Should().Be(5.00m);
        });

        // Test Upsert: change price to 4.50 EUR
        var updateReq = new BatchPriceRulesRequestDto(
            HappyHourTargetType.Product,
            [p1.Id.ToString()],
            HappyHourPricingMode.FixedPrice,
            FixedPrice: 4.50m,
            DiscountPercent: null
        );
        var updateResult = await service.ApplyBatchPriceRulesAsync(schedule.Id, updateReq);
        updateResult.Success.Should().BeTrue();

        var reloadedRules = await dbContext.HappyHourPriceRules.Where(r => r.ScheduleId == schedule.Id).ToListAsync();
        reloadedRules.Should().HaveCount(2); // No duplicates
        var p1Rule = reloadedRules.First(r => r.TargetId == p1.Id.ToString());
        p1Rule.FixedPrice!.Value.ToDecimal().Should().Be(4.50m);
    }

    [Fact]
    public async Task ApplyBatchPriceRulesAsync_CategoriesWithPercentageDiscount_AppliesDiscountCorrectly()
    {
        // Arrange
        using var dbContext = CreateInMemoryDb();
        var authMock = new Mock<IOperatorAuthenticationService>();
        var service = new HappyHourPricingService(dbContext, authMock.Object);

        var schedule = new HappyHourSchedule
        {
            Name = "Soirée Familles",
            DaysOfWeek = [DayOfWeek.Thursday],
            StartTime = new TimeOnly(17, 0),
            EndTime = new TimeOnly(20, 0),
            IsActive = true
        };
        dbContext.HappyHourSchedules.Add(schedule);

        var cat1 = new Category { Id = "CAT-BEER", Name = "Bières" };
        var cat2 = new Category { Id = "CAT-COCKTAILS", Name = "Cocktails" };
        dbContext.Categories.AddRange(cat1, cat2);
        await dbContext.SaveChangesAsync();

        var batchReq = new BatchPriceRulesRequestDto(
            HappyHourTargetType.Category,
            [cat1.Id, cat2.Id],
            HappyHourPricingMode.PercentageDiscount,
            FixedPrice: null,
            DiscountPercent: 25.0m
        );

        // Act
        var result = await service.ApplyBatchPriceRulesAsync(schedule.Id, batchReq);

        // Assert
        result.Success.Should().BeTrue();
        result.AppliedCount.Should().Be(2);

        var rules = await dbContext.HappyHourPriceRules.Where(r => r.ScheduleId == schedule.Id).ToListAsync();
        rules.Should().HaveCount(2);
        rules.Should().AllSatisfy(r =>
        {
            r.TargetType.Should().Be(HappyHourTargetType.Category);
            r.PricingMode.Should().Be(HappyHourPricingMode.PercentageDiscount);
            r.DiscountPercent.Should().Be(25.0m);
        });
    }

    [Fact]
    public async Task DeleteBatchPriceRulesAsync_ValidRuleIds_RemovesSpecifiedRules()
    {
        // Arrange
        using var dbContext = CreateInMemoryDb();
        var authMock = new Mock<IOperatorAuthenticationService>();
        var service = new HappyHourPricingService(dbContext, authMock.Object);

        var schedule = new HappyHourSchedule
        {
            Name = "Soirée Cleanup",
            DaysOfWeek = [DayOfWeek.Wednesday],
            StartTime = new TimeOnly(17, 0),
            EndTime = new TimeOnly(20, 0),
            IsActive = true
        };
        dbContext.HappyHourSchedules.Add(schedule);

        var rule1 = new HappyHourPriceRule { ScheduleId = schedule.Id, TargetType = HappyHourTargetType.Product, TargetId = "PROD-1", TargetName = "Item 1", PricingMode = HappyHourPricingMode.FixedPrice, FixedPrice = Money.FromEuros(5m) };
        var rule2 = new HappyHourPriceRule { ScheduleId = schedule.Id, TargetType = HappyHourTargetType.Product, TargetId = "PROD-2", TargetName = "Item 2", PricingMode = HappyHourPricingMode.FixedPrice, FixedPrice = Money.FromEuros(6m) };
        var rule3 = new HappyHourPriceRule { ScheduleId = schedule.Id, TargetType = HappyHourTargetType.Product, TargetId = "PROD-3", TargetName = "Item 3", PricingMode = HappyHourPricingMode.FixedPrice, FixedPrice = Money.FromEuros(7m) };
        dbContext.HappyHourPriceRules.AddRange(rule1, rule2, rule3);
        await dbContext.SaveChangesAsync();

        var deleteReq = new BatchDeleteRulesRequestDto([rule1.Id, rule2.Id]);

        // Act
        var result = await service.DeleteBatchPriceRulesAsync(schedule.Id, deleteReq);

        // Assert
        result.Success.Should().BeTrue();
        result.DeletedCount.Should().Be(2);

        var remaining = await dbContext.HappyHourPriceRules.Where(r => r.ScheduleId == schedule.Id).ToListAsync();
        remaining.Should().HaveCount(1);
        remaining[0].Id.Should().Be(rule3.Id);
    }

    [Fact]
    public async Task GetCurrentStatusAsync_WhenSchedulesOverlap_SelectsHighestPrioritySchedule()
    {
        // Arrange
        using var dbContext = CreateInMemoryDb();
        var authMock = new Mock<IOperatorAuthenticationService>();
        var service = new HappyHourPricingService(dbContext, authMock.Object);

        // Get current day of week and a wide time range covering the entire day
        var today = DateTime.UtcNow.DayOfWeek;
        var lowPrioritySchedule = new HappyHourSchedule
        {
            Name = "Créneau Standard (Faible Priorité)",
            DaysOfWeek = [today],
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IsActive = true,
            Priority = 1
        };

        var highPrioritySchedule = new HappyHourSchedule
        {
            Name = "Créneau VIP (Haute Priorité)",
            DaysOfWeek = [today],
            StartTime = new TimeOnly(0, 0),
            EndTime = new TimeOnly(23, 59),
            IsActive = true,
            Priority = 10
        };

        dbContext.HappyHourSchedules.AddRange(lowPrioritySchedule, highPrioritySchedule);
        await dbContext.SaveChangesAsync();

        // Act
        var status = await service.GetCurrentStatusAsync("POS_MAIN");

        // Assert: highPrioritySchedule must win over lowPrioritySchedule
        status.IsActive.Should().BeTrue();
        status.ActiveScheduleId.Should().Be(highPrioritySchedule.Id);
        status.ActiveScheduleName.Should().Be("Créneau VIP (Haute Priorité)");
    }
}

