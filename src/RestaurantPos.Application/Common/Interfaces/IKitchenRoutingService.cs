using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RestaurantPos.Domain.Entities;

namespace RestaurantPos.Application.Common.Interfaces;

public record KitchenTicketItemDto(
    Guid ItemId,
    Guid ProductId,
    string ProductName,
    int Quantity,
    string? ModifiersSummary,
    string? KitchenComment,
    TicketItemStatus Status);

public record KitchenTicketDto(
    Guid TicketId,
    Guid OrderId,
    string TableNumber,
    string ServerName,
    int CoversCount,
    string StationId,
    TicketStatus Status,
    DateTimeOffset DispatchedAtUtc,
    IReadOnlyList<KitchenTicketItemDto> Items);

public interface IKitchenHubClient
{
    Task OnNewTicketReceived(KitchenTicketDto ticket);
    Task OnTicketStatusChanged(Guid ticketId, TicketStatus newStatus);
    Task OnItemStatusChanged(Guid ticketId, Guid itemId, TicketItemStatus newStatus);
    Task OnTicketRecalled(Guid ticketId, TicketStatus restoredStatus);
}

public interface IKitchenRoutingService
{
    Task<IReadOnlyList<KitchenTicketDto>> SplitAndRouteOrderAsync(
        Guid orderId,
        CancellationToken cancellationToken = default);

    Task<KitchenTicketDto?> BumpTicketStateAsync(
        Guid ticketId,
        CancellationToken cancellationToken = default);

    Task<KitchenTicketDto?> RecallTicketAsync(
        Guid ticketId,
        CancellationToken cancellationToken = default);
}
