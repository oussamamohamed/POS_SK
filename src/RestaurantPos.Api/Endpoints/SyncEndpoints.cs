using System;
using System.Collections.Generic;
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

namespace RestaurantPos.Api.Endpoints;

public record SyncReceiptDto(
    Guid OrderId,
    string? TableNumber,
    long TotalTtcCents,
    List<SyncTenderDto> Tenders,
    string? TerminalId
);

public record SyncTenderDto(
    PaymentMethod Method,
    long AmountCents,
    long TenderedCents
);

public static class SyncEndpoints
{
    public static void MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Sync & Network");

        group.MapGet("/network/info", () =>
        {
            var hostName = System.Net.Dns.GetHostName();
            var ipAddresses = System.Net.Dns.GetHostAddresses(hostName)
                .Where(ip => ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                .Select(ip => ip.ToString())
                .ToList();

            return Results.Ok(new
            {
                HostName = hostName,
                IpAddresses = ipAddresses,
                PrimaryIp = ipAddresses.FirstOrDefault() ?? "127.0.0.1",
                Port = 5000,
                ServerName = "Caisse Principale (Master POS)",
                Status = "Online",
                Version = "1.0.0",
                TimestampUtc = DateTimeOffset.UtcNow
            });
        });

        group.MapPost("/sync/batch", async (SyncBatchRequest req, AppDbContext db) =>
        {
            int processedCount = 0;
            foreach (var msg in req.Messages)
            {
                var existing = await db.OutboxMessages.FindAsync(msg.Id);
                if (existing is null)
                {
                    db.OutboxMessages.Add(new OutboxSyncMessage
                    {
                        Id = msg.Id,
                        TerminalId = msg.TerminalId ?? "OFFLINE_TERM",
                        EventType = msg.EventType ?? "OrderCreated",
                        IdempotencyKey = msg.IdempotencyKey ?? msg.Id.ToString(),
                        PayloadJson = msg.PayloadJson,
                        Status = SyncStatus.Completed,
                        CreatedAtUtc = msg.CreatedAtUtc,
                        LastAttemptUtc = DateTimeOffset.UtcNow
                    });
                    processedCount++;
                }
            }
            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                Success = true,
                ProcessedMessagesCount = processedCount,
                ServerTimestampUtc = DateTimeOffset.UtcNow
            });
        }).RequireAuthorization();

        group.MapPost("/sync/receipt", async (SyncReceiptDto req, ICheckoutPaymentService checkout, AppDbContext db, HttpContext http) =>
        {
            var orderId = req.OrderId != Guid.Empty ? req.OrderId : Guid.NewGuid();
            var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId);
            long totalCents = req.TotalTtcCents > 0 ? req.TotalTtcCents : (req.Tenders?.Sum(t => t.AmountCents) ?? 0);

            if (order is null)
            {
                // Commande créée à la volée : le total ne peut être ni nul ni négatif ; règlements > 0 et sans dépasser le total.
                if (totalCents <= 0 || (req.Tenders ?? []).Any(t => t.AmountCents <= 0) || (req.Tenders ?? []).Sum(t => t.AmountCents) > totalCents)
                {
                    return Results.BadRequest(new { Message = Texts.T("errors.sync_payment_failed") });
                }

                order = new Order
                {
                    Id = orderId,
                    TableNumber = !string.IsNullOrWhiteSpace(req.TableNumber) ? req.TableNumber : "Comptoir",
                    Status = OrderStatus.Open,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };
                order.Items.Add(new OrderItem
                {
                    OrderId = order.Id,
                    ProductId = Guid.NewGuid(),
                    ProductName = $"Vente {order.TableNumber}",
                    UnitPrice = Money.FromCents(totalCents),
                    Quantity = 1,
                    TaxRatePercent = 10.0m
                });
                db.Orders.Add(order);
                await db.SaveChangesAsync();
            }

            var tenderRequests = (req.Tenders ?? []).Select(t => new PaymentTenderRequest(
                t.Method,
                t.AmountCents,
                t.TenderedCents > 0 ? t.TenderedCents : t.AmountCents
            )).ToList();

            var terminalId = RequireDeviceFilter.PairedDevice(http).TerminalId;
            var result = await checkout.ProcessPaymentTendersAsync(orderId, terminalId, tenderRequests);

            if (!result.IsSuccess)
            {
                return Results.BadRequest(new { Message = Texts.T("errors.sync_payment_failed") });
            }

            if (!string.IsNullOrWhiteSpace(req.TableNumber))
            {
                var table = await db.DiningTables.FirstOrDefaultAsync(t => t.TableNumber == req.TableNumber);
                if (table != null)
                {
                    table.Status = TableStatus.Free;
                    table.ActiveOrderId = null;
                    await db.SaveChangesAsync();
                }
            }

            return Results.Ok(new
            {
                result.ReceiptNumber,
                TotalPaid = result.TotalPaidCents / 100.0m,
                ChangeGiven = result.ChangeGivenCents / 100.0m,
                result.FiscalSignature,
                FiscalTimestampUtc = DateTimeOffset.UtcNow
            });
        }).RequireAuthorization().RequirePairedDevice();

        group.MapGet("/sync/status", async (AppDbContext db) =>
        {
            var total = await db.OutboxMessages.CountAsync();
            var completed = await db.OutboxMessages.CountAsync(m => m.Status == SyncStatus.Completed);
            var pending = await db.OutboxMessages.CountAsync(m => m.Status == SyncStatus.Pending);

            return Results.Ok(new
            {
                TotalMessages = total,
                CompletedMessages = completed,
                PendingMessages = pending,
                Status = "Synchronized",
                LastSyncUtc = DateTimeOffset.UtcNow
            });
        }).RequireAuthorization();

        group.MapGet("/sync/fiscal/x-report", async (string? terminalId, INF525FiscalAuditService fiscal) =>
        {
            var term = string.IsNullOrWhiteSpace(terminalId) ? "POS_MAIN_TERM" : terminalId;
            var summary = await fiscal.GenerateXReportAsync(term);
            return Results.Ok(new
            {
                summary.TerminalId,
                TotalSalesTtc = summary.TotalSalesTtcCents / 100.0m,
                TotalSalesHt = summary.TotalSalesHtCents / 100.0m,
                summary.ReceiptCount,
                VatBreakdown = summary.VatBreakdownCents.ToDictionary(k => k.Key.ToString(System.Globalization.CultureInfo.InvariantCulture), v => v.Value / 100.0m),
                PaymentTotals = summary.PaymentTotalsCents.ToDictionary(k => k.Key.ToString(), v => v.Value / 100.0m),
                PerpetualGrandTotal = summary.PerpetualGrandTotalCents / 100.0m,
                summary.PeriodStartUtc,
                summary.PeriodEndUtc
            });
        }).RequireAuthorization("RequireManagerOrAdmin");

        group.MapPost("/sync/fiscal/z-closure", async (
            ZClosureRequest req,
            INF525FiscalAuditService fiscal,
            System.Security.Claims.ClaimsPrincipal user,
            HttpContext http) =>
        {
            var terminalId = RequireDeviceFilter.PairedDevice(http).TerminalId;
            var sub = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;
            var name = user.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
                ?? user.FindFirst("name")?.Value
                ?? "Manager";

            if (!Guid.TryParse(sub, out var managerId))
            {
                return Results.Unauthorized();
            }

            var open = await fiscal.FindOpenOrdersAsync();
            if (open.Count > 0)
            {
                var list = string.Join(", ", open.Select(o => $"{o.Label} ({(o.RemainingTtcCents / 100m).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)})"));
                return Results.Json(new
                {
                    code = "open_orders",
                    message = Texts.T("errors.z_closure_open_orders", ("count", open.Count), ("orders", list)),
                    openOrders = open.Select(o => new { tableNumber = o.Label, remainingTtc = o.RemainingTtcCents / 100m })
                }, statusCode: StatusCodes.Status409Conflict);
            }

            var closure = await fiscal.ExecuteDailyZClosureAsync(terminalId, managerId, name);
            return Results.Ok(new
            {
                closure.ClosureSequence,
                TotalSalesTtc = closure.TotalSalesTtcCents / 100.0m,
                TotalSalesHt = closure.TotalSalesHtCents / 100.0m,
                closure.ReceiptCount,
                PerpetualGrandTotal = closure.PerpetualGrandTotalCents / 100.0m,
                closure.SignatureHash,
                closure.ClosedAtUtc
            });
        }).RequireAuthorization("RequireManagerOrAdmin").RequirePairedDevice();
    }
}
