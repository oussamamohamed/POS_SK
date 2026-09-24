# Contract: Multi-Station Kitchen Routing Service

**Feature**: `005-phase3-kds-realtime-routing`
**Domain**: Kitchen Order Splitting & Station Routing

## 1. Kitchen Routing Service Interface

```csharp
namespace RestaurantPos.Application.Common.Interfaces;

public enum TicketStatus
{
    Pending = 0,
    InPreparation = 1,
    Ready = 2,
    Served = 3
}

public enum TicketItemStatus
{
    Pending = 0,
    InPrep = 1,
    Ready = 2
}

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

public interface IKitchenRoutingService
{
    Task<IReadOnlyList<KitchenTicketDto>> SplitAndRouteOrderAsync(
        Guid orderId, 
        CancellationToken cancellationToken = default);

    Task<KitchenTicketDto> BumpTicketStateAsync(
        Guid ticketId, 
        CancellationToken cancellationToken = default);

    Task<KitchenTicketDto> RecallTicketAsync(
        Guid ticketId, 
        CancellationToken cancellationToken = default);
}
```
