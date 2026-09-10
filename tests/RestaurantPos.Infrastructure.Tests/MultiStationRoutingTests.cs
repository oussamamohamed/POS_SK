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

public class MultiStationRoutingTests
{
    private static readonly string[] ExpectedStations = ["STATION-BAR", "STATION-HOT", "STATION-PASTRY"];

    [Fact]
    public async Task SplitAndRouteOrderAsyncRoutesThreeCourseMealToThreeDistinctStations()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "MultiStationTestDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);
        var routingService = new KitchenRoutingService(dbContext);

        var cocktail = new Product { Name = "Margarita", CategoryId = "CAT-DRINKS", Price = Money.FromDecimal(9m) };
        var burger = new Product { Name = "Burger Gourmet", CategoryId = "CAT-MAINS", Price = Money.FromDecimal(18m) };
        var dessert = new Product { Name = "Tiramisu", CategoryId = "CAT-DESSERTS", Price = Money.FromDecimal(7m) };

        dbContext.Products.Add(cocktail);
        dbContext.Products.Add(burger);
        dbContext.Products.Add(dessert);

        var order = new Order { TableNumber = "T10" };
        order.Items.Add(new OrderItem { ProductId = cocktail.Id, ProductName = cocktail.Name, Quantity = 2 });
        order.Items.Add(new OrderItem { ProductId = burger.Id, ProductName = burger.Name, Quantity = 2 });
        order.Items.Add(new OrderItem { ProductId = dessert.Id, ProductName = dessert.Name, Quantity = 2 });

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync();

        // Act
        var tickets = await routingService.SplitAndRouteOrderAsync(order.Id);

        // Assert
        tickets.Should().HaveCount(3);
        tickets.Select(t => t.StationId).Should().BeEquivalentTo(ExpectedStations);
    }
}
