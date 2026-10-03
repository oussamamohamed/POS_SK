using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Services;
using Xunit;

namespace RestaurantPos.Infrastructure.Tests;

public class VoidWithoutMutationTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task VoidReceiptAsync_OriginalReceiptColumnsRemainUnchanged()
    {
        using var db = CreateInMemoryDbContext();
        var audit = new NF525FiscalAuditService(db);
        var checkout = new CheckoutPaymentService(db, audit);

        var orderId = UuidV7.NewGuid();
        var order = new Order
        {
            Id = orderId,
            TableNumber = "12",
            Status = OrderStatus.Open,
            Items = new List<OrderItem>
            {
                new()
                {
                    Id = UuidV7.NewGuid(),
                    OrderId = orderId,
                    ProductId = Guid.NewGuid(),
                    ProductName = "Entrecôte",
                    Quantity = 1,
                    UnitPrice = Money.FromCents(2500),
                    TaxRatePercent = 10.0m
                }
            }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var tenders = new List<PaymentTenderRequest>
        {
            new(PaymentMethod.CreditCard, 2500, 2500)
        };

        var settleResult = await checkout.ProcessPaymentTendersAsync(orderId, "POS_MAIN_TERM", tenders);
        settleResult.IsSuccess.Should().BeTrue();

        var original = await db.FiscalReceipts.AsNoTracking().FirstAsync(r => r.ReceiptNumber == settleResult.ReceiptNumber);
#pragma warning disable CS0618
        original.IsVoid.Should().BeFalse();
#pragma warning restore CS0618
        original.VoidedReceiptId.Should().BeNull();

        var opId = Guid.NewGuid();
        var voidResult = await checkout.VoidReceiptAsync(original.Id, "POS_MAIN_TERM", opId);
        voidResult.IsSuccess.Should().BeTrue();

        // Reload original from fresh query
        var originalAfterVoid = await db.FiscalReceipts.AsNoTracking().FirstAsync(r => r.Id == original.Id);

        // INV-3: original receipt must not have mutated
#pragma warning disable CS0618
        originalAfterVoid.IsVoid.Should().BeFalse("original receipt must never have its IsVoid column set to true");
#pragma warning restore CS0618
        originalAfterVoid.VoidedReceiptId.Should().BeNull("original receipt does not point to another voided receipt");
        originalAfterVoid.ReceiptNumber.Should().Be(original.ReceiptNumber);
        originalAfterVoid.SequenceNumber.Should().Be(original.SequenceNumber);
        originalAfterVoid.TotalTtcAmount.AmountInCents.Should().Be(original.TotalTtcAmount.AmountInCents);
        originalAfterVoid.TotalHtAmount.AmountInCents.Should().Be(original.TotalHtAmount.AmountInCents);
        originalAfterVoid.SignatureHash.Should().Be(original.SignatureHash);
        originalAfterVoid.PreviousSignatureHash.Should().Be(original.PreviousSignatureHash);
        originalAfterVoid.CreatedAtUtc.Should().Be(original.CreatedAtUtc);

        // Corrective receipt exists and points to original
        var voidReceipt = await db.FiscalReceipts.AsNoTracking().FirstOrDefaultAsync(r => r.VoidedReceiptId == original.Id);
        voidReceipt.Should().NotBeNull();
        voidReceipt!.TotalTtcAmount.AmountInCents.Should().Be(-2500);
    }

    [Fact]
    public async Task VoidReceiptAsync_SecondVoidAttempt_IsRefused()
    {
        using var db = CreateInMemoryDbContext();
        var audit = new NF525FiscalAuditService(db);
        var checkout = new CheckoutPaymentService(db, audit);

        var orderId = UuidV7.NewGuid();
        var order = new Order
        {
            Id = orderId,
            TableNumber = "14",
            Status = OrderStatus.Open,
            Items = new List<OrderItem>
            {
                new()
                {
                    Id = UuidV7.NewGuid(),
                    OrderId = orderId,
                    ProductId = Guid.NewGuid(),
                    ProductName = "Burger",
                    Quantity = 1,
                    UnitPrice = Money.FromCents(1500),
                    TaxRatePercent = 10.0m
                }
            }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var settleResult = await checkout.ProcessPaymentTendersAsync(orderId, "POS_MAIN_TERM", [new PaymentTenderRequest(PaymentMethod.Cash, 1500, 1500)]);
        settleResult.IsSuccess.Should().BeTrue();

        var original = await db.FiscalReceipts.FirstAsync(r => r.ReceiptNumber == settleResult.ReceiptNumber);
        var firstVoid = await checkout.VoidReceiptAsync(original.Id, "POS_MAIN_TERM", Guid.NewGuid());
        firstVoid.IsSuccess.Should().BeTrue();

        // Second void must be refused
        var secondVoid = await checkout.VoidReceiptAsync(original.Id, "POS_MAIN_TERM", Guid.NewGuid());
        secondVoid.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task VoidReceiptAsync_AttemptingToVoidAVoidReceipt_IsRefused()
    {
        using var db = CreateInMemoryDbContext();
        var audit = new NF525FiscalAuditService(db);
        var checkout = new CheckoutPaymentService(db, audit);

        var orderId = UuidV7.NewGuid();
        var order = new Order
        {
            Id = orderId,
            TableNumber = "15",
            Status = OrderStatus.Open,
            Items = new List<OrderItem>
            {
                new()
                {
                    Id = UuidV7.NewGuid(),
                    OrderId = orderId,
                    ProductId = Guid.NewGuid(),
                    ProductName = "Salade",
                    Quantity = 1,
                    UnitPrice = Money.FromCents(1200),
                    TaxRatePercent = 10.0m
                }
            }
        };
        db.Orders.Add(order);
        await db.SaveChangesAsync();

        var settleResult = await checkout.ProcessPaymentTendersAsync(orderId, "POS_MAIN_TERM", [new PaymentTenderRequest(PaymentMethod.Cash, 1200, 1200)]);
        var original = await db.FiscalReceipts.FirstAsync(r => r.ReceiptNumber == settleResult.ReceiptNumber);
        var firstVoid = await checkout.VoidReceiptAsync(original.Id, "POS_MAIN_TERM", Guid.NewGuid());
        firstVoid.IsSuccess.Should().BeTrue();

        var voidReceipt = await db.FiscalReceipts.FirstAsync(r => r.VoidedReceiptId == original.Id);

        // Attempting to void the void/avoir itself must fail
        var voidOfVoid = await checkout.VoidReceiptAsync(voidReceipt.Id, "POS_MAIN_TERM", Guid.NewGuid());
        voidOfVoid.IsSuccess.Should().BeFalse();
    }
}
