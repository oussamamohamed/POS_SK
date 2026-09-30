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

public class ClosureRulesTests
{
    private static (NF525FiscalAuditService Fiscal, AppDbContext Db) Create()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("Closure_" + Guid.NewGuid().ToString("N")).Options);
        return (new NF525FiscalAuditService(db), db);
    }

    private static Order AddOrder(AppDbContext db, string table, long unitCents, OrderStatus status = OrderStatus.Open, bool comp = false)
    {
        var order = new Order { TableNumber = table, Status = status };
        order.Items.Add(new OrderItem { ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(unitCents), IsComp = comp });
        db.Orders.Add(order);
        db.SaveChanges();
        return order;
    }

    [Fact]
    public async Task OpenOrders_ListsUnpaidTablesAndPartialPayments_WithRemaining()
    {
        var (fiscal, db) = Create();
        AddOrder(db, "T5", 1250);
        var partial = AddOrder(db, "T6", 2000);
        var receipt = new FiscalReceipt { TerminalId = "T01", ReceiptNumber = "T01-000001", OrderId = partial.Id, TotalTtcAmount = Money.FromCents(800) };
        receipt.Tenders.Add(new PaymentTender { Method = PaymentMethod.Cash, Amount = Money.FromCents(800) });
        db.FiscalReceipts.Add(receipt);
        AddOrder(db, "T7", 900, OrderStatus.Paid);
        AddOrder(db, "T8", 900, OrderStatus.Cancelled);
        db.SaveChanges();

        var open = await fiscal.FindOpenOrdersAsync();

        open.Select(o => (o.Label, o.RemainingTtcCents)).Should().Equal(("T5", 1250L), ("T6", 1200L));
    }

    [Fact]
    public async Task OpenOrders_IgnoresEmptyZeroRemainingAndVoidedHeld_LabelsHeldByCustomer()
    {
        var (fiscal, db) = Create();
        db.Orders.Add(new Order { TableNumber = "T9" });                         // table ouverte sans article
        db.SaveChanges();
        AddOrder(db, "T10", 1500, comp: true);                                    // entièrement offerte
        var voided = AddOrder(db, "Comptoir", 650);
        db.HeldOrders.Add(new HeldOrder { TerminalId = "T01", OrderId = voided.Id, CustomerLabel = "Client 1", OrderSnapshotJson = "{}", IsVoided = true });
        var held = AddOrder(db, "Comptoir", 700);
        db.HeldOrders.Add(new HeldOrder { TerminalId = "T01", OrderId = held.Id, CustomerLabel = "Client 2", OrderSnapshotJson = "{}" });
        db.SaveChanges();

        var open = await fiscal.FindOpenOrdersAsync();

        open.Should().ContainSingle().Which.Label.Should().Be("Client 2");
    }

    [Fact]
    public async Task ClosedPeriod_OwnTerminalAndMainTerminalClosures()
    {
        var (fiscal, db) = Create();
        var end = new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero);
        db.DailyFiscalClosures.Add(new DailyFiscalClosure { TerminalId = "POS_MAIN_TERM", ClosureSequence = 1, PeriodStartUtc = end.AddDays(-1), PeriodEndUtc = end, SignatureHash = "h", PreviousSignatureHash = "g" });
        db.SaveChanges();

        (await fiscal.IsInClosedPeriodAsync("T01", end.AddMinutes(-5))).Should().BeTrue();   // couvert par la Z du terminal principal
        (await fiscal.IsInClosedPeriodAsync("T01", end.AddMinutes(5))).Should().BeFalse();
        (await fiscal.IsInClosedPeriodAsync("POS_MAIN_TERM", end)).Should().BeTrue();
    }

    [Fact]
    public async Task ClosedPeriod_OtherTerminalClosureDoesNotCover()
    {
        var (fiscal, db) = Create();
        var end = DateTimeOffset.UtcNow;
        db.DailyFiscalClosures.Add(new DailyFiscalClosure { TerminalId = "T02", ClosureSequence = 1, PeriodStartUtc = end.AddDays(-1), PeriodEndUtc = end, SignatureHash = "h", PreviousSignatureHash = "g" });
        db.SaveChanges();
        (await fiscal.IsInClosedPeriodAsync("T01", end.AddMinutes(-5))).Should().BeFalse();
    }
}
