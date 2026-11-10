using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Api.Endpoints;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

/// <summary>Paiement par TPE : refus ou terminal indisponible = commande intacte et relance possible ; accord = ticket soldé.</summary>
public class CardTerminalPaymentTests
{
    private sealed class ScriptedTerminal(params CardChargeStatus[] script) : ICardTerminal
    {
        private int _next;
        public List<long> Amounts { get; } = [];

        public Task<CardChargeResult> ChargeAsync(long amountCents, string reference, CancellationToken cancellationToken)
        {
            Amounts.Add(amountCents);
            return Task.FromResult(new CardChargeResult(script[Math.Min(_next++, script.Length - 1)], reference));
        }
    }

    private static WebApplicationFactory<Program> FactoryWith(ICardTerminal terminal) =>
        new PosApiApplicationFactory().WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(terminal)));

    private static async Task<HttpClient> PairedClientAsync(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        var login = await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"));
        var result = await login.Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", result!.Token);
        await DeviceTestHelper.PairAsync(factory, client);
        return client;
    }

    private static async Task<Guid> SeedOrderAsync(WebApplicationFactory<Program> factory, long unitPriceCents)
    {
        var orderId = Guid.NewGuid();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order { Id = orderId, TableNumber = "Comptoir", Status = OrderStatus.Open };
        order.Items.Add(new OrderItem { OrderId = orderId, ProductId = Guid.NewGuid(), ProductName = "Plat", Quantity = 1, UnitPrice = Money.FromCents(unitPriceCents), TaxRatePercent = 10 });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return orderId;
    }

    private static async Task<(int Receipts, OrderStatus Status)> StateAsync(WebApplicationFactory<Program> factory, Guid orderId)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.FiscalReceipts.CountAsync(r => r.OrderId == orderId), (await db.Orders.FirstAsync(o => o.Id == orderId)).Status);
    }

    private static Task<HttpResponseMessage> PayAsync(HttpClient client, Guid orderId, PaymentMethod method = PaymentMethod.CreditCard) =>
        client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(orderId, null, Guid.NewGuid(), [new TenderItemRequest(method, 10m, 10m, 0m)], "POS_TEST"));

    [Fact]
    public async Task Declined_ThenRetryApproved_SettlesTheTicket()
    {
        var terminal = new ScriptedTerminal(CardChargeStatus.Declined, CardChargeStatus.Approved);
        using var factory = FactoryWith(terminal);
        var client = await PairedClientAsync(factory);
        var orderId = await SeedOrderAsync(factory, 1000);

        var first = await PayAsync(client, orderId);
        first.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("card_declined");
        (await StateAsync(factory, orderId)).Should().Be((0, OrderStatus.Open));

        (await PayAsync(client, orderId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StateAsync(factory, orderId)).Should().Be((1, OrderStatus.Paid));
        terminal.Amounts.Should().Equal(1000, 1000);
    }

    [Fact]
    public async Task TerminalUnavailable_WritesNothing()
    {
        using var factory = FactoryWith(new ScriptedTerminal(CardChargeStatus.Unavailable));
        var client = await PairedClientAsync(factory);
        var orderId = await SeedOrderAsync(factory, 1000);

        var response = await PayAsync(client, orderId);

        response.StatusCode.Should().Be(HttpStatusCode.PaymentRequired);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString().Should().Be("card_terminal_unavailable");
        (await StateAsync(factory, orderId)).Should().Be((0, OrderStatus.Open));
    }

    [Fact]
    public async Task CashPayment_NeverCallsTheTerminal()
    {
        var terminal = new ScriptedTerminal(CardChargeStatus.Declined);
        using var factory = FactoryWith(terminal);
        var client = await PairedClientAsync(factory);
        var orderId = await SeedOrderAsync(factory, 1000);

        (await PayAsync(client, orderId, PaymentMethod.Cash)).StatusCode.Should().Be(HttpStatusCode.OK);
        terminal.Amounts.Should().BeEmpty();
    }
}
