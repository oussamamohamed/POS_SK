using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

/// <summary>Clé d'idempotence des routes d'encaissement (/api/checkout/pay, /api/orders/counter/checkout) : un rejeu n'encaisse qu'une fois.</summary>
public class PaymentIdempotencyTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public PaymentIdempotencyTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> PairedClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await login.Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);
        await DeviceTestHelper.PairAsync(_factory, client);
        return client;
    }

    private async Task<Guid> SeedOrderAsync(long unitPriceCents)
    {
        var orderId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order { Id = orderId, TableNumber = "Comptoir", Status = OrderStatus.Open };
        order.Items.Add(new OrderItem { OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(unitPriceCents), TaxRatePercent = 10 });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return orderId;
    }

    private async Task<T> ReadAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        using var scope = _factory.Services.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private Task<int> ReceiptCountAsync(Guid orderId) => ReadAsync(db => db.FiscalReceipts.CountAsync(r => r.OrderId == orderId));

    private static PaymentSettlementRequest Pay(Guid orderId, decimal amount) =>
        new(orderId, null, Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.Cash, amount, amount, 0m)]);

    private static CounterCheckoutRequest Counter(Guid orderId, decimal amount) =>
        new(orderId, null, OrderDestination.Takeaway, null, null, 0m, false, null, [new CounterPaymentTender(PaymentMethod.Cash, amount, amount, 0m)]);

    private static Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string url, T body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static async Task<string> CodeAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString()!;

    [Fact]
    public async Task Pay_Replay_WritesOneReceipt_AndReturnsTheSameResponse()
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);
        var key = Guid.NewGuid().ToString();
        var body = Pay(orderId, 4m);

        var first = await PostAsync(client, "/api/checkout/pay", body, key);
        var replay = await PostAsync(client, "/api/checkout/pay", body, key);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        (await ReceiptCountAsync(orderId)).Should().Be(1);
    }

    [Fact]
    public async Task Pay_WithoutKey_TwoIdenticalPartialPayments_WriteTwoReceipts()
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);
        var body = Pay(orderId, 4m);

        (await PostAsync(client, "/api/checkout/pay", body, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, "/api/checkout/pay", body, null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReceiptCountAsync(orderId)).Should().Be(2);
    }

    [Fact]
    public async Task Pay_EmptyKeyHeader_IsTreatedAsAbsent()
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);
        var body = Pay(orderId, 4m);

        (await PostAsync(client, "/api/checkout/pay", body, "")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, "/api/checkout/pay", body, "")).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReceiptCountAsync(orderId)).Should().Be(2);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("order")]
    [InlineData("device")]
    public async Task Pay_SameKeyDifferentRequest_Returns409_WithoutNewReceipt(string change)
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);
        var otherOrderId = await SeedOrderAsync(1000);
        var key = Guid.NewGuid().ToString();
        (await PostAsync(client, "/api/checkout/pay", Pay(orderId, 4m), key)).StatusCode.Should().Be(HttpStatusCode.OK);

        var second = change == "device" ? await PairedClientAsync() : client;
        var changed = change switch { "amount" => Pay(orderId, 3m), "order" => Pay(otherOrderId, 4m), _ => Pay(orderId, 4m) };
        var response = await PostAsync(second, "/api/checkout/pay", changed, key);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodeAsync(response)).Should().Be("idempotency_key_reuse");
        (await ReceiptCountAsync(orderId)).Should().Be(1);
        (await ReceiptCountAsync(otherOrderId)).Should().Be(0);
    }

    [Theory]
    [InlineData("not-a-uuid")]
    [InlineData("12345")]
    public async Task Pay_InvalidKey_Returns400_WithoutReceipt(string key)
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);

        var response = await PostAsync(client, "/api/checkout/pay", Pay(orderId, 4m), key);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CodeAsync(response)).Should().Be("idempotency_key_invalid");
        (await ReceiptCountAsync(orderId)).Should().Be(0);
    }

    [Fact]
    public async Task Pay_Refusal_DoesNotConsumeTheKey()
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);
        var key = Guid.NewGuid().ToString();

        (await PostAsync(client, "/api/checkout/pay", Pay(orderId, 10.01m), key)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ReadAsync(db => db.IdempotentRequests.AnyAsync(r => r.IdempotencyKey == Guid.Parse(key)))).Should().BeFalse();
        (await PostAsync(client, "/api/checkout/pay", Pay(orderId, 10m), key)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReceiptCountAsync(orderId)).Should().Be(1);
    }

    [Fact]
    public async Task Pay_ConcurrentRequestsWithTheSameKey_WriteOneReceipt()
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);
        var key = Guid.NewGuid().ToString();
        var body = Pay(orderId, 4m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => PostAsync(client, "/api/checkout/pay", body, key)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK);
        (await Task.WhenAll(responses.Select(r => r.Content.ReadAsStringAsync()))).Distinct().Should().HaveCount(1);
        (await ReceiptCountAsync(orderId)).Should().Be(1);
    }

    [Fact]
    public async Task Pay_KeysOlderThanSevenDays_ArePurgedOnTheNextKeyedPayment()
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);
        Guid old = Guid.NewGuid(), recent = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.IdempotentRequests.Add(new IdempotentRequest { IdempotencyKey = old, RequestHash = "x", ResponseJson = "{}", CreatedAtUtc = DateTime.UtcNow.AddDays(-8) });
            db.IdempotentRequests.Add(new IdempotentRequest { IdempotencyKey = recent, RequestHash = "x", ResponseJson = "{}", CreatedAtUtc = DateTime.UtcNow.AddDays(-6) });
            await db.SaveChangesAsync();
        }

        (await PostAsync(client, "/api/checkout/pay", Pay(orderId, 10m), Guid.NewGuid().ToString())).StatusCode.Should().Be(HttpStatusCode.OK);

        var keys = await ReadAsync(db => db.IdempotentRequests.AsNoTracking().Select(r => r.IdempotencyKey).ToListAsync());
        keys.Should().Contain(recent).And.NotContain(old);
    }

    [Fact]
    public async Task CounterCheckout_Replay_ReturnsTheStoredSuccess_WithoutNewReceipt()
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(900);
        var key = Guid.NewGuid().ToString();
        var body = Counter(orderId, 9m);

        var first = await PostAsync(client, "/api/orders/counter/checkout", body, key);
        var replay = await PostAsync(client, "/api/orders/counter/checkout", body, key);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        (await ReceiptCountAsync(orderId)).Should().Be(1);
        // Sans clé, la commande soldée est refusée : c'est bien la clé qui rend le rejeu.
        (await PostAsync(client, "/api/orders/counter/checkout", body, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CounterCheckout_SameKeyDifferentRequest_Returns409()
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(900);
        var key = Guid.NewGuid().ToString();
        (await PostAsync(client, "/api/orders/counter/checkout", Counter(orderId, 4m), key)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await PostAsync(client, "/api/orders/counter/checkout", Counter(orderId, 5m), key);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ReceiptCountAsync(orderId)).Should().Be(1);
    }

    [Fact]
    public async Task SameKeyOnTwoRoutes_IsRefusedAsAnotherRequest()
    {
        var client = await PairedClientAsync();
        var orderId = await SeedOrderAsync(900);
        var key = Guid.NewGuid().ToString();
        (await PostAsync(client, "/api/checkout/pay", Pay(orderId, 4m), key)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PostAsync(client, "/api/orders/counter/checkout", Counter(orderId, 4m), key)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
