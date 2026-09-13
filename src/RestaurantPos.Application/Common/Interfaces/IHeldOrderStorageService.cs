using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;
using RestaurantPos.Domain.Enums;
using RestaurantPos.Domain.ValueObjects;

namespace RestaurantPos.Application.Common.Interfaces;

public record HeldOrderDto(
    Guid HoldId,
    string TerminalId,
    Guid OrderId,
    string? CustomerLabel,
    OrderDestination Destination,
    int ItemCount,
    Money TotalTtc,
    DateTimeOffset HeldAtUtc,
    Guid HeldByStaffId
);

public interface IHeldOrderStorageService
{
    Task<HeldOrder> HoldOrderAsync(Order order, string terminalId, Guid staffId, string? customerLabel, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<HeldOrderDto>> GetActiveHeldOrdersAsync(string terminalId, CancellationToken cancellationToken = default);
    Task<Order?> RecallOrderAsync(Guid holdId, CancellationToken cancellationToken = default);
    Task<bool> VoidHeldOrderAsync(Guid holdId, Guid supervisorStaffId, string reason, CancellationToken cancellationToken = default);
    Task<int> GetHeldOrderCountAsync(string terminalId, CancellationToken cancellationToken = default);
}
