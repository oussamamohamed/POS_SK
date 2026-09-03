using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;

namespace RestaurantPos.Api.Endpoints;

public static class CheckoutEndpoints
{
    public static void MapCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/checkout")
                       .WithTags("Checkout & Payments")
                       .RequireAuthorization();

        group.MapPost("/pay", async (PaymentSettlementRequest req, ICheckoutPaymentService checkout) =>
        {
            var tenderRequests = req.Tenders.Select(t => new PaymentTenderRequest(
                t.Method,
                (long)Math.Round(t.Amount * 100),
                (long)Math.Round(t.Tendered * 100)
            )).ToList();

            var result = await checkout.ProcessPaymentTendersAsync(req.OrderId, req.TableNumber, tenderRequests);

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
}
