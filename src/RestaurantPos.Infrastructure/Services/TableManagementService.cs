using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.ValueObjects;
using RestaurantPos.Infrastructure.Persistence;

namespace RestaurantPos.Infrastructure.Services;

public class TableManagementService : ITableManagementService
{
    private readonly AppDbContext _dbContext;

    public TableManagementService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<DiningTableDto>> GetFloorPlanTablesAsync(CancellationToken cancellationToken = default)
    {
        var tables = await _dbContext.DiningTables
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return tables.Select(t => new DiningTableDto(
            t.TableNumber,
            t.Capacity,
            t.Status,
            t.PositionX,
            t.PositionY,
            t.AssignedWaiterName,
            t.CoversCount,
            t.ActiveOrderId,
            t.OpenedAtUtc
        )).ToList();
    }

    public async Task<DiningTableDto> CreateTableAsync(
        string tableNumber,
        int capacity,
        double positionX = 0,
        double positionY = 0,
        CancellationToken cancellationToken = default)
    {
        var normalizedNumber = tableNumber.Trim().ToUpperInvariant();
        var existing = await _dbContext.DiningTables.FindAsync([normalizedNumber], cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new DiningTableDto(
                existing.TableNumber,
                existing.Capacity,
                existing.Status,
                existing.PositionX,
                existing.PositionY,
                existing.AssignedWaiterName,
                existing.CoversCount,
                existing.ActiveOrderId,
                existing.OpenedAtUtc
            );
        }

        return await _dbContext.ExecuteInTransactionAsync(
            async ct =>
            {
                var table = new DiningTable
                {
                    TableNumber = normalizedNumber,
                    Capacity = capacity <= 0 ? 2 : capacity,
                    Status = TableStatus.Free,
                    PositionX = positionX,
                    PositionY = positionY,
                    CoversCount = 0,
                    AssignedWaiterName = null,
                    ActiveOrderId = null,
                    OpenedAtUtc = null
                };

                _dbContext.DiningTables.Add(table);
                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

                return new DiningTableDto(
                    table.TableNumber,
                    table.Capacity,
                    table.Status,
                    table.PositionX,
                    table.PositionY,
                    table.AssignedWaiterName,
                    table.CoversCount,
                    table.ActiveOrderId,
                    table.OpenedAtUtc
                );
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<DiningTableDto> OpenTableAsync(
        string tableNumber,
        int coversCount,
        Guid operatorId,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExecuteInTransactionAsync(
            async ct =>
            {
                var table = await _dbContext.DiningTables.FindAsync([tableNumber], ct).ConfigureAwait(false);
                if (table is null)
                {
                    table = new DiningTable
                    {
                        TableNumber = tableNumber,
                        Capacity = coversCount
                    };
                    _dbContext.DiningTables.Add(table);
                }

                var order = new Order
                {
                    Id = UuidV7.NewGuid(),
                    TableNumber = tableNumber,
                    OperatorId = operatorId,
                    Status = OrderStatus.Open,
                    CreatedAtUtc = DateTimeOffset.UtcNow
                };

                _dbContext.Orders.Add(order);

                table.Status = TableStatus.Occupied;
                table.CoversCount = coversCount;
                table.AssignedWaiterId = operatorId;
                table.AssignedWaiterName = operatorName;
                table.ActiveOrderId = order.Id;
                table.OpenedAtUtc = DateTimeOffset.UtcNow;
                table.UpdatedAtUtc = DateTimeOffset.UtcNow;

                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

                return new DiningTableDto(
                    table.TableNumber,
                    table.Capacity,
                    table.Status,
                    table.PositionX,
                    table.PositionY,
                    table.AssignedWaiterName,
                    table.CoversCount,
                    table.ActiveOrderId,
                    table.OpenedAtUtc
                );
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<ActiveTableOrderDto?> GetActiveOrderForTableAsync(
        string tableNumber,
        CancellationToken cancellationToken = default)
    {
        var table = await _dbContext.DiningTables
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TableNumber == tableNumber, cancellationToken)
            .ConfigureAwait(false);

        if (table is null || table.ActiveOrderId is null)
        {
            return null;
        }

        var order = await _dbContext.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == table.ActiveOrderId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (order is null)
        {
            return null;
        }

        var linesDto = order.Items.Select(item => new ActiveOrderLineDto(
            item.Id,
            item.ProductId,
            item.ProductName,
            item.Quantity,
            item.UnitPrice.ToDecimal(),
            item.CalculateTotalTtc().ToDecimal(),
            item.TaxRatePercent,
            item.PreparationStationId,
            item.IsDispatched,
            item.SelectedModifiers,
            item.Course,
            item.IsComp,
            item.DiscountPercent
        )).ToList();

        decimal totalTtc = order.TotalTtc.ToDecimal();
        decimal totalHt = order.TotalHt.ToDecimal();
        decimal totalVat = totalTtc - totalHt;

        return new ActiveTableOrderDto(
            order.Id,
            table.TableNumber,
            table.AssignedWaiterName,
            table.CoversCount,
            table.OpenedAtUtc ?? order.CreatedAtUtc,
            linesDto,
            totalHt,
            totalVat,
            totalTtc,
            order.GlobalDiscountType,
            order.GlobalDiscountValue,
            order.GlobalDiscountReason
        );
    }

    public async Task<ActiveTableOrderDto> AddOrUpdateTableOrderItemsAsync(
        string tableNumber,
        IReadOnlyList<OrderItemInputDto> items,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExecuteInTransactionAsync(
            async ct =>
            {
                var table = await _dbContext.DiningTables
                    .FirstOrDefaultAsync(t => t.TableNumber == tableNumber, ct)
                    .ConfigureAwait(false);

                if (table is null)
                {
                    table = new DiningTable
                    {
                        TableNumber = tableNumber,
                        Capacity = 4,
                        Status = TableStatus.Occupied,
                        CoversCount = 2,
                        OpenedAtUtc = DateTimeOffset.UtcNow
                    };
                    _dbContext.DiningTables.Add(table);
                }

                Order? order = null;
                if (table.ActiveOrderId is not null)
                {
                    order = await _dbContext.Orders
                        .Include(o => o.Items)
                        .FirstOrDefaultAsync(o => o.Id == table.ActiveOrderId.Value, ct)
                        .ConfigureAwait(false);
                }

                if (order is null)
                {
                    order = new Order
                    {
                        Id = UuidV7.NewGuid(),
                        TableNumber = tableNumber,
                        Status = OrderStatus.Open,
                        CreatedAtUtc = DateTimeOffset.UtcNow
                    };
                    _dbContext.Orders.Add(order);
                    table.ActiveOrderId = order.Id;
                    table.Status = TableStatus.Occupied;
                    table.OpenedAtUtc ??= DateTimeOffset.UtcNow;
                }

                foreach (var input in items)
                {
                    var existing = order.Items.FirstOrDefault(i => i.ProductId == input.ProductId && !i.IsDispatched && i.Course == input.Course);
                    if (existing is not null)
                    {
                        existing.Quantity += input.Quantity;
                    }
                    else
                    {
                        var newItem = new OrderItem
                        {
                            Id = UuidV7.NewGuid(),
                            OrderId = order.Id,
                            ProductId = input.ProductId,
                            ProductName = input.ProductName,
                            Quantity = input.Quantity,
                            UnitPrice = Money.FromDecimal(input.UnitPrice, "EUR"),
                            TaxRatePercent = input.TaxRatePercent,
                            PreparationStationId = input.PreparationStationId,
                            SelectedModifiers = input.Modifiers?.ToList() ?? [],
                            Course = input.Course
                        };
                        _dbContext.OrderItems.Add(newItem);
                        order.Items.Add(newItem);
                    }
                }

                table.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

                return (await GetActiveOrderForTableAsync(tableNumber, ct).ConfigureAwait(false))!;
            }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> DispatchOrderLinesAsync(
        string tableNumber,
        CancellationToken cancellationToken = default)
    {
        var table = await _dbContext.DiningTables
            .FirstOrDefaultAsync(t => t.TableNumber == tableNumber, cancellationToken)
            .ConfigureAwait(false);

        if (table?.ActiveOrderId is null) return false;

        var order = await _dbContext.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == table.ActiveOrderId.Value, cancellationToken)
            .ConfigureAwait(false);

        if (order is null) return false;

        bool hasUndispatched = false;
        foreach (var item in order.Items.Where(i => !i.IsDispatched))
        {
            item.IsDispatched = true;
            hasUndispatched = true;
        }

        if (hasUndispatched)
        {
            order.Status = OrderStatus.SentToKitchen;
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    public async Task<bool> TransferTableAsync(
        string sourceTableNumber,
        string targetTableNumber,
        CancellationToken cancellationToken = default)
    {
        // RepeatableRead: prevents another request from modifying either table
        // between our initial reads and the final SaveChanges.
        return await _dbContext.ExecuteInTransactionAsync(
            async ct =>
            {
                var source = await _dbContext.DiningTables.FindAsync([sourceTableNumber], ct).ConfigureAwait(false);
                var target = await _dbContext.DiningTables.FindAsync([targetTableNumber], ct).ConfigureAwait(false);

                if (source is null || target is null || source.ActiveOrderId is null)
                {
                    return false;
                }

                var order = await _dbContext.Orders.FindAsync([source.ActiveOrderId.Value], ct).ConfigureAwait(false);
                if (order is not null)
                {
                    order.TableNumber = targetTableNumber;
                }

                target.ActiveOrderId = source.ActiveOrderId;
                target.Status = TableStatus.Occupied;
                target.CoversCount = source.CoversCount;
                target.AssignedWaiterId = source.AssignedWaiterId;
                target.AssignedWaiterName = source.AssignedWaiterName;
                target.OpenedAtUtc = source.OpenedAtUtc;

                var transferLog = new TableTransferLog
                {
                    SourceTableNumber = sourceTableNumber,
                    TargetTableNumber = targetTableNumber,
                    OrderId = source.ActiveOrderId.Value,
                    OperatorName = source.AssignedWaiterName ?? "Serveur",
                    IsMerge = false,
                    TimestampUtc = DateTimeOffset.UtcNow
                };
                _dbContext.TableTransferLogs.Add(transferLog);

                source.ActiveOrderId = null;
                source.Status = TableStatus.Free;
                source.CoversCount = 0;
                source.AssignedWaiterId = null;
                source.AssignedWaiterName = null;
                source.OpenedAtUtc = null;

                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                return true;
            },
            System.Data.IsolationLevel.RepeatableRead,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> MergeTablesAsync(
        string sourceTableNumber,
        string targetTableNumber,
        CancellationToken cancellationToken = default)
    {
        // RepeatableRead: both table + order reads must be stable throughout the merge.
        return await _dbContext.ExecuteInTransactionAsync(
            async ct =>
            {
                var source = await _dbContext.DiningTables.FindAsync([sourceTableNumber], ct).ConfigureAwait(false);
                var target = await _dbContext.DiningTables.FindAsync([targetTableNumber], ct).ConfigureAwait(false);

                if (source is null || target is null || source.ActiveOrderId is null)
                {
                    return false;
                }

                if (target.ActiveOrderId is null)
                {
                    // Target is free: merge is equivalent to transfer (already transactional)
                    return await TransferTableAsync(sourceTableNumber, targetTableNumber, ct).ConfigureAwait(false);
                }

                var sourceOrder = await _dbContext.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == source.ActiveOrderId.Value, ct)
                    .ConfigureAwait(false);

                var targetOrder = await _dbContext.Orders
                    .Include(o => o.Items)
                    .FirstOrDefaultAsync(o => o.Id == target.ActiveOrderId.Value, ct)
                    .ConfigureAwait(false);

                if (sourceOrder is null || targetOrder is null)
                {
                    return false;
                }

                foreach (var item in sourceOrder.Items)
                {
                    item.OrderId = targetOrder.Id;
                    targetOrder.Items.Add(item);
                }
                sourceOrder.Items.Clear();
                sourceOrder.Status = OrderStatus.Cancelled;

                target.CoversCount += source.CoversCount;

                var transferLog = new TableTransferLog
                {
                    SourceTableNumber = sourceTableNumber,
                    TargetTableNumber = targetTableNumber,
                    OrderId = targetOrder.Id,
                    OperatorName = target.AssignedWaiterName ?? source.AssignedWaiterName ?? "Serveur",
                    IsMerge = true,
                    TimestampUtc = DateTimeOffset.UtcNow
                };
                _dbContext.TableTransferLogs.Add(transferLog);

                source.ActiveOrderId = null;
                source.Status = TableStatus.Free;
                source.CoversCount = 0;
                source.AssignedWaiterId = null;
                source.AssignedWaiterName = null;
                source.OpenedAtUtc = null;

                await _dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
                return true;
            },
            System.Data.IsolationLevel.RepeatableRead,
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> UpdateTableStatusAsync(
        string tableNumber,
        TableStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        var table = await _dbContext.DiningTables.FindAsync([tableNumber], cancellationToken).ConfigureAwait(false);
        if (table is null) return false;

        table.Status = newStatus;
        table.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (newStatus == TableStatus.Free)
        {
            table.ActiveOrderId = null;
            table.CoversCount = 0;
            table.AssignedWaiterId = null;
            table.AssignedWaiterName = null;
            table.OpenedAtUtc = null;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
