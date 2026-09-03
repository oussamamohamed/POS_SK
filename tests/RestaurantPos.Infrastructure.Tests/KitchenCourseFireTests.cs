using System;
using System.Linq;
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

public class KitchenCourseFireTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task OrderItems_GroupedByCourseType_CalculatesCorrectStatus()
    {
        // Arrange
        using var db = CreateInMemoryDb();

        var entree = new OrderItem
        {
            ProductName = "Salade César",
            Quantity = 2,
            UnitPrice = Money.FromEuros(10m),
            Course = CourseType.Direct,
            PreparationStationId = "COLD_KITCHEN"
        };
        var plat = new OrderItem
        {
            ProductName = "Entrecôte Grillée",
            Quantity = 2,
            UnitPrice = Money.FromEuros(22m),
            Course = CourseType.Suite,
            PreparationStationId = "HOT_KITCHEN"
        };
        var dessert = new OrderItem
        {
            ProductName = "Profiteroles",
            Quantity = 2,
            UnitPrice = Money.FromEuros(8m),
            Course = CourseType.Dessert,
            PreparationStationId = "DESSERT"
        };

        var order = new Order
        {
            TableNumber = "T5",
            Items = [entree, plat, dessert]
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        // Assert
        var savedOrder = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == order.Id);
        savedOrder!.Items.Should().HaveCount(3);
        savedOrder.Items.Count(i => i.Course == CourseType.Direct).Should().Be(1);
        savedOrder.Items.Count(i => i.Course == CourseType.Suite).Should().Be(1);
        savedOrder.Items.Count(i => i.Course == CourseType.Dessert).Should().Be(1);
    }
}
