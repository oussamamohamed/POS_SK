using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

/// <summary>Règles de clôture : Z refusée avec commandes en cours, pas d'annulation après Z.</summary>
public class ClosureRulesApiTests
{
    private static async Task<(HttpClient Client, Guid OperatorId)> ManagerAsync(PosApiApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("1234"))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        return (client, login.OperatorId);
    }

    [Fact]
    public async Task ZClosure_WithOpenOrder_Returns409_WithList_NothingWritten()
    {
        using var factory = new PosApiApplicationFactory();
        var (client, _) = await ManagerAsync(factory);

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new Order { TableNumber = "T30", Status = OrderStatus.Open };
            order.Items.Add(new OrderItem { ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(1250) });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync("/api/fiscal/z-closure", new ZClosureRequest("POS_MAIN_TERM", Guid.NewGuid(), "Gérant"));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("open_orders");
        var openOrders = json.GetProperty("openOrders");
        openOrders[0].GetProperty("tableNumber").GetString().Should().Be("T30");
        openOrders[0].GetProperty("remainingTtc").GetDecimal().Should().Be(12.50m);
        json.GetProperty("message").GetString().Should().Contain("T30");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.DailyFiscalClosures.CountAsync()).Should().Be(0);
        }
    }

    [Fact]
    public async Task Void_ReceiptBeforeLatestClosure_Returns409()
    {
        using var factory = new PosApiApplicationFactory();
        var (client, operatorId) = await ManagerAsync(factory);
        var paired = await DeviceTestHelper.PairAsync(factory, client);

        var receiptId = await PayNewOrderAsync(factory, client, "T31", operatorId, 20.00m, paired.TerminalId);

        var closureEnd = DateTimeOffset.UtcNow.AddSeconds(1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DailyFiscalClosures.Add(new DailyFiscalClosure
            {
                TerminalId = paired.TerminalId,
                ClosureSequence = 1,
                PeriodStartUtc = closureEnd.AddDays(-1),
                PeriodEndUtc = closureEnd,
                SignatureHash = "h",
                PreviousSignatureHash = "g"
            });
            await db.SaveChangesAsync();
        }

        var response = await client.PostAsJsonAsync($"/api/checkout/void/{receiptId}", new VoidReceiptRequest { TerminalId = paired.TerminalId, OperatorId = operatorId });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("void_after_closure");

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var voidReceiptExists = await db.FiscalReceipts.AnyAsync(r => r.VoidedReceiptId == receiptId);
            voidReceiptExists.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Void_ReceiptAfterLatestClosure_StillAllowed()
    {
        using var factory = new PosApiApplicationFactory();
        var (client, operatorId) = await ManagerAsync(factory);
        var paired = await DeviceTestHelper.PairAsync(factory, client);

        var closureEnd = DateTimeOffset.UtcNow.AddHours(-1);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.DailyFiscalClosures.Add(new DailyFiscalClosure
            {
                TerminalId = "POS_MAIN_TERM",
                ClosureSequence = 1,
                PeriodStartUtc = closureEnd.AddDays(-1),
                PeriodEndUtc = closureEnd,
                SignatureHash = "h",
                PreviousSignatureHash = "g"
            });
            await db.SaveChangesAsync();
        }

        var receiptId = await PayNewOrderAsync(factory, client, "T32", operatorId, 15.00m, paired.TerminalId);

        var response = await client.PostAsJsonAsync($"/api/checkout/void/{receiptId}", new VoidReceiptRequest { TerminalId = paired.TerminalId, OperatorId = operatorId });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<Guid> PayNewOrderAsync(PosApiApplicationFactory factory, HttpClient client, string tableNumber, Guid operatorId, decimal price, string terminalId)
    {
        var orderId = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new Order
            {
                Id = orderId,
                TableNumber = tableNumber,
                Status = OrderStatus.Open,
                Items =
                {
                    new OrderItem { Id = Guid.NewGuid(), OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents((long)(price * 100)), TaxRatePercent = 10 }
                }
            };
            db.Orders.Add(order);
            db.DiningTables.Add(new DiningTable { TableNumber = tableNumber, Status = TableStatus.Occupied, ActiveOrderId = orderId, CoversCount = 2 });
            await db.SaveChangesAsync();
        }

        var payResp = await client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(
            orderId, tableNumber, operatorId, [new TenderItemRequest(PaymentMethod.Cash, price, price, 0m)], terminalId));
        payResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await payResp.Content.ReadFromJsonAsync<JsonElement>();
        var receiptNumber = body.GetProperty("receiptNumber").GetString();

        using var verifyScope = factory.Services.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var receipt = await verifyDb.FiscalReceipts.FirstAsync(r => r.ReceiptNumber == receiptNumber);
        return receipt.Id;
    }
}
