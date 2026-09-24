# Quickstart: UI Profile Simulation Tests

**Feature**: `015-ui-profile-simulation-tests`

This guide explains how to validate that the profile simulation feature is correctly implemented. It covers prerequisites, how to run the simulations, and the expected outcomes for each role.

---

## Prerequisites

- .NET 9 SDK installed
- Repository cloned and solution builds cleanly:
  ```powershell
  dotnet build RestaurantPos.slnx
  ```
- The new project `tests/RestaurantPos.Client.Maui.ProfileSimulations/` has been created as defined in the [plan.md](./plan.md).

---

## Running All Profile Simulations

From the repository root:

```powershell
dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/ --logger "console;verbosity=normal"
```

Expected output (all 5 simulation classes pass):

```
Test run for RestaurantPos.Client.Maui.ProfileSimulations.dll (.NETCoreApp,Version=v9.0)
Microsoft (R) Test Execution Command Line Tool Version X.X.X

Starting test execution, please wait...

Passed  WaiterProfileSimulation.AuthenticateWaiter_WithValidPin_ShouldSucceed
Passed  WaiterProfileSimulation.Waiter_SelectTableAndAddItems_ShouldPopulateCart
Passed  WaiterProfileSimulation.Waiter_SendOrder_ShouldClearCart
Passed  WaiterProfileSimulation.Waiter_AttemptVoidItem_ShouldBeDenied
Passed  WaiterProfileSimulation.Waiter_IncorrectPin5Times_ShouldLockOut

Passed  CashierProfileSimulation.AuthenticateCashier_WithValidPin_ShouldSucceed
Passed  CashierProfileSimulation.Cashier_FullPayment_ShouldCompleteCheckout
Passed  CashierProfileSimulation.Cashier_SplitBill3Ways_ShouldProduceThreeEqualPartitions
Passed  CashierProfileSimulation.Cashier_AttemptBackOfficeAccess_ShouldBeDenied

Passed  KitchenStaffProfileSimulation.AuthenticateKitchenStaff_WithValidPin_ShouldSucceed
Passed  KitchenStaffProfileSimulation.KitchenStaff_ViewTickets_ShouldSeeAllPendingTickets
Passed  KitchenStaffProfileSimulation.KitchenStaff_BumpTicket_ShouldTransitionToInPrep
Passed  KitchenStaffProfileSimulation.KitchenStaff_CompleteTicket_ShouldMoveToReady
Passed  KitchenStaffProfileSimulation.KitchenStaff_EmptyQueue_ShouldShowNoPendingTickets

Passed  FloorManagerProfileSimulation.AuthenticateFloorManager_WithValidPin_ShouldSucceed
Passed  FloorManagerProfileSimulation.FloorManager_CanVoidItems_DomainCheckShouldPass
Passed  FloorManagerProfileSimulation.FloorManager_VoidCartItem_ShouldRemoveItem
Passed  FloorManagerProfileSimulation.FloorManager_AttemptBackOfficeAccess_ShouldBeDenied

Passed  AdminProfileSimulation.AuthenticateAdmin_WithValidPin_ShouldSucceed
Passed  AdminProfileSimulation.Admin_CreateStaffMember_ShouldAppearInStaffList
Passed  AdminProfileSimulation.Admin_RegisterPrinter_ShouldAppearInPrinterList
Passed  AdminProfileSimulation.Admin_CreateCatalogProduct_ShouldAppearInProductList
Passed  AdminProfileSimulation.Admin_CreateDuplicateStaff_ShouldFail
Passed  AdminProfileSimulation.Admin_DeactivatedOperator_ShouldNotAuthenticate

Total tests: 25
     Passed: 25
     Failed: 0
```

---

## Verifying Individual Simulations

Run a specific simulation class:

```powershell
dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/ --filter "FullyQualifiedName~WaiterProfileSimulation"
```

---

## Verifying Role Permission Boundaries

All permission boundary tests follow the pattern:

1. Authenticate as role X using `PinLockViewModel` + `FakeOperatorAuthenticationService`
2. Assert `CurrentOperatorRole == X`
3. Assert domain method (e.g., `user.CanAccessBackOffice()`) returns expected value
4. Attempt to invoke restricted command or navigate to restricted area
5. Assert the attempt is denied (error message set, no state mutation)

---

## Running Alongside Existing Tests

All existing tests must remain green:

```powershell
dotnet test tests/ --logger "console;verbosity=minimal"
```

Expected: 0 failures across all test projects.

---

## Key Assertions Per Role

| Role | Authentication | Primary Flow | Permission Boundary |
|---|---|---|---|
| Waiter | PIN "1111" → `IsAuthenticated=true`, `Role=Waiter` | CartItems populated, order dispatched, cart cleared | `CanVoidItems()=false` |
| Cashier | PIN "2222" → `IsAuthenticated=true`, `Role=Cashier` | Checkout completed, split 3 ways → 3 partitions | `CanAccessBackOffice()=false` |
| KitchenStaff | PIN "3333" → `IsAuthenticated=true`, `Role=KitchenStaff` | Ticket Pending→InPrep→Ready, recall works | `CanVoidItems()=false` |
| FloorManager | PIN "4444" → `IsAuthenticated=true`, `Role=FloorManager` | Void item removes it, `CanVoidItems()=true` | `CanAccessBackOffice()=false` |
| Admin | PIN "5555" → `IsAuthenticated=true`, `Role=Admin` | Staff created, printer added, product created | `CanAccessBackOffice()=true` |

---

## Troubleshooting

| Symptom | Likely Cause | Fix |
|---|---|---|
| Build error on `ILocalJournalService` | Missing project reference | Ensure `RestaurantPos.Client.Maui` is referenced |
| `MAUI_UI` platform guards fail | Wrong TFM | Ensure `.csproj` uses `<TargetFramework>net9.0</TargetFramework>` not `net9.0-ios` |
| `DevicePlatformProfile` not found | Missing reference to `RestaurantPos.Client.Maui.Models` | Project reference to MAUI client covers this |
| Null reference in `PosTerminalViewModel` | `ILocalJournalService` not provided | Use `FakeLocalJournalService` (or `Mock<ILocalJournalService>().Object`) |
