# Feature Specification: UI Profile Simulation Tests

**Feature Branch**: `015-ui-profile-simulation-tests`

**Created**: 2026-09-10

**Status**: Draft

**Input**: User description: "je veux un projet de test qui simule un utilisateur de chaque profil qui utilise l'interface graphique"

## User Scenarios & Testing *(mandatory)*

<!--
  User journeys ordered by importance. Each story is independently testable.
-->

### User Story 1 - Waiter Flow End-to-End (Priority: P1)

A simulated **Waiter** operator authenticates with their PIN on the POS lock screen, navigates to the floor plan, selects a table, opens the order terminal, adds items from the catalog grid (including applying modifiers), sends the order to the kitchen, and navigates back to the floor plan. The test verifies that all ViewModel states transition correctly and that the Waiter cannot access Admin or Manager-only actions.

**Why this priority**: The Waiter flow is the most-executed daily workflow. Ensuring end-to-end ViewModel correctness for this role is the highest-value safety net for regression.

**Independent Test**: Can be fully tested by simulating the `PinLockViewModel` -> `FloorPlanViewModel` -> `PosTerminalViewModel` -> `ModifiersViewModel` chain and verifying expected state transitions; delivers a full "happy path" regression guard for the most common operator role.

**Acceptance Scenarios**:

1. **Given** a Waiter user seeded in the in-memory staff store with a known PIN, **When** the simulated operator enters the correct PIN, **Then** `PinLockViewModel.IsAuthenticated` is `true`, `CurrentOperatorRole` equals `Waiter`, and no Admin or FloorManager-gated commands are accessible.
2. **Given** an authenticated Waiter session, **When** the operator selects a table and adds two catalog items with a modifier, **Then** `PosTerminalViewModel.CartItems` contains the correct items, modifiers are attached, and the total is computed correctly.
3. **Given** a fully prepared order, **When** the operator sends the order to the kitchen, **Then** the order submission service receives the correct payload and `PosTerminalViewModel.CartItems` is cleared.
4. **Given** an authenticated Waiter, **When** the operator attempts to invoke a void-item or Z-report command, **Then** the command is blocked and an appropriate denial message is displayed.

---

### User Story 2 - Cashier Checkout Flow (Priority: P2)

A simulated **Cashier** operator authenticates, retrieves an existing open order on a table, completes the checkout process (full payment, split bill), and closes the order. The test verifies that the Cashier role can access the checkout page, process payments, and cannot access back-office configuration.

**Why this priority**: The Cashier role owns the end-of-order financial flow; errors here directly impact revenue and fiscal compliance.

**Independent Test**: Can be fully tested by exercising `PinLockViewModel` -> `CheckoutViewModel` -> `SplitBillViewModel` in isolation with a mock order, verifying correct payment totals and state transitions.

**Acceptance Scenarios**:

1. **Given** a Cashier user with a valid PIN, **When** the PIN is entered, **Then** authentication succeeds with `CurrentOperatorRole` equals `Cashier`.
2. **Given** an open order with multiple items, **When** the Cashier initiates a full payment, **Then** `CheckoutViewModel` computes the correct total including VAT breakdown, and the payment confirmation is recorded.
3. **Given** an open order for 3 guests, **When** the Cashier performs a 3-way split, **Then** `SplitBillViewModel` correctly divides the total into 3 equal parts and processes each payment independently.
4. **Given** an authenticated Cashier, **When** the operator attempts to navigate to back-office configuration, **Then** access is denied and the operator is redirected.

---

### User Story 3 - Kitchen Staff KDS Flow (Priority: P2)

A simulated **Kitchen Staff** operator authenticates and interacts with the Kitchen Display System (KDS): views incoming tickets, marks items as in-preparation, and marks tickets as completed. The test verifies that the KDS ViewModel correctly reflects order states and that Kitchen Staff cannot perform front-of-house operations.

**Why this priority**: KDS correctness ensures food preparation is tracked and served on time; incorrect state transitions cause service failures.

**Independent Test**: Can be fully tested by driving `PinLockViewModel` -> `KdsViewModel` with simulated incoming order events, verifying ticket state progression from pending to completed.

**Acceptance Scenarios**:

1. **Given** a Kitchen Staff user with a valid PIN, **When** the PIN is entered, **Then** authentication succeeds with `CurrentOperatorRole` equals `KitchenStaff`.
2. **Given** a newly dispatched order with 3 items, **When** the Kitchen Staff views the KDS, **Then** all 3 items appear as pending tickets in `KdsViewModel.ActiveTickets`.
3. **Given** a pending ticket, **When** the Kitchen Staff marks it as in-preparation, **Then** the ticket state transitions to `InPreparation` and the timestamp is recorded.
4. **Given** an in-preparation ticket, **When** the Kitchen Staff marks it as completed, **Then** the ticket is removed from active tickets and moved to the completed queue.
5. **Given** an authenticated Kitchen Staff, **When** the operator attempts to access the floor plan or cash drawer, **Then** access is denied.

---

### User Story 4 - Floor Manager Supervisory Flow (Priority: P3)

A simulated **Floor Manager** operator authenticates, voids an existing order item, reassigns a table, prints an X-report, and reviews active operator sessions. The test verifies that the Floor Manager role can exercise all elevated privileges without requiring full Admin access.

**Why this priority**: Floor Manager oversight prevents revenue leakage and ensures operational control; testing these elevated capabilities reduces risk at the management level.

**Independent Test**: Can be fully tested by driving `PinLockViewModel` and invoking void-item and X-report commands on the respective ViewModels, checking authorization pass-through and result states.

**Acceptance Scenarios**:

1. **Given** a Floor Manager user with a valid PIN, **When** the PIN is entered, **Then** `CurrentOperatorRole` equals `FloorManager` and `CanVoidItems()` returns `true`.
2. **Given** an existing cart item, **When** the Floor Manager invokes the void-item command, **Then** the item is removed and a void record is logged.
3. **Given** a completed shift, **When** the Floor Manager requests an X-report, **Then** the report is generated with correct sales totals.
4. **Given** an authenticated Floor Manager, **When** the operator attempts to access back-office configuration (staff creation, printer admin), **Then** access is denied.

---

### User Story 5 - Admin Back-Office Full Access (Priority: P3)

A simulated **Admin** operator authenticates and exercises all back-office administrative screens: creates a new staff member, configures a printer, modifies a catalog category, edits the floor plan layout, and reviews the audit log. The test verifies unrestricted access to all Administrative ViewModels.

**Why this priority**: Admin configuration is low-frequency but high-impact; errors in access control or data persistence at this level affect every other role.

**Independent Test**: Can be fully tested by driving `PinLockViewModel` -> `StaffAdminViewModel` -> `PrinterAdminViewModel` -> `CatalogAdminViewModel` -> `LayoutAdminViewModel` in sequence, verifying CRUD operations and access grants.

**Acceptance Scenarios**:

1. **Given** an Admin user with a valid PIN, **When** the PIN is entered, **Then** `CurrentOperatorRole` equals `Admin` and `CanAccessBackOffice()` returns `true`.
2. **Given** the Staff Admin screen, **When** the Admin creates a new Waiter with a valid name and PIN, **Then** the new user appears in the staff list and is persisted.
3. **Given** the Printer Admin screen, **When** the Admin registers a new ESC/POS printer with a valid IP address, **Then** the printer is added to the list and a test print can be dispatched.
4. **Given** the Catalog Admin screen, **When** the Admin adds a new menu item to an existing category, **Then** the item appears in the catalog and is visible to other operator roles.
5. **Given** the Layout Admin screen, **When** the Admin repositions a table on the floor plan, **Then** the new table position is persisted and reflected on the floor plan view.

---

### Edge Cases

- What happens when an operator enters an incorrect PIN 5 times in a row? (Lockout or delay policy)
- How does the simulation handle a profile whose role has been deactivated mid-session?
- What happens when a simulated Waiter tries to execute a command that requires `FloorManager` role programmatically (not through the UI)?
- How does the KDS simulation handle an empty ticket queue?
- What happens when an Admin creates a staff member with a duplicate name or PIN?

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The test project MUST contain one dedicated simulation test class per user role (Waiter, Cashier, KitchenStaff, FloorManager, Admin).
- **FR-002**: Each simulation test class MUST seed an in-memory representation of its respective operator with a known PIN and role, without relying on a real database or live backend.
- **FR-003**: The simulation MUST exercise the PIN authentication flow for each profile through `PinLockViewModel` and verify that `IsAuthenticated` becomes `true` and `CurrentOperatorRole` is set correctly.
- **FR-004**: Each simulation MUST navigate through the primary ViewModel chain appropriate for the given role (e.g., Waiter: FloorPlan -> PosTerminal -> Modifiers; Cashier: Checkout -> SplitBill; KitchenStaff: KDS).
- **FR-005**: The simulation MUST verify that role-restricted actions are inaccessible to profiles that do not hold the required permissions.
- **FR-006**: All external dependencies (staff service, order service, printer service, payment service, KDS hub) MUST be replaced by deterministic test doubles (fakes or mocks) so that simulations run without network or hardware.
- **FR-007**: Each simulation MUST assert on the final ViewModel state after each major interaction step (e.g., cart contents, ticket states, staff list length).
- **FR-008**: The test project MUST be runnable from the existing test runner infrastructure without manual configuration.
- **FR-009**: Simulations MUST be isolated from each other; no shared mutable state is permitted between profile simulations.
- **FR-010**: The Admin simulation MUST perform at minimum one create operation on `StaffAdminViewModel`, one on `PrinterAdminViewModel`, and one on `CatalogAdminViewModel`.

### Key Entities

- **SimulatedOperator**: Represents a test user with a fixed identity (name, role, known plain-text PIN), used to seed the fake authentication service for each profile simulation.
- **FakeOperatorAuthenticationService**: A test double for `IOperatorAuthenticationService` that validates a pre-configured PIN and returns the corresponding operator name and role.
- **FakeStaffManagementService**: A test double for `IStaffManagementService` that stores staff members in an in-memory list.
- **FakeKdsHubService**: A test double that delivers a fixed set of order tickets for KDS simulations.
- **ProfileSimulationTestBase**: An optional shared base class providing common setup utilities (fake services, ViewModel factory) reusable across all profile simulation test classes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 5 profile simulation test classes execute successfully with zero failures on a clean build, covering at least 3 major interaction steps per profile.
- **SC-002**: Each simulation completes in under 5 seconds on a standard developer workstation, ensuring fast feedback during CI.
- **SC-003**: Role-permission boundary assertions achieve 100% pass rate - no role accesses a feature above its permission level during simulation.
- **SC-004**: At least 80% of the existing ViewModel public commands and observable properties relevant to each role's primary workflow are exercised by the corresponding simulation.
- **SC-005**: The test project integrates without modifying any source production code - only test doubles and test infrastructure are added.
- **SC-006**: All 5 edge case scenarios identified in the Edge Cases section are covered by at least one assertion.

## Assumptions

- The test project will be created within the existing `tests/` directory as `RestaurantPos.Client.Maui.ProfileSimulations` (or a similarly named project), following the established test project naming convention.
- All ViewModels are directly instantiable with their dependencies injected via constructor, enabling straightforward test double injection without requiring a DI container.
- The existing `IOperatorAuthenticationService` interface is the correct extension point for simulating PIN-based authentication; no additional abstractions need to be introduced in production code.
- Plain-text PINs are used exclusively within the simulation scope (test doubles) and are never written to production storage.
- The simulations run in the context of the existing xUnit test runner already configured for the `.Client.Maui.Tests` project family.
- MAUI platform-specific rendering (UI rendering engine, graphics, haptics) is not exercised; simulations operate at the ViewModel layer only (no UI automation/Appium), relying on `IPlatformEnvironmentService` test doubles for haptic feedback calls.
- The `FakePlatformEnvironmentService` test double already exists or can be trivially extracted from existing `RestaurantPos.Client.Maui.Tests` test infrastructure.
