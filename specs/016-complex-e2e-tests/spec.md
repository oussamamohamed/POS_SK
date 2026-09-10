# Feature Specification: Complex E2E Tests

**Feature Branch**: `016-complex-e2e-tests`

**Created**: 2026-09-10

**Status**: Draft

**Input**: User description: "je veux plus de cas de tests, test complexe et des tests de bout en bout", User also indicated "mock the client" for SignalR simulation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - True End-to-End Multi-Profile Simulation (Priority: P1)

Currently, the headless tests isolate each profile with independent fakes. We need a unified "Shared Backend Fake" (or orchestrator) that allows a complete restaurant lifecycle to occur seamlessly across the ViewModels.
The SignalR client for the KDS ViewModel MUST be mocked to trigger live events correctly when the Waiter dispatches an order.

**Why this priority**: Validates that the different modules of the application (Order Taking -> Kitchen -> Checkout) communicate correctly via the application contracts, ensuring system-level integrity.

**Independent Test**: Can be tested by running a single long-form E2E test that cycles through 4 profiles sequentially.

**Acceptance Scenarios**:

1. **Given** an empty restaurant, **When** the Waiter creates an order and dispatches it, **Then** the Kitchen KDS immediately receives the ticket via the mocked SignalR client.
2. **Given** a ticket in the Kitchen, **When** the Kitchen Staff bumps it to Ready, **Then** the system updates the table status.
3. **Given** a Ready order, **When** the Cashier opens the table, **Then** they can view the full bill and check out successfully.

---

### User Story 2 - Advanced Hospitality Features Testing (Priority: P2)

The application possesses advanced hospitality features (Discounts, Comp items, Hotel Room Billing) that are not currently covered in the UI simulations.

**Why this priority**: Assures that critical revenue-impacting features function correctly in the UI.

**Independent Test**: Can be tested via a new `HospitalityProfileSimulation` test class.

**Acceptance Scenarios**:

1. **Given** an active cart, **When** a Floor Manager applies a 10% global discount, **Then** the total TTC dynamically updates.
2. **Given** an active cart, **When** a Floor Manager comps (offers) a specific item, **Then** the item's price is set to 0.00 but it remains on the receipt.
3. **Given** an active order, **When** the Cashier selects "Room Charge" and assigns it to "Room 304", **Then** the checkout completes and the room billing service is called.

---

### User Story 3 - Complex Fractional Split Bill Scenarios (Priority: P3)

We need a complex test case involving fractional splits combined with hospitality rules (e.g. splitting an odd total 3 ways while a discount is applied).

**Why this priority**: Ensures that edge cases in decimal arithmetic and rounding do not cause application crashes or NF525 certification failures.

**Independent Test**: A specific `Cashier_ComplexSplit_ShouldBalanceCorrectly` test.

**Acceptance Scenarios**:

1. **Given** a bill of an odd amount (e.g. 10.01) with a 5% discount applied, **When** the cashier splits the bill 3 ways, **Then** the partitions perfectly sum up to the total without any lost pennies.

### Edge Cases

- What happens when the Waiter dispatches an order but the mocked SignalR client fails to broadcast?
- How does the system handle a room charge checkout if the Hotel Room is marked as "Checked Out"?
- What happens to a fractionally split bill if an item is voided mid-split?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a `SharedFakeBackend` capable of synchronizing state between `ITableManagementService`, `IKitchenRoutingService`, and `ICheckoutPaymentService`.
- **FR-002**: System MUST mock `IKitchenSignalRClient` to invoke event delegates when orders are dispatched, simulating real-time KDS updates.
- **FR-003**: System MUST provide deterministic Fakes for `IOrderDiscountService` and `IRoomBillingService`.
- **FR-004**: System MUST verify that a full Waiter -> Kitchen -> Manager -> Cashier flow works without memory leaks or state corruption.
- **FR-005**: System MUST accurately test odd-number splits in the checkout UI simulations.

### Key Entities *(include if feature involves data)*

- **SharedFakeBackend**: The singleton or scoped state container that links the various Fake services together during an E2E test.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of the new complex E2E tests and hospitality tests pass in headless mode.
- **SC-002**: The full multi-profile E2E test executes in under 2 seconds.
- **SC-003**: No regressions are introduced to the existing 35 simulation tests.

## Assumptions

- We assume the existing XUnit test runner can handle a longer, state-sharing test class without parallelization conflicts (we may need `[Collection]` or similar to prevent static state leaking, though instance-based shared state is preferred).
- We assume that "mock the client" for SignalR implies writing a `FakeKitchenSignalRClient` that exposes internal methods (e.g., `SimulateNewTicket()`) for the `SharedFakeBackend` to trigger.
