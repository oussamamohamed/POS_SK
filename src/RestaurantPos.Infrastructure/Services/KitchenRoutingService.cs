using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class KitchenRoutingService : IKitchenRoutingService
{
    private readonly AppDbContext _dbContext;

    public KitchenRoutingService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<KitchenTicketDto>> SplitAndRouteOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, cancellationToken)
            .ConfigureAwait(false);

        if (order is null || order.Items.Count == 0)
        {
            return [];
        }

        // B5/B9 FIX: Resolve actual server name and covers count from the dining table record.
        var diningTable = await _dbContext.DiningTables
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TableNumber == order.TableNumber, cancellationToken)
            .ConfigureAwait(false);

        string serverName = diningTable?.AssignedWaiterName ?? "Serveur";
        int coversCount = diningTable?.CoversCount ?? 1;

        var products = await _dbContext.Products
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var productMap = products.ToDictionary(p => p.Id, p => p.CategoryId);

        // Only route items that have NOT been dispatched yet (avoid duplicating existing tickets)
        var pendingItems = order.Items.Where(i => !i.IsDispatched).ToList();
        if (pendingItems.Count == 0)
        {
            return [];
        }

        // Group items by station: CAT-DRINKS -> STATION-BAR, CAT-DESSERTS -> STATION-PASTRY, other -> STATION-HOT
        var stationGroups = pendingItems.GroupBy(item =>
        {
            // Use PreparationStationId on the item itself if it is already set
            if (!string.IsNullOrWhiteSpace(item.PreparationStationId))
            {
                return item.PreparationStationId;
            }

            if (productMap.TryGetValue(item.ProductId, out string? catId))
            {
                if (catId.Contains("DRINK", StringComparison.OrdinalIgnoreCase)) return "STATION-BAR";
                if (catId.Contains("DESSERT", StringComparison.OrdinalIgnoreCase)) return "STATION-PASTRY";
            }
            return "STATION-HOT";
        });

        var createdTickets = new List<KitchenTicketDto>();

        foreach (var group in stationGroups)
        {
            var ticket = new KitchenTicket
            {
                Id = UuidV7.NewGuid(),
                OrderId = order.Id,
                TableNumber = order.TableNumber,
                // B9 FIX: Use real values from the DiningTable, not hardcoded constants
                ServerName = serverName,
                CoversCount = coversCount,
                StationId = group.Key,
                Status = TicketStatus.Pending,
                DispatchedAtUtc = DateTimeOffset.UtcNow
            };

            foreach (var item in group)
            {
                ticket.Items.Add(new KitchenTicketItem
                {
                    Id = UuidV7.NewGuid(),
                    TicketId = ticket.Id,
                    ProductId = item.ProductId,
                    ProductName = item.ProductName,
                    Quantity = item.Quantity,
                    ModifiersSummary = string.Join(", ", item.SelectedModifiers),
                    KitchenComment = item.KitchenComment,
                    Status = TicketItemStatus.Pending
                });
            }

            _dbContext.KitchenTickets.Add(ticket);

            createdTickets.Add(new KitchenTicketDto(
                ticket.Id,
                ticket.OrderId,
                ticket.TableNumber,
                ticket.ServerName,
                ticket.CoversCount,
                ticket.StationId,
                ticket.Status,
                ticket.DispatchedAtUtc,
                ticket.Items.Select(i => new KitchenTicketItemDto(
                    i.Id,
                    i.ProductId,
                    i.ProductName,
                    i.Quantity,
                    i.ModifiersSummary,
                    i.KitchenComment,
                    i.Status
                )).ToList()
            ));
        }

        order.Status = OrderStatus.SentToKitchen;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return createdTickets;
    }

    public async Task<KitchenTicketDto?> BumpTicketStateAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await _dbContext.KitchenTickets
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null) return null;

        ticket.Status = ticket.Status switch
        {
            TicketStatus.Pending => TicketStatus.InPreparation,
            TicketStatus.InPreparation => TicketStatus.Ready,
            TicketStatus.Ready => TicketStatus.Served,
            _ => ticket.Status
        };

        if (ticket.Status == TicketStatus.InPreparation && ticket.PreparedAtUtc is null)
        {
            ticket.PreparedAtUtc = DateTimeOffset.UtcNow;
        }
        else if (ticket.Status == TicketStatus.Served)
        {
            ticket.CompletedAtUtc = DateTimeOffset.UtcNow;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new KitchenTicketDto(
            ticket.Id,
            ticket.OrderId,
            ticket.TableNumber,
            ticket.ServerName,
            ticket.CoversCount,
            ticket.StationId,
            ticket.Status,
            ticket.DispatchedAtUtc,
            ticket.Items.Select(i => new KitchenTicketItemDto(
                i.Id,
                i.ProductId,
                i.ProductName,
                i.Quantity,
                i.ModifiersSummary,
                i.KitchenComment,
                i.Status
            )).ToList()
        );
    }

    public async Task<KitchenTicketDto?> RecallTicketAsync(Guid ticketId, CancellationToken cancellationToken = default)
    {
        var ticket = await _dbContext.KitchenTickets
            .Include(t => t.Items)
            .FirstOrDefaultAsync(t => t.Id == ticketId, cancellationToken)
            .ConfigureAwait(false);

        if (ticket is null) return null;

        ticket.Status = ticket.Status switch
        {
            TicketStatus.Served => TicketStatus.Ready,
            TicketStatus.Ready => TicketStatus.InPreparation,
            TicketStatus.InPreparation => TicketStatus.Pending,
            _ => ticket.Status
        };

        ticket.CompletedAtUtc = null;
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new KitchenTicketDto(
            ticket.Id,
            ticket.OrderId,
            ticket.TableNumber,
            ticket.ServerName,
            ticket.CoversCount,
            ticket.StationId,
            ticket.Status,
            ticket.DispatchedAtUtc,
            ticket.Items.Select(i => new KitchenTicketItemDto(
                i.Id,
                i.ProductId,
                i.ProductName,
                i.Quantity,
                i.ModifiersSummary,
                i.KitchenComment,
                i.Status
            )).ToList()
        );
    }
}
