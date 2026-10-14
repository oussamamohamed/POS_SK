using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class HeldOrderStorageServiceTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("HeldOrderTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task HoldOrderAsync_PersistsOrderSnapshotAndMetadata()
    {
        using var db = CreateDbContext();
        var service = new HeldOrderStorageService(db);

        var staffId = Guid.NewGuid();
        var order = new Order
        {
            Id = Guid.NewGuid(),
            TableNumber = "Comptoir",
            Destination = OrderDestination.Takeaway
        };
        order.Items.Add(new OrderItem
        {
            OrderId = order.Id,
            ProductId = Guid.NewGuid(),
            ProductName = "Café",
            Quantity = 2,
            UnitPrice = Money.FromCents(250),
            TaxRatePercent = 10m
        });

        var held = await service.HoldOrderAsync(order, "T01", staffId, "Client 42");

        held.Should().NotBeNull();
        held.TerminalId.Should().Be("T01");
        held.CustomerLabel.Should().Be("Client 42");
        held.Destination.Should().Be(OrderDestination.Takeaway);
        held.ItemCount.Should().Be(2);
        held.TotalTtc.AmountInCents.Should().Be(500);
        held.HeldByStaffId.Should().Be(staffId);
        held.IsRecalled.Should().BeFalse();
        held.IsVoided.Should().BeFalse();

        var inDb = await db.HeldOrders.FindAsync(held.Id);
        inDb.Should().NotBeNull();
        var deserialized = System.Text.Json.JsonSerializer.Deserialize<Order>(inDb!.OrderSnapshotJson);
        deserialized!.Items[0].ProductName.Should().Be("Café");
    }

    [Fact]
    public async Task GetActiveHeldOrdersAsync_FiltersByTerminalAndExcludesRecalledOrVoided()
    {
        using var db = CreateDbContext();
        var service = new HeldOrderStorageService(db);
        var staffId = Guid.NewGuid();

        var order1 = new Order { Id = Guid.NewGuid(), TableNumber = "Comptoir" };
        var order2 = new Order { Id = Guid.NewGuid(), TableNumber = "Comptoir" };
        var order3 = new Order { Id = Guid.NewGuid(), TableNumber = "Comptoir" };
        var orderOtherTerm = new Order { Id = Guid.NewGuid(), TableNumber = "Comptoir" };

        var held1 = await service.HoldOrderAsync(order1, "T01", staffId, "Actif");
        var held2 = await service.HoldOrderAsync(order2, "T01", staffId, "Rappele");
        var held3 = await service.HoldOrderAsync(order3, "T01", staffId, "Annule");
        var heldOther = await service.HoldOrderAsync(orderOtherTerm, "T02", staffId, "Autre terminal");

        await service.RecallOrderAsync(held2.Id);
        await service.VoidHeldOrderAsync(held3.Id, staffId, "Annulation client");

        var activeT01 = await service.GetActiveHeldOrdersAsync("T01");
        activeT01.Should().HaveCount(1);
        activeT01[0].HoldId.Should().Be(held1.Id);
        activeT01[0].CustomerLabel.Should().Be("Actif");

        var allActive = await service.GetActiveHeldOrdersAsync(string.Empty);
        allActive.Should().HaveCount(2);
        allActive.Select(h => h.HoldId).Should().Contain([held1.Id, heldOther.Id]);

        var countT01 = await service.GetHeldOrderCountAsync("T01");
        countT01.Should().Be(1);
    }

    [Fact]
    public async Task RecallOrderAsync_MarksRecalledAndRestoresOrder()
    {
        using var db = CreateDbContext();
        var service = new HeldOrderStorageService(db);
        var staffId = Guid.NewGuid();

        var originalOrder = new Order
        {
            Id = Guid.NewGuid(),
            TableNumber = "Comptoir",
            Destination = OrderDestination.Takeaway
        };
        originalOrder.Items.Add(new OrderItem
        {
            OrderId = originalOrder.Id,
            ProductId = Guid.NewGuid(),
            ProductName = "Burger",
            Quantity = 1,
            UnitPrice = Money.FromCents(1200),
            TaxRatePercent = 10m
        });

        var held = await service.HoldOrderAsync(originalOrder, "T01", staffId, "Client 10");

        var recalled = await service.RecallOrderAsync(held.Id);

        recalled.Should().NotBeNull();
        recalled!.Id.Should().Be(originalOrder.Id);
        recalled.Items.Should().HaveCount(1);
        recalled.Items[0].ProductName.Should().Be("Burger");

        var inDb = await db.HeldOrders.FindAsync(held.Id);
        inDb!.IsRecalled.Should().BeTrue();
        inDb.RecalledAtUtc.Should().NotBeNull();

        // Second recall should return null
        var recallAgain = await service.RecallOrderAsync(held.Id);
        recallAgain.Should().BeNull();
    }

    [Fact]
    public async Task VoidHeldOrderAsync_MarksVoidedWithReasonAndSupervisor()
    {
        using var db = CreateDbContext();
        var service = new HeldOrderStorageService(db);
        var staffId = Guid.NewGuid();
        var supervisorId = Guid.NewGuid();

        var order = new Order { Id = Guid.NewGuid(), TableNumber = "Comptoir", Status = OrderStatus.Open };
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        var held = await service.HoldOrderAsync(order, "T01", staffId, "Client 99");

        var ok = await service.VoidHeldOrderAsync(held.Id, supervisorId, "Client parti sans régler");

        ok.Should().BeTrue();

        var inDb = await db.HeldOrders.FindAsync(held.Id);
        inDb!.IsVoided.Should().BeTrue();
        inDb.VoidedByStaffId.Should().Be(supervisorId);
        inDb.VoidReason.Should().Be("Client parti sans régler");
        inDb.VoidedAtUtc.Should().NotBeNull();

        var cancelledOrder = await db.Orders.FindAsync(order.Id);
        cancelledOrder!.Status.Should().Be(OrderStatus.Cancelled);

        // Already voided, cannot void again or recall
        (await service.VoidHeldOrderAsync(held.Id, supervisorId, "Raison 2")).Should().BeFalse();
        (await service.RecallOrderAsync(held.Id)).Should().BeNull();
    }
}
