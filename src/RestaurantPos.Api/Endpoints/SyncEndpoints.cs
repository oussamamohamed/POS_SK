using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Api.Services;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
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
                DiscoveryPort = NetworkDiscoveryBeaconService.DiscoveryPort,
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
        });

        group.MapPost("/sync/receipt", async (SyncReceiptDto req, ICheckoutPaymentService checkout, AppDbContext db) =>
        {
            var orderId = req.OrderId != Guid.Empty ? req.OrderId : Guid.NewGuid();
            var order = await db.Orders.Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == orderId);
            long totalCents = req.TotalTtcCents > 0 ? req.TotalTtcCents : (req.Tenders?.Sum(t => t.AmountCents) ?? 0);

            if (order is null)
            {
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

            var terminalId = !string.IsNullOrWhiteSpace(req.TerminalId) ? req.TerminalId : "POS01";
            var result = await checkout.ProcessPaymentTendersAsync(orderId, terminalId, tenderRequests);

            if (!result.IsSuccess)
            {
                return Results.BadRequest(new { Message = "Échec de l'encaissement synchronisé." });
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
        });

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
        });

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
        });

        group.MapPost("/sync/fiscal/z-closure", async (ZClosureRequest req, INF525FiscalAuditService fiscal) =>
        {
            var terminalId = string.IsNullOrWhiteSpace(req.TerminalId) ? "POS_MAIN_TERM" : req.TerminalId;
            var managerId = req.ManagerId == Guid.Empty ? Guid.NewGuid() : req.ManagerId;
            var managerName = string.IsNullOrWhiteSpace(req.ManagerName) ? "Responsable Caisse (iPad)" : req.ManagerName;

            var closure = await fiscal.ExecuteDailyZClosureAsync(terminalId, managerId, managerName);
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
        });
    }
}
