using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Application.Common.Interfaces;
using RestaurantPos.Domain.Common;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Client.Maui.ProfileSimulations.Fakes;

/// <summary>
/// Unified in-memory backend fake implementing <see cref="ITableManagementService"/>,
/// <see cref="IKitchenRoutingService"/>, and <see cref="ICheckoutPaymentService"/>.
/// Enables multi-actor profile simulation (Waiter -> Kitchen -> Manager -> Cashier)
/// sharing synchronized state without external dependencies or network hops.
/// </summary>
public class SharedFakeBackend : ITableManagementService, IKitchenRoutingService, ICheckoutPaymentService
{
    public FakeKitchenSignalRClient? SignalRClient { get; set; }

    public Dictionary<string, ActiveTableOrderDto> ActiveOrders { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, DiningTableDto> Tables { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<KitchenTicketDto> KitchenQueue { get; } = [];
    public List<CheckoutResult> ProcessedPayments { get; } = [];

    public SharedFakeBackend(FakeKitchenSignalRClient? signalRClient = null)
    {
        SignalRClient = signalRClient;
        SeedDefaultTables();
    }

    private void SeedDefaultTables()
    {
        for (int i = 1; i <= 6; i++)
        {
            string number = $"T{i:D2}";
            Tables[number] = new DiningTableDto(
                TableNumber: number,
                Capacity: 4,
                Status: TableStatus.Free,
                PositionX: i * 100,
                PositionY: 100,
                AssignedWaiterName: null,
                CoversCount: 0,
                ActiveOrderId: null,
                OpenedAtUtc: null
            );
        }
    }

    // ── ITableManagementService ──────────────────────────────────────────────

    public Task<IReadOnlyList<DiningTableDto>> GetFloorPlanTablesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<DiningTableDto> result = Tables.Values.ToList();
        return Task.FromResult(result);
    }

    public Task<DiningTableDto> CreateTableAsync(
        string tableNumber,
        int capacity,
        double positionX = 0,
        double positionY = 0,
        CancellationToken cancellationToken = default)
    {
        string normalized = tableNumber.Trim().ToUpperInvariant();
        if (Tables.TryGetValue(normalized, out var existing))
        {
            return Task.FromResult(existing);
        }

        var table = new DiningTableDto(
            TableNumber: normalized,
            Capacity: capacity <= 0 ? 2 : capacity,
            Status: TableStatus.Free,
            PositionX: positionX,
            PositionY: positionY,
            AssignedWaiterName: null,
            CoversCount: 0,
            ActiveOrderId: null,
            OpenedAtUtc: null
        );
        Tables[normalized] = table;
        return Task.FromResult(table);
    }

    public Task<DiningTableDto> OpenTableAsync(
        string tableNumber,
        int coversCount,
        Guid operatorId,
        string operatorName,
        CancellationToken cancellationToken = default)
    {
        string normalized = tableNumber.Trim().ToUpperInvariant();
        var orderId = UuidV7.NewGuid();
        var openedAt = DateTimeOffset.UtcNow;

        var activeOrder = new ActiveTableOrderDto(
            OrderId: orderId,
            TableNumber: normalized,
            WaiterName: operatorName,
            CoversCount: coversCount,
            OpenedAtUtc: openedAt,
            Lines: [],
            TotalHtAmount: 0m,
            TotalVatAmount: 0m,
            TotalTtcAmount: 0m
        );
        ActiveOrders[normalized] = activeOrder;

        int capacity = 4;
        double posX = 0;
        double posY = 0;
        if (Tables.TryGetValue(normalized, out var existing))
        {
            capacity = existing.Capacity;
            posX = existing.PositionX;
            posY = existing.PositionY;
        }

        var updatedTable = new DiningTableDto(
            TableNumber: normalized,
            Capacity: capacity,
            Status: TableStatus.Occupied,
            PositionX: posX,
            PositionY: posY,
            AssignedWaiterName: operatorName,
            CoversCount: coversCount,
            ActiveOrderId: orderId,
            OpenedAtUtc: openedAt
        );
        Tables[normalized] = updatedTable;

        return Task.FromResult(updatedTable);
    }

    public Task<ActiveTableOrderDto?> GetActiveOrderForTableAsync(
        string tableNumber,
        CancellationToken cancellationToken = default)
    {
        string normalized = tableNumber.Trim().ToUpperInvariant();
        ActiveOrders.TryGetValue(normalized, out var order);
        return Task.FromResult(order);
    }

    public Task<ActiveTableOrderDto> AddOrUpdateTableOrderItemsAsync(
        string tableNumber,
        IReadOnlyList<OrderItemInputDto> items,
        CancellationToken cancellationToken = default)
    {
        string normalized = tableNumber.Trim().ToUpperInvariant();
        if (!ActiveOrders.TryGetValue(normalized, out var activeOrder))
        {
            activeOrder = new ActiveTableOrderDto(
                OrderId: UuidV7.NewGuid(),
                TableNumber: normalized,
                WaiterName: "Serveur",
                CoversCount: 2,
                OpenedAtUtc: DateTimeOffset.UtcNow,
                Lines: [],
                TotalHtAmount: 0m,
                TotalVatAmount: 0m,
                TotalTtcAmount: 0m
            );
        }

        var lines = activeOrder.Lines.ToList();

        foreach (var input in items)
        {
            var existing = lines.FirstOrDefault(l =>
                !l.IsDispatched &&
                l.ProductId == input.ProductId &&
                l.Course == input.Course &&
                l.UnitPrice == input.UnitPrice &&
                l.ModifiersPriceExtra == input.ModifiersPriceExtra &&
                string.Join(",", l.ModifiersSummary) == string.Join(",", input.Modifiers ?? []));

            if (existing is not null)
            {
                int newQty = existing.Quantity + input.Quantity;
                decimal newTotalPrice = (existing.UnitPrice + existing.ModifiersPriceExtra) * newQty;
                int index = lines.IndexOf(existing);
                lines[index] = existing with { Quantity = newQty, TotalPrice = newTotalPrice };
            }
            else
            {
                var newLine = new ActiveOrderLineDto(
                    LineId: UuidV7.NewGuid(),
                    ProductId: input.ProductId,
                    ProductName: input.ProductName,
                    Quantity: input.Quantity,
                    UnitPrice: input.UnitPrice,
                    TotalPrice: (input.UnitPrice + input.ModifiersPriceExtra) * input.Quantity,
                    TaxRatePercent: input.TaxRatePercent,
                    PreparationStationId: input.PreparationStationId,
                    IsDispatched: false,
                    ModifiersSummary: input.Modifiers?.ToList() ?? [],
                    Course: input.Course,
                    IsComp: false,
                    DiscountPercent: 0m,
                    ModifiersPriceExtra: input.ModifiersPriceExtra
                );
                lines.Add(newLine);
            }
        }

        decimal totalTtc = lines.Where(l => !l.IsComp).Sum(l => l.TotalPrice * (1.0m - (l.DiscountPercent / 100.0m)));
        if (activeOrder.GlobalDiscountType == DiscountType.Percentage && activeOrder.GlobalDiscountValue > 0)
        {
            totalTtc = Math.Max(0, totalTtc * (1.0m - (activeOrder.GlobalDiscountValue / 100.0m)));
        }
        else if (activeOrder.GlobalDiscountType == DiscountType.FixedAmount && activeOrder.GlobalDiscountValue > 0)
        {
            totalTtc = Math.Max(0, totalTtc - activeOrder.GlobalDiscountValue);
        }

        decimal totalHt = Math.Round(totalTtc / 1.10m, 2);
        decimal totalVat = totalTtc - totalHt;

        var updatedOrder = activeOrder with
        {
            Lines = lines,
            TotalTtcAmount = totalTtc,
            TotalHtAmount = totalHt,
            TotalVatAmount = totalVat
        };

        ActiveOrders[normalized] = updatedOrder;

        if (Tables.TryGetValue(normalized, out var table))
        {
            Tables[normalized] = table with
            {
                Status = TableStatus.Occupied,
                ActiveOrderId = updatedOrder.OrderId
            };
        }

        return Task.FromResult(updatedOrder);
    }

    public Task<bool> DispatchOrderLinesAsync(
        string tableNumber,
        CancellationToken cancellationToken = default)
    {
        string normalized = tableNumber.Trim().ToUpperInvariant();
        if (!ActiveOrders.TryGetValue(normalized, out var order))
        {
            return Task.FromResult(false);
        }

        var undispatched = order.Lines.Where(l => !l.IsDispatched).ToList();
        if (undispatched.Count == 0)
        {
            return Task.FromResult(true);
        }

        var updatedLines = order.Lines.Select(l => l.IsDispatched ? l : l with { IsDispatched = true }).ToList();
        ActiveOrders[normalized] = order with { Lines = updatedLines };

        var ticketItems = undispatched.Select(l => new KitchenTicketItemDto(
            ItemId: l.LineId,
            ProductId: l.ProductId,
            ProductName: l.ProductName,
            Quantity: l.Quantity,
            ModifiersSummary: l.ModifiersSummary.Count > 0 ? string.Join(", ", l.ModifiersSummary) : null,
            KitchenComment: null,
            Status: TicketItemStatus.Pending
        )).ToList();

        var ticket = new KitchenTicketDto(
            TicketId: UuidV7.NewGuid(),
            OrderId: order.OrderId,
            TableNumber: normalized,
            ServerName: order.WaiterName ?? "Serveur",
            CoversCount: order.CoversCount,
            StationId: "STATION-ALL",
            Status: TicketStatus.Pending,
            DispatchedAtUtc: DateTimeOffset.UtcNow,
            Items: ticketItems
        );

        KitchenQueue.Add(ticket);
        SignalRClient?.RaiseNewTicket(ticket);

        return Task.FromResult(true);
    }

    public Task<bool> TransferTableAsync(
        string sourceTableNumber,
        string targetTableNumber,
        CancellationToken cancellationToken = default)
    {
        string src = sourceTableNumber.Trim().ToUpperInvariant();
        string tgt = targetTableNumber.Trim().ToUpperInvariant();

        if (!ActiveOrders.TryGetValue(src, out var sourceOrder))
            return Task.FromResult(false);

        ActiveOrders.Remove(src);
        ActiveOrders[tgt] = sourceOrder with { TableNumber = tgt };

        if (Tables.TryGetValue(src, out var srcTable))
        {
            Tables[src] = srcTable with
            {
                Status = TableStatus.Free,
                ActiveOrderId = null,
                AssignedWaiterName = null,
                CoversCount = 0,
                OpenedAtUtc = null
            };
        }

        if (Tables.TryGetValue(tgt, out var tgtTable))
        {
            Tables[tgt] = tgtTable with
            {
                Status = TableStatus.Occupied,
                ActiveOrderId = sourceOrder.OrderId,
                AssignedWaiterName = sourceOrder.WaiterName,
                CoversCount = sourceOrder.CoversCount,
                OpenedAtUtc = sourceOrder.OpenedAtUtc
            };
        }

        return Task.FromResult(true);
    }

    public Task<bool> MergeTablesAsync(
        string sourceTableNumber,
        string targetTableNumber,
        CancellationToken cancellationToken = default)
    {
        string src = sourceTableNumber.Trim().ToUpperInvariant();
        string tgt = targetTableNumber.Trim().ToUpperInvariant();

        if (!ActiveOrders.TryGetValue(src, out var srcOrder) || !ActiveOrders.TryGetValue(tgt, out var tgtOrder))
            return Task.FromResult(false);

        var mergedLines = tgtOrder.Lines.Concat(srcOrder.Lines).ToList();
        decimal totalTtc = mergedLines.Sum(l => l.TotalPrice);
        decimal totalHt = Math.Round(totalTtc / 1.10m, 2);
        decimal totalVat = totalTtc - totalHt;

        ActiveOrders[tgt] = tgtOrder with
        {
            Lines = mergedLines,
            CoversCount = tgtOrder.CoversCount + srcOrder.CoversCount,
            TotalTtcAmount = totalTtc,
            TotalHtAmount = totalHt,
            TotalVatAmount = totalVat
        };

        ActiveOrders.Remove(src);

        if (Tables.TryGetValue(src, out var srcTable))
        {
            Tables[src] = srcTable with
            {
                Status = TableStatus.Free,
                ActiveOrderId = null,
                AssignedWaiterName = null,
                CoversCount = 0,
                OpenedAtUtc = null
            };
        }

        return Task.FromResult(true);
    }

    public Task<bool> UpdateTableStatusAsync(
        string tableNumber,
        TableStatus newStatus,
        CancellationToken cancellationToken = default)
    {
        string normalized = tableNumber.Trim().ToUpperInvariant();
        if (Tables.TryGetValue(normalized, out var table))
        {
            Tables[normalized] = table with { Status = newStatus };
            return Task.FromResult(true);
        }
        return Task.FromResult(false);
    }

    // ── IKitchenRoutingService ───────────────────────────────────────────────

    public Task<IReadOnlyList<KitchenTicketDto>> SplitAndRouteOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<KitchenTicketDto> tickets = KitchenQueue.Where(t => t.OrderId == orderId).ToList();
        return Task.FromResult(tickets);
    }

    public Task<KitchenTicketDto?> BumpTicketStateAsync(
        Guid ticketId,
        CancellationToken cancellationToken = default)
    {
        int index = KitchenQueue.FindIndex(t => t.TicketId == ticketId);
        if (index < 0)
        {
            return Task.FromResult<KitchenTicketDto?>(null);
        }

        var current = KitchenQueue[index];
        var nextStatus = current.Status switch
        {
            TicketStatus.Pending => TicketStatus.InPreparation,
            TicketStatus.InPreparation => TicketStatus.Ready,
            TicketStatus.Ready => TicketStatus.Served,
            _ => current.Status
        };

        var updated = current with { Status = nextStatus };
        KitchenQueue[index] = updated;

        SignalRClient?.RaiseTicketStatusChanged(ticketId, nextStatus);

        return Task.FromResult<KitchenTicketDto?>(updated);
    }

    public Task<KitchenTicketDto?> RecallTicketAsync(
        Guid ticketId,
        CancellationToken cancellationToken = default)
    {
        int index = KitchenQueue.FindIndex(t => t.TicketId == ticketId);
        if (index < 0)
        {
            return Task.FromResult<KitchenTicketDto?>(null);
        }

        var updated = KitchenQueue[index] with { Status = TicketStatus.Pending };
        KitchenQueue[index] = updated;

        SignalRClient?.RaiseTicketRecalled(ticketId, TicketStatus.Pending);

        return Task.FromResult<KitchenTicketDto?>(updated);
    }

    // ── ICheckoutPaymentService ──────────────────────────────────────────────

    public Task<CheckoutResult> ProcessPaymentTendersAsync(
        Guid orderId,
        string terminalId,
        IReadOnlyList<PaymentTenderRequest> tenders,
        CancellationToken cancellationToken = default)
    {
        long totalPaid = tenders.Sum(t => t.AmountInCents);

        // Find table associated with order
        var tableEntry = ActiveOrders.FirstOrDefault(kvp => kvp.Value.OrderId == orderId);
        if (tableEntry.Key is not null)
        {
            ActiveOrders.Remove(tableEntry.Key);
            if (Tables.TryGetValue(tableEntry.Key, out var table))
            {
                Tables[tableEntry.Key] = table with
                {
                    Status = TableStatus.Free,
                    ActiveOrderId = null,
                    AssignedWaiterName = null,
                    CoversCount = 0,
                    OpenedAtUtc = null
                };
            }
        }

        var result = new CheckoutResult(
            IsSuccess: true,
            TotalPaidCents: totalPaid,
            ChangeGivenCents: 0,
            RemainingBalanceCents: 0,
            ReceiptNumber: $"{terminalId}-SIM-{orderId:N}"[..24],
            FiscalSignature: "SIM-SIG");

        ProcessedPayments.Add(result);

        return Task.FromResult(result);
    }

    public Task<CheckoutResult> VoidReceiptAsync(
        Guid originalReceiptId,
        string terminalId,
        Guid operatorId,
        CancellationToken cancellationToken = default)
    {
        var result = new CheckoutResult(
            IsSuccess: true,
            TotalPaidCents: 0,
            ChangeGivenCents: 0,
            RemainingBalanceCents: 0,
            ReceiptNumber: $"VOID-{originalReceiptId:N}"[..24],
            FiscalSignature: "VOID-SIG");

        return Task.FromResult(result);
    }

    public IReadOnlyList<long> CalculateEqualSplitPartitions(long totalAmountCents, int numberOfGuests)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numberOfGuests);

        long baseAmount = totalAmountCents / numberOfGuests;
        long remainder = totalAmountCents % numberOfGuests;

        var parts = new long[numberOfGuests];
        for (int i = 0; i < numberOfGuests; i++)
            parts[i] = baseAmount;

        parts[numberOfGuests - 1] += remainder;
        return parts;
    }
}
