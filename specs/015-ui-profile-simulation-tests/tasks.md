# Tasks: UI Profile Simulation Tests

**Feature**: `015-ui-profile-simulation-tests`
**Branch**: `015-ui-profile-simulation-tests`
**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)
**Generated**: 2026-09-10

---

## Phase 1: Setup

**Purpose**: Create the new test project, configure its toolchain, and wire project references.

- [x] T001 Create directory `tests/RestaurantPos.Client.Maui.ProfileSimulations/` with subdirectories `Fakes/`, `Helpers/`, `Simulations/`
- [x] T002 Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/RestaurantPos.Client.Maui.ProfileSimulations.csproj` targeting `net9.0` with xunit 2.9.3, FluentAssertions 8.0.1, Moq 4.20.72, and project references to `RestaurantPos.Domain`, `RestaurantPos.Application`, `RestaurantPos.Client.Maui`
- [x] T003 Add the new project to `RestaurantPos.slnx` so it is included in `dotnet build` and `dotnet test`
- [x] T004 Verify `dotnet build tests/RestaurantPos.Client.Maui.ProfileSimulations/` exits with code 0 (empty project compiles)

**Checkpoint**: Project builds cleanly — ready to add code.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared infrastructure used by ALL five simulations. Must be complete before any simulation class.

> **CRITICAL**: No simulation can be written until T005–T012 are complete.

- [x] T005 [P] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Helpers/SimulatedOperator.cs` — static record with fields `Name`, `Role`, `KnownPin`, `OperatorId` and five factory methods: `Waiter()` PIN "1111", `Cashier()` PIN "2222", `KitchenStaff()` PIN "3333", `FloorManager()` PIN "4444", `Admin()` PIN "5555"
- [x] T006 [P] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Fakes/FakePlatformEnvironmentService.cs` — implements `IPlatformEnvironmentService`; records all `TriggerHapticFeedback` calls in `List<HapticFeedbackType> HapticCalls`; all other methods return stubs (see contracts/fake-services.md)
- [x] T007 [P] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Fakes/FakeOperatorAuthenticationService.cs` — implements `IOperatorAuthenticationService`; accepts a `SimulatedOperator`; returns `OperatorAuthenticationResult(IsSuccess=true, …)` for the correct PIN; increments `_failedAttempts` counter; returns lockout error after 5 consecutive failures
- [x] T008 [P] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Fakes/FakeStaffManagementService.cs` — implements `IStaffManagementService`; in-memory `List<User>` backing store; `CreateStaffMemberAsync` throws `InvalidOperationException` on duplicate name
- [x] T009 [P] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Fakes/FakeCheckoutPaymentService.cs` — implements `ICheckoutPaymentService`; `ProcessPaymentTendersAsync` always returns success; `CalculateEqualSplitPartitions` does real integer arithmetic (base = totalCents/n, remainder appended to last partition)
- [x] T010 [P] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Fakes/FakeBackOfficeCatalogService.cs` — implements `IBackOfficeCatalogService`; seeded with two categories ("Entrées" CAT-001, "Plats" CAT-002) and four products; all CRUD methods mutate the in-memory list
- [x] T011 [P] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Fakes/FakePrinterConfigurationService.cs` — implements `IPrinterConfigurationService`; `RegisterPrinterAsync` appends to in-memory list; `SendTestPrintAsync` returns `TestPrintResult(Success=true, "Test OK (Simulation)", 50ms)`
- [x] T012 [P] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Fakes/FakeLocalJournalService.cs` — implements `ILocalJournalService`; all operations are no-ops returning default/success values
- [x] T013 Verify `dotnet build tests/RestaurantPos.Client.Maui.ProfileSimulations/` still exits with code 0 after all Fakes are added

**Checkpoint**: All shared fakes compile — simulation classes can now be written independently.

---

## Phase 3: User Story 1 — Waiter Flow End-to-End (Priority: P1) ⭐ MVP

**Goal**: Authenticate as Waiter, exercise FloorPlan → PosTerminal → Modifiers flow, assert cart population and kitchen dispatch, verify void/Z-report commands are blocked.

**Independent Test**:
```powershell
dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/ --filter "FullyQualifiedName~WaiterProfileSimulation"
```

### Implementation for User Story 1

- [x] T014 [US1] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Simulations/WaiterProfileSimulation.cs` — xUnit test class with constructor setting up `FakePlatformEnvironmentService`, `FakeOperatorAuthenticationService(SimulatedOperator.Waiter())`, and `FakeLocalJournalService`
- [x] T015 [US1] Add test `AuthenticateWaiter_WithValidPin_ShouldSucceed` — instantiate `PinLockViewModel(env, fakeAuth)`, call `AppendDigitAsync` for each digit of PIN "1111", assert `IsAuthenticated == true`, `CurrentOperatorRole == UserRole.Waiter`, `CurrentOperatorName == "Sophie Durand"`, `HapticCalls` contains `HapticFeedbackType.Success`
- [x] T016 [US1] Add test `Waiter_SelectTableAndAddItems_ShouldPopulateCart` — instantiate `FloorPlanViewModel(env)`, assert `Tables` is not empty; instantiate `PosTerminalViewModel(env, fakeJournal)`, call `AddProductAsync` twice with two different products from `AvailableProducts`, assert `CartItems.Count == 2` and `TotalTtc > 0`
- [x] T017 [US1] Add test `Waiter_AddSameProductTwice_ShouldIncrementQuantity` — `PosTerminalViewModel(env, fakeJournal)`, add same product twice, assert `CartItems.Count == 1`, `CartItems[0].Quantity == 2`
- [x] T018 [US1] Add test `Waiter_SendOrder_ShouldClearCart` — `PosTerminalViewModel(env, fakeJournal)`, set `ActiveTable = "T01"`, add one product, call `SendKitchenAndResetAsync(Mock<ITableManagementService>().Object)`, assert `CartItems` is empty and table service was called once for each of `AddOrUpdateTableOrderItemsAsync` and `DispatchOrderLinesAsync`
- [x] T019 [US1] Add test `Waiter_AttemptVoidItem_DomainCheck_ShouldBeDenied` — create a `User { Role = UserRole.Waiter }`, assert `user.CanVoidItems() == false` and `user.CanPrintZReports() == false` and `user.CanAccessBackOffice() == false`
- [x] T020 [US1] Add test `Waiter_IncorrectPin5Times_ShouldLockOut` — instantiate `PinLockViewModel(env, fakeAuth)`, enter wrong PIN "9999" five times via `AppendDigitAsync` four digits each time, assert after 5th attempt that `IsAuthenticated == false` and error message contains "verrouillé"
- [x] T021 [US1] Run `dotnet test --filter "FullyQualifiedName~WaiterProfileSimulation"` and confirm all tests pass

**Checkpoint**: Waiter simulation green — MVP validated.

---

## Phase 4: User Story 2 — Cashier Checkout Flow (Priority: P2)

**Goal**: Authenticate as Cashier, complete full checkout, perform 3-way split, verify back-office access is denied.

**Independent Test**:
```powershell
dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/ --filter "FullyQualifiedName~CashierProfileSimulation"
```

### Implementation for User Story 2

- [x] T022 [US2] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Simulations/CashierProfileSimulation.cs` — constructor wiring `FakePlatformEnvironmentService`, `FakeOperatorAuthenticationService(SimulatedOperator.Cashier())`, `FakeCheckoutPaymentService`
- [x] T023 [US2] Add test `AuthenticateCashier_WithValidPin_ShouldSucceed` — `PinLockViewModel(env, fakeAuth)`, enter PIN "2222", assert `IsAuthenticated == true`, `CurrentOperatorRole == UserRole.Cashier`
- [x] T024 [US2] Add test `Cashier_FullPayment_ShouldCompleteCheckout` — `CheckoutViewModel(env, fakeCheckout)`, call `Initialize(Guid.NewGuid(), 5000)`, call `SelectPaymentMethod(PaymentMethod.CreditCard)`, call `FinalizeCheckoutAsync()`, assert `IsCompleted == true`, `RemainingBalanceCents == 0`, `ReceiptNumber` is not empty, `HapticCalls` contains `Success`
- [x] T025 [US2] Add test `Cashier_AddCashBill_ShouldComputeChangeCorrectly` — `CheckoutViewModel(env)`, `Initialize(orderId, 3500)`, `AddCashFastBill(5000)`, assert `RemainingBalanceCents == 0`, `ChangeDueCents == 1500`, `AppliedTenders.Count == 1`
- [x] T026 [US2] Add test `Cashier_SplitBill3Ways_ShouldProduceThreeEqualPartitions` — `SplitBillViewModel(env, fakeCheckout)`, `Initialize(9000, 3)`, assert `Partitions.Count == 3`, each `Partitions[i].AmountCents == 3000`, `Partitions.Sum(p => p.AmountCents) == 9000`
- [x] T027 [US2] Add test `Cashier_SplitBill_WithRemainder_ShouldAddRemainderToLastPartition` — `Initialize(1001, 3)`, assert `Partitions[2].AmountCents == 335` (333+333+335 = 1001)
- [x] T028 [US2] Add test `Cashier_AttemptBackOfficeAccess_DomainCheck_ShouldBeDenied` — create `User { Role = UserRole.Cashier }`, assert `user.CanAccessBackOffice() == false`
- [x] T029 [US2] Run `dotnet test --filter "FullyQualifiedName~CashierProfileSimulation"` and confirm all tests pass

**Checkpoint**: Cashier simulation green.

---

## Phase 5: User Story 3 — Kitchen Staff KDS Flow (Priority: P2)

**Goal**: Authenticate as KitchenStaff, load tickets, progress them Pending→InPrep→Ready, recall, verify empty queue.

**Independent Test**:
```powershell
dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/ --filter "FullyQualifiedName~KitchenStaffProfileSimulation"
```

### Implementation for User Story 3

- [x] T030 [US3] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Simulations/KitchenStaffProfileSimulation.cs` — constructor wiring `FakePlatformEnvironmentService`, `FakeOperatorAuthenticationService(SimulatedOperator.KitchenStaff())`
- [x] T031 [US3] Add test `AuthenticateKitchenStaff_WithValidPin_ShouldSucceed` — enter PIN "3333", assert `IsAuthenticated == true`, `CurrentOperatorRole == UserRole.KitchenStaff`
- [x] T032 [US3] Add test `KitchenStaff_ViewTickets_ShouldSeeAllPendingTickets` — `KdsViewModel(env)`, call `AddIncomingTicket` three times with three distinct `KitchenTicketDto` records (all `Status=Pending`, `StationId="STATION-ALL"`), assert `PendingTickets.Count == 3`, `InPrepTickets.Count == 0`
- [x] T033 [US3] Add test `KitchenStaff_BumpTicket_ShouldTransitionToInPrep` — `KdsViewModel(env)`, add one ticket, call `BumpTicketAsync(PendingTickets.First())`, assert `PendingTickets.Count == 0`, `InPrepTickets.Count == 1`, `InPrepTickets.First().Ticket.Status == TicketStatus.InPreparation`
- [x] T034 [US3] Add test `KitchenStaff_CompleteTicket_ShouldMoveToReady` — add one ticket, bump twice, assert `PendingTickets.Count == 0`, `InPrepTickets.Count == 0`, `ReadyTickets.Count == 1`, `ReadyTickets.First().Ticket.Status == TicketStatus.Ready`
- [x] T035 [US3] Add test `KitchenStaff_RecallTicket_ShouldRestoreToPending` — add ticket, bump once (→InPrep), call `RecallTicketAsync`, assert `InPrepTickets.Count == 0`, `PendingTickets.Count == 1`, status is `Pending`
- [x] T036 [US3] Add test `KitchenStaff_EmptyQueue_ShouldShowNoPendingTickets` — `KdsViewModel(env)`, do not add any tickets, assert `PendingTickets.Count == 0`, `InPrepTickets.Count == 0`, `ReadyTickets.Count == 0`
- [x] T037 [US3] Add test `KitchenStaff_DomainCheck_CannotVoidOrAccessBackOffice` — create `User { Role = UserRole.KitchenStaff }`, assert `user.CanVoidItems() == false`, `user.CanAccessBackOffice() == false`
- [x] T038 [US3] Run `dotnet test --filter "FullyQualifiedName~KitchenStaffProfileSimulation"` and confirm all tests pass

**Checkpoint**: KitchenStaff simulation green.

---

## Phase 6: User Story 4 — Floor Manager Supervisory Flow (Priority: P3)

**Goal**: Authenticate as FloorManager, verify elevated domain permissions, void cart item, assert back-office access denied.

**Independent Test**:
```powershell
dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/ --filter "FullyQualifiedName~FloorManagerProfileSimulation"
```

### Implementation for User Story 4

- [x] T039 [US4] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Simulations/FloorManagerProfileSimulation.cs` — constructor wiring `FakePlatformEnvironmentService`, `FakeOperatorAuthenticationService(SimulatedOperator.FloorManager())`, `FakeLocalJournalService`
- [x] T040 [US4] Add test `AuthenticateFloorManager_WithValidPin_ShouldSucceed` — enter PIN "4444", assert `IsAuthenticated == true`, `CurrentOperatorRole == UserRole.FloorManager`
- [x] T041 [US4] Add test `FloorManager_CanVoidItems_DomainCheckShouldPass` — create `User { Role = UserRole.FloorManager }`, assert `user.CanVoidItems() == true`, `user.CanPrintZReports() == true`, `user.CanAccessBackOffice() == false`
- [x] T042 [US4] Add test `FloorManager_VoidCartItem_ShouldRemoveItemAndAdjustTotal` — `PosTerminalViewModel(env, fakeJournal)`, add two products, call `RemoveCartItemCommand` (or `vm.CartItems.Remove(item)` if no command exists), assert `CartItems.Count == 1`, `TotalTtc` equals remaining item price
- [x] T043 [US4] Add test `FloorManager_ClearPendingItems_ShouldLeaveDispatchedItems` — `PosTerminalViewModel(env, fakeJournal)`, add one dispatched item and one pending item, call `ClearCart()`, assert `CartItems.Count == 1`, remaining item `IsDispatched == true`
- [x] T044 [US4] Add test `FloorManager_AttemptBackOfficeAccess_DomainCheck_ShouldBeDenied` — create `User { Role = UserRole.FloorManager }`, assert `user.CanAccessBackOffice() == false`
- [x] T045 [US4] Run `dotnet test --filter "FullyQualifiedName~FloorManagerProfileSimulation"` and confirm all tests pass

**Checkpoint**: FloorManager simulation green.

---

## Phase 7: User Story 5 — Admin Back-Office Full Access (Priority: P3)

**Goal**: Authenticate as Admin, exercise all back-office ViewModels (StaffAdmin, PrinterAdmin, CatalogAdmin), verify full access, verify duplicate staff edge case.

**Independent Test**:
```powershell
dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/ --filter "FullyQualifiedName~AdminProfileSimulation"
```

### Implementation for User Story 5

- [x] T046 [US5] Create `tests/RestaurantPos.Client.Maui.ProfileSimulations/Simulations/AdminProfileSimulation.cs` — constructor wiring `FakePlatformEnvironmentService`, `FakeOperatorAuthenticationService(SimulatedOperator.Admin())`, `FakeStaffManagementService`, `FakeBackOfficeCatalogService`, `FakePrinterConfigurationService`
- [x] T047 [US5] Add test `AuthenticateAdmin_WithValidPin_ShouldSucceed` — enter PIN "5555", assert `IsAuthenticated == true`, `CurrentOperatorRole == UserRole.Admin`
- [x] T048 [US5] Add test `Admin_CanAccessBackOffice_DomainCheckShouldPass` — create `User { Role = UserRole.Admin }`, assert `user.CanAccessBackOffice() == true`, `user.CanVoidItems() == true`, `user.CanPrintZReports() == true`
- [x] T049 [US5] Add test `Admin_CreateStaffMember_ShouldAppearInStaffList` — `StaffAdminViewModel(fakeStaff, env)`, call `LoadStaffAsync()`, call `ShowCreateForm()`, set `NewStaffName = "Lena"`, `NewStaffRole = UserRole.Cashier`, `NewStaffPin = "6789"`, call `CreateStaffAsync()`, assert `StaffMembers.Any(u => u.Name == "Lena")`, `IsFormVisible == false`, `StatusMessage` contains "Lena"
- [x] T050 [US5] Add test `Admin_CreateDuplicateStaff_ShouldSetErrorMessage` — pre-seed `FakeStaffManagementService` with a user named "Lena", attempt to create another "Lena", assert `ErrorMessage` is not empty, `StaffMembers.Count(u => u.Name == "Lena") == 1`
- [x] T051 [US5] Add test `Admin_RegisterPrinter_ShouldAppearInPrinterList` — `PrinterAdminViewModel(fakePrinter, env)`, call `LoadPrintersAsync()` (initial list empty), call command to register a printer with Name="Küche Drucker", IpAddress="192.168.1.50", Port=9100, assert `Printers.Count == 1`, `Printers.First().IpAddress == "192.168.1.50"`
- [x] T052 [US5] Add test `Admin_SendTestPrint_ShouldSucceed` — `PrinterAdminViewModel(fakePrinter, env)`, register a printer, call `SendTestPrintAsync(printer.Id)`, assert result `Success == true`
- [x] T053 [US5] Add test `Admin_CreateCatalogProduct_ShouldAppearInProductList` — `CatalogAdminViewModel(fakeCatalog, env)`, call `LoadCategoriesAsync()`, select first category, call `CreateProductAsync("Mousse au Chocolat", "CAT-001", 6.50m, 10.0m, ...)`, assert `Products.Any(p => p.Name == "Mousse au Chocolat")`
- [x] T054 [US5] Add test `Admin_DeactivateStaffMember_ShouldSetInactive` — `StaffAdminViewModel(fakeStaff, env)` pre-seeded with one user, call `DeactivateStaffAsync(user)`, assert `user.IsActive == false`
- [x] T055 [US5] Run `dotnet test --filter "FullyQualifiedName~AdminProfileSimulation"` and confirm all tests pass

**Checkpoint**: Admin simulation green — all 5 profiles validated.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Full suite run, edge case validation, code quality.

- [x] T056 Run `dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/` (all simulations) and confirm 0 failures, total ≥ 25 tests
- [x] T057 Run `dotnet test tests/` (entire test suite including existing projects) and confirm 0 regressions introduced
- [x] T058 [P] Verify each simulation class has its own isolated fakes — no shared static fields or `static` mutable state between test classes
- [x] T059 [P] Check `TreatWarningsAsErrors=true` compiles with 0 warnings across the new project
- [x] T060 Validate SC-002 timing: run each simulation filter and confirm each completes in < 5 seconds wall-clock time
- [x] T061 [P] Review `FakeOperatorAuthenticationService` lockout: write a brief comment block explaining the `_failedAttempts` counter reset policy

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all simulation phases
- **Phases 3–7 (Simulations)**: All depend on Phase 2 completion; can proceed in parallel
- **Phase 8 (Polish)**: Depends on all simulation phases complete

### User Story Dependencies

| Story | Phase | Depends on | Parallelizable? |
|---|---|---|---|
| US1 — Waiter | Phase 3 | Phase 2 | ✅ Yes (with US2–US5) |
| US2 — Cashier | Phase 4 | Phase 2 | ✅ Yes |
| US3 — KitchenStaff | Phase 5 | Phase 2 | ✅ Yes |
| US4 — FloorManager | Phase 6 | Phase 2 | ✅ Yes |
| US5 — Admin | Phase 7 | Phase 2 | ✅ Yes |

### Within Each Phase

- Tasks marked `[P]` within a phase can run in parallel (they touch different files)
- Non-`[P]` tasks within a simulation class must run sequentially (same file)

---

## Parallel Execution Examples

```powershell
# Phase 2 — all fakes in parallel (different files):
# T005: SimulatedOperator.cs
# T006: FakePlatformEnvironmentService.cs
# T007: FakeOperatorAuthenticationService.cs
# T008: FakeStaffManagementService.cs
# T009: FakeCheckoutPaymentService.cs
# T010: FakeBackOfficeCatalogService.cs
# T011: FakePrinterConfigurationService.cs
# T012: FakeLocalJournalService.cs

# Phases 3–7 — all five simulation classes in parallel after Phase 2:
# WaiterProfileSimulation.cs (T014–T020)
# CashierProfileSimulation.cs (T022–T028)
# KitchenStaffProfileSimulation.cs (T030–T037)
# FloorManagerProfileSimulation.cs (T039–T044)
# AdminProfileSimulation.cs (T046–T054)
```

---

## Implementation Strategy

### MVP First (User Story 1 only — Waiter)

1. Complete Phase 1: Setup
2. Complete Phase 2: All fakes (T005–T012)
3. Complete Phase 3: Waiter simulation (T014–T021)
4. **STOP and VALIDATE**: `dotnet test --filter WaiterProfileSimulation` — all green
5. Demo: PIN authentication + cart + kitchen dispatch fully simulated

### Incremental Delivery

1. Setup + Foundational → foundation ready
2. Waiter (P1) → validate → ✅ MVP
3. Cashier + KitchenStaff (P2) → validate → ✅ v1.1
4. FloorManager + Admin (P3) → validate → ✅ v1.2
5. Polish (Phase 8) → full suite → ✅ complete

---

## Notes

- `[P]` tasks touch different files and have no inter-dependencies
- `[USn]` label maps task to user story for traceability
- `FakeOperatorAuthenticationService` must reset `_failedAttempts` on correct PIN so tests are stateless
- Do not use `static` fields in any fake — xUnit creates a new test class instance per test method
- `PosTerminalViewModel.ClearCart()` existing behaviour (retains dispatched items) is tested in T043 — no production code change needed
- The `PrinterAdminViewModel.LoadPrintersAsync()` method signature should be confirmed against the actual ViewModel before writing T051; adjust method name if different
