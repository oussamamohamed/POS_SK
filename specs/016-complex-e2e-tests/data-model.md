# Data Model: Complex E2E Tests

The primary data structures for this feature revolve around the `SharedFakeBackend` which maintains the state for the duration of the test.

## `SharedFakeBackend` State

The backend acts as an in-memory repository simulating the application's domain layer behavior.

### Collections

- `Dictionary<string, ActiveTableOrderDto> ActiveOrders`: Maps a table number (e.g., "T01") to its current active order.
- `List<KitchenTicketDto> KitchenQueue`: The global list of kitchen tickets.

### Entities & ViewModels Involved

1.  **`PosTerminalViewModel`**: Submits `OrderItemInputDto` to the backend.
2.  **`KdsViewModel`**: Receives `KitchenTicketDto` (via `FakeKitchenSignalRClient` events).
3.  **`CheckoutViewModel`**: Queries `ActiveTableOrderDto` from the backend to process payment.

## State Transitions

1.  **Waiter Action**: `DispatchOrderLinesAsync("T01")`
    *   *Backend Logic*: Extracts `IsDispatched = false` lines from `ActiveOrders["T01"]`.
    *   *Backend Logic*: Maps them to a new `KitchenTicketDto`.
    *   *Backend Logic*: Adds ticket to `KitchenQueue`.
    *   *Backend Logic*: Calls `FakeKitchenSignalRClient.RaiseNewTicket(ticket)`.
2.  **Kitchen Action**: `UpdateTicketStatusAsync(ticketId, TicketStatus.Ready)`
    *   *Backend Logic*: Updates the ticket in `KitchenQueue`.
    *   *Backend Logic*: Calls `FakeKitchenSignalRClient.RaiseTicketStatusChanged(...)`.
3.  **Cashier Action**: `ProcessPaymentAsync(orderId, amount)`
    *   *Backend Logic*: Validates amount. If fully paid, removes `ActiveOrders["T01"]`.
