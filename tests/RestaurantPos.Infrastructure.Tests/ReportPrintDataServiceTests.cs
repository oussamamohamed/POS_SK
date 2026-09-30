using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class ReportPrintDataServiceTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 30, 6, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddHours(16);

    private sealed class Seed
    {
        public AppDbContext Db { get; } = new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("Report_" + Guid.NewGuid().ToString("N")).Options);
        private int _seq;

        public Product Product(string name, string? categoryId)
        {
            var p = new Product { Name = name, CategoryId = categoryId ?? "CAT-DELETED", Price = Money.FromCents(100) };
            Db.Products.Add(p);
            Db.SaveChanges();
            return p;
        }

        public void Category(string id, string name)
        {
            Db.Categories.Add(new Category { Id = id, Name = name });
            Db.SaveChanges();
        }

        public User Server(string name)
        {
            var u = new User { Name = name, PinHash = "x", PinSalt = "x" };
            Db.Users.Add(u);
            Db.SaveChanges();
            return u;
        }

        public Order PaidOrder(DateTimeOffset at, Guid operatorId, long tipCents, params (Product P, int Qty, long UnitCents, bool Comp)[] lines)
        {
            var order = new Order { Status = OrderStatus.Paid, OperatorId = operatorId, TipAmount = Money.FromCents(tipCents) };
            foreach (var l in lines)
                order.Items.Add(new OrderItem { ProductId = l.P.Id, ProductName = l.P.Name, Quantity = l.Qty, UnitPrice = Money.FromCents(l.UnitCents), IsComp = l.Comp });
            Db.Orders.Add(order);
            Db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = "T01", ReceiptNumber = $"T01-{++_seq:D6}", OrderId = order.Id, TotalTtcAmount = Money.FromCents(Math.Max(1, order.TotalTtc.AmountInCents)), CreatedAtUtc = at });
            Db.SaveChanges();
            return order;
        }
    }

    [Fact]
    public async Task Items_GroupedByCategory_SortedByCategoryThenName_OthersLast()
    {
        var s = new Seed();
        s.Category("C1", "Plats"); s.Category("C2", "Desserts"); s.Category("C3", "boissons");
        var tiramisu = s.Product("Tiramisu", "C2");
        var brownie = s.Product("Brownie", "C2");
        var steak = s.Product("Entrecôte", "C1");
        var eau = s.Product("Eau", "C3");
        var ghost = s.Product("Vente Comptoir", null);
        s.PaidOrder(Start.AddHours(1), Guid.Empty, 0, (tiramisu, 2, 700, false), (steak, 1, 2200, false), (brownie, 1, 650, true));
        s.PaidOrder(Start.AddHours(2), Guid.Empty, 0, (tiramisu, 1, 700, false), (eau, 3, 300, false), (ghost, 1, 500, false));

        var data = await new ReportPrintDataService(s.Db).BuildAsync("POS_MAIN_TERM", Start, End);

        data.Categories.Select(c => c.CategoryName).Should().Equal("boissons", "Desserts", "Plats", null);
        var desserts = data.Categories[1];
        desserts.Items.Should().Equal(new ReportItemLine("Brownie", 1, 0), new ReportItemLine("Tiramisu", 3, 2100));
        desserts.SubtotalTtcCents.Should().Be(2100);
        data.Categories[^1].Items.Single().ProductName.Should().Be("Vente Comptoir");
    }

    [Fact]
    public async Task ExcludesCancelledOrders_OutOfPeriod_AndOtherTerminalWhenNotMain()
    {
        var s = new Seed();
        s.Category("C1", "Plats");
        var steak = s.Product("Entrecôte", "C1");
        s.PaidOrder(Start.AddHours(1), Guid.Empty, 0, (steak, 1, 2200, false));
        s.PaidOrder(Start.AddHours(-1), Guid.Empty, 0, (steak, 5, 2200, false));
        var cancelled = s.PaidOrder(Start.AddHours(3), Guid.Empty, 0, (steak, 7, 2200, false));
        cancelled.Status = OrderStatus.Cancelled;
        s.Db.SaveChanges();

        var data = await new ReportPrintDataService(s.Db).BuildAsync("POS_MAIN_TERM", Start, End);
        data.Categories.Single().Items.Single().Quantity.Should().Be(1);

        (await new ReportPrintDataService(s.Db).BuildAsync("T02", Start, End)).Categories.Should().BeEmpty();
    }

    [Fact]
    public async Task Tips_ByServer_SortedByName_UnknownLast_TotalAndOmittedWhenZero()
    {
        var s = new Seed();
        var steak = s.Product("Entrecôte", null);
        var zoe = s.Server("Zoé");
        var alex = s.Server("Alex");
        s.PaidOrder(Start.AddHours(1), zoe.Id, 300, (steak, 1, 2200, false));
        s.PaidOrder(Start.AddHours(2), alex.Id, 150, (steak, 1, 2200, false));
        s.PaidOrder(Start.AddHours(3), zoe.Id, 200, (steak, 1, 2200, false));
        s.PaidOrder(Start.AddHours(4), Guid.Empty, 100, (steak, 1, 2200, false));
        s.PaidOrder(Start.AddHours(5), alex.Id, 0, (steak, 1, 2200, false));

        var data = await new ReportPrintDataService(s.Db).BuildAsync("POS_MAIN_TERM", Start, End);

        data.Tips.Should().Equal(new ReportTipLine("Alex", 150), new ReportTipLine("Zoé", 500), new ReportTipLine(null, 100));
        data.TotalTipsCents.Should().Be(750);
    }
}
