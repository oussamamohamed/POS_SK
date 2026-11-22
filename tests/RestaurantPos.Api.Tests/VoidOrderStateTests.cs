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

    private async Task<Guid> SeedOrderAsync(long totalCents, string tableNumber = "Comptoir")
    {
        var orderId = Guid.NewGuid();
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var order = new Order { Id = orderId, TableNumber = tableNumber, Status = OrderStatus.SentToKitchen };
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

    private async Task<DiningTable> TableAsync(string number)
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DiningTables.AsNoTracking().SingleAsync(t => t.TableNumber == number);
    }

    private async Task AddTableAsync(string number, Guid? activeOrderId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.DiningTables.Add(new DiningTable { TableNumber = number, ActiveOrderId = activeOrderId, Status = activeOrderId is null ? TableStatus.Free : TableStatus.Occupied });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Void_FirstOfTwoReceipts_RelinksFreeTable()
    {
        await AddTableAsync("V1");
        var orderId = await SeedOrderAsync(10000, "V1");
        var first = await PayAsync(orderId, 6000);
        await PayAsync(orderId, 4000); // règlement soldé : la table est libérée
        (await TableAsync("V1")).ActiveOrderId.Should().BeNull();

        await VoidAsync(first);

        var table = await TableAsync("V1");
        table.ActiveOrderId.Should().Be(orderId);
        table.Status.Should().Be(TableStatus.Occupied);
    }

    [Fact]
    public async Task Void_PartialReceipt_LeavesTableTakenByAnotherOrder()
    {
        await AddTableAsync("V2");
        var orderId = await SeedOrderAsync(10000, "V2");
        var first = await PayAsync(orderId, 6000);
        await PayAsync(orderId, 4000);
        var other = await SeedOrderAsync(500, "V2");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var t = await db.DiningTables.SingleAsync(x => x.TableNumber == "V2");
            t.ActiveOrderId = other;
            t.Status = TableStatus.Occupied;
            await db.SaveChangesAsync();
        }

        await VoidAsync(first);

        (await StatusAsync(orderId)).Should().Be(OrderStatus.BillRequested);
        (await TableAsync("V2")).ActiveOrderId.Should().Be(other);
        (await StatusAsync(other)).Should().Be(OrderStatus.SentToKitchen);
    }

    [Fact]
    public async Task Void_AllSplitReceipts_KeepsOrderBillRequestedAndPayableFromZero()
    {
        var orderId = await SeedOrderAsync(10_000);
        var r1 = await PayAsync(orderId, 4_000);
        var r2 = await PayAsync(orderId, 3_000);
        var r3 = await PayAsync(orderId, 3_000);

        await VoidAsync(r1);
        await VoidAsync(r2);
        await VoidAsync(r3);

        (await StatusAsync(orderId)).Should().Be(OrderStatus.BillRequested);
        await PayAsync(orderId, 10_000);
        (await StatusAsync(orderId)).Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public async Task Void_OrderWithGlobalDiscountAndTip_CancelsOnFullAndReopensOnPartial()
    {
        var fullId = await SeedOrderAsync(10_000);
        var partialId = await SeedOrderAsync(10_000);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var id in new[] { fullId, partialId })
            {
                var o = await db.Orders.SingleAsync(x => x.Id == id);
                o.GlobalDiscountType = DiscountType.Percentage;
                o.GlobalDiscountValue = 20m; // TTC après remise = 8 000 c
                o.TipAmount = Money.FromCents(500);
            }
            await db.SaveChangesAsync();
        }

        // Reçu intégral (8 000 c fiscal + 500 c pourboire = 8 500 c réglés) -> Cancelled
        var fullReceipt = await PayAsync(fullId, 8_500);
        await VoidAsync(fullReceipt);
        (await StatusAsync(fullId)).Should().Be(OrderStatus.Cancelled);

        // Note partagée avec remise + pourboire -> annuler la part pourboire rouvre et remet TipAmount à 0 : on peut re-solder juste le TTC (5 000 c) sans pourboire
        await PayAsync(partialId, 3_000);
        var second = await PayAsync(partialId, 5_500);
        await VoidAsync(second);
        (await StatusAsync(partialId)).Should().Be(OrderStatus.BillRequested);
        using (var scope = _factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders.AsNoTracking().SingleAsync(o => o.Id == partialId)).TipAmount.AmountInCents.Should().Be(0);
        }
        await PayAsync(partialId, 5_000);
        (await StatusAsync(partialId)).Should().Be(OrderStatus.Paid);
    }

    [Fact]
    public async Task Void_DoubleVoid_VoidOfVoidReceipt_AndWrongTerminal_AreAllRefused()
    {
        var orderId = await SeedOrderAsync(5_000);
        var receiptId = await PayAsync(orderId, 5_000);

        using var scope = _factory.Services.CreateScope();
        var checkout = scope.ServiceProvider.GetRequiredService<ICheckoutPaymentService>();

        // Autre poste -> refusé
        (await checkout.VoidReceiptAsync(receiptId, "T02", Guid.NewGuid())).IsSuccess.Should().BeFalse();

        // Première annulation -> acceptée
        var first = await checkout.VoidReceiptAsync(receiptId, Terminal, Guid.NewGuid());
        first.IsSuccess.Should().BeTrue();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var voidReceiptId = (await db.FiscalReceipts.AsNoTracking().SingleAsync(r => r.ReceiptNumber == first.ReceiptNumber)).Id;

        // Double annulation -> refusée ; annulation du reçu -VOID- -> refusée
        (await checkout.VoidReceiptAsync(receiptId, Terminal, Guid.NewGuid())).IsSuccess.Should().BeFalse();
        (await checkout.VoidReceiptAsync(voidReceiptId, Terminal, Guid.NewGuid())).IsSuccess.Should().BeFalse();
    }
}
