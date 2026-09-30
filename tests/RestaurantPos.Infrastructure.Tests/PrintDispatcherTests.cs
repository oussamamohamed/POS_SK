using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class PrintDispatcherTests
{
    private static (PrintDispatcher Dispatcher, AppDbContext Db) Create()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("Dispatch_" + Guid.NewGuid().ToString("N")).Options);
        var dispatcher = new PrintDispatcher(db, new PrintQueue(db, new PrintSignal(), TimeProvider.System), new RestaurantSettingsService(db), new ReportPrintDataService(db), TimeProvider.System, NullLogger<PrintDispatcher>.Instance);
        return (dispatcher, db);
    }

    private static PrinterConfiguration Printer(AppDbContext db, string name, string[] stations, bool drawer = false, bool active = true)
    {
        var p = new PrinterConfiguration { Name = name, IpAddress = "10.0.0.1", OpenCashDrawerOnReceipt = drawer, IsActive = active, AssignedStationIds = [.. stations] };
        db.PrinterConfigurations.Add(p);
        db.SaveChanges();
        return p;
    }

    private static Order PaidCounterOrder(AppDbContext db)
    {
        var order = new Order { TableNumber = "Comptoir", Destination = OrderDestination.Takeaway, PickupNumber = "A-01", Items = { new OrderItem { ProductName = "Wrap", Quantity = 1, UnitPrice = Money.FromCents(650) } } };
        db.Orders.Add(order);
        db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = "T01", ReceiptNumber = "T01-000001", OrderId = order.Id, TotalTtcAmount = Money.FromCents(650), TotalHtAmount = Money.FromCents(591), TaxBreakdownJson = "{}", SignatureHash = "sig" });
        db.SaveChanges();
        return order;
    }

    [Fact]
    public async Task CounterSale_PickupVoucherByDefault_OnReceiptStationPrinter()
    {
        var (d, db) = Create();
        var caisse = Printer(db, "Caisse", ["RECEIPT"], drawer: true);
        var order = PaidCounterOrder(db);

        (await d.QueueCounterSaleAsync(order.Id, "T01", "T01-000001", withFiscalReceipt: false, hasCash: false)).Should().BeTrue();

        var job = db.PrintJobs.Single();
        job.PrinterId.Should().Be(caisse.Id);
        job.Kind.Should().Be(PrintJobKind.PickupVoucher);
        job.OpenCashDrawer.Should().BeFalse();
    }

    [Fact]
    public async Task CounterSale_FiscalReceiptRequested_CombinedTicket_DrawerWithCash()
    {
        var (d, db) = Create();
        Printer(db, "Caisse", ["RECEIPT"], drawer: true);
        var order = PaidCounterOrder(db);

        await d.QueueCounterSaleAsync(order.Id, "T01", "T01-000001", withFiscalReceipt: true, hasCash: true);

        var job = db.PrintJobs.Single();
        job.Kind.Should().Be(PrintJobKind.Receipt);
        job.OpenCashDrawer.Should().BeTrue();
        TicketDocumentJson.Deserialize(job.DocumentJson).Lines.OfType<TicketSeparator>().Should().Contain(s => s.Cut);
    }

    [Fact]
    public async Task CounterSale_CashButPrinterWithoutDrawer_NoKick()
    {
        var (d, db) = Create();
        Printer(db, "Caisse", ["RECEIPT"], drawer: false);
        await d.QueueCounterSaleAsync(PaidCounterOrder(db).Id, "T01", "T01-000001", false, hasCash: true);
        db.PrintJobs.Single().OpenCashDrawer.Should().BeFalse();
    }

    [Fact]
    public async Task CounterSale_DeviceReceiptPrinter_WinsOverReceiptStation()
    {
        var (d, db) = Create();
        Printer(db, "A-Caisse", ["RECEIPT"]);
        var own = Printer(db, "Caisse terrasse", []);
        db.Devices.Add(new Device { Name = "iPad terrasse", TerminalId = "T01", TokenHash = "h", ReceiptPrinterId = own.Id });
        db.SaveChanges();

        await d.QueueCounterSaleAsync(PaidCounterOrder(db).Id, "T01", "T01-000001", false, false);

        db.PrintJobs.Single().PrinterId.Should().Be(own.Id);
    }

    [Fact]
    public async Task CounterSale_NoReceiptPrinter_ReturnsFalse_NoJob()
    {
        var (d, db) = Create();
        Printer(db, "Caisse désactivée", ["RECEIPT"], active: false);
        Printer(db, "Cuisine", ["HOT_KITCHEN"]);
        (await d.QueueCounterSaleAsync(PaidCounterOrder(db).Id, "T01", "T01-000001", false, true)).Should().BeFalse();
        db.PrintJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task CounterSale_UnknownReceipt_ReturnsFalse_NoThrow()
    {
        var (d, db) = Create();
        Printer(db, "Caisse", ["RECEIPT"]);
        (await d.QueueCounterSaleAsync(PaidCounterOrder(db).Id, "T01", "INCONNU", withFiscalReceipt: true, hasCash: false)).Should().BeFalse();
    }

    [Fact]
    public async Task TableReceipt_QueuesReceiptOnly_InReceiptLanguage()
    {
        var (d, db) = Create();
        Printer(db, "Caisse", ["RECEIPT"]);
        var order = PaidCounterOrder(db);
        await new RestaurantSettingsService(db).UpdateAsync(new UpdateRestaurantSettingsRequest("ar", "fr"));

        (await d.QueueTableReceiptAsync(order.Id, "T01", "T01-000001", hasCash: false)).Should().BeTrue();

        var job = db.PrintJobs.Single();
        job.Kind.Should().Be(PrintJobKind.Receipt);
        var doc = TicketDocumentJson.Deserialize(job.DocumentJson);
        doc.Language.Should().Be("ar");
        doc.Lines.OfType<TicketSeparator>().Should().NotContain(s => s.Cut);
    }

    [Fact]
    public async Task Kitchen_OneCopyPerActivePrinterOfStation_InKitchenLanguage()
    {
        var (d, db) = Create();
        var hot1 = Printer(db, "Chaud 1", ["HOT_KITCHEN", "GRILL"]);
        var hot2 = Printer(db, "Chaud 2", ["HOT_KITCHEN"]);
        Printer(db, "Chaud éteinte", ["HOT_KITCHEN"], active: false);
        Printer(db, "Bar", ["BAR"]);
        var order = new Order { TableNumber = "T05", Destination = OrderDestination.EatIn };
        db.Orders.Add(order);
        var ticket = new KitchenTicket { OrderId = order.Id, TableNumber = "T05", StationId = "HOT_KITCHEN", Items = { new KitchenTicketItem { ProductName = "Burger" } } };
        db.KitchenTickets.Add(ticket);
        db.SaveChanges();
        await new RestaurantSettingsService(db).UpdateAsync(new UpdateRestaurantSettingsRequest("fr", "ar"));

        await d.QueueKitchenTicketsAsync([ticket.Id]);

        db.PrintJobs.Select(j => j.PrinterId).Should().BeEquivalentTo([hot1.Id, hot2.Id]);
        db.PrintJobs.ToList().Should().OnlyContain(j => j.Kind == PrintJobKind.KitchenTicket && !j.OpenCashDrawer
            && TicketDocumentJson.Deserialize(j.DocumentJson).Language == "ar");
    }

    [Fact]
    public async Task Kitchen_StationWithoutPrinter_QueuesNothing()
    {
        var (d, db) = Create();
        Printer(db, "Bar", ["BAR"]);
        var order = new Order { TableNumber = "T05" };
        db.Orders.Add(order);
        var ticket = new KitchenTicket { OrderId = order.Id, TableNumber = "T05", StationId = "DESSERT" };
        db.KitchenTickets.Add(ticket);
        db.SaveChanges();

        await d.QueueKitchenTicketsAsync([ticket.Id]);

        db.PrintJobs.Should().BeEmpty();
    }

    [Fact]
    public async Task ZClosure_QueuesReportOnReceiptPrinter_InReceiptLanguage_NoDrawer()
    {
        var (d, db) = Create();
        var caisse = Printer(db, "Caisse", ["RECEIPT"], drawer: true);
        await new RestaurantSettingsService(db).UpdateAsync(new UpdateRestaurantSettingsRequest("en", "fr"));
        var closure = new DailyFiscalClosureDto(Guid.NewGuid(), "POS_MAIN_TERM", 3, 0, 0, 0, new Dictionary<decimal, long>(), new Dictionary<PaymentMethod, long>(), 0, "sig", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(-8), "Gérant");

        (await d.QueueZClosureAsync(closure)).Should().BeTrue();

        var job = db.PrintJobs.Single();
        job.PrinterId.Should().Be(caisse.Id);
        job.Kind.Should().Be(PrintJobKind.Report);
        job.OpenCashDrawer.Should().BeFalse();
        var doc = TicketDocumentJson.Deserialize(job.DocumentJson);
        doc.Language.Should().Be("en");
        ((TicketText)doc.Lines[0]).Text.Should().Be("Z CLOSURE #3");
    }

    [Fact]
    public async Task XReport_NoReceiptPrinter_ReturnsFalse_NoJob()
    {
        var (d, db) = Create();
        var summary = new FiscalSummaryDto("POS_MAIN_TERM", DateTimeOffset.MinValue, DateTimeOffset.UtcNow, 0, 0, 0, new Dictionary<decimal, long>(), new Dictionary<PaymentMethod, long>(), 0);
        (await d.QueueXReportAsync(summary)).Should().BeFalse();
        db.PrintJobs.Should().BeEmpty();
    }
}
