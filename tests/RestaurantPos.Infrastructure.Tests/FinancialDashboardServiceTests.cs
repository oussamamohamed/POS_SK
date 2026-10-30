using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public sealed class FinancialDashboardServiceTests
{
    [Fact]
    public async Task GetTakingsAsync_BucketsByLocalHourAndComparesWithSameDayLastWeek()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("TakingsDb_" + Guid.NewGuid().ToString("N")).Options;
        using var db = new AppDbContext(options);
        var dayStart = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        var now = dayStart.AddHours(19);

        void Receipt(DateTimeOffset at, int cents, string number)
        {
            var order = new Order { TableNumber = "T1", Status = OrderStatus.Paid, CreatedAtUtc = at };
            order.Items.Add(new OrderItem { ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(cents), TaxRatePercent = 10m });
            db.Orders.Add(order);
            db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = "T01", ReceiptNumber = number, OrderId = order.Id, SequenceNumber = db.FiscalReceipts.Local.Count + 1, TotalTtcAmount = Money.FromCents(cents), TotalHtAmount = Money.FromCents(cents), CreatedAtUtc = at });
        }
        Receipt(dayStart.AddHours(11).AddMinutes(30), 2000, "A");   // 12 h 30 locale (UTC+1)
        Receipt(dayStart.AddHours(18).AddMinutes(10), 3000, "B");   // 19 h 10 locale
        Receipt(dayStart.AddDays(-7).AddHours(18), 1000, "C");      // semaine passée, avant l'heure courante
        Receipt(dayStart.AddDays(-7).AddHours(21), 4000, "D");      // semaine passée, après l'heure courante
        await db.SaveChangesAsync();

        var report = await new FinancialDashboardService(db).GetTakingsAsync(dayStart, dayStart.AddDays(1), tzOffsetMinutes: 60, nowUtc: now);

        report.TotalTtc.Should().Be(50.00m);
        report.Granularity.Should().Be("hour");
        report.Compare.Should().Be("lastWeek");
        report.Series.Should().HaveCount(24);
        report.Series[12].Current.Should().Be(20.00m);
        report.Series[19].Current.Should().Be(30.00m);
        report.Series[19].Compare.Should().Be(10.00m);
        report.Series[22].Compare.Should().Be(40.00m);
        report.CompareTotalTtc.Should().Be(50.00m);
        report.CompareByNowTtc.Should().Be(10.00m);
        report.TablesTurned.Should().Be(2);
    }

    [Fact]
    public async Task GetTakingsAsync_OverSeveralDays_ComparesWithThePreviousPeriodByDay()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("TakingsDays_" + Guid.NewGuid().ToString("N")).Options);
        var start = new DateTimeOffset(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
        void Receipt(DateTimeOffset at, int cents, string number)
        {
            var order = new Order { TableNumber = "T1", Status = OrderStatus.Paid, CreatedAtUtc = at };
            order.Items.Add(new OrderItem { ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(cents), TaxRatePercent = 10m });
            db.Orders.Add(order);
            db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = "T01", ReceiptNumber = number, OrderId = order.Id, SequenceNumber = db.FiscalReceipts.Local.Count + 1, TotalTtcAmount = Money.FromCents(cents), TotalHtAmount = Money.FromCents(cents), CreatedAtUtc = at });
        }
        Receipt(start.AddHours(12), 1000, "A");                  // jour 0
        Receipt(start.AddDays(2).AddHours(12), 3000, "B");       // jour 2
        Receipt(start.AddDays(-3).AddHours(12), 500, "C");       // période précédente, jour 0
        await db.SaveChangesAsync();

        var report = await new FinancialDashboardService(db).GetTakingsAsync(start, start.AddDays(3), 0, start.AddDays(10));

        report.Granularity.Should().Be("day");
        report.Compare.Should().Be("previous");
        report.Series.Select(p => p.Label).Should().Equal("10-10", "10-11", "10-12");
        report.Series.Select(p => p.Current).Should().Equal(10.00m, 0m, 30.00m);
        report.Series.Select(p => p.Compare).Should().Equal(5.00m, 0m, 0m);
        report.CompareTotalTtc.Should().Be(5.00m);
        report.CompareByNowTtc.Should().Be(5.00m);
    }

    [Fact]
    public async Task GetTakingsAsync_AWeekAcrossADaylightSavingChange_StillHasSevenDays()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("TakingsDst_" + Guid.NewGuid().ToString("N")).Options);
        // Semaine locale du 24 au 30 octobre 2026 à Paris : le 25 dure 25 h, donc 7 jours + 1 h en UTC.
        var from = new DateTimeOffset(2026, 10, 23, 22, 0, 0, TimeSpan.Zero);
        var to = new DateTimeOffset(2026, 10, 30, 23, 0, 0, TimeSpan.Zero);

        var report = await new FinancialDashboardService(db).GetTakingsAsync(from, to, 120, to);

        report.Series.Should().HaveCount(7);
    }

    [Fact]
    public async Task GetTakingsAsync_BreaksDownTipsDiscountsCompsVoidsCategoriesAndVat()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("TakingsDetail_" + Guid.NewGuid().ToString("N")).Options);
        var day = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        var at = day.AddHours(12);
        db.Categories.Add(new Category { Id = "PLATS", Name = "Plats" });
        db.Categories.Add(new Category { Id = "BOISSONS", Name = "Boissons" });
        var plat = new Product { Name = "Plat", CategoryId = "PLATS", Price = Money.FromCents(2000) };
        var boisson = new Product { Name = "Boisson", CategoryId = "BOISSONS", Price = Money.FromCents(500) };
        db.Products.AddRange(plat, boisson);

        var order = new Order { TableNumber = "T1", Status = OrderStatus.Paid, CreatedAtUtc = at, TipAmount = Money.FromCents(300), GlobalDiscountType = DiscountType.Percentage, GlobalDiscountValue = 10m };
        order.Items.Add(new OrderItem { ProductId = plat.Id, ProductName = "Plat", Quantity = 2, UnitPrice = Money.FromCents(2000), TaxRatePercent = 10m });      // 40,00
        order.Items.Add(new OrderItem { ProductId = boisson.Id, ProductName = "Boisson", Quantity = 2, UnitPrice = Money.FromCents(500), TaxRatePercent = 20m, IsComp = true }); // offert 10,00
        db.Orders.Add(order);                                                                                                                                       // sous-total 40,00, remise 10 % = 4,00, total 36,00
        var receipt = new FiscalReceipt { TerminalId = "T01", ReceiptNumber = "R1", OrderId = order.Id, SequenceNumber = 1, TotalTtcAmount = Money.FromCents(3600), TotalHtAmount = Money.FromCents(3273), TaxBreakdownJson = "{\"10\":327}", HtBreakdownJson = "{\"10\":3273}", CreatedAtUtc = at };
        receipt.Tenders.Add(new PaymentTender { Method = PaymentMethod.CreditCard, Amount = Money.FromCents(3600), Tendered = Money.FromCents(3600) });
        db.FiscalReceipts.Add(receipt);

        // Un second ticket annulé par un avoir : n'entre ni dans le total ni dans la TVA, mais dans les annulations.
        var cancelledOrder = new Order { TableNumber = "T2", Status = OrderStatus.Paid, CreatedAtUtc = at };
        cancelledOrder.Items.Add(new OrderItem { ProductId = plat.Id, ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(2000), TaxRatePercent = 10m });
        db.Orders.Add(cancelledOrder);
        var original = new FiscalReceipt { TerminalId = "T01", ReceiptNumber = "R2", OrderId = cancelledOrder.Id, SequenceNumber = 2, TotalTtcAmount = Money.FromCents(2000), TotalHtAmount = Money.FromCents(1818), TaxBreakdownJson = "{\"10\":182}", CreatedAtUtc = at };
        db.FiscalReceipts.Add(original);
        db.FiscalReceipts.Add(new FiscalReceipt { TerminalId = "T01", ReceiptNumber = "R3", OrderId = cancelledOrder.Id, SequenceNumber = 3, TotalTtcAmount = Money.FromCents(-2000), TotalHtAmount = Money.FromCents(-1818), TaxBreakdownJson = "{\"10\":-182}", VoidedReceiptId = original.Id, CreatedAtUtc = at });
        await db.SaveChangesAsync();

        var service = new FinancialDashboardService(db);
        var report = await service.GetTakingsAsync(day, day.AddDays(1), 0, at);

        report.TotalTtc.Should().Be(36.00m);
        report.TipsTotalTtc.Should().Be(3.00m);
        report.DiscountsTtc.Should().Be(4.00m);
        report.CompsTtc.Should().Be(10.00m);
        report.VoidsCount.Should().Be(1);
        report.VoidsTtc.Should().Be(20.00m);
        report.Categories.Select(c => c.Name).Should().Equal("Plats");
        report.Categories[0].SalesTtc.Should().Be(36.00m);
        report.TopProducts.Single().ProductName.Should().Be("Plat");
        report.Vat.Should().ContainSingle().Which.Should().BeEquivalentTo(new VatLineDto(10m, 32.73m, 3.27m));

        var receipts = await service.GetReceiptsAsync(day, day.AddDays(1));
        receipts.Should().HaveCount(3);
        receipts.Single(r => r.ReceiptNumber == "R3").IsCredit.Should().BeTrue();
        receipts.Single(r => r.ReceiptNumber == "R2").IsCancelled.Should().BeTrue();
        receipts.Single(r => r.ReceiptNumber == "R1").Methods.Should().ContainSingle();
    }

    [Fact]
    public async Task GetJournalAsync_ReturnsOnlyEntriesSinceTheGivenDayStartNewestFirst()
    {
        using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase("Journal_" + Guid.NewGuid().ToString("N")).Options);
        var start = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero);
        TransactionJournalEntry Entry(string type, DateTimeOffset at, long seq) => new() { LocalSequence = seq, OccurredAtUtc = at, IdempotencyKey = type + seq, EventType = type, PayloadJson = "{}", EntryHash = "h" + seq };
        db.JournalEntries.AddRange(Entry("OLD", start.AddHours(-1), 1), Entry("A", start.AddHours(9), 2), Entry("B", start.AddHours(10), 3));
        await db.SaveChangesAsync();

        var entries = await new FinancialDashboardService(db).GetJournalAsync(start);

        entries.Select(e => e.EventType).Should().Equal("B", "A");
    }

    [Fact]
    public async Task GetFinancialDashboardAsync_CalculatesAccurateKpisAndAverages()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "DashDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);

        var now = new DateTimeOffset(2026, 9, 3, 12, 30, 0, TimeSpan.Zero);

        // Add table
        dbContext.DiningTables.Add(new DiningTable
        {
            TableNumber = "T10",
            Capacity = 4,
            CoversCount = 2,
            AssignedWaiterName = "Sophie Martin"
        });

        // Add Order with 2 covers
        var order = new Order
        {
            TableNumber = "T10",
            Status = OrderStatus.Paid,
            CreatedAtUtc = now
        };

        order.Items.Add(new OrderItem
        {
            ProductName = "Entrecôte Grillée",
            Quantity = 2,
            UnitPrice = Money.FromCents(2500), // 25.00 € each => 50.00 €
            TaxRatePercent = 10.0m
        });

        dbContext.Orders.Add(order);

        // Add Fiscal Receipt for the order
        var receipt = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "T-20260903-0010",
            OrderId = order.Id,
            SequenceNumber = 10,
            TotalTtcAmount = Money.FromCents(5000), // 50.00 €
            TotalHtAmount = Money.FromCents(4545),  // ~45.45 €
            CreatedAtUtc = now
        };

        receipt.Tenders.Add(new PaymentTender
        {
            FiscalReceiptId = receipt.Id,
            Method = PaymentMethod.CreditCard,
            Amount = Money.FromCents(5000),
            Tendered = Money.FromCents(5000)
        });

        dbContext.FiscalReceipts.Add(receipt);
        await dbContext.SaveChangesAsync();

        var service = new FinancialDashboardService(dbContext);

        // Act
        var result = await service.GetFinancialDashboardAsync(new FinancialDashboardFilterDto(
            now.AddDays(-1),
            now.AddDays(1)
        ));

        // Assert
        result.Kpis.TotalSalesTtc.Should().Be(50.00m);
        result.Kpis.TotalOrdersCount.Should().Be(1);
        result.Kpis.TotalCoversCount.Should().Be(2);
        result.Kpis.AverageOrderTtc.Should().Be(50.00m);
        result.Kpis.AverageCoverTtc.Should().Be(25.00m);

        // Service Midi
        var midiService = result.Services.FirstOrDefault(s => s.ServiceName.Contains("Midi"));
        midiService.Should().NotBeNull();
        midiService!.SalesTtc.Should().Be(50.00m);
        midiService.OrdersCount.Should().Be(1);

        // Top product
        result.TopProducts.Should().HaveCount(1);
        result.TopProducts[0].ProductName.Should().Be("Entrecôte Grillée");
        result.TopProducts[0].QuantitySold.Should().Be(2);
        result.TopProducts[0].TotalSalesTtc.Should().Be(50.00m);
        result.TopProducts[0].PercentageOfTotal.Should().Be(100.0m);

        // Payment method
        result.PaymentMethods.Should().HaveCount(1);
        result.PaymentMethods[0].Method.Should().Be(PaymentMethod.CreditCard);
        result.PaymentMethods[0].TotalAmount.Should().Be(50.00m);
    }

    [Fact]
    public async Task GetFinancialDashboardAsync_VoidedSale_ExcludesOriginalAndCreditNote()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "DashDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);

        var now = new DateTimeOffset(2026, 9, 3, 12, 30, 0, TimeSpan.Zero);

        var kept = new Order { TableNumber = "T1", Status = OrderStatus.Paid, CreatedAtUtc = now };
        var voided = new Order { TableNumber = "T2", Status = OrderStatus.Cancelled, CreatedAtUtc = now };
        dbContext.Orders.AddRange(kept, voided);

        FiscalReceipt Receipt(Guid orderId, long sequence, long cents, Guid? voidedReceiptId = null)
        {
            var r = new FiscalReceipt
            {
                TerminalId = "POS01",
                ReceiptNumber = $"POS01-{sequence:D6}",
                OrderId = orderId,
                SequenceNumber = sequence,
                TotalTtcAmount = Money.FromCents(cents),
                TotalHtAmount = Money.FromCents(cents * 10 / 11),
                CreatedAtUtc = now,
                VoidedReceiptId = voidedReceiptId
            };
            r.Tenders.Add(new PaymentTender { FiscalReceiptId = r.Id, Method = PaymentMethod.CreditCard, Amount = Money.FromCents(cents), Tendered = Money.FromCents(cents) });
            return r;
        }

        var original = Receipt(voided.Id, 2, 3000);
        dbContext.FiscalReceipts.AddRange(
            Receipt(kept.Id, 1, 5000),
            original,
            Receipt(voided.Id, 3, -3000, voidedReceiptId: original.Id));
        await dbContext.SaveChangesAsync();

        var result = await new FinancialDashboardService(dbContext).GetFinancialDashboardAsync(new FinancialDashboardFilterDto(
            now.AddDays(-1),
            now.AddDays(1)
        ));

        result.Kpis.TotalSalesTtc.Should().Be(50.00m);
        result.Kpis.TotalOrdersCount.Should().Be(1);
        result.PaymentMethods.Should().ContainSingle().Which.TotalAmount.Should().Be(50.00m);
    }

    [Fact]
    public async Task GetFinancialDashboardAsync_SegmentsServicesByHour()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "DashDb_" + Guid.NewGuid().ToString("N"))
            .Options;

        using var dbContext = new AppDbContext(options);

        var lunchTime = new DateTimeOffset(2026, 9, 3, 13, 0, 0, TimeSpan.Zero);
        var dinnerTime = new DateTimeOffset(2026, 9, 3, 20, 0, 0, TimeSpan.Zero);

        var lunchOrder = new Order
        {
            TableNumber = "T1",
            Status = OrderStatus.Paid,
            CreatedAtUtc = lunchTime
        };
        lunchOrder.Items.Add(new OrderItem
        {
            ProductName = "Menu du Jour",
            Quantity = 1,
            UnitPrice = Money.FromCents(1800),
            TaxRatePercent = 10.0m
        });

        var dinnerOrder = new Order
        {
            TableNumber = "T2",
            Status = OrderStatus.Paid,
            CreatedAtUtc = dinnerTime
        };
        dinnerOrder.Items.Add(new OrderItem
        {
            ProductName = "Côte de Bœuf",
            Quantity = 1,
            UnitPrice = Money.FromCents(3200),
            TaxRatePercent = 10.0m
        });

        dbContext.Orders.AddRange(lunchOrder, dinnerOrder);
        await dbContext.SaveChangesAsync();

        var service = new FinancialDashboardService(dbContext);

        // Act
        var result = await service.GetFinancialDashboardAsync(new FinancialDashboardFilterDto(
            lunchTime.Date,
            lunchTime.Date.AddDays(1)
        ));

        // Assert
        var lunch = result.Services.FirstOrDefault(s => s.ServiceName.Contains("Midi"));
        var dinner = result.Services.FirstOrDefault(s => s.ServiceName.Contains("Soir"));

        lunch.Should().NotBeNull();
        dinner.Should().NotBeNull();

        lunch!.SalesTtc.Should().Be(18.00m);
        dinner!.SalesTtc.Should().Be(32.00m);
    }
}
