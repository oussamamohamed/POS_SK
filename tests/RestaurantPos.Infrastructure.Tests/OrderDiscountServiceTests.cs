using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class OrderDiscountServiceTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ApplyGlobalDiscount_Percentage_RecalculatesTotalAndCreatesAudit()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var service = new OrderDiscountService(db);

        var order = new Order
        {
            TableNumber = "T1",
            Items =
            [
                new OrderItem { ProductName = "Burger", Quantity = 2, UnitPrice = Money.FromEuros(20m), TaxRatePercent = 10m } // 40 EUR
            ]
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        // Act (Apply 10% discount)
        var updated = await service.ApplyGlobalDiscountAsync(order.Id, DiscountType.Percentage, 10m, "Remise Guide Gourmand", Guid.NewGuid());

        // Assert
        updated.TotalTtc.ToDecimal().Should().Be(36.00m); // 40 - 4 = 36 EUR

        var audits = await service.GetDiscountAuditTrailAsync(order.Id);
        audits.Should().HaveCount(1);
        audits[0].Reason.Should().Be("Remise Guide Gourmand");
        audits[0].AmountSaved.ToDecimal().Should().Be(4.00m);
    }

    [Fact]
    public async Task CompOrderItem_SetsLineToZero_AndCreatesAudit()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var service = new OrderDiscountService(db);

        var itemBurger = new OrderItem { ProductName = "Burger", Quantity = 1, UnitPrice = Money.FromEuros(15m), TaxRatePercent = 10m };
        var itemDessert = new OrderItem { ProductName = "Tarte Tatin", Quantity = 1, UnitPrice = Money.FromEuros(8m), TaxRatePercent = 10m };

        var order = new Order
        {
            TableNumber = "T2",
            Items = [itemBurger, itemDessert] // 23 EUR total
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        // Act (Comp dessert)
        var updated = await service.CompOrderItemAsync(order.Id, itemDessert.Id, "Geste commercial retard cuisine", Guid.NewGuid());

        // Assert
        itemDessert.IsComp.Should().BeTrue();
        itemDessert.CalculateTotalTtc().AmountInCents.Should().Be(0);
        updated.TotalTtc.ToDecimal().Should().Be(15.00m);

        var audits = await service.GetDiscountAuditTrailAsync(order.Id);
        audits.Should().HaveCount(1);
        audits[0].DiscountType.Should().Be(DiscountType.Comp);
        audits[0].AmountSaved.ToDecimal().Should().Be(8.00m);
    }

    [Fact]
    public async Task ApplyGlobalDiscount_NegativeOrExcessivePercentage_ThrowsArgumentException()
    {
        using var db = CreateInMemoryDb();
        var service = new OrderDiscountService(db);

        var order = new Order { TableNumber = "T1" };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        // Negative discount
        var actNegative = () => service.ApplyGlobalDiscountAsync(order.Id, DiscountType.Percentage, -5m, "Test", Guid.NewGuid());
        await actNegative.Should().ThrowAsync<ArgumentException>();

        // Over 100% discount
        var actOver100 = () => service.ApplyGlobalDiscountAsync(order.Id, DiscountType.Percentage, 105m, "Test", Guid.NewGuid());
        await actOver100.Should().ThrowAsync<ArgumentException>();
    }
}
