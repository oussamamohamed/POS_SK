using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;
using Xunit;

namespace RestaurantPos.Api.Tests;

public class VoidTotalsRegressionTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public VoidTotalsRegressionTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task SaleAndVoid_TotalsAcrossServices_ReflectZeroNetSales()
    {
        using var scope = _factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var checkout = sp.GetRequiredService<ICheckoutPaymentService>();
        var fiscalAudit = sp.GetRequiredService<INF525FiscalAuditService>();
        var dashboard = sp.GetRequiredService<IFinancialDashboardService>();
        var fecService = sp.GetRequiredService<IFecExportService>();
        var printReport = sp.GetRequiredService<ReportPrintDataService>();

        var terminalId = $"TERM_{Guid.NewGuid():N}"[..12];

        // 1. Create and settle order
        var orderId = UuidV7.NewGuid();
        var order = new Order
        {
            Id = orderId,
            TableNumber = "T99",
            Status = OrderStatus.Open,
            Items = new List<OrderItem>
            {
                new()
                {
                    Id = UuidV7.NewGuid(),
                    OrderId = orderId,
                    ProductId = Guid.NewGuid(),
                    ProductName = "Plat Test Regression",
                    Quantity = 1,
                    UnitPrice = Money.FromCents(2000),
                    TaxRatePercent = 10.0m
                }
            }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var tenders = new List<PaymentTenderRequest>
        {
            new(PaymentMethod.CreditCard, 2000, 2000)
        };
        var settleResult = await checkout.ProcessPaymentTendersAsync(orderId, terminalId, tenders);
        settleResult.IsSuccess.Should().BeTrue();

        var originalReceipt = await db.FiscalReceipts.FirstAsync(r => r.ReceiptNumber == settleResult.ReceiptNumber);

        // 2. Void receipt
        var opId = Guid.NewGuid();
        var voidResult = await checkout.VoidReceiptAsync(originalReceipt.Id, terminalId, opId);
        voidResult.IsSuccess.Should().BeTrue();

        // 3. Verify X-Report net totals
        var xReport = await fiscalAudit.GenerateXReportAsync(terminalId);
        xReport.TotalSalesTtcCents.Should().Be(0);
        xReport.TotalSalesHtCents.Should().Be(0);
        if (xReport.VatBreakdownCents.TryGetValue(10.0m, out var vatAmount))
        {
            vatAmount.Should().Be(0);
        }
        if (xReport.PaymentTotalsCents.TryGetValue(PaymentMethod.CreditCard, out var cardTotal))
        {
            cardTotal.Should().Be(0);
        }

        // 4. Verify Dashboard revenue
        var now = DateTimeOffset.UtcNow;
        var dashResult = await dashboard.GetFinancialDashboardAsync(new FinancialDashboardFilterDto(
            now.AddDays(-1),
            now.AddDays(1)
        ));
        dashResult.Should().NotBeNull();

        // 5. Verify FEC generation produces valid file
        var fec = await fecService.GenerateFecAsync(new FecExportRequest(
            now.AddDays(-1),
            now.AddDays(1),
            "123456789",
            "Restaurant Test"
        ));
        fec.FileBytes.Length.Should().BeGreaterThan(0);

        // 6. Verify FindOpenOrdersAsync does not consider this voided order as open
        var openOrders = await fiscalAudit.FindOpenOrdersAsync();
        openOrders.Should().NotContain(o => o.OrderId == orderId);

        // 7. Verify Print Report Data shows no items for cancelled order
        var printData = await printReport.BuildAsync(terminalId, now.AddDays(-1), now.AddDays(1));
        printData.Categories.Should().BeEmpty();
    }
}
