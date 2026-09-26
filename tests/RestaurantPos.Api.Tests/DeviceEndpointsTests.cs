using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;

namespace RestaurantPos.Api.Tests;

public class DeviceEndpointsTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public DeviceEndpointsTests(PosApiApplicationFactory factory) => _factory = factory;

    private HttpClient ClientWithRole(string role)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", role);
        return client;
    }

    private async Task<string> CreateCodeAsync(string name = "Caisse comptoir", string role = "Caisse")
    {
        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/devices/pairing-codes", new CreatePairingCodeRequest(name, role));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PairingCodeResponse>())!.Code;
    }

    private async Task<PairResponse> PairOverHttpAsync(string code)
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/devices/pair", new PairRequest(code));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<PairResponse>())!;
    }

    /// <summary>Crée une commande ouverte sur une table dédiée et renvoie son identifiant.</summary>
    private async Task<Guid> SeedOpenOrderAsync(string table, long cents)
    {
        var orderId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Orders.Add(new Order
        {
            Id = orderId,
            TableNumber = table,
            Status = OrderStatus.Open,
            Items =
            {
                new OrderItem { Id = Guid.NewGuid(), OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Plat test", Quantity = 1, UnitPrice = Money.FromCents(cents), TaxRatePercent = 10 }
            }
        });
        await db.SaveChangesAsync();
        return orderId;
    }

    private static PaymentSettlementRequest Payment(Guid orderId, string table, decimal amount) =>
        new(orderId, table, Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.Cash, amount, amount, 0m)], "POS_A");

    [Fact]
    public async Task CreatePairingCode_ReturnsCodeQrPayloadAndPng()
    {
        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/devices/pairing-codes", new CreatePairingCodeRequest("Caisse comptoir", "Caisse"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = (await response.Content.ReadFromJsonAsync<PairingCodeResponse>())!;
        body.Code.Should().HaveLength(8);
        body.QrPayload.Should().StartWith("posdevice://pair?url=http%3A%2F%2Flocalhost").And.EndWith($"&code={body.Code}");
        Convert.FromBase64String(body.QrPngBase64).Take(4).Should().Equal(0x89, 0x50, 0x4E, 0x47); // signature PNG
    }

    [Theory]
    [InlineData("", "Caisse")]
    [InlineData("Caisse", "Plongeur")]
    [InlineData("Caisse", "99")]
    public async Task CreatePairingCode_WithInvalidInput_Returns400(string name, string role)
    {
        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/devices/pairing-codes", new CreatePairingCodeRequest(name, role));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ManagerOnlyRoutes_RejectWaiter()
    {
        var waiter = ClientWithRole("Waiter");
        (await waiter.PostAsJsonAsync("/api/devices/pairing-codes", new CreatePairingCodeRequest("X", "Caisse"))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.GetAsync("/api/devices")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await waiter.PostAsync($"/api/devices/{Guid.NewGuid()}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Pair_ReturnsTokenTerminalAndServerName()
    {
        var paired = await PairOverHttpAsync(await CreateCodeAsync("iPad terrasse", "Serveur"));

        paired.Token.Should().NotBeNullOrWhiteSpace();
        paired.TerminalId.Should().MatchRegex(@"^T\d{2,}$");
        paired.Name.Should().Be("iPad terrasse");
        paired.Role.Should().Be("Serveur");
        paired.ServerName.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Pair_WithReusedOrUnknownCode_Returns400PairingCodeInvalid()
    {
        var code = await CreateCodeAsync();
        await PairOverHttpAsync(code);

        foreach (var attempt in new[] { code, "ZZZZZZZZ" })
        {
            var response = await _factory.CreateClient().PostAsJsonAsync("/api/devices/pair", new PairRequest(attempt));
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            body.GetProperty("code").GetString().Should().Be("pairing_code_invalid");
            body.GetProperty("message").GetString().Should().Be("Code invalide ou expiré");
        }
    }

    [Fact]
    public async Task Pay_WithoutDeviceToken_Returns401DeviceNotPaired()
    {
        var orderId = await SeedOpenOrderAsync("D10", 1000);

        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/checkout/pay", Payment(orderId, "D10", 10m));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("device_not_paired");
    }

    [Fact]
    public async Task CounterCheckout_WithoutDeviceToken_Returns401DeviceNotPaired()
    {
        var request = new CounterCheckoutRequest(
            OrderId: Guid.NewGuid(), TerminalId: "POS_A", Destination: OrderDestination.Takeaway,
            PickupBuzzer: null, PickupScheduledAtUtc: null, TipAmount: 0m, RequestFiscalReceiptPrint: false,
            MealVoucherPolicy: null, Tenders: [new CounterPaymentTender(PaymentMethod.Cash, 5m, 5m, null)]);

        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/orders/counter/checkout", request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("device_not_paired");
    }

    [Fact]
    public async Task Pay_IgnoresBodyTerminalId_AndStartsDeviceChainFromGenesis()
    {
        var client = ClientWithRole("Admin");
        var paired = await DeviceTestHelper.PairAsync(_factory, client, "Caisse chaîne");
        var orderId = await SeedOpenOrderAsync("D11", 1500);

        var response = await client.PostAsJsonAsync("/api/checkout/pay", Payment(orderId, "D11", 15m));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _factory.Services.CreateScope();
        var receipt = scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.Single(r => r.OrderId == orderId);
        receipt.TerminalId.Should().Be(paired.TerminalId);
        receipt.PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);
    }

    [Fact]
    public async Task CounterCheckout_IgnoresBodyTerminalId_AndWritesDeviceTerminal()
    {
        var client = ClientWithRole("Admin");
        var paired = await DeviceTestHelper.PairAsync(_factory, client, "Caisse comptoir chaîne");
        var orderId = await SeedOpenOrderAsync("D16", 1000);
        var request = new CounterCheckoutRequest(
            OrderId: orderId, TerminalId: "POS_A", Destination: OrderDestination.Takeaway,
            PickupBuzzer: null, PickupScheduledAtUtc: null, TipAmount: 0m, RequestFiscalReceiptPrint: false,
            MealVoucherPolicy: null, Tenders: [new CounterPaymentTender(PaymentMethod.Cash, 10m, 10m, null)]);

        var response = await client.PostAsJsonAsync("/api/orders/counter/checkout", request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts
            .Single(r => r.OrderId == orderId).TerminalId.Should().Be(paired.TerminalId);
    }

    private static SyncReceiptDto SyncReceipt(Guid orderId) =>
        new(orderId, null, 1000, [new SyncTenderDto(PaymentMethod.Cash, 1000, 1000)], "POS_A");

    [Fact]
    public async Task SyncReceipt_Anonymous_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/sync/receipt", SyncReceipt(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SyncReceipt_WithoutDeviceToken_Returns401DeviceNotPaired()
    {
        var response = await ClientWithRole("Admin").PostAsJsonAsync("/api/sync/receipt", SyncReceipt(Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("device_not_paired");
    }

    [Fact]
    public async Task SyncReceipt_IgnoresBodyTerminalId_AndWritesDeviceTerminal()
    {
        var client = ClientWithRole("Admin");
        var paired = await DeviceTestHelper.PairAsync(_factory, client, "Caisse synchro");
        var orderId = await SeedOpenOrderAsync("D17", 1000);

        var response = await client.PostAsJsonAsync("/api/sync/receipt", SyncReceipt(orderId));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts
            .Single(r => r.OrderId == orderId).TerminalId.Should().Be(paired.TerminalId);
    }

    [Fact]
    public async Task TwoDevices_KeepIndependentChains()
    {
        var clientA = ClientWithRole("Admin");
        var clientB = ClientWithRole("Admin");
        var a = await DeviceTestHelper.PairAsync(_factory, clientA, "Caisse A");
        var b = await DeviceTestHelper.PairAsync(_factory, clientB, "Caisse B");
        var orderA = await SeedOpenOrderAsync("D12", 1000);
        var orderB = await SeedOpenOrderAsync("D13", 2000);

        (await clientA.PostAsJsonAsync("/api/checkout/pay", Payment(orderA, "D12", 10m))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await clientB.PostAsJsonAsync("/api/checkout/pay", Payment(orderB, "D13", 20m))).StatusCode.Should().Be(HttpStatusCode.OK);

        a.TerminalId.Should().NotBe(b.TerminalId);
        using var scope = _factory.Services.CreateScope();
        var receipts = scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts;
        receipts.Single(r => r.OrderId == orderA).PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);
        receipts.Single(r => r.OrderId == orderB).PreviousSignatureHash.Should().Be(NF525FiscalAuditService.GenesisHash);
    }

    [Fact]
    public async Task Void_WritesIntoDeviceChain()
    {
        var client = ClientWithRole("Admin");
        var paired = await DeviceTestHelper.PairAsync(_factory, client, "Caisse annulation");
        var orderId = await SeedOpenOrderAsync("D14", 1200);
        (await client.PostAsJsonAsync("/api/checkout/pay", Payment(orderId, "D14", 12m))).StatusCode.Should().Be(HttpStatusCode.OK);
        Guid receiptId;
        using (var scope = _factory.Services.CreateScope())
        {
            receiptId = scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.Single(r => r.OrderId == orderId).Id;
        }

        var response = await client.PostAsJsonAsync($"/api/checkout/void/{receiptId}", new VoidReceiptRequest("POS_A", Guid.NewGuid()));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var verify = _factory.Services.CreateScope();
        verify.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts
            .Single(r => r.VoidedReceiptId == receiptId).TerminalId.Should().Be(paired.TerminalId);
    }

    [Fact]
    public async Task RevokedDevice_CannotPay_AndIsListedAsRevoked()
    {
        var client = ClientWithRole("Admin");
        var paired = await DeviceTestHelper.PairAsync(_factory, client, "Caisse volée");

        (await ClientWithRole("Admin").PostAsync($"/api/devices/{paired.DeviceId}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var orderId = await SeedOpenOrderAsync("D15", 1000);
        (await client.PostAsJsonAsync("/api/checkout/pay", Payment(orderId, "D15", 10m))).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var list = await ClientWithRole("Admin").GetFromJsonAsync<List<DeviceDto>>("/api/devices");
        list!.Single(d => d.Id == paired.DeviceId).IsRevoked.Should().BeTrue();
        (await ClientWithRole("Admin").GetStringAsync("/api/devices")).Should().NotContain("tokenHash");
    }

    [Fact]
    public async Task Revoke_UnknownDevice_Returns404()
    {
        (await ClientWithRole("Admin").PostAsync($"/api/devices/{Guid.NewGuid()}/revoke", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
