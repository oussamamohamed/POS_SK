using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

public class CheckoutE2ETests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;
    private readonly HttpClient _client;

    public CheckoutE2ETests(PosApiApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<string> GetAuthTokenAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await response.Content.ReadFromJsonAsync<LoginResultDto>();
        return result!.Token;
    }

    [Fact]
    public async Task VoidReceipt_WithValidToken_ShouldSucceed()
    {
        // Arrange
        var token = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Guid receiptId = Guid.NewGuid();
        Guid operatorId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var operatorUser = db.Users.FirstOrDefault(u => u.Name == "Admin Système");
            if (operatorUser != null)
            {
                operatorId = operatorUser.Id;
            }

            db.FiscalReceipts.Add(new FiscalReceipt
            {
                Id = receiptId,
                ReceiptNumber = "REC-001",
                OrderId = Guid.NewGuid(),
                TerminalId = "TERM-1",
                TotalTtcAmount = Money.FromCents(1500, "EUR"),
                SignatureHash = "signature-test",
                CreatedAtUtc = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var request = new VoidReceiptRequest
        {
            TerminalId = "TERM-1",
            OperatorId = operatorId
        };

        // Act
        var response = await _client.PostAsJsonAsync($"/api/checkout/void/{receiptId}", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var originalReceipt = await db.FiscalReceipts.FindAsync(receiptId);
            originalReceipt.Should().NotBeNull();
            originalReceipt!.IsVoid.Should().BeTrue();

            var voidReceipt = db.FiscalReceipts.FirstOrDefault(r => r.VoidedReceiptId == receiptId);
            voidReceipt.Should().NotBeNull();
            voidReceipt!.TotalTtcAmount.ToDecimal().Should().Be(-15m); // Negative amount for void
        }
    }

    [Fact]
    public async Task Pay_WithTableNumber_ShouldSucceedFreeTableAndIncrementReceiptNumber()
    {
        var token = await GetAuthTokenAsync();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        Guid orderId1 = Guid.NewGuid();
        Guid orderId2 = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var order1 = new Order
            {
                Id = orderId1,
                TableNumber = "T91",
                Status = OrderStatus.Open,
                Items =
                {
                    new OrderItem { Id = Guid.NewGuid(), OrderId = orderId1, ProductId = Guid.NewGuid(), ProductName = "Menu Test 1", Quantity = 1, UnitPrice = Money.FromCents(2000), TaxRatePercent = 10 }
                }
            };
            var order2 = new Order
            {
                Id = orderId2,
                TableNumber = "T92",
                Status = OrderStatus.Open,
                Items =
                {
                    new OrderItem { Id = Guid.NewGuid(), OrderId = orderId2, ProductId = Guid.NewGuid(), ProductName = "Menu Test 2", Quantity = 1, UnitPrice = Money.FromCents(3000), TaxRatePercent = 10 }
                }
            };
            db.Orders.AddRange(order1, order2);

            var table1 = new DiningTable { TableNumber = "T91", Status = TableStatus.Occupied, ActiveOrderId = orderId1, CoversCount = 2 };
            var table2 = new DiningTable { TableNumber = "T92", Status = TableStatus.Occupied, ActiveOrderId = orderId2, CoversCount = 3 };
            db.DiningTables.AddRange(table1, table2);

            await db.SaveChangesAsync();
        }

        // Act 1: Pay table T91 without explicit orderId
        var req1 = new PaymentSettlementRequest(
            Guid.Empty,
            "T91",
            Guid.NewGuid(),
            [new TenderItemRequest(PaymentMethod.Cash, 20.0m, 20.0m, 0.0m)],
            "POS_TEST"
        );
        var resp1 = await _client.PostAsJsonAsync("/api/checkout/pay", req1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = await resp1.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var receiptNum1 = res1.GetProperty("receiptNumber").GetString();
        receiptNum1.Should().NotBeNullOrEmpty();

        // Act 2: Pay table T92
        var req2 = new PaymentSettlementRequest(
            Guid.Empty,
            "T92",
            Guid.NewGuid(),
            [new TenderItemRequest(PaymentMethod.Cash, 30.0m, 30.0m, 0.0m)],
            "POS_TEST"
        );
        var resp2 = await _client.PostAsJsonAsync("/api/checkout/pay", req2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = await resp2.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var receiptNum2 = res2.GetProperty("receiptNumber").GetString();
        receiptNum2.Should().NotBeNullOrEmpty();

        // Assert receipt numbers differ and increment
        receiptNum1.Should().NotBe(receiptNum2);

        // Assert table T91 is freed directly
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var table = await db.DiningTables.FindAsync("T91");
            table.Should().NotBeNull();
            table!.Status.Should().Be(TableStatus.Free);
            table.ActiveOrderId.Should().BeNull();
            table.CoversCount.Should().Be(0);
        }
    }
}
