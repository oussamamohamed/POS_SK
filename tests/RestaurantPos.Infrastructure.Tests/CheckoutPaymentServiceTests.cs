using System;
using System.Collections.Generic;
using System.Linq;
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

public class CheckoutPaymentServiceTests
{
    [Fact]
    public async Task ProcessPaymentTendersAsyncMultiTenderCalculatesChangeAndClosesTable()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "CheckoutTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var fiscalService = new NF525FiscalAuditService(dbContext);
        var checkoutService = new CheckoutPaymentService(dbContext, fiscalService);

        var table = new DiningTable { TableNumber = "T02", Status = TableStatus.Occupied };
        dbContext.DiningTables.Add(table);

        var order = new Order { TableNumber = "T02" };
        order.Items.Add(new OrderItem
        {
            ProductName = "Menu Déjeuner",
            Quantity = 2,
            UnitPrice = Money.FromDecimal(20.00m), // 40.00 EUR
            TaxRatePercent = 10m
        });
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var tenders = new List<PaymentTenderRequest>
        {
            new(PaymentMethod.MealVoucher, 1500, 1500), // 15.00 EUR Meal Voucher
            new(PaymentMethod.Cash, 2500, 3000)          // 25.00 EUR paid with 30.00 EUR Cash
        };

        // Act
        var result = await checkoutService.ProcessPaymentTendersAsync(order.Id, "POS01", tenders);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.TotalPaidCents.Should().Be(4000);
        result.ChangeGivenCents.Should().Be(500); // 30 - 25 = 5.00 EUR
        result.RemainingBalanceCents.Should().Be(0);
        result.ReceiptNumber.Should().Be("POS01-000001");
        result.FiscalSignature.Should().NotBeNullOrEmpty();

        var updatedTable = await dbContext.DiningTables.FindAsync("T02");
        updatedTable!.Status.Should().Be(TableStatus.Paid);
    }

    // Bug fix: TipAmount was excluded from totalDueCents → change was inflated by tip amount.
    [Fact]
    public async Task ChangeGiven_WithTip_DeductsTipFromChange()
    {
        // Arrange — total 12.50 €, tip 2.00 €, client donne 20 €
        // Expected change = 20 - (12.50 + 2.00) = 5.50 €  (was wrongly 7.50 € before fix)
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "CheckoutTipDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var checkoutService = new CheckoutPaymentService(dbContext, new NF525FiscalAuditService(dbContext));

        var order = new Order { TableNumber = "Comptoir" };
        order.Items.Add(new OrderItem
        {
            ProductName = "Burger",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(12.50m),
            TaxRatePercent = 10m
        });
        order.TipAmount = Money.FromDecimal(2.00m);
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        // Client paie 20 € en espèces (tendered=2000, amount=1450 cappé au dû)
        var tenders = new List<PaymentTenderRequest>
        {
            new(PaymentMethod.Cash, 1450, 2000) // amount=14.50, tendered=20.00
        };

        // Act
        var result = await checkoutService.ProcessPaymentTendersAsync(order.Id, "POS01", tenders);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.ChangeGivenCents.Should().Be(550, // 20.00 - 14.50 = 5.50 €
            because: "la monnaie doit être calculée sur total + tip, pas seulement le total des articles");
    }

    [Fact]
    public async Task ChangeGiven_WithoutTip_IsCorrect()
    {
        // Régression : sans tip, la monnaie = tendered - totalTtc
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "CheckoutNoTipDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var checkoutService = new CheckoutPaymentService(dbContext, new NF525FiscalAuditService(dbContext));

        var order = new Order { TableNumber = "Comptoir" };
        order.Items.Add(new OrderItem
        {
            ProductName = "Café",
            Quantity = 1,
            UnitPrice = Money.FromDecimal(3.50m),
            TaxRatePercent = 10m
        });
        // TipAmount = 0 (défaut)
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var tenders = new List<PaymentTenderRequest>
        {
            new(PaymentMethod.Cash, 350, 500) // amount=3.50, tendered=5.00
        };

        var result = await checkoutService.ProcessPaymentTendersAsync(order.Id, "POS01", tenders);

        result.IsSuccess.Should().BeTrue();
        result.ChangeGivenCents.Should().Be(150, // 5.00 - 3.50 = 1.50 €
            because: "sans tip, la monnaie = billet remis - total note");
    }
}
