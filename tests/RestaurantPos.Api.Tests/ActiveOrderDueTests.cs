using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

/// <summary>
/// Reste dû de la commande active (D5) : <c>AmountPaidTtc</c> = règlements des reçus actifs (paire annulée exclue),
/// <c>AmountDueTtc</c> = TTC + pourboire conservé − réglé, la même règle que la Z (<c>FindOpenOrdersAsync</c>).
/// </summary>
public class ActiveOrderDueTests : IClassFixture<PosApiApplicationFactory>
{
    private const string Terminal = "T01";
    private readonly PosApiApplicationFactory _factory;

    public ActiveOrderDueTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<(Guid OrderId, string Table)> SeedTableOrderAsync(long cents, bool comp = false, decimal discountPercent = 0m)
    {
        var table = "D5_" + Guid.NewGuid().ToString("N")[..8];
        var orderId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order { Id = orderId, TableNumber = table, Status = OrderStatus.SentToKitchen };
        if (discountPercent > 0)
        {
            order.GlobalDiscountType = DiscountType.Percentage;
            order.GlobalDiscountValue = discountPercent;
        }
        order.Items.Add(new OrderItem
        {
            OrderId = orderId,
            ProductId = Guid.NewGuid(),
            ProductName = "Plat",
            Quantity = 1,
            UnitPrice = Money.FromCents(cents),
            TaxRatePercent = 10,
            IsDispatched = true,
            IsComp = comp
        });
        db.Orders.Add(order);
        db.DiningTables.Add(new DiningTable { TableNumber = table, ActiveOrderId = orderId, Status = TableStatus.Occupied });
        await db.SaveChangesAsync();
        return (orderId, table);
    }

    private async Task<Guid> PayAsync(Guid orderId, long cents, long tipCents = 0)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (tipCents > 0)
        {
            var o = await db.Orders.SingleAsync(x => x.Id == orderId);
            o.TipAmount = Money.FromCents(o.TipAmount.AmountInCents + tipCents);
            await db.SaveChangesAsync();
        }
        var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutPaymentService>();
        var result = await checkout.ProcessPaymentTendersAsync(orderId, Terminal, [new PaymentTenderRequest(PaymentMethod.CreditCard, cents, cents)]);
        result.IsSuccess.Should().BeTrue();
        return (await db.FiscalReceipts.AsNoTracking().SingleAsync(r => r.ReceiptNumber == result.ReceiptNumber)).Id;
    }

    private async Task VoidAsync(Guid receiptId)
    {
        using var scope = _factory.Services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutPaymentService>();
        (await checkout.VoidReceiptAsync(receiptId, Terminal, Guid.NewGuid())).IsSuccess.Should().BeTrue();
    }

    private async Task<HttpResponseMessage> GetOrderAsync(string table)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Role", "FloorManager");
        return await client.GetAsync($"/api/tables/{table}/order");
    }

    private async Task AssertDueAsync(string table, decimal paid, decimal due)
    {
        var response = await GetOrderAsync(table);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<ActiveTableOrderDto>();
        dto!.AmountPaidTtc.Should().Be(paid);
        dto.AmountDueTtc.Should().Be(due);
        using var scope = _factory.Services.CreateScope();
        var open = await scope.ServiceProvider.GetRequiredService<INF525FiscalAuditService>().FindOpenOrdersAsync();
        var remaining = open.SingleOrDefault(o => o.OrderId == dto.OrderId)?.RemainingTtcCents ?? 0;
        Money.FromCents(remaining).ToDecimal().Should().Be(due, "le reste dû de la commande active est celui de la Z");
    }

    [Fact]
    public async Task Unpaid_DueIsTheTotal()
    {
        var (_, table) = await SeedTableOrderAsync(10_000);
        await AssertDueAsync(table, 0m, 100m);
    }

    [Fact]
    public async Task PartlyPaid_DueIsTheRest()
    {
        var (orderId, table) = await SeedTableOrderAsync(10_000);
        await PayAsync(orderId, 4_000);
        await AssertDueAsync(table, 40m, 60m);
    }

    [Fact]
    public async Task ReopenedAfterShareVoid_DueIsTheVoidedShare_ThenZeroOnceRepaid()
    {
        var (orderId, table) = await SeedTableOrderAsync(10_000);
        var first = await PayAsync(orderId, 6_000);
        await PayAsync(orderId, 4_000); // soldée : la table est libérée
        (await GetOrderAsync(table)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await VoidAsync(first); // la paire reçu / avoir est exclue des règlements actifs

        await AssertDueAsync(table, 40m, 60m);

        await PayAsync(orderId, 6_000);
        // Re-soldée : plus de commande active sur la table, rien à encaisser.
        (await GetOrderAsync(table)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task WithTip_VoidingTheTiplessShare_DueIsThatShare_AndVoidingTheTippedShare_DropsTheTip()
    {
        var (a, tableA) = await SeedTableOrderAsync(10_000);
        var firstA = await PayAsync(a, 3_000);
        await PayAsync(a, 7_500, tipCents: 500); // 70 € + 5 € de pourboire
        await VoidAsync(firstA);
        // Pourboire conservé sur le reçu actif : 100 + 5 − 75 = 30.
        await AssertDueAsync(tableA, 75m, 30m);

        var (b, tableB) = await SeedTableOrderAsync(10_000);
        await PayAsync(b, 3_000);
        var tippedB = await PayAsync(b, 7_500, tipCents: 500);
        await VoidAsync(tippedB);
        // Pourboire retiré avec son reçu : 100 + 0 − 30 = 70.
        await AssertDueAsync(tableB, 30m, 70m);
    }

    [Fact]
    public async Task CompAndDiscount_DueFollowsTheOrderTotal()
    {
        var (_, compTable) = await SeedTableOrderAsync(10_000, comp: true);
        await AssertDueAsync(compTable, 0m, 0m);

        var (orderId, discountTable) = await SeedTableOrderAsync(10_000, discountPercent: 20m);
        await PayAsync(orderId, 3_000);
        await AssertDueAsync(discountTable, 30m, 50m);
    }
}
