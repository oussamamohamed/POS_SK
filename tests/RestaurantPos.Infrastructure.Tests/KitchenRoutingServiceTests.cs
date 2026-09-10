using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class KitchenRoutingServiceTests
{
    [Fact]
    public async Task SplitAndRouteOrderAsyncSegregatesDrinkAndHotFoodItems()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "KdsRoutingDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var routingService = new KitchenRoutingService(dbContext);

        var drinkProd = new Product { Name = "Mojito", CategoryId = "CAT-DRINKS", Price = Money.FromDecimal(8m) };
        var steakProd = new Product { Name = "Entrecôte", CategoryId = "CAT-MAINS", Price = Money.FromDecimal(22m) };

        dbContext.Products.Add(drinkProd);
        dbContext.Products.Add(steakProd);

        var order = new Order { TableNumber = "T05" };
        order.Items.Add(new OrderItem { ProductId = drinkProd.Id, ProductName = drinkProd.Name, Quantity = 2 });
        order.Items.Add(new OrderItem { ProductId = steakProd.Id, ProductName = steakProd.Name, Quantity = 1, SelectedModifiers = ["Saignant"] });

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        // Act
        var tickets = await routingService.SplitAndRouteOrderAsync(order.Id);

        // Assert
        tickets.Should().HaveCount(2);

        var barTicket = tickets.FirstOrDefault(t => t.StationId == "STATION-BAR");
        barTicket.Should().NotBeNull();
        barTicket!.Items.Should().HaveCount(1);
        barTicket.Items[0].ProductName.Should().Be("Mojito");

        var hotTicket = tickets.FirstOrDefault(t => t.StationId == "STATION-HOT");
        hotTicket.Should().NotBeNull();
        hotTicket!.Items.Should().HaveCount(1);
        hotTicket.Items[0].ProductName.Should().Be("Entrecôte");
        hotTicket.Items[0].ModifiersSummary.Should().Be("Saignant");
    }

    [Fact]
    public async Task BumpTicketStateAsyncTransitionsStatusThroughLifecycle()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "KdsBumpDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var routingService = new KitchenRoutingService(dbContext);

        var prod = new Product { Name = "Burger", CategoryId = "CAT-MAINS", Price = Money.FromDecimal(15m) };
        dbContext.Products.Add(prod);

        var order = new Order { TableNumber = "T01" };
        order.Items.Add(new OrderItem { ProductId = prod.Id, ProductName = prod.Name, Quantity = 1 });
        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        var tickets = await routingService.SplitAndRouteOrderAsync(order.Id);
        var ticketId = tickets[0].TicketId;

        // Act & Assert 1: Bump from Pending -> InPreparation
        var bumped1 = await routingService.BumpTicketStateAsync(ticketId);
        bumped1!.Status.Should().Be(TicketStatus.InPreparation);

        // Act & Assert 2: Bump from InPreparation -> Ready
        var bumped2 = await routingService.BumpTicketStateAsync(ticketId);
        bumped2!.Status.Should().Be(TicketStatus.Ready);

        // Act & Assert 3: Bump from Ready -> Served
        var bumped3 = await routingService.BumpTicketStateAsync(ticketId);
        bumped3!.Status.Should().Be(TicketStatus.Served);

        // Act & Assert 4: Recall from Served -> Ready
        var recalled = await routingService.RecallTicketAsync(ticketId);
        recalled!.Status.Should().Be(TicketStatus.Ready);
    }
}
