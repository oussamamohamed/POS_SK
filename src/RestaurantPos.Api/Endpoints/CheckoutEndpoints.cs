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
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Endpoints;

public static class CheckoutEndpoints
{
    public static void MapCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/checkout")
                       .WithTags("Checkout & Payments")
                       .RequireAuthorization();

        group.MapPost("/pay", async (PaymentSettlementRequest req, ICheckoutPaymentService checkout, AppDbContext db) =>
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
                    return Results.BadRequest(new { Message = "Commande introuvable pour ce règlement." });
                }
            }

            var tenderRequests = (req.Tenders ?? []).Select(t => new PaymentTenderRequest(
                t.Method,
                (long)Math.Round(t.Amount * 100),
                (long)Math.Round(t.Tendered * 100)
            )).ToList();

            var terminalId = !string.IsNullOrWhiteSpace(req.TerminalId)
                ? req.TerminalId
                : "POS_MAIN_TERM";

            var result = await checkout.ProcessPaymentTendersAsync(orderId, terminalId, tenderRequests);

            if (!result.IsSuccess)
            {
                return Results.BadRequest(new { Message = "Échec de l'encaissement." });
            }

            return Results.Ok(new
            {
                result.ReceiptNumber,
                TotalPaid = result.TotalPaidCents / 100.0m,
                ChangeGiven = result.ChangeGivenCents / 100.0m,
                RemainingBalance = result.RemainingBalanceCents / 100.0m,
                result.FiscalSignature,
                FiscalTimestampUtc = DateTimeOffset.UtcNow
            });
        });

        // Void receipt endpoint
        group.MapPost("/void/{receiptId:guid}", async (Guid receiptId, VoidReceiptRequest req, ICheckoutPaymentService checkout) =>
        {
            if (string.IsNullOrWhiteSpace(req.TerminalId) || req.OperatorId == Guid.Empty)
            {
                return Results.BadRequest(new { Message = "TerminalId et OperatorId sont obligatoires pour l'annulation." });
            }

            var result = await checkout.VoidReceiptAsync(receiptId, req.TerminalId, req.OperatorId);
            if (!result.IsSuccess)
            {
                return Results.BadRequest(new { Message = "Impossible d'annuler le reçu." });
            }

            return Results.Ok(new
            {
                result.ReceiptNumber,
                RefundedAmount = result.TotalPaidCents / 100.0m,
                result.FiscalSignature,
                FiscalTimestampUtc = DateTimeOffset.UtcNow
            });
        }).RequireAuthorization("RequireManagerOrAdmin");
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
