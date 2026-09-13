using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class HeldOrderStorageService : IHeldOrderStorageService
{
    private readonly AppDbContext _context;

    public HeldOrderStorageService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<HeldOrder> HoldOrderAsync(Order order, string terminalId, Guid staffId, string? customerLabel, CancellationToken cancellationToken = default)
    {
        string snapshotJson = JsonSerializer.Serialize(order);

        var heldOrder = new HeldOrder
        {
            TerminalId = terminalId,
            OrderId = order.Id,
            CustomerLabel = customerLabel,
            Destination = order.Destination,
            ItemCount = order.Items.Sum(i => i.Quantity),
            TotalTtc = order.TotalTtc,
            OrderSnapshotJson = snapshotJson,
            HeldAtUtc = DateTimeOffset.UtcNow,
            HeldByStaffId = staffId,
            IsRecalled = false,
            IsVoided = false
        };

        _context.HeldOrders.Add(heldOrder);
        await _context.SaveChangesAsync(cancellationToken);
        return heldOrder;
    }

    public async Task<IReadOnlyList<HeldOrderDto>> GetActiveHeldOrdersAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.HeldOrders
            .AsNoTracking()
            .Where(h => (string.IsNullOrEmpty(terminalId) || h.TerminalId == terminalId) && !h.IsRecalled && !h.IsVoided)
            .ToListAsync(cancellationToken);

        return entities
            .OrderByDescending(h => h.HeldAtUtc)
            .Select(h => new HeldOrderDto(
                h.Id,
                h.TerminalId,
                h.OrderId,
                h.CustomerLabel,
                h.Destination,
                h.ItemCount,
                h.TotalTtc,
                h.HeldAtUtc,
                h.HeldByStaffId
            ))
            .ToList();
    }

    public async Task<Order?> RecallOrderAsync(Guid holdId, CancellationToken cancellationToken = default)
    {
        var heldOrder = await _context.HeldOrders.FirstOrDefaultAsync(h => h.Id == holdId && !h.IsRecalled && !h.IsVoided, cancellationToken);
        if (heldOrder == null)
        {
            return null;
        }

        heldOrder.IsRecalled = true;
        heldOrder.RecalledAtUtc = DateTimeOffset.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        var order = JsonSerializer.Deserialize<Order>(heldOrder.OrderSnapshotJson);
        return order;
    }

    public async Task<bool> VoidHeldOrderAsync(Guid holdId, Guid supervisorStaffId, string reason, CancellationToken cancellationToken = default)
    {
        var heldOrder = await _context.HeldOrders.FirstOrDefaultAsync(h => h.Id == holdId && !h.IsRecalled && !h.IsVoided, cancellationToken);
        if (heldOrder == null)
        {
            return false;
        }

        heldOrder.IsVoided = true;
        heldOrder.VoidedAtUtc = DateTimeOffset.UtcNow;
        heldOrder.VoidedByStaffId = supervisorStaffId;
        heldOrder.VoidReason = reason;

        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<int> GetHeldOrderCountAsync(string terminalId, CancellationToken cancellationToken = default)
    {
        return await _context.HeldOrders
            .CountAsync(h => (string.IsNullOrEmpty(terminalId) || h.TerminalId == terminalId) && !h.IsRecalled && !h.IsVoided, cancellationToken);
    }
}
