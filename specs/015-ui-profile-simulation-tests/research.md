# Research: UI Profile Simulation Tests

**Feature**: `015-ui-profile-simulation-tests`
**Phase**: 0 — Outline & Research

---

## 1. Existing Test Infrastructure

### Decision: Reuse existing test project toolchain (xunit + FluentAssertions + Moq)

**Rationale**: `RestaurantPos.Client.Maui.Tests.csproj` already uses xUnit 2.9.3, FluentAssertions 8.0.1, Moq 4.20.72, and targets `net9.0`. The new project will use the same package versions to avoid dependency conflicts and keep CI configuration uniform.

**Findings**:
- Project references needed: `RestaurantPos.Domain`, `RestaurantPos.Application`, `RestaurantPos.Client.Maui`
- `TreatWarningsAsErrors=true` is enforced in the existing tests — same will apply
- `Nullable=enable` and `ImplicitUsings=enable` must be set identically

**Alternatives considered**: NSubstitute instead of Moq — rejected because Moq is already present and the team is familiar with it.

---

## 2. ViewModel Instantiation Patterns

### Decision: Constructor injection, no DI container, hand-written Fakes for integration simulations

**Rationale**: All ViewModels use constructor injection with optional service parameters (e.g., `KdsViewModel(IPlatformEnvironmentService, IKitchenRoutingService? = null, IKitchenSignalRClient? = null)`). The optional-null pattern means the ViewModel falls back to local offline behavior when a service is not provided, which is ideal for deterministic simulation testing.

**Key ViewModel constructors confirmed**:

| ViewModel | Required deps | Optional deps |
|---|---|---|
| `PinLockViewModel` | `IPlatformEnvironmentService` | `IOperatorAuthenticationService?` |
| `FloorPlanViewModel` | `IPlatformEnvironmentService` | `ITableManagementService?`, `ITableSignalRClient?` |
| `PosTerminalViewModel` | `IPlatformEnvironmentService`, `ILocalJournalService` | — |
| `KdsViewModel` | `IPlatformEnvironmentService` | `IKitchenRoutingService?`, `IKitchenSignalRClient?` |
| `CheckoutViewModel` | `IPlatformEnvironmentService` | `ICheckoutPaymentService?` |
| `SplitBillViewModel` | `IPlatformEnvironmentService`, `ICheckoutPaymentService` | — |
| `StaffAdminViewModel` | `IStaffManagementService`, `IPlatformEnvironmentService` | — |
| `PrinterAdminViewModel` | `IPrinterConfigurationService`, `IPlatformEnvironmentService` | — |
| `CatalogAdminViewModel` | `IBackOfficeCatalogService`, `IPlatformEnvironmentService` | — |
| `LayoutAdminViewModel` | `ITerminalLayoutService`, `IPlatformEnvironmentService` | — |

**Strategy**: For the Waiter, KDS, and FloorPlan simulations, null optional deps will cause the ViewModel to use its built-in offline/demo state — no fake needed. For services with required non-nullable deps (SplitBillViewModel, StaffAdminViewModel, etc.), hand-written fakes will be provided.

---

## 3. Role Permission Enforcement

### Decision: Verify via `User.CanVoidItems()`, `User.CanPrintZReports()`, `User.CanAccessBackOffice()` domain methods

**Rationale**: The `User` domain entity already exposes role capability methods:
```
CanVoidItems()       => FloorManager or Admin
CanPrintZReports()   => FloorManager or Admin
CanAccessBackOffice() => Admin only
```
Simulations will assert these methods on the authenticated operator's `User` object to verify role boundary enforcement at the domain level, complementing ViewModel-level access checks.

**Finding**: There is no centralized `IAuthorizationService` at the ViewModel level. Role enforcement is done inline by checking the authenticated role (stored in `PinLockViewModel.CurrentOperatorRole`). Simulations must hold a reference to `PinLockViewModel` after authentication and pass the role information forward to each downstream ViewModel scenario.

---

## 4. PIN Authentication Flow

### Decision: Inject `FakeOperatorAuthenticationService` for all profiles

**Rationale**: `PinLockViewModel.ValidatePinAsync()` uses `IOperatorAuthenticationService.AuthenticatePinAsync(rawPin)` when injected. The interface returns `OperatorAuthenticationResult(IsSuccess, OperatorId, OperatorName, Role, ErrorMessage)`. A simple fake can map a pre-configured PIN string to a specific `OperatorAuthenticationResult`.

**Default fallback**: When `IOperatorAuthenticationService` is null, `PinLockViewModel` accepts PIN `"1234"` or `"0000"` and sets `CurrentOperatorRole = Waiter`. This is not usable for Cashier/Admin simulations → `FakeOperatorAuthenticationService` is mandatory.

---

## 5. `ILocalJournalService` Dependency

### Decision: Use Moq mock for `ILocalJournalService` in `PosTerminalViewModel`

**Rationale**: `PosTerminalViewModel` requires a non-null `ILocalJournalService`. The existing unit tests mock it with `new Mock<ILocalJournalService>()`. The profile simulation will do the same (no real SQLite journal is needed).

---

## 6. `MAUI_UI` Preprocessor Guard

### Decision: Target `net9.0` (non-MAUI) — `MAUI_UI` symbol is NOT defined

**Rationale**: `KdsViewModel` and `FloorPlanViewModel` include `#if MAUI_UI` guards around `MainThread.BeginInvokeOnMainThread(...)`. Targeting `net9.0` (not `net9.0-ios` or `net9.0-maccatalyst`) means these guards compile to the non-MAUI branch, which calls handlers directly — perfect for synchronous test assertions.

**Confirmed**: The existing `RestaurantPos.Client.Maui.Tests.csproj` already uses `<TargetFramework>net9.0</TargetFramework>` successfully. Same will apply.

---

## 7. Admin ViewModel Dependencies

### Decision: `LayoutAdminViewModel` requires `ITerminalLayoutService` — provide a fake

**Rationale**: The Admin simulation must drive `LayoutAdminViewModel`. It requires `ITerminalLayoutService`, which returns `TerminalLayoutProfile` objects. A minimal fake can return a default profile on `GetActiveProfileAsync` and record calls to `SaveProfileAsync`.

---

## 8. Edge Case — PIN Lockout

### Decision: Implement 5-attempt lockout in `FakeOperatorAuthenticationService`

**Rationale**: The spec edge case "What happens when an operator enters an incorrect PIN 5 times" requires a lockout assertion. The `FakeOperatorAuthenticationService` will track failed attempt counts and return a lockout error after 5 failures. This does not require any production code changes.

---

## Summary of Resolved NEEDS CLARIFICATION

All items from spec were pre-resolved. No outstanding clarifications.

| Item | Resolution |
|---|---|
| Test project naming | `RestaurantPos.Client.Maui.ProfileSimulations` |
| ViewModel instantiation method | Constructor injection, no DI container |
| PIN validation mechanism | `FakeOperatorAuthenticationService` implementing `IOperatorAuthenticationService` |
| MAUI platform dependency | Target `net9.0`; `#if MAUI_UI` guards compile out automatically |
| Mock vs Fake strategy | Hand-written Fakes for integration simulations; Moq for simple required deps |
