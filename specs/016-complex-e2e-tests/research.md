# Research: Complex E2E Tests & State Management

**Date**: 2026-09-10

## 1. Multi-Profile State Synchronization

**Context**: In a true E2E simulation, a Waiter adds an order via `PosTerminalViewModel`, which then must be routed to the `KdsViewModel` for the Kitchen, and finally checked out via `CheckoutViewModel` by the Cashier.

**Decision**: Implement a `SharedFakeBackend` singleton-like class for the test context.
**Rationale**: `Moq` is designed for isolated component testing. To simulate the end-to-end lifecycle, we need a lightweight in-memory fake that implements all 3 core services (`ITableManagementService`, `IKitchenRoutingService`, `ICheckoutPaymentService`). This fake will hold a `Dictionary<string, ActiveTableOrderDto>` and a `List<KitchenTicketDto>`.
**Alternatives considered**: 
- Launching the real `WebApplicationFactory` API in memory (Rejected: Violates the constraint of these being lightweight UI simulations, and increases test execution time dramatically).
- Passing data manually from one ViewModel to another in the test (Rejected: Fails to test the actual application service contracts).

## 2. SignalR Mocking for KDS Updates

**Context**: `KdsViewModel` depends on `IKitchenSignalRClient` and its C# events (`OnNewTicketReceived`, `OnTicketStatusChanged`) to update its UI without manual polling.

**Decision**: Create a `FakeKitchenSignalRClient` that exposes `internal void RaiseNewTicket(KitchenTicketDto)` methods. The `SharedFakeBackend` will take this fake client as a dependency and invoke its methods whenever `DispatchOrderLinesAsync` is called by the Waiter.
**Rationale**: This precisely mirrors the behavior of a real SignalR Hub broadcasting a message, without requiring any WebSocket infrastructure.
**Alternatives considered**:
- Calling `KdsViewModel.LoadTicketsAsync()` manually after dispatching (Rejected: Does not validate the reactive real-time behavior of the ViewModel).

## 3. Fractional Splitting & NF525 Math

**Context**: Need to split a 10.01 amount 3 ways.
**Decision**: Use `RestaurantPos.Domain.ValueObjects.Money` and its `Split(int parts)` method (or equivalent) if it exists, or verify the logic in the Cashier ViewModel. The test will ensure the sum of splits equals exactly 10.01 without floating point loss.
**Rationale**: Ensures NF525 compliance under extreme UI testing scenarios.
