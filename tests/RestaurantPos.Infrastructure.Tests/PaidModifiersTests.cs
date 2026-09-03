using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PaidModifiersTests
{
    [Fact]
    public void OrderItemWithPaidModifiers_CalculatesCorrectTotalTtcAndTax()
    {
        // Arrange: Burger at 19.50 € with 3.50 € modifiers (Cheddar 2.00 € + Bacon 1.50 €)
        var item = new OrderItem
        {
            ProductId = Guid.NewGuid(),
            ProductName = "Burger Gourmet Rossini",
            UnitPrice = Money.FromDecimal(19.50m, "EUR"),
            ModifiersPriceExtra = Money.FromDecimal(3.50m, "EUR"),
            Quantity = 2,
            TaxRatePercent = 10.0m,
            SelectedModifiers = ["Cuisson : Saignant", "Double Cheddar Fondu (+2.00€)", "Bacon Fumé Croustillant (+1.50€)"]
        };

        // Act
        var totalTtc = item.CalculateTotalTtc();
        var taxAmount = item.TaxAmount;

        // Assert: (19.50 + 3.50) * 2 = 23.00 * 2 = 46.00 €
        totalTtc.ToDecimal().Should().Be(46.00m);
        // Tax at 10% on 46.00 € = 46.00 - (46.00 / 1.10) = 4.18 €
        taxAmount.ToDecimal().Should().Be(4.18m);
    }

    [Fact]
    public void OrderItemWithDiscount_AppliesDiscountOnEffectivePriceIncludingModifiers()
    {
        // Arrange: Burger 19.50 € + 3.50 € modifiers = 23.00 €, Quantity = 1, Discount = 20%
        var item = new OrderItem
        {
            ProductId = Guid.NewGuid(),
            ProductName = "Burger Gourmet Rossini",
            UnitPrice = Money.FromDecimal(19.50m, "EUR"),
            ModifiersPriceExtra = Money.FromDecimal(3.50m, "EUR"),
            Quantity = 1,
            DiscountPercent = 20.0m
        };

        // Act
        var totalTtc = item.CalculateTotalTtc();

        // Assert: 23.00 * (1 - 0.20) = 18.40 €
        totalTtc.ToDecimal().Should().Be(18.40m);
    }

    [Fact]
    public void OrderItemComp_ReturnsZeroTtcEvenWithPaidModifiers()
    {
        // Arrange
        var item = new OrderItem
        {
            ProductId = Guid.NewGuid(),
            ProductName = "Burger Gourmet Rossini",
            UnitPrice = Money.FromDecimal(19.50m, "EUR"),
            ModifiersPriceExtra = Money.FromDecimal(3.50m, "EUR"),
            Quantity = 2,
            IsComp = true,
            CompReason = "Geste commercial DG"
        };

        // Act & Assert
        item.CalculateTotalTtc().AmountInCents.Should().Be(0);
        item.TaxAmount.AmountInCents.Should().Be(0);
    }

    [Fact]
    public async Task TableManagementService_AddsAndRecallsOrderWithPaidModifiers()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        using var db = new AppDbContext(options);
        var table = new DiningTable
        {
            TableNumber = "T4",
            Capacity = 4,
            Status = TableStatus.Free
        };
        db.DiningTables.Add(table);
        await db.SaveChangesAsync();

        var service = new TableManagementService(db);

        var input = new OrderItemInputDto(
            ProductId: Guid.NewGuid(),
            ProductName: "Burger Gourmet Rossini",
            Quantity: 1,
            UnitPrice: 19.50m,
            TaxRatePercent: 10.0m,
            PreparationStationId: "HOT_KITCHEN",
            Modifiers: ["Cuisson : À Point", "Double Cheddar Fondu (+2.00€)"],
            Course: CourseType.Direct,
            ModifiersPriceExtra: 2.00m
        );

        // Act
        var orderDto = await service.AddOrUpdateTableOrderItemsAsync("T4", [input]);

        // Assert
        orderDto.Lines.Should().HaveCount(1);
        orderDto.Lines[0].UnitPrice.Should().Be(19.50m);
        orderDto.Lines[0].ModifiersPriceExtra.Should().Be(2.00m);
        orderDto.Lines[0].TotalPrice.Should().Be(21.50m);
        orderDto.TotalTtcAmount.Should().Be(21.50m);

        // Verify database persistence and recall
        var recalled = await service.GetActiveOrderForTableAsync("T4");
        recalled.Should().NotBeNull();
        recalled!.Lines[0].ModifiersPriceExtra.Should().Be(2.00m);
        recalled.Lines[0].TotalPrice.Should().Be(21.50m);
        recalled.TotalTtcAmount.Should().Be(21.50m);
    }
}
