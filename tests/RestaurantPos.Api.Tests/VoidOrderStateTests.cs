using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Tests;

/// <summary>État de la commande après annulation d'un reçu : <c>Cancelled</c> seulement si le reçu couvrait tout le TTC, sinon elle redevient réglable.</summary>
public class VoidOrderStateTests : IClassFixture<PosApiApplicationFactory>
{
    private const string Terminal = "T01";
    private readonly PosApiApplicationFactory _factory;

    public VoidOrderStateTests(PosApiApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<Guid> SeedOrderAsync(long totalCents)
    {
        var orderId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order { Id = orderId, TableNumber = "Comptoir", Status = OrderStatus.SentToKitchen };
        order.Items.Add(new OrderItem
        {
            OrderId = orderId,
            ProductId = Guid.NewGuid(),
            ProductName = "Plat",
            Quantity = 1,
            UnitPrice = Money.FromCents(totalCents),
            TaxRatePercent = 10,
            IsDispatched = true
        });
        db.Orders.Add(order);
        await db.SaveChangesAsync();
        return orderId;
    }

    private async Task<Guid> PayAsync(Guid orderId, long cents)
    {
        using var scope = _factory.Services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutPaymentService>();
        var result = await checkout.ProcessPaymentTendersAsync(orderId, Terminal, [new PaymentTenderRequest(PaymentMethod.Cash, cents, cents)]);
        result.IsSuccess.Should().BeTrue();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.FiscalReceipts.AsNoTracking().SingleAsync(r => r.ReceiptNumber == result.ReceiptNumber)).Id;
    }

    private async Task VoidAsync(Guid receiptId)
    {
        using var scope = _factory.Services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutPaymentService>();
        (await checkout.VoidReceiptAsync(receiptId, Terminal, Guid.NewGuid())).IsSuccess.Should().BeTrue();
    }

    private async Task<OrderStatus> StatusAsync(Guid orderId)
    {
        using var scope = _factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == orderId)).Status;
    }

    [Fact]
    public async Task Void_PartialReceipt_ReopensOrderAsBillRequested()
    {
        var orderId = await SeedOrderAsync(10000);
        var receipt = await PayAsync(orderId, 4000);

        await VoidAsync(receipt);

        (await StatusAsync(orderId)).Should().Be(OrderStatus.BillRequested);
    }

    [Fact]
    public async Task Void_FullReceipt_CancelsOrder()
    {
        var orderId = await SeedOrderAsync(10000);
        var receipt = await PayAsync(orderId, 10000);

        await VoidAsync(receipt);

        (await StatusAsync(orderId)).Should().Be(OrderStatus.Cancelled);
    }

    [Fact]
    public async Task Void_FirstOfTwoReceipts_KeepsOrderPayable()
    {
        var orderId = await SeedOrderAsync(10000);
        var first = await PayAsync(orderId, 6000);
        await PayAsync(orderId, 4000);

        await VoidAsync(first);

        (await StatusAsync(orderId)).Should().Be(OrderStatus.BillRequested);
        // Reste dû : 60 € (le second reçu de 40 € est toujours valable).
        await PayAsync(orderId, 6000);
        (await StatusAsync(orderId)).Should().Be(OrderStatus.Paid);
    }
}
