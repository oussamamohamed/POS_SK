using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class MenuAnalysisServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("MenuDb_" + Guid.NewGuid().ToString("N")).Options);

    // Prix TTC 11,00 € à 10 % de TVA = 10,00 € HT ; coût en euros HT (null = inconnu).
    private static Product AddProduct(AppDbContext db, string name, decimal? cost)
    {
        var product = new Product
        {
            Name = name,
            CategoryId = "PLATS",
            Price = Money.FromCents(1100),
            Cost = cost.HasValue ? Money.FromDecimal(cost.Value) : null
        };
        db.Products.Add(product);
        return product;
    }

    private static Order AddPaidOrder(AppDbContext db, params (Product Product, int Qty, bool Comp)[] lines)
    {
        var order = new Order { Status = OrderStatus.Paid, CreatedAtUtc = Now };
        foreach (var (product, qty, comp) in lines)
        {
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = qty,
                UnitPrice = product.Price,
                TaxRatePercent = 10m,
                IsComp = comp
            });
        }
        db.Orders.Add(order);
        return order;
    }

    [Fact]
    public async Task Classifies_stars_plowhorses_puzzles_and_dogs_and_flags_missing_cost()
    {
        using var db = NewDb();
        db.Categories.Add(new Category { Id = "PLATS", Name = "Plats" });
        var star = AddProduct(db, "Star", 3m);          // marge 7,00
        var plow = AddProduct(db, "Plow", 9m);          // marge 1,00
        var puzzle = AddProduct(db, "Puzzle", 2m);      // marge 8,00
        var dog = AddProduct(db, "Dog", 9.5m);          // marge 0,50
        var noCost = AddProduct(db, "NoCost", null);

        AddPaidOrder(db, (star, 10, false), (plow, 10, false), (puzzle, 1, false), (dog, 1, false), (noCost, 5, false));
        AddPaidOrder(db, (dog, 50, true)); // offert : ni vendu ni compté
        var voided = AddPaidOrder(db, (star, 100, false));
        var original = new FiscalReceipt { TerminalId = "T01", ReceiptNumber = "R1", OrderId = voided.Id, SequenceNumber = 1, TotalTtcAmount = Money.FromCents(110000), TotalHtAmount = Money.FromCents(100000), CreatedAtUtc = Now };
        db.FiscalReceipts.Add(original);
        db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = "T01", ReceiptNumber = "R2", OrderId = voided.Id, SequenceNumber = 2, VoidedReceiptId = original.Id, TotalTtcAmount = Money.FromCents(-110000), TotalHtAmount = Money.FromCents(-100000), CreatedAtUtc = Now });
        await db.SaveChangesAsync();

        var report = await new MenuAnalysisService(db).GetMenuAnalysisAsync(Now.AddDays(-1), Now.AddDays(1));

        var byName = report.Items.ToDictionary(i => i.Name);
        report.Items.Should().HaveCount(5);
        byName["Star"].Quantity.Should().Be(10);
        byName["Star"].Class.Should().Be(MenuClass.Star);
        byName["Plow"].Class.Should().Be(MenuClass.Plowhorse);
        byName["Puzzle"].Class.Should().Be(MenuClass.Puzzle);
        byName["Dog"].Class.Should().Be(MenuClass.Dog);
        byName["Dog"].Quantity.Should().Be(1);
        byName["Star"].UnitMarginHt.Should().Be(7.00m);
        byName["Star"].CategoryName.Should().Be("Plats");
        byName["NoCost"].MissingCost.Should().BeTrue();
        byName["NoCost"].Class.Should().BeNull();
        byName["NoCost"].Popular.Should().BeTrue();
        report.MissingCostCount.Should().Be(1);
        report.AverageUnitMarginHt.Should().Be(4.02m);
        report.PopularityThreshold.Should().Be(0.14m);
    }

    [Fact]
    public async Task Category_filter_recomputes_the_classes_on_the_selection()
    {
        using var db = NewDb();
        db.Categories.Add(new Category { Id = "PLATS", Name = "Plats" });
        db.Categories.Add(new Category { Id = "DESSERTS", Name = "Desserts" });
        var a = AddProduct(db, "PlatA", 3m);
        var b = AddProduct(db, "PlatB", 9m);
        var dessert = AddProduct(db, "Dessert", 1m);
        dessert.CategoryId = "DESSERTS";
        AddPaidOrder(db, (a, 8, false), (b, 8, false), (dessert, 40, false));
        await db.SaveChangesAsync();

        var all = await new MenuAnalysisService(db).GetMenuAnalysisAsync(Now.AddDays(-1), Now.AddDays(1));
        var plats = await new MenuAnalysisService(db).GetMenuAnalysisAsync(Now.AddDays(-1), Now.AddDays(1), "PLATS");

        all.Items.Should().HaveCount(3);
        plats.Items.Select(i => i.Name).Should().BeEquivalentTo("PlatA", "PlatB");
        // Sur les seuls plats, chacun pèse 50 % : tous deux populaires ; PlatA (marge 7,00) dépasse la moyenne (4,00) et PlatB (1,00) non.
        plats.Items.Single(i => i.Name == "PlatA").Class.Should().Be(MenuClass.Star);
        plats.Items.Single(i => i.Name == "PlatB").Class.Should().Be(MenuClass.Plowhorse);
        // Avec les desserts, PlatA et PlatB ne pèsent plus que 8/56 chacun, sous 70 % de la part moyenne (0,7/3 = 23 %).
        all.Items.Single(i => i.Name == "PlatA").Popular.Should().BeFalse();
    }

    [Fact]
    public async Task Ignores_orders_outside_the_period_and_returns_empty_report_without_sales()
    {
        using var db = NewDb();
        var dish = AddProduct(db, "Dish", 3m);
        AddPaidOrder(db, (dish, 2, false));
        await db.SaveChangesAsync();

        var report = await new MenuAnalysisService(db).GetMenuAnalysisAsync(Now.AddDays(10), Now.AddDays(20));

        report.Items.Should().BeEmpty();
        report.AverageUnitMarginHt.Should().BeNull();
    }
}
