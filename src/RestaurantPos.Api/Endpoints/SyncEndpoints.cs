using System;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Api.Services;
using RestaurantPos.Application.DTOs;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Api.Endpoints;

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
    }
}
