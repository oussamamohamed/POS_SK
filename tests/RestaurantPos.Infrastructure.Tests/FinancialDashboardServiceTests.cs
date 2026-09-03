using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class FinancialDashboardServiceTests
{
    [Fact]
    public async Task GetFinancialDashboardAsync_CalculatesAccurateKpisAndAverages()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "DashDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);

        var now = new DateTimeOffset(2026, 9, 3, 12, 30, 0, TimeSpan.Zero);

        // Add table
        dbContext.DiningTables.Add(new DiningTable
        {
            TableNumber = "T10",
            Capacity = 4,
            CoversCount = 2,
            AssignedWaiterName = "Sophie Martin"
        });

        // Add Order with 2 covers
        var order = new Order
        {
            TableNumber = "T10",
            Status = OrderStatus.Paid,
            CreatedAtUtc = now
        };

        order.Items.Add(new OrderItem
        {
            ProductName = "Entrecôte Grillée",
            Quantity = 2,
            UnitPrice = Money.FromCents(2500), // 25.00 € each => 50.00 €
            TaxRatePercent = 10.0m
        });

        dbContext.Orders.Add(order);

        // Add Fiscal Receipt for the order
        var receipt = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "T-20260903-0010",
            OrderId = order.Id,
            SequenceNumber = 10,
            TotalTtcAmount = Money.FromCents(5000), // 50.00 €
            TotalHtAmount = Money.FromCents(4545),  // ~45.45 €
            CreatedAtUtc = now
        };

        receipt.Tenders.Add(new PaymentTender
        {
            FiscalReceiptId = receipt.Id,
            Method = PaymentMethod.CreditCard,
            Amount = Money.FromCents(5000),
            Tendered = Money.FromCents(5000)
        });

        dbContext.FiscalReceipts.Add(receipt);
        await dbContext.SaveChangesAsync();

        var service = new FinancialDashboardService(dbContext);

        // Act
        var result = await service.GetFinancialDashboardAsync(new FinancialDashboardFilterDto(
            now.AddDays(-1),
            now.AddDays(1)
        ));

        // Assert
        result.Kpis.TotalSalesTtc.Should().Be(50.00m);
        result.Kpis.TotalOrdersCount.Should().Be(1);
        result.Kpis.TotalCoversCount.Should().Be(2);
        result.Kpis.AverageOrderTtc.Should().Be(50.00m);
        result.Kpis.AverageCoverTtc.Should().Be(25.00m);

        // Service Midi
        var midiService = result.Services.FirstOrDefault(s => s.ServiceName.Contains("Midi"));
        midiService.Should().NotBeNull();
        midiService!.SalesTtc.Should().Be(50.00m);
        midiService.OrdersCount.Should().Be(1);

        // Top product
        result.TopProducts.Should().HaveCount(1);
        result.TopProducts[0].ProductName.Should().Be("Entrecôte Grillée");
        result.TopProducts[0].QuantitySold.Should().Be(2);
        result.TopProducts[0].TotalSalesTtc.Should().Be(50.00m);
        result.TopProducts[0].PercentageOfTotal.Should().Be(100.0m);

        // Payment method
        result.PaymentMethods.Should().HaveCount(1);
        result.PaymentMethods[0].Method.Should().Be(PaymentMethod.CreditCard);
        result.PaymentMethods[0].TotalAmount.Should().Be(50.00m);
    }

    [Fact]
    public async Task GetFinancialDashboardAsync_SegmentsServicesByHour()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "DashDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);

        var lunchTime = new DateTimeOffset(2026, 9, 3, 13, 0, 0, TimeSpan.Zero);
        var dinnerTime = new DateTimeOffset(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

        var lunchOrder = new Order
        {
            TableNumber = "T1",
            Status = OrderStatus.Paid,
            CreatedAtUtc = lunchTime
        };
        lunchOrder.Items.Add(new OrderItem
        {
            ProductName = "Menu du Jour",
            Quantity = 1,
            UnitPrice = Money.FromCents(1800),
            TaxRatePercent = 10.0m
        });

        var dinnerOrder = new Order
        {
            TableNumber = "T2",
            Status = OrderStatus.Paid,
            CreatedAtUtc = dinnerTime
        };
        dinnerOrder.Items.Add(new OrderItem
        {
            ProductName = "Côte de Bœuf",
            Quantity = 1,
            UnitPrice = Money.FromCents(3200),
            TaxRatePercent = 10.0m
        });

        dbContext.Orders.AddRange(lunchOrder, dinnerOrder);
        await dbContext.SaveChangesAsync();

        var service = new FinancialDashboardService(dbContext);

        // Act
        var result = await service.GetFinancialDashboardAsync(new FinancialDashboardFilterDto(
            lunchTime.Date,
            lunchTime.Date.AddDays(1)
        ));

        // Assert
        var lunch = result.Services.FirstOrDefault(s => s.ServiceName.Contains("Midi"));
        var dinner = result.Services.FirstOrDefault(s => s.ServiceName.Contains("Soir"));

        lunch.Should().NotBeNull();
        dinner.Should().NotBeNull();

        lunch!.SalesTtc.Should().Be(18.00m);
        dinner!.SalesTtc.Should().Be(32.00m);
    }
}
