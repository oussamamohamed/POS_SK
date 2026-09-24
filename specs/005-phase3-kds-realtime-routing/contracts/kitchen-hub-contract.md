# Contract: Real-Time Kitchen SignalR Hub (`KitchenHub`)

**Feature**: `005-phase3-kds-realtime-routing`
**Protocol**: WebSockets via ASP.NET Core SignalR
**Hub Path**: `/hubs/kitchen`

## 1. Hub Client Interface (Server to Client Messages)

```csharp
namespace RestaurantPos.Application.Common.Interfaces;

public interface IKitchenHubClient
{
    Task OnNewTicketReceived(KitchenTicketDto ticket);
    Task OnTicketStatusChanged(Guid ticketId, TicketStatus newStatus);
    Task OnItemStatusChanged(Guid ticketId, Guid itemId, TicketItemStatus newStatus);
    Task OnTicketRecalled(Guid ticketId, TicketStatus restoredStatus);
}
```

## 2. Hub Server Interface (Client to Server Invocations)

```csharp
namespace RestaurantPos.Application.Common.Interfaces;

public interface IKitchenHubServer
{
    Task JoinStationGroupAsync(string stationId);
    Task LeaveStationGroupAsync(string stationId);
    Task DispatchOrderToKitchenAsync(Guid orderId);
    Task BumpTicketAsync(Guid ticketId);
    Task BumpItemAsync(Guid ticketId, Guid itemId);
    Task RecallTicketAsync(Guid ticketId);
}
```
