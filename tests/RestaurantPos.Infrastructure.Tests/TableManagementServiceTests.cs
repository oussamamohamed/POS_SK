using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class TableManagementServiceTests
{
    [Fact]
    public async Task OpenTableAsyncCreatesOrderAndUpdatesTableState()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TableTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var service = new TableManagementService(dbContext);

        var operatorId = Guid.NewGuid();

        // Act
        var tableDto = await service.OpenTableAsync("T01", 4, operatorId, "Alexandre Dupont");

        // Assert
        tableDto.TableNumber.Should().Be("T01");
        tableDto.Status.Should().Be(TableStatus.Occupied);
        tableDto.CoversCount.Should().Be(4);
        tableDto.AssignedWaiterName.Should().Be("Alexandre Dupont");
        tableDto.ActiveOrderId.Should().NotBeNull();

        var orderInDb = await dbContext.Orders.FindAsync(tableDto.ActiveOrderId!.Value);
        orderInDb.Should().NotBeNull();
        orderInDb!.TableNumber.Should().Be("T01");
    }

    [Fact]
    public async Task TransferTableAsyncMovesOrderToTargetTableAndFreesSource()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TableTransferTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var service = new TableManagementService(dbContext);

        var operatorId = Guid.NewGuid();
        var openedTable = await service.OpenTableAsync("T01", 2, operatorId, "Alexandre");

        dbContext.DiningTables.Add(new DiningTable { TableNumber = "T05", Capacity = 6 });
        await dbContext.SaveChangesAsync();

        // Act
        bool transferSuccess = await service.TransferTableAsync("T01", "T05");

        // Assert
        transferSuccess.Should().BeTrue();

        var sourceTable = await dbContext.DiningTables.FindAsync("T01");
        sourceTable!.Status.Should().Be(TableStatus.Free);
        sourceTable.ActiveOrderId.Should().BeNull();

        var targetTable = await dbContext.DiningTables.FindAsync("T05");
        targetTable!.Status.Should().Be(TableStatus.Occupied);
        targetTable.ActiveOrderId.Should().Be(openedTable.ActiveOrderId);
    }

    [Fact]
    public async Task GetActiveOrderForTableAsync_WithPopulatedOrder_ShouldReturnAllLinesAndTotals()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TableRecallTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var service = new TableManagementService(dbContext);

        var operatorId = Guid.NewGuid();
        var openedTable = await service.OpenTableAsync("T02", 3, operatorId, "Sophie Martin");

        // Add 2 items
        var items = new List<Application.Common.Interfaces.OrderItemInputDto>
        {
            new(Guid.NewGuid(), "Salade César", 2, 9.50m, 10.0m, "COLD", null),
            new(Guid.NewGuid(), "Burger Rossini", 1, 19.50m, 10.0m, "HOT_KITCHEN", ["Cuisson : À Point"])
        };

        await service.AddOrUpdateTableOrderItemsAsync("T02", items);

        // Act
        var recalled = await service.GetActiveOrderForTableAsync("T02");

        // Assert
        recalled.Should().NotBeNull();
        recalled!.TableNumber.Should().Be("T02");
        recalled.CoversCount.Should().Be(3);
        recalled.Lines.Should().HaveCount(2);
        recalled.TotalTtcAmount.Should().Be(38.50m);
        recalled.Lines.Should().ContainSingle(l => l.ProductName == "Salade César" && l.Quantity == 2);
        var burgerLine = recalled.Lines.Single(l => l.ProductName == "Burger Rossini");
        burgerLine.ModifiersSummary.Should().Contain("Cuisson : À Point");
    }

    [Fact]
    public async Task DispatchOrderLinesAsync_ShouldMarkItemsAsDispatched()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TableDispatchTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var service = new TableManagementService(dbContext);

        await service.OpenTableAsync("T03", 2, Guid.NewGuid(), "Alexandre");
        await service.AddOrUpdateTableOrderItemsAsync("T03", [
            new(Guid.NewGuid(), "Pizza Margherita", 1, 12.50m, 10.0m, "HOT", null)
        ]);

        var beforeDispatch = await service.GetActiveOrderForTableAsync("T03");
        beforeDispatch!.Lines[0].IsDispatched.Should().BeFalse();

        // Act
        var dispatchResult = await service.DispatchOrderLinesAsync("T03");

        // Assert
        dispatchResult.Should().BeTrue();
        var afterDispatch = await service.GetActiveOrderForTableAsync("T03");
        afterDispatch!.Lines[0].IsDispatched.Should().BeTrue();
    }

    [Fact]
    public async Task CreateTableAsync_ShouldPersistNewTableWithFreeStatus()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "TableCreateTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var service = new TableManagementService(dbContext);

        // Act
        var created = await service.CreateTableAsync("T09", 6, 120, 80);

        // Assert
        created.Should().NotBeNull();
        created.TableNumber.Should().Be("T09");
        created.Capacity.Should().Be(6);
        created.Status.Should().Be(TableStatus.Free);
        created.PositionX.Should().Be(120);
        created.PositionY.Should().Be(80);

        var inDb = await dbContext.DiningTables.FindAsync("T09");
        inDb.Should().NotBeNull();
        inDb!.Capacity.Should().Be(6);
    }
}
