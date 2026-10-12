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

/// <summary>Surpaiement et règlements ≤ 0 : refusés en 400 par tous les endpoints qui écrivent un reçu, sans 500 ni reçu écrit.</summary>
public class PaymentGuardApiTests : IClassFixture<PosApiApplicationFactory>
{
    private readonly PosApiApplicationFactory _factory;

    public PaymentGuardApiTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(HttpClient Client, string TerminalId)> PairedClientAsync()
    {
        var client = _factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await login.Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);
        var paired = await DeviceTestHelper.PairAsync(_factory, client);
        return (client, paired.TerminalId);
    }

    private async Task<Guid> SeedOrderAsync(long unitPriceCents)
    {
        var orderId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order { Id = orderId, TableNumber = "Comptoir", Status = OrderStatus.Open };
        order.Items.Add(new OrderItem
        {
            OrderId = orderId,
            ProductId = Guid.NewGuid(),
            ProductName = "Plat",
            Quantity = 1,
            UnitPrice = Money.FromCents(unitPriceCents),
            TaxRatePercent = 10
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return orderId;
    }

    private async Task<int> ReceiptCountAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().FiscalReceipts.CountAsync(r => r.OrderId == orderId);
    }

    private static async Task AssertPaymentFailedAsync(HttpResponseMessage response, string key = "errors.payment_failed")
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().NotBeNullOrWhiteSpace().And.NotBe(key);
    }

    [Theory]
    [InlineData(10.01)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task CheckoutPay_RejectsOverpaymentAndNonPositiveTender(double amount)
    {
        var (client, _) = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);

        var response = await client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(
            orderId, null, Guid.NewGuid(),
            [new TenderItemRequest(PaymentMethod.CreditCard, (decimal)amount, (decimal)Math.Max(0, amount), 0m)], "POS_TEST"));

        await AssertPaymentFailedAsync(response);
        (await ReceiptCountAsync(orderId)).Should().Be(0);
    }

    [Fact]
    public async Task CheckoutPay_ExactAndTippedPayments_AreAccepted()
    {
        var (client, _) = await PairedClientAsync();
        var exact = await SeedOrderAsync(1000);
        var tipped = await SeedOrderAsync(1000);

        (await client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(
            exact, null, Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.Cash, 10m, 20m, 0m)], "POS_TEST")))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var withTip = await client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(
            tipped, null, Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.CreditCard, 12m, 12m, 0m)], "POS_TEST", TipAmount: 2m));
        withTip.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CheckoutPay_OverpaymentWithTip_LeavesNoTipBehind()
    {
        var (client, _) = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);

        var response = await client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(
            orderId, null, Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.CreditCard, 12.01m, 12.01m, 0m)], "POS_TEST", TipAmount: 2m));

        await AssertPaymentFailedAsync(response);
        using var scope = _factory.Services.CreateScope();
        var order = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.FindAsync(orderId);
        order!.TipAmount.AmountInCents.Should().Be(0);
    }

    [Theory]
    [InlineData(1001)]
    [InlineData(0)]
    [InlineData(-500)]
    public async Task CounterCheckout_RejectsOverpaymentAndNonPositiveTender_With400(int amountCents)
    {
        var (client, terminalId) = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);
        var amount = amountCents / 100m;

        var response = await client.PostAsJsonAsync("/api/orders/counter/checkout", new CounterCheckoutRequest(
            orderId, terminalId, OrderDestination.Takeaway, null, null, 0m, false, null,
            [new CounterPaymentTender(PaymentMethod.CreditCard, amount, Math.Max(0m, amount))]));

        await AssertPaymentFailedAsync(response);
        (await ReceiptCountAsync(orderId)).Should().Be(0);
    }

    [Fact]
    public async Task CounterCheckout_ExactPayment_IsAccepted()
    {
        var (client, terminalId) = await PairedClientAsync();
        var orderId = await SeedOrderAsync(1000);

        var response = await client.PostAsJsonAsync("/api/orders/counter/checkout", new CounterCheckoutRequest(
            orderId, terminalId, OrderDestination.Takeaway, null, null, 0m, false, null,
            [new CounterPaymentTender(PaymentMethod.Cash, 10m, 20m)]));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData(1001, 1000)] // surpaiement sur commande existante
    [InlineData(0, 0)]
    [InlineData(-500, -500)] // total <= 0 : aucune ligne à prix négatif ne doit être créée
    public async Task SyncReceipt_RejectsOverpaymentAndNonPositiveTender_With400(long amountCents, long totalCents)
    {
        var (client, terminalId) = await PairedClientAsync();
        var existing = await SeedOrderAsync(1000);
        var orderId = totalCents == 1000 ? existing : Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/sync/receipt", new SyncReceiptDto(
            orderId, "Comptoir", totalCents, [new SyncTenderDto(PaymentMethod.CreditCard, amountCents, amountCents)], terminalId));

        await AssertPaymentFailedAsync(response, "errors.sync_payment_failed");
        (await ReceiptCountAsync(orderId)).Should().Be(0);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.AnyAsync(o => o.Id == orderId && o.Items.Any(i => i.UnitPrice.AmountInCents < 0)))
            .Should().BeFalse();
    }

    [Fact]
    public async Task SyncReceipt_OverpaymentOnNewOrder_CreatesNoOrder()
    {
        var (client, terminalId) = await PairedClientAsync();
        var orderId = Guid.NewGuid();

        var response = await client.PostAsJsonAsync("/api/sync/receipt", new SyncReceiptDto(
            orderId, "Comptoir", 1000, [new SyncTenderDto(PaymentMethod.CreditCard, 1500, 1500)], terminalId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = _factory.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.AnyAsync(o => o.Id == orderId)).Should().BeFalse();
    }
}
