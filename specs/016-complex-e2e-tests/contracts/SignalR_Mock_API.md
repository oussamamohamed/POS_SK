# SignalR Mock API Contract

This defines the internal API surface for the `FakeKitchenSignalRClient` that allows tests (or the `SharedFakeBackend`) to simulate WebSocket messages pushing to the frontend `KdsViewModel`.

## `FakeKitchenSignalRClient`

Implements: `IKitchenSignalRClient`

### Internal Simulation Methods

These methods are specifically designed to be called by the `SharedFakeBackend` to trigger the events that the `KdsViewModel` is listening to.

```csharp
/// <summary>
/// Simulates a server push event containing a new ticket.
/// </summary>
internal void RaiseNewTicket(KitchenTicketDto ticket)
{
    OnNewTicketReceived?.Invoke(ticket);
}

/// <summary>
/// Simulates a server push event indicating a ticket's status has changed.
/// </summary>
internal void RaiseTicketStatusChanged(Guid ticketId, TicketStatus newStatus)
{
    OnTicketStatusChanged?.Invoke(ticketId, newStatus);
}
```

### Usage in E2E

The backend receives a command from the Waiter's Terminal via `ITableManagementService.DispatchOrderLinesAsync()`. It creates a `KitchenTicketDto` and then calls `_fakeSignalRClient.RaiseNewTicket(dto)`. 
The `KdsViewModel`, which is subscribed to `OnNewTicketReceived`, will automatically add this ticket to its `PendingTickets` observable collection, validating the UI updates.
