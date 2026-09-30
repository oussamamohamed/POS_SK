using System;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Infrastructure.Persistence;
using Xunit;

namespace RestaurantPos.Api.Tests;

/// <summary>Pourboire à table (paiement soldant la note) et attribution de l'opérateur au comptoir.</summary>
public class TableTipTests
{
    private static async Task<(HttpClient Client, Guid OperatorId)> CashierAsync(PosApiApplicationFactory factory)
    {
        var client = factory.CreateClient();
        var login = await (await client.PostAsJsonAsync("/api/auth/login", new PinLoginRequest("9999"))).Content.ReadFromJsonAsync<LoginResultDto>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login!.Token);
        await DeviceTestHelper.PairAsync(factory, client);
        return (client, login.OperatorId);
    }

    private static async Task<Guid> OpenTableWithItemAsync(HttpClient client, string tableNumber, Guid operatorId, decimal price)
    {
        var openResp = await client.PostAsJsonAsync($"/api/tables/{tableNumber}/open", new OpenTableRequest("Serveur Test", 2, operatorId));
        openResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var table = await openResp.Content.ReadFromJsonAsync<DiningTableDto>();
        var orderId = table!.ActiveOrderId!.Value;

        (await client.PostAsJsonAsync($"/api/tables/{tableNumber}/items", new AddOrderItemsRequest(
        [
            new(Guid.NewGuid(), "Plat du jour", 1, price, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true)
        ]))).EnsureSuccessStatusCode();

        return orderId;
    }

    [Fact]
    public async Task Pay_FullWithTip_RecordsTipOnOrder_AttributedToTableServer()
    {
        using var factory = new PosApiApplicationFactory();
        var (client, operatorId) = await CashierAsync(factory);
        var orderId = await OpenTableWithItemAsync(client, "T21", operatorId, 20.00m);

        var payResp = await client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(
            Guid.Empty, "T21", Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.CreditCard, 22.00m, 22.00m, 0m)], null, false, 2.00m));

        payResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await payResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("remainingBalance").GetDecimal().Should().Be(0m);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        order.TipAmount.AmountInCents.Should().Be(200);
        order.OperatorId.Should().Be(operatorId);
    }

    [Fact]
    public async Task Pay_TipOnPartialPayment_Returns400_NothingWritten()
    {
        using var factory = new PosApiApplicationFactory();
        var (client, operatorId) = await CashierAsync(factory);
        var orderId = await OpenTableWithItemAsync(client, "T22", operatorId, 20.00m);

        var payResp = await client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(
            Guid.Empty, "T22", Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.CreditCard, 10.00m, 10.00m, 0m)], null, false, 1.00m));

        payResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await payResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Be("A tip can only be added to the payment that settles the bill.");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == orderId);
        order.TipAmount.AmountInCents.Should().Be(0);
        (await db.FiscalReceipts.AsNoTracking().AnyAsync(r => r.OrderId == orderId)).Should().BeFalse();
    }

    [Fact]
    public async Task Pay_NegativeTip_Returns400()
    {
        using var factory = new PosApiApplicationFactory();
        var (client, operatorId) = await CashierAsync(factory);
        var orderId = await OpenTableWithItemAsync(client, "T23", operatorId, 20.00m);

        var payResp = await client.PostAsJsonAsync("/api/checkout/pay", new PaymentSettlementRequest(
            Guid.Empty, "T23", Guid.NewGuid(), [new TenderItemRequest(PaymentMethod.CreditCard, 20.00m, 20.00m, 0m)], null, false, -1m));

        payResp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await payResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("message").GetString().Should().Be("Invalid tip amount.");

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        (await db.FiscalReceipts.AsNoTracking().AnyAsync(r => r.OrderId == orderId)).Should().BeFalse();
    }

    [Fact]
    public async Task CounterCheckout_SetsOperatorFromToken_WhenOrderHasNone()
    {
        using var factory = new PosApiApplicationFactory();
        var (client, operatorId) = await CashierAsync(factory);

        var order = await (await client.PostAsJsonAsync("/api/orders/counter/direct", new DirectCounterOpenRequest("T01", OrderDestination.Takeaway))).Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        (await client.PostAsJsonAsync("/api/tables/Comptoir/items", new AddOrderItemsRequest(
        [
            new(Guid.NewGuid(), "Sandwich Poulet", 1, 6.50m, 10.0m, null, null, CourseType.Direct, 0m, 10.0m, true)
        ]))).EnsureSuccessStatusCode();

        var res = await client.PostAsJsonAsync("/api/orders/counter/checkout", new CounterCheckoutRequest(
            order!.OrderId, null, OrderDestination.Takeaway, null, null, 1.00m, false, null, [new CounterPaymentTender(PaymentMethod.CreditCard, 7.50m, 7.50m)]));

        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedOrder = await db.Orders.AsNoTracking().SingleAsync(o => o.Id == order.OrderId);
        savedOrder.OperatorId.Should().Be(operatorId);
        savedOrder.TipAmount.AmountInCents.Should().Be(100);
    }
}
