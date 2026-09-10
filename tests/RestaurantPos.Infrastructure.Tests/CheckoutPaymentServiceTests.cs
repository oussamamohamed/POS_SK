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
}
