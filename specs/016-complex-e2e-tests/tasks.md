# Implementation Tasks: Complex E2E Tests

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure. Since the test project `RestaurantPos.Client.Maui.ProfileSimulations` already exists, no setup is required.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented.

**🚨 CRITICAL**: No user story work can begin until this phase is complete.

- [x] T001 [P] Create `SharedFakeBackend` in `tests/RestaurantPos.Client.Maui.ProfileSimulations/Fakes/SharedFakeBackend.cs` implementing `ITableManagementService`, `ICheckoutPaymentService`, and `IKitchenRoutingService`.
- [x] T002 [P] Create `FakeKitchenSignalRClient` in `tests/RestaurantPos.Client.Maui.ProfileSimulations/Fakes/FakeKitchenSignalRClient.cs` adhering to `contracts/SignalR_Mock_API.md`.
- [x] T003 [P] Create `FakeHospitalityServices` in `tests/RestaurantPos.Client.Maui.ProfileSimulations/Fakes/FakeHospitalityServices.cs` implementing `IOrderDiscountService` and `IRoomBillingService`.

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel.

---

## Phase 3: User Story 1 - True End-to-End Multi-Profile Simulation (Priority: P1) 🏆 MVP

**Goal**: Validate that Waiter -> Kitchen -> Manager -> Cashier flows execute seamlessly via the shared backend state.

**Independent Test**: Can be tested by running the long-form E2E simulation.

### Implementation for User Story 1

- [x] T004 [US1] Create `EndToEndRestaurantSimulation` in `tests/RestaurantPos.Client.Maui.ProfileSimulations/Simulations/EndToEndRestaurantSimulation.cs`.
- [x] T005 [US1] Implement `E2E_FullRestaurantLifecycle_ShouldSucceed` test inside `EndToEndRestaurantSimulation.cs` (Stage 1: Waiter).
- [x] T006 [US1] Extend `E2E_FullRestaurantLifecycle_ShouldSucceed` (Stage 2: Kitchen).
- [x] T007 [US1] Extend `E2E_FullRestaurantLifecycle_ShouldSucceed` (Stage 3: Manager).
- [x] T008 [US1] Extend `E2E_FullRestaurantLifecycle_ShouldSucceed` (Stage 4: Cashier).

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently.

---

## Phase 4: User Story 2 - Advanced Hospitality Features Testing (Priority: P2)

**Goal**: Assures that critical revenue-impacting features (discounts, comps, room billing) function correctly in the UI.

**Independent Test**: Can be tested by running `HospitalityProfileSimulation` tests.

### Implementation for User Story 2

- [x] T009 [P] [US2] Create `HospitalityProfileSimulation` in `tests/RestaurantPos.Client.Maui.ProfileSimulations/Simulations/HospitalityProfileSimulation.cs`.
- [x] T010 [US2] Implement `Manager_ApplyGlobalDiscount_ShouldUpdateTotal` in `HospitalityProfileSimulation.cs`.
- [x] T011 [US2] Implement `Manager_CompOrderItem_ShouldSetPriceToZero` in `HospitalityProfileSimulation.cs`.
- [x] T012 [US2] Implement `Cashier_PostRoomCharge_ShouldRouteToRoom` in `HospitalityProfileSimulation.cs`.

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently.

---

## Phase 5: User Story 3 - Complex Fractional Split Bill Scenarios (Priority: P3)

**Goal**: Ensure odd-number splits do not cause loss of pennies or NF525 non-compliance.

**Independent Test**: Can be tested via a specific fractional split test.

### Implementation for User Story 3

- [x] T013 [P] [US3] Implement `Cashier_ComplexSplit_ShouldBalanceCorrectly` in `tests/RestaurantPos.Client.Maui.ProfileSimulations/Simulations/CashierProfileSimulation.cs`.

**Checkpoint**: All user stories should now be independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories.

- [x] T014 Run validation using `quickstart.md` commands to ensure E2E tests complete in under 500ms.
- [x] T015 Verify that zero regressions were introduced to existing tests.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Completed implicitly.
- **Foundational (Phase 2)**: BLOCKS all user stories.
- **User Stories (Phase 3+)**: All depend on Foundational phase completion.
  - User stories can then proceed in parallel.
- **Polish (Final Phase)**: Depends on all user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2).
- **User Story 2 (P2)**: Can start after Foundational (Phase 2).
- **User Story 3 (P3)**: Can start after Foundational (Phase 2).

### Parallel Opportunities

- All Foundational tasks marked `[P]` (T001, T002, T003) can be worked on in parallel.
- `[US2]` and `[US3]` tasks can be developed concurrently alongside `[US1]` tasks since they operate in different files (`HospitalityProfileSimulation.cs` and `CashierProfileSimulation.cs`).

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 2: Foundational (CRITICAL).
2. Complete Phase 3: User Story 1.
3. **STOP and VALIDATE**: Run `dotnet test ... --filter "E2E_FullRestaurantLifecycle_ShouldSucceed"`.
4. Proceed to other stories if successful.
