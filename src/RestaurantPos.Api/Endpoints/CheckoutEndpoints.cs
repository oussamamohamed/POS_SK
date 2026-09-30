using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Localization;
using RestaurantPos.Infrastructure.Persistence;
using RestaurantPos.Infrastructure.Printing;

namespace RestaurantPos.Api.Endpoints;

public static class CheckoutEndpoints
{
    public static void MapCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/checkout")
                       .WithTags("Checkout & Payments")
                       .RequireAuthorization();

        group.MapPost("/pay", async (PaymentSettlementRequest req, ICheckoutPaymentService checkout, AppDbContext db, PrintDispatcher printing, HttpContext http) =>
        {
            var orderId = req.OrderId;
            if (orderId == Guid.Empty || !await db.Orders.AnyAsync(o => o.Id == orderId))
            {
                if (!string.IsNullOrWhiteSpace(req.TableNumber))
                {
                    var alt = NormalizeAltTableNumber(req.TableNumber);
                    var table = await db.DiningTables.FirstOrDefaultAsync(t => t.TableNumber == req.TableNumber || (alt != null && t.TableNumber == alt));
                    if (table?.ActiveOrderId != null && await db.Orders.AnyAsync(o => o.Id == table.ActiveOrderId.Value))
                    {
                        orderId = table.ActiveOrderId.Value;
                    }
                    else
                    {
                        var openOrder = await db.Orders
                            .Where(o => (o.TableNumber == req.TableNumber || (alt != null && o.TableNumber == alt))
                                        && o.Status != OrderStatus.Paid && o.Status != OrderStatus.Cancelled)
                            .OrderByDescending(o => o.CreatedAtUtc)
                            .FirstOrDefaultAsync();
                        if (openOrder is not null)
                        {
                            orderId = openOrder.Id;
                        }
                    }
                }
            }

            if (orderId == Guid.Empty || !await db.Orders.AnyAsync(o => o.Id == orderId))
            {
                long totalTendersCents = req.Tenders != null ? (long)Math.Round(req.Tenders.Sum(t => t.Amount) * 100) : 0;
                if (totalTendersCents > 0)
                {
                    var newOrder = new Order
                    {
                        Id = orderId != Guid.Empty ? orderId : Guid.NewGuid(),
                        TableNumber = !string.IsNullOrWhiteSpace(req.TableNumber) ? req.TableNumber : "Comptoir",
                        Status = OrderStatus.Open,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    };
                    newOrder.Items.Add(new OrderItem
                    {
                        OrderId = newOrder.Id,
                        ProductId = Guid.NewGuid(),
                        ProductName = $"Vente {newOrder.TableNumber}",
                        UnitPrice = Money.FromCents(totalTendersCents),
                        Quantity = 1,
                        TaxRatePercent = 10.0m
                    });
                    db.Orders.Add(newOrder);
                    await db.SaveChangesAsync();
                    orderId = newOrder.Id;
                }
                else
                {
                    return Results.BadRequest(new { Message = Texts.T("errors.order_not_found_for_payment") });
                }
            }

            var tenderRequests = (req.Tenders ?? []).Select(t => new PaymentTenderRequest(
                t.Method,
                (long)Math.Round(t.Amount * 100),
                (long)Math.Round(t.Tendered * 100)
            )).ToList();

            if (req.TipAmount < 0)
            {
                return Results.BadRequest(new { Message = Texts.T("errors.tip_invalid") });
            }

            Order? tippedOrder = null;
            long tipCents = (long)Math.Round(req.TipAmount * 100);
            if (tipCents > 0)
            {
                tippedOrder = await db.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
                var paidCents = (await db.FiscalReceipts.Include(r => r.Tenders).Where(r => r.OrderId == orderId && !r.IsVoid).ToListAsync())
                    .SelectMany(r => r.Tenders).Sum(t => t.Amount.AmountInCents);
                var remainingCents = tippedOrder.TotalTtc.AmountInCents + tippedOrder.TipAmount.AmountInCents - paidCents;
                // Un pourboire sur un paiement partiel entrerait dans le montant fiscal du reçu (ratio) : refusé.
                if (tenderRequests.Sum(t => t.AmountInCents) < remainingCents + tipCents)
                {
                    return Results.BadRequest(new { Message = Texts.T("errors.tip_only_on_final_payment") });
                }
                tippedOrder.TipAmount = Money.FromCents(tippedOrder.TipAmount.AmountInCents + tipCents);
                await db.SaveChangesAsync();
            }

            var terminalId = RequireDeviceFilter.PairedDevice(http).TerminalId;

            var result = await checkout.ProcessPaymentTendersAsync(orderId, terminalId, tenderRequests);

            if (!result.IsSuccess)
            {
                if (tippedOrder is not null)
                {
                    tippedOrder.TipAmount = Money.FromCents(tippedOrder.TipAmount.AmountInCents - tipCents);
                    await db.SaveChangesAsync();
                }
                return Results.BadRequest(new { Message = Texts.T("errors.payment_failed") });
            }

            var hasCash = (req.Tenders ?? []).Any(t => t.Method == PaymentMethod.Cash);
            var printQueued = req.RequestReceiptPrint && result.RemainingBalanceCents == 0
                && await printing.QueueTableReceiptAsync(orderId, terminalId, result.ReceiptNumber, hasCash);

            return Results.Ok(new
            {
                result.ReceiptNumber,
                TotalPaid = result.TotalPaidCents / 100.0m,
                ChangeGiven = result.ChangeGivenCents / 100.0m,
                RemainingBalance = result.RemainingBalanceCents / 100.0m,
                result.FiscalSignature,
                FiscalTimestampUtc = DateTimeOffset.UtcNow,
                PrintQueued = printQueued
            });
        }).RequirePairedDevice();

        // Void receipt endpoint
        group.MapPost("/void/{receiptId:guid}", async (Guid receiptId, VoidReceiptRequest req, ICheckoutPaymentService checkout, HttpContext http) =>
        {
            if (req.OperatorId == Guid.Empty)
            {
                return Results.BadRequest(new { Message = Texts.T("errors.operator_required_for_void") });
            }

            var result = await checkout.VoidReceiptAsync(receiptId, RequireDeviceFilter.PairedDevice(http).TerminalId, req.OperatorId);
            if (!result.IsSuccess)
            {
                return Results.BadRequest(new { Message = Texts.T("errors.void_failed") });
            }

            return Results.Ok(new
            {
                result.ReceiptNumber,
                RefundedAmount = result.TotalPaidCents / 100.0m,
                result.FiscalSignature,
                FiscalTimestampUtc = DateTimeOffset.UtcNow
            });
        }).RequireAuthorization("RequireManagerOrAdmin").RequirePairedDevice();
    }

    private static string? NormalizeAltTableNumber(string tableNumber)
    {
        if (tableNumber.StartsWith("T0", StringComparison.OrdinalIgnoreCase) && tableNumber.Length == 3)
        {
            return "T" + tableNumber[2];
        }
        if (tableNumber.StartsWith("T", StringComparison.OrdinalIgnoreCase) && tableNumber.Length == 2 && char.IsDigit(tableNumber[1]))
        {
            return "T0" + tableNumber[1];
        }
        return null;
    }
}
