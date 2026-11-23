using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

/// <summary>Clé d'idempotence de l'ajout de lignes, de la facturation chambre et de l'annulation de reçu.</summary>
public class RouteIdempotencyTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public RouteIdempotencyTests(PosApiApplicationFactory factory) => _factory = factory;

    private async Task<HttpClient> PairedClientAsync(bool pair = true)
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await login.Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);
        if (pair) await DeviceTestHelper.PairAsync(_factory, client);
        return client;
    }

    private static Task<HttpResponseMessage> PostAsync<T>(HttpClient client, string url, T body, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (key is not null) request.Headers.Add("Idempotency-Key", key);
        return client.SendAsync(request);
    }

    private static AddOrderItemsRequest Items(int quantity, string name = "Plat") =>
        new([new OrderItemInputDto(Guid.NewGuid(), name, quantity, 10m, 10m, null, null)]);

    private static string NewTable() => "KR" + Random.Shared.Next(100000, 999999);

    private static async Task<int> QuantityAsync(HttpClient client, string table) =>
        (await client.GetFromJsonAsync<ActiveTableOrderDto>($"/api/tables/{table}/order"))!.Lines.Sum(l => l.Quantity);

    [Fact]
    public async Task AddItems_Replay_AddsTheLinesOnce_AndReturnsTheSameResponse()
    {
        var client = await PairedClientAsync(pair: false);
        var table = NewTable();
        var body = Items(2);
        var key = Guid.NewGuid().ToString();

        var first = await PostAsync(client, $"/api/tables/{table}/items", body, key);
        var replay = await PostAsync(client, $"/api/tables/{table}/items", body, key);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        (await QuantityAsync(client, table)).Should().Be(2);
    }

    [Fact]
    public async Task AddItems_WithoutKey_TwoIdenticalRequests_AddTwice()
    {
        var client = await PairedClientAsync(pair: false);
        var table = NewTable();
        var body = Items(2);

        (await PostAsync(client, $"/api/tables/{table}/items", body, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/api/tables/{table}/items", body, null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await QuantityAsync(client, table)).Should().Be(4);
    }

    [Fact]
    public async Task AddItems_SameKeyOtherLinesOrOtherTable_Returns409_AndAddsNothing()
    {
        var client = await PairedClientAsync(pair: false);
        var table = NewTable();
        var key = Guid.NewGuid().ToString();
        (await PostAsync(client, $"/api/tables/{table}/items", Items(2), key)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PostAsync(client, $"/api/tables/{table}/items", Items(3), key)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await PostAsync(client, $"/api/tables/{NewTable()}/items", Items(2), key)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await QuantityAsync(client, table)).Should().Be(2);
    }

    [Fact]
    public async Task AddItems_InvalidKey_Returns400_AndAddsNothing()
    {
        var client = await PairedClientAsync(pair: false);
        var table = NewTable();

        var response = await PostAsync(client, $"/api/tables/{table}/items", Items(2), "not-a-uuid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync($"/api/tables/{table}/order")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AddItems_Refusal_DoesNotConsumeTheKey()
    {
        var client = await PairedClientAsync(pair: false);
        var table = NewTable();
        var key = Guid.NewGuid();
        var bad = new AddOrderItemsRequest([new OrderItemInputDto(Guid.NewGuid(), "Plat", 0, 10m, 10m, null, null)]);

        (await PostAsync(client, $"/api/tables/{table}/items", bad, key.ToString())).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().IdempotentRequests.AnyAsync(r => r.IdempotencyKey == key)).Should().BeFalse();
    }

    [Fact]
    public async Task RoomCharge_Replay_ChargesTheRoomOnce()
    {
        var client = await PairedClientAsync(pair: false);
        var table = NewTable();
        (await PostAsync(client, $"/api/tables/{table}/items", Items(1), null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var orderId = (await client.GetFromJsonAsync<ActiveTableOrderDto>($"/api/tables/{table}/order"))!.OrderId;
        var charge = new RoomChargeRequest(orderId, table, "204", "Alexandre Dupont", 10m, 0m, null, null);
        var key = Guid.NewGuid().ToString();

        var first = await PostAsync(client, "/api/hotel/room-charge", charge, key);
        var replay = await PostAsync(client, "/api/hotel/room-charge", charge, key);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().RoomFolioCharges.CountAsync(c => c.OrderId == orderId)).Should().Be(1);
    }

    [Fact]
    public async Task RoomCharge_SameKeyOtherAmount_Returns409()
    {
        var client = await PairedClientAsync(pair: false);
        var table = NewTable();
        (await PostAsync(client, $"/api/tables/{table}/items", Items(1), null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var orderId = (await client.GetFromJsonAsync<ActiveTableOrderDto>($"/api/tables/{table}/order"))!.OrderId;
        var key = Guid.NewGuid().ToString();
        (await PostAsync(client, "/api/hotel/room-charge", new RoomChargeRequest(orderId, table, "204", "Alexandre Dupont", 10m, 0m, null, null), key))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var other = new RoomChargeRequest(orderId, table, "204", "Alexandre Dupont", 12m, 0m, null, null);
        (await PostAsync(client, "/api/hotel/room-charge", other, key)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<(HttpClient Client, Guid ReceiptId)> PaidReceiptAsync()
    {
        var client = await PairedClientAsync();
        var orderId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = new Order { Id = orderId, TableNumber = "Comptoir", Status = OrderStatus.Open };
            order.Items.Add(new OrderItem { OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(1000), TaxRatePercent = 10 });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        }

        var pay = new PaymentSettlementRequest(orderId, null, Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.Cash, 10m, 10m, 0m)]);
        (await client.PostAsJsonAsync("/api/checkout/pay", pay)).StatusCode.Should().Be(HttpStatusCode.OK);
        using var read = _factory.Services.CreateScope();
        var receiptId = await read.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.Where(r => r.OrderId == orderId).Select(r => r.Id).SingleAsync();
        return (client, receiptId);
    }

    private async Task<int> VoidCountAsync(Guid receiptId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.CountAsync(r => r.VoidedReceiptId == receiptId);
    }

    [Fact]
    public async Task VoidReceipt_Replay_ReturnsTheStoredSuccess_WithOneVoidReceipt()
    {
        var (client, receiptId) = await PaidReceiptAsync();
        var body = new VoidReceiptRequest("POS_A", Guid.NewGuid());
        var key = Guid.NewGuid().ToString();

        var first = await PostAsync(client, $"/api/checkout/void/{receiptId}", body, key);
        var replay = await PostAsync(client, $"/api/checkout/void/{receiptId}", body, key);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Be(await first.Content.ReadAsStringAsync());
        (await VoidCountAsync(receiptId)).Should().Be(1);
    }

    [Fact]
    public async Task VoidReceipt_WithoutKey_SecondAttemptIsRefused()
    {
        var (client, receiptId) = await PaidReceiptAsync();
        var body = new VoidReceiptRequest("POS_A", Guid.NewGuid());

        (await PostAsync(client, $"/api/checkout/void/{receiptId}", body, null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await PostAsync(client, $"/api/checkout/void/{receiptId}", body, null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await VoidCountAsync(receiptId)).Should().Be(1);
    }

    [Fact]
    public async Task VoidReceipt_SameKeyOtherReceipt_Returns409()
    {
        var (client, receiptId) = await PaidReceiptAsync();
        var key = Guid.NewGuid().ToString();
        var body = new VoidReceiptRequest("POS_A", Guid.NewGuid());
        (await PostAsync(client, $"/api/checkout/void/{receiptId}", body, key)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await PostAsync(client, $"/api/checkout/void/{Guid.NewGuid()}", body, key)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
