using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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

public class ReportAccuracyTests
{
    private static AppDbContext CreateInMemoryDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase("ReportAccuracyDb_" + Guid.NewGuid().ToString("N"))
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task ZeroState_XReportAndDashboard_ReturnZeroTotalsAndNoExceptions()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var fiscalService = new NF525FiscalAuditService(db);
        var dashboardService = new FinancialDashboardService(db);

        // Act 1: X-Report on empty database
        var xReport = await fiscalService.GenerateXReportAsync("POS01");

        // Assert X-Report
        xReport.TotalSalesTtcCents.Should().Be(0);
        xReport.TotalSalesHtCents.Should().Be(0);
        xReport.ReceiptCount.Should().Be(0);
        xReport.VatBreakdownCents.Should().BeEmpty();
        xReport.PaymentTotalsCents.Should().BeEmpty();
        xReport.PerpetualGrandTotalCents.Should().Be(0);

        // Act 2: Financial Dashboard on empty database
        var filter = new FinancialDashboardFilterDto(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var dashboard = await dashboardService.GetFinancialDashboardAsync(filter);

        // Assert Dashboard KPIs
        dashboard.Kpis.TotalSalesTtc.Should().Be(0m);
        dashboard.Kpis.TotalSalesHt.Should().Be(0m);
        dashboard.Kpis.TotalOrdersCount.Should().Be(0);
        dashboard.Kpis.TotalCoversCount.Should().Be(0);
        dashboard.Kpis.AverageOrderTtc.Should().Be(0m);
        dashboard.Kpis.AverageCoverTtc.Should().Be(0m);
        dashboard.TopProducts.Should().BeEmpty();
        dashboard.PaymentMethods.Should().BeEmpty();
    }

    [Fact]
    public async Task XReport_MultiRateVatBreakdown_CalculatesExactTtcHtAndVatSums()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var fiscalService = new NF525FiscalAuditService(db);
        var now = DateTimeOffset.UtcNow;

        // Receipt 1: Food (10% VAT) -> 44.00 € TTC = 40.00 € HT + 4.00 € VAT
        var r1 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(4400),
            TotalHtAmount = Money.FromCents(4000),
            TaxBreakdownJson = JsonSerializer.Serialize(new Dictionary<string, long> { ["10"] = 400 }),
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash,
            SignatureHash = "SIG1",
            CreatedAtUtc = now.AddMinutes(-30)
        };
        r1.Tenders.Add(new PaymentTender { Method = PaymentMethod.CreditCard, Amount = Money.FromCents(4400) });

        // Receipt 2: Alcohol (20% VAT) -> 24.00 € TTC = 20.00 € HT + 4.00 € VAT
        // Cold Food / Water (5.5% VAT) -> 10.55 € TTC = 10.00 € HT + 0.55 € VAT
        // Total Receipt 2: 34.55 € TTC = 30.00 € HT + 4.55 € VAT
        var r2 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000002",
            SequenceNumber = 2,
            TotalTtcAmount = Money.FromCents(3455),
            TotalHtAmount = Money.FromCents(3000),
            TaxBreakdownJson = JsonSerializer.Serialize(new Dictionary<string, long> { ["20"] = 400, ["5.5"] = 55 }),
            PreviousSignatureHash = "SIG1",
            SignatureHash = "SIG2",
            CreatedAtUtc = now.AddMinutes(-10)
        };
        r2.Tenders.Add(new PaymentTender { Method = PaymentMethod.Cash, Amount = Money.FromCents(3455) });

        db.FiscalReceipts.AddRange(r1, r2);
        await db.SaveChangesAsync();

        // Act
        var xReport = await fiscalService.GenerateXReportAsync("POS01");

        // Assert totals
        xReport.ReceiptCount.Should().Be(2);
        xReport.TotalSalesTtcCents.Should().Be(4400 + 3455); // 78.55 €
        xReport.TotalSalesHtCents.Should().Be(4000 + 3000);  // 70.00 €

        // Assert VAT Breakdown by rate
        xReport.VatBreakdownCents.Should().ContainKey(10m).WhoseValue.Should().Be(400); // 4.00 €
        xReport.VatBreakdownCents.Should().ContainKey(20m).WhoseValue.Should().Be(400); // 4.00 €
        xReport.VatBreakdownCents.Should().ContainKey(5.5m).WhoseValue.Should().Be(55); // 0.55 €

        // Total VAT must equal TTC - HT
        long totalVat = xReport.VatBreakdownCents.Values.Sum();
        totalVat.Should().Be(855); // 8.55 €
        (xReport.TotalSalesHtCents + totalVat).Should().Be(xReport.TotalSalesTtcCents);
    }

    [Fact]
    public async Task SplitTenders_AcrossMultipleReceipts_YieldsExactPaymentMethodTotals()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var fiscalService = new NF525FiscalAuditService(db);
        var now = DateTimeOffset.UtcNow;

        // Receipt with 3 split tenders: 50 € CC, 30 € Cash, 20 € MealVoucher = 100.00 €
        var r1 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(10000),
            TotalHtAmount = Money.FromCents(9000),
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash,
            SignatureHash = "SIG1",
            CreatedAtUtc = now
        };
        r1.Tenders.Add(new PaymentTender { Method = PaymentMethod.CreditCard, Amount = Money.FromCents(5000) });
        r1.Tenders.Add(new PaymentTender { Method = PaymentMethod.Cash, Amount = Money.FromCents(3000) });
        r1.Tenders.Add(new PaymentTender { Method = PaymentMethod.MealVoucher, Amount = Money.FromCents(2000) });

        // Receipt 2: 45 € RoomCharge
        var r2 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000002",
            SequenceNumber = 2,
            TotalTtcAmount = Money.FromCents(4500),
            TotalHtAmount = Money.FromCents(4000),
            PreviousSignatureHash = "SIG1",
            SignatureHash = "SIG2",
            CreatedAtUtc = now
        };
        r2.Tenders.Add(new PaymentTender { Method = PaymentMethod.RoomCharge, Amount = Money.FromCents(4500) });

        db.FiscalReceipts.AddRange(r1, r2);
        await db.SaveChangesAsync();

        // Act
        var xReport = await fiscalService.GenerateXReportAsync("POS01");

        // Assert payment totals breakdown
        xReport.PaymentTotalsCents.Should().ContainKey(PaymentMethod.CreditCard).WhoseValue.Should().Be(5000);
        xReport.PaymentTotalsCents.Should().ContainKey(PaymentMethod.Cash).WhoseValue.Should().Be(3000);
        xReport.PaymentTotalsCents.Should().ContainKey(PaymentMethod.MealVoucher).WhoseValue.Should().Be(2000);
        xReport.PaymentTotalsCents.Should().ContainKey(PaymentMethod.RoomCharge).WhoseValue.Should().Be(4500);

        long sumTenders = xReport.PaymentTotalsCents.Values.Sum();
        sumTenders.Should().Be(14500); // 145.00 €
        sumTenders.Should().Be(xReport.TotalSalesTtcCents);
    }

    [Fact]
    public async Task DailyZClosure_SealsPeriod_IncrementsSequenceAndPerpetualGrandTotalAccurately()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var fiscalService = new NF525FiscalAuditService(db);
        var managerId = Guid.NewGuid();
        var t0 = DateTimeOffset.UtcNow.AddHours(-4);

        // Day 1: 2 receipts -> 120.00 € + 80.00 € = 200.00 €
        var r1 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(12000),
            TotalHtAmount = Money.FromCents(10909),
            TaxBreakdownJson = JsonSerializer.Serialize(new Dictionary<string, long> { ["10"] = 1091 }),
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash,
            SignatureHash = "SIG1",
            CreatedAtUtc = t0
        };
        r1.Tenders.Add(new PaymentTender { Method = PaymentMethod.CreditCard, Amount = Money.FromCents(12000) });

        var r2 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000002",
            SequenceNumber = 2,
            TotalTtcAmount = Money.FromCents(8000),
            TotalHtAmount = Money.FromCents(7273),
            TaxBreakdownJson = JsonSerializer.Serialize(new Dictionary<string, long> { ["10"] = 727 }),
            PreviousSignatureHash = "SIG1",
            SignatureHash = "SIG2",
            CreatedAtUtc = t0.AddHours(1)
        };
        r2.Tenders.Add(new PaymentTender { Method = PaymentMethod.Cash, Amount = Money.FromCents(8000) });

        db.FiscalReceipts.AddRange(r1, r2);
        await db.SaveChangesAsync();

        // Act 1: Execute Z-Closure for Day 1
        var closure1 = await fiscalService.ExecuteDailyZClosureAsync("POS01", managerId, "Alexandre Dupont");

        // Assert Closure 1
        closure1.ClosureSequence.Should().Be(1);
        closure1.TotalSalesTtcCents.Should().Be(20000); // 200.00 €
        closure1.PerpetualGrandTotalCents.Should().Be(20000); // Perpetual = 200.00 €
        closure1.ReceiptCount.Should().Be(2);
        closure1.SignatureHash.Should().NotBeNullOrEmpty();

        // Seal Day 1 period end to t0 + 2h so Day 2 starts from there
        var storedClosure1 = await db.DailyFiscalClosures.FindAsync(closure1.ClosureId);
        storedClosure1!.PeriodEndUtc = t0.AddHours(2);
        await db.SaveChangesAsync();

        // Day 2: 1 new receipt -> 150.00 €
        var r3 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000003",
            SequenceNumber = 3,
            TotalTtcAmount = Money.FromCents(15000),
            TotalHtAmount = Money.FromCents(13636),
            TaxBreakdownJson = JsonSerializer.Serialize(new Dictionary<string, long> { ["10"] = 1364 }),
            PreviousSignatureHash = "SIG2",
            SignatureHash = "SIG3",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        r3.Tenders.Add(new PaymentTender { Method = PaymentMethod.CreditCard, Amount = Money.FromCents(15000) });

        db.FiscalReceipts.Add(r3);
        await db.SaveChangesAsync();

        // Act 2: Execute Z-Closure for Day 2
        var closure2 = await fiscalService.ExecuteDailyZClosureAsync("POS01", managerId, "Alexandre Dupont");

        // Assert Closure 2
        closure2.ClosureSequence.Should().Be(2);
        closure2.TotalSalesTtcCents.Should().Be(15000); // Exactly Day 2 sales (150.00 €), not 350.00 €!
        closure2.PerpetualGrandTotalCents.Should().Be(35000); // 200.00 € + 150.00 € = 350.00 €
        closure2.ReceiptCount.Should().Be(1);

        // Assert Chaining: Closure 2 previous signature links to Closure 1
        var storedClosure2 = await db.DailyFiscalClosures.FirstOrDefaultAsync(c => c.ClosureSequence == 2);
        storedClosure2!.PreviousSignatureHash.Should().Be(closure1.SignatureHash);
    }

    [Fact]
    public async Task DailyZClosure_ThrowsException_WhenUnsettledHeldOrdersExist()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var fiscalService = new NF525FiscalAuditService(db);

        // Add a held (parked) order that was never recalled or voided
        db.HeldOrders.Add(new HeldOrder
        {
            TerminalId = "POS01",
            OrderId = Guid.NewGuid(),
            TotalTtc = Money.FromCents(3500),
            OrderSnapshotJson = "{}",
            HeldAtUtc = DateTimeOffset.UtcNow,
            HeldByStaffId = Guid.NewGuid(),
            IsRecalled = false,
            IsVoided = false
        });
        await db.SaveChangesAsync();

        // Act
        var act = () => fiscalService.ExecuteDailyZClosureAsync("POS01", Guid.NewGuid(), "Admin");

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*commande(s) en attente subsistent*");
    }

    [Fact]
    public async Task FinancialDashboard_CalculatesExactServiceSegmentationAndCoversAverages()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var dashboardService = new FinancialDashboardService(db);

        var lunchTime = new DateTimeOffset(2026, 9, 17, 12, 30, 0, TimeSpan.Zero);
        var dinnerTime = new DateTimeOffset(2026, 9, 17, 20, 0, 0, TimeSpan.Zero);

        // Tables with covers
        db.DiningTables.AddRange(
            new DiningTable { TableNumber = "T01", Capacity = 2, CoversCount = 2, AssignedWaiterName = "Sophie" },
            new DiningTable { TableNumber = "T02", Capacity = 4, CoversCount = 4, AssignedWaiterName = "Alexandre" }
        );

        // Lunch Order: Table T01 (2 covers), 50.00 € TTC, 45.45 € HT
        var order1 = new Order
        {
            TableNumber = "T01",
            Status = OrderStatus.Paid,
            CreatedAtUtc = lunchTime
        };
        order1.Items.Add(new OrderItem
        {
            ProductId = Guid.NewGuid(),
            ProductName = "Menu Midi",
            Quantity = 2,
            UnitPrice = Money.FromCents(2500),
            TaxRatePercent = 10.0m
        });

        var receipt1 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "R-001",
            OrderId = order1.Id,
            TotalTtcAmount = Money.FromCents(5000),
            TotalHtAmount = Money.FromCents(4545),
            CreatedAtUtc = lunchTime
        };
        receipt1.Tenders.Add(new PaymentTender { Method = PaymentMethod.CreditCard, Amount = Money.FromCents(5000) });

        // Dinner Order: Table T02 (4 covers), 120.00 € TTC, 109.09 € HT
        var order2 = new Order
        {
            TableNumber = "T02",
            Status = OrderStatus.Paid,
            CreatedAtUtc = dinnerTime
        };
        order2.Items.Add(new OrderItem
        {
            ProductId = Guid.NewGuid(),
            ProductName = "Côte de Bœuf",
            Quantity = 4,
            UnitPrice = Money.FromCents(3000),
            TaxRatePercent = 10.0m
        });

        var receipt2 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "R-002",
            OrderId = order2.Id,
            TotalTtcAmount = Money.FromCents(12000),
            TotalHtAmount = Money.FromCents(10909),
            CreatedAtUtc = dinnerTime
        };
        receipt2.Tenders.Add(new PaymentTender { Method = PaymentMethod.Cash, Amount = Money.FromCents(12000) });

        db.Orders.AddRange(order1, order2);
        db.FiscalReceipts.AddRange(receipt1, receipt2);
        await db.SaveChangesAsync();

        // Act
        var filter = new FinancialDashboardFilterDto(lunchTime.Date, lunchTime.Date.AddDays(1));
        var report = await dashboardService.GetFinancialDashboardAsync(filter);

        // Assert Global KPIs
        report.Kpis.TotalSalesTtc.Should().Be(170.00m);
        report.Kpis.TotalSalesHt.Should().Be(154.54m);
        report.Kpis.TotalOrdersCount.Should().Be(2);
        report.Kpis.TotalCoversCount.Should().Be(6); // 2 + 4
        report.Kpis.AverageOrderTtc.Should().Be(85.00m); // 170.00 / 2
        report.Kpis.AverageCoverTtc.Should().Be(28.33m); // 170.00 / 6 = 28.3333...

        // Assert Service Segmentation
        var midi = report.Services.FirstOrDefault(s => s.ServiceName.Contains("Midi"));
        var soir = report.Services.FirstOrDefault(s => s.ServiceName.Contains("Soir"));

        midi.Should().NotBeNull();
        midi!.SalesTtc.Should().Be(50.00m);
        midi.OrdersCount.Should().Be(1);
        midi.CoversCount.Should().Be(2);
        midi.AverageCoverTtc.Should().Be(25.00m); // 50.00 / 2

        soir.Should().NotBeNull();
        soir!.SalesTtc.Should().Be(120.00m);
        soir.OrdersCount.Should().Be(1);
        soir.CoversCount.Should().Be(4);
        soir.AverageCoverTtc.Should().Be(30.00m); // 120.00 / 4

        // Assert Top Products
        report.TopProducts.Should().HaveCount(2);
        var bœuf = report.TopProducts.First(p => p.ProductName == "Côte de Bœuf");
        bœuf.QuantitySold.Should().Be(4);
        bœuf.TotalSalesTtc.Should().Be(120.00m);
        bœuf.PercentageOfTotal.Should().Be(70.6m); // (120 / 170) * 100 = 70.588 -> 70.6%

        var midiProd = report.TopProducts.First(p => p.ProductName == "Menu Midi");
        midiProd.QuantitySold.Should().Be(2);
        midiProd.TotalSalesTtc.Should().Be(50.00m);
        midiProd.PercentageOfTotal.Should().Be(29.4m); // (50 / 170) * 100 = 29.411 -> 29.4%
    }

    [Fact]
    public async Task XReport_MultiTerminalIsolation_FiltersOnlyTargetTerminalReceipts()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var fiscalService = new NF525FiscalAuditService(db);
        var now = DateTimeOffset.UtcNow;

        // Terminal A Receipt: 60.00 €
        var rA = new FiscalReceipt
        {
            TerminalId = "TERM_ALPHA",
            ReceiptNumber = "ALPHA-001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(6000),
            TotalHtAmount = Money.FromCents(5455),
            CreatedAtUtc = now
        };
        rA.Tenders.Add(new PaymentTender { Method = PaymentMethod.CreditCard, Amount = Money.FromCents(6000) });

        // Terminal B Receipt: 110.00 €
        var rB = new FiscalReceipt
        {
            TerminalId = "TERM_BETA",
            ReceiptNumber = "BETA-001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(11000),
            TotalHtAmount = Money.FromCents(10000),
            CreatedAtUtc = now
        };
        rB.Tenders.Add(new PaymentTender { Method = PaymentMethod.Cash, Amount = Money.FromCents(11000) });

        db.FiscalReceipts.AddRange(rA, rB);
        await db.SaveChangesAsync();

        // Act
        var reportAlpha = await fiscalService.GenerateXReportAsync("TERM_ALPHA");
        var reportBeta = await fiscalService.GenerateXReportAsync("TERM_BETA");

        // Assert Isolation
        reportAlpha.TerminalId.Should().Be("TERM_ALPHA");
        reportAlpha.ReceiptCount.Should().Be(1);
        reportAlpha.TotalSalesTtcCents.Should().Be(6000); // 60.00 €
        reportAlpha.PaymentTotalsCents.Should().ContainKey(PaymentMethod.CreditCard).WhoseValue.Should().Be(6000);
        reportAlpha.PaymentTotalsCents.Should().NotContainKey(PaymentMethod.Cash);

        reportBeta.TerminalId.Should().Be("TERM_BETA");
        reportBeta.ReceiptCount.Should().Be(1);
        reportBeta.TotalSalesTtcCents.Should().Be(11000); // 110.00 €
        reportBeta.PaymentTotalsCents.Should().ContainKey(PaymentMethod.Cash).WhoseValue.Should().Be(11000);
        reportBeta.PaymentTotalsCents.Should().NotContainKey(PaymentMethod.CreditCard);
    }

    [Fact]
    public async Task FinancialDashboard_DiscountedOrders_AccuratelyReflectsNetTotals()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var dashboardService = new FinancialDashboardService(db);
        var now = DateTimeOffset.UtcNow;

        // Order with 20% global discount on 100 € gross = 80 € net
        var order = new Order
        {
            TableNumber = "T01",
            Status = OrderStatus.Paid,
            GlobalDiscountType = DiscountType.Percentage,
            GlobalDiscountValue = 20m,
            GlobalDiscountReason = "Commercial VIP",
            CreatedAtUtc = now
        };
        order.Items.Add(new OrderItem
        {
            ProductName = "Plateau Fruits de Mer",
            Quantity = 1,
            UnitPrice = Money.FromCents(10000),
            TaxRatePercent = 10m
        });

        // Fiscal receipt recorded for net 80.00 €
        var receipt = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "R-DISC-001",
            OrderId = order.Id,
            TotalTtcAmount = Money.FromCents(8000), // 80.00 € net
            TotalHtAmount = Money.FromCents(7273),
            CreatedAtUtc = now
        };
        receipt.Tenders.Add(new PaymentTender { Method = PaymentMethod.CreditCard, Amount = Money.FromCents(8000) });

        db.Orders.Add(order);
        db.FiscalReceipts.Add(receipt);
        await db.SaveChangesAsync();

        // Act
        var filter = new FinancialDashboardFilterDto(now.Date, now.Date.AddDays(1));
        var report = await dashboardService.GetFinancialDashboardAsync(filter);

        // Assert net totals reflect settled receipt
        report.Kpis.TotalSalesTtc.Should().Be(80.00m);
        report.Kpis.TotalSalesHt.Should().Be(72.73m);
        report.PaymentMethods.Should().ContainSingle();
        report.PaymentMethods[0].TotalAmount.Should().Be(80.00m);
    }

    [Fact]
    public async Task ZClosure_ResetsDailySessionTurnoverToZero_AndRetainsCumulativePerpetualGrandTotal()
    {
        // Arrange
        using var db = CreateInMemoryDb();
        var fiscalService = new NF525FiscalAuditService(db);
        var now = DateTimeOffset.UtcNow;

        // Create initial sale of 50.00 € TTC
        var r1 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000001",
            SequenceNumber = 1,
            TotalTtcAmount = Money.FromCents(5000),
            TotalHtAmount = Money.FromCents(4545),
            TaxBreakdownJson = "{\"10\":455}",
            PreviousSignatureHash = NF525FiscalAuditService.GenesisHash,
            SignatureHash = "SIG1",
            CreatedAtUtc = now.AddHours(-2)
        };
        r1.Tenders.Add(new PaymentTender { Method = PaymentMethod.CreditCard, Amount = Money.FromCents(5000) });
        db.FiscalReceipts.Add(r1);
        await db.SaveChangesAsync();

        // Check before Z: X-Report should show 50.00 € TTC
        var xBefore = await fiscalService.GenerateXReportAsync("POS01");
        xBefore.TotalSalesTtcCents.Should().Be(5000);
        xBefore.PerpetualGrandTotalCents.Should().Be(5000);
        xBefore.ReceiptCount.Should().Be(1);

        // Act: Execute Daily Z Closure
        var managerId = Guid.NewGuid();
        var zClosure = await fiscalService.ExecuteDailyZClosureAsync("POS01", managerId, "Responsable Caisse");

        // Assert Z-Closure contents
        zClosure.TotalSalesTtcCents.Should().Be(5000);
        zClosure.PerpetualGrandTotalCents.Should().Be(5000);
        zClosure.ClosureSequence.Should().Be(1);

        // Act: Query X-Report right after Z-Closure (NEW session)
        var xAfter = await fiscalService.GenerateXReportAsync("POS01");

        // Assert: Daily session sales are RESET to 0, but Perpetual Grand Total is preserved!
        xAfter.TotalSalesTtcCents.Should().Be(0, "daily sales counter must reset to 0 after Z closure");
        xAfter.ReceiptCount.Should().Be(0, "active receipt count must reset to 0 after Z closure");
        xAfter.PerpetualGrandTotalCents.Should().Be(5000, "perpetual grand total must remain cumulative");

        // Act: New sale in the new session (20.00 € TTC)
        var r2 = new FiscalReceipt
        {
            TerminalId = "POS01",
            ReceiptNumber = "POS01-000002",
            SequenceNumber = 2,
            TotalTtcAmount = Money.FromCents(2000),
            TotalHtAmount = Money.FromCents(1818),
            TaxBreakdownJson = "{\"10\":182}",
            PreviousSignatureHash = "SIG1",
            SignatureHash = "SIG2",
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
        r2.Tenders.Add(new PaymentTender { Method = PaymentMethod.Cash, Amount = Money.FromCents(2000) });
        db.FiscalReceipts.Add(r2);
        await db.SaveChangesAsync();

        // Assert: New session sales reflect only new sale (20.00 €), Perpetual Grand Total = 50 + 20 = 70.00 €
        var xAfterNewSale = await fiscalService.GenerateXReportAsync("POS01");
        xAfterNewSale.TotalSalesTtcCents.Should().Be(2000);
        xAfterNewSale.ReceiptCount.Should().Be(1);
        xAfterNewSale.PerpetualGrandTotalCents.Should().Be(7000);
    }
}
