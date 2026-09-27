using System;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class TableTransferAndMergeTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task TransferTable_MovesActiveOrderToTargetTable_AndFreesSourceTable()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var service = new TableManagementService(db);

        var order = new Order
        {
            TableNumber = "T1",
            Status = OrderStatus.Open,
            Items =
            [
                new OrderItem { ProductName = "Burger", Quantity = 2, UnitPrice = Money.FromEuros(15m), TaxRatePercent = 10m },
                new OrderItem { ProductName = "Bière", Quantity = 2, UnitPrice = Money.FromEuros(6m), TaxRatePercent = 20m }
            ]
        };
        db.Orders.Add(order);

        db.DiningTables.AddRange(
            new DiningTable { TableNumber = "T1", Capacity = 2, Status = TableStatus.Occupied, CoversCount = 2, ActiveOrderId = order.Id, AssignedWaiterName = "Alexandre" },
            new DiningTable { TableNumber = "T4", Capacity = 4, Status = TableStatus.Free, CoversCount = 0 }
        );
        await db.SaveChangesAsync();

        // Act
        var result = await service.TransferTableAsync("T1", "T4");

        // Assert
        result.Should().BeTrue();

        var t1 = await db.DiningTables.FindAsync(["T1"]);
        var t4 = await db.DiningTables.FindAsync(["T4"]);

        t1!.Status.Should().Be(TableStatus.Free);
        t1.ActiveOrderId.Should().BeNull();

        t4!.Status.Should().Be(TableStatus.Occupied);
        t4.ActiveOrderId.Should().Be(order.Id);
        t4.CoversCount.Should().Be(2);

        var updatedOrder = await db.Orders.FindAsync([order.Id]);
        updatedOrder!.TableNumber.Should().Be("T4");

        var log = await db.TableTransferLogs.FirstOrDefaultAsync();
        log.Should().NotBeNull();
        log!.SourceTableNumber.Should().Be("T1");
        log.TargetTableNumber.Should().Be("T4");
        log.IsMerge.Should().BeFalse();
    }

    [Fact]
    public async Task MergeTables_CombinesItemsFromSourceToTarget_AndCancelsSourceOrder()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var service = new TableManagementService(db);

        var orderT2 = new Order
        {
            TableNumber = "T2",
            Status = OrderStatus.Open,
            Items =
            [
                new OrderItem { ProductName = "Salade César", Quantity = 1, UnitPrice = Money.FromEuros(12m), TaxRatePercent = 10m }
            ]
        };
        var orderT3 = new Order
        {
            TableNumber = "T3",
            Status = OrderStatus.Open,
            Items =
            [
                new OrderItem { ProductName = "Pizza Royale", Quantity = 1, UnitPrice = Money.FromEuros(14m), TaxRatePercent = 10m }
            ]
        };
        db.Orders.AddRange(orderT2, orderT3);

        db.DiningTables.AddRange(
            new DiningTable { TableNumber = "T2", Capacity = 2, Status = TableStatus.Occupied, CoversCount = 1, ActiveOrderId = orderT2.Id },
            new DiningTable { TableNumber = "T3", Capacity = 4, Status = TableStatus.Occupied, CoversCount = 2, ActiveOrderId = orderT3.Id }
        );
        await db.SaveChangesAsync();

        // Act
        var result = await service.MergeTablesAsync("T2", "T3");

        // Assert
        result.Should().BeTrue();

        var t2 = await db.DiningTables.FindAsync(["T2"]);
        var t3 = await db.DiningTables.FindAsync(["T3"]);

        t2!.Status.Should().Be(TableStatus.Free);
        t2.ActiveOrderId.Should().BeNull();

        t3!.Status.Should().Be(TableStatus.Occupied);
        t3.CoversCount.Should().Be(3);

        var updatedT3Order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderT3.Id);
        updatedT3Order!.Items.Should().HaveCount(2);
        updatedT3Order.TotalTtc.ToDecimal().Should().Be(26m);

        var log = await db.TableTransferLogs.FirstOrDefaultAsync(l => l.IsMerge);
        log.Should().NotBeNull();
        log!.SourceTableNumber.Should().Be("T2");
        log.TargetTableNumber.Should().Be("T3");
    }
}
