# 🧪 UI Profile Simulations Walkthrough

The **UI Profile Simulation Tests** (`015-ui-profile-simulation-tests`) implementation is complete! We successfully built an automated, headless suite that proves the 5 primary user roles (Waiter, Cashier, KitchenStaff, FloorManager, Admin) function correctly through the MAUI ViewModels according to our Offline-First and Clean Architecture principles.

## 🏗️ What We Built

We created a new dedicated xUnit project: **`RestaurantPos.Client.Maui.ProfileSimulations`**. This project references the Domain, Application, and Client.Maui projects directly to perform high-speed, headless tests.

### 1. Deterministic Fakes (`/Fakes`)
To ensure synchronous, reliable tests without touching a database or the network, we implemented several lightweight Fakes:

*   **`FakeOperatorAuthenticationService`**: Uses a `SimulatedOperator` record to authenticate known PINs (e.g., `1111` for Waiter). It specifically implements the lockout behavior after 5 consecutive incorrect PIN entries.
*   **`FakePlatformEnvironmentService`**: Mocks out the physical hardware limits. Notably, it records every `HapticFeedbackType` request into a list so we can verify the UI is dispatching the correct physical responses (e.g., `LightTap` vs `Success`).
*   **`FakeCheckoutPaymentService`**: Replaces the hardware payment terminal integration. It implements the real cent-level integer split arithmetic required by the specs.
*   **`FakeBackOfficeCatalogService` & `FakeStaffManagementService`**: In-memory backed lists to simulate state changes during the admin tests (e.g., adding a product or locking out a duplicate staff member).
*   **`FakePrinterConfigurationService` & `FakeLocalJournalService`**: Simple stubbed implementations to satisfy dependencies and log mock data.

### 2. End-to-End Simulations (`/Simulations`)
We implemented five simulation classes corresponding to the five RBAC profiles.

*   **Waiter** (`WaiterProfileSimulation.cs`):
    *   Authenticates with PIN "1111".
    *   Verifies the Floor Plan generates default tables.
    *   Populates a cart with items, handles quantity increments, and verifies total calculations.
    *   Dispatches the order to the kitchen (clearing the cart).
    *   Verifies security domain checks prevent them from voiding items or accessing back-office.
    *   *Edge Case verified:* Lockout after 5 incorrect PIN attempts.

*   **Cashier** (`CashierProfileSimulation.cs`):
    *   Authenticates with PIN "2222".
    *   Completes a full card payment checkout and a cash checkout with exact change calculation.
    *   Splits a bill 3 ways, handling the integer remainder dynamically.
    *   *Edge Case verified:* Changing the number of guests correctly recalculates the split partitions.

*   **KitchenStaff** (`KitchenStaffProfileSimulation.cs`):
    *   Authenticates with PIN "3333".
    *   Loads the KDS queue and bumps tickets from `Pending` → `InPreparation` → `Ready`.
    *   Recalls an `InPreparation` ticket back to `Pending`.
    *   *Edge Case verified:* The queue displays correctly when entirely empty.

*   **FloorManager** (`FloorManagerProfileSimulation.cs`):
    *   Authenticates with PIN "4444".
    *   Verifies elevated permissions (e.g., allowed to Void items and print Z-Reports).
    *   Voids an item from the cart, dynamically recalculating the total.
    *   *Edge Case verified:* Attempting to clear the cart retains items that have already been dispatched to the kitchen.

*   **Admin** (`AdminProfileSimulation.cs`):
    *   Authenticates with PIN "5555".
    *   Exercises back-office views: Creates a new staff member, deactivates a staff member, creates a catalog product, and registers a printer.
    *   *Edge Case verified:* A deactivated or invalid operator cannot authenticate under any circumstances.
    *   *Edge Case verified:* Creating a duplicate staff member sets the correct UI error state.

## ✅ Verification Results

We verified that the code adheres to all architectural constraints and code-quality rules:

1.  **Fast Execution**: The complete suite of 35 UI profile simulation tests runs in **< 1 second**.
2.  **Analyzers & Warnings**: We resolved two strict analyzer violations (`CA1512` regarding argument exceptions, and `CA1707` regarding underscore naming conventions, which we suppressed for simulation files via `.editorconfig`). The solution builds with **0 warnings and 0 errors**.
3.  **No Regressions**: The global test suite execution (`dotnet test RestaurantPos.slnx`) passed successfully, executing **all 150+ tests** across the API, Domain, and Maui simulations.
4.  **No View Modification**: We verified these scenarios exclusively through the ViewModel layer without altering a single line of production code in `src/`.

You can review the detailed execution output by running:
> `dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/`
