# Implementation Plan: UI Profile Simulation Tests

**Branch**: `015-ui-profile-simulation-tests` | **Date**: 2026-09-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/015-ui-profile-simulation-tests/spec.md`

## Summary

Create a dedicated xUnit test project (`RestaurantPos.Client.Maui.ProfileSimulations`) containing five simulation test classes — one per RBAC role: Waiter, Cashier, KitchenStaff, FloorManager, Admin. Each simulation exercises the role's full ViewModel chain using constructor-injected Moq/Fake test doubles in place of all external services. No production code is modified. No UI automation (Appium) is involved; all verification is at the ViewModel layer.

## Technical Context

**Language/Version**: C# 13 / .NET 9

**Primary Dependencies**:
- `CommunityToolkit.Mvvm` — already used by all ViewModels
- `xunit` 2.9.3 + `xunit.runner.visualstudio` 3.0.2 — existing test runner
- `FluentAssertions` 8.0.1 — existing assertion library
- `Moq` 4.20.72 — existing mocking framework

**Storage**: None — in-memory list-based fakes only

**Testing**: xUnit (same runner as `RestaurantPos.Client.Maui.Tests`)

**Target Platform**: `net9.0` (non-MAUI; ViewModel layer only, no `MAUI_UI` preprocessor)

**Project Type**: xUnit test library (.NET 9)

**Performance Goals**: Each simulation must complete in < 5 seconds (SC-002)

**Constraints**:
- Zero modifications to production source code
- No DI container in test project
- No network or hardware access
- Simulations must be independent (no shared mutable state)

**Scale/Scope**: 5 simulation classes, ~5 test methods each, ~25 total assertions minimum

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|---|---|---|
| I. Touch-First Ergonomics | ✅ PASS | ViewModel layer only; no UI rendering involved |
| II. Clean Architecture | ✅ PASS | New test project takes project references to Domain, Application, Client.Maui — no layer boundary violations |
| III. Transactional Integrity / Offline-First | ✅ PASS | Fakes operate in-memory; no real SQLite or journal writes |
| IV. Hardware Abstraction | ✅ PASS | `IPlatformEnvironmentService`, `IPrinterConfigurationService` fully mocked; no raw TCP or mDNS |
| V. Test-First / Fiscal Traceability | ✅ PASS | Feature IS a test project; no fiscal calculations in scope |

All gates pass. No complexity violations to justify.

## Project Structure

### Documentation (this feature)

```text
specs/015-ui-profile-simulation-tests/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── fake-services.md # Phase 1 output — fake service contracts
└── tasks.md             # Phase 2 output (/speckit-tasks command)
```

### Source Code (repository root)

```text
tests/
├── RestaurantPos.Client.Maui.Tests/        # Existing unit tests (unchanged)
└── RestaurantPos.Client.Maui.ProfileSimulations/   # NEW project
    ├── RestaurantPos.Client.Maui.ProfileSimulations.csproj
    ├── Fakes/
    │   ├── FakePlatformEnvironmentService.cs
    │   ├── FakeOperatorAuthenticationService.cs
    │   ├── FakeStaffManagementService.cs
    │   ├── FakeCheckoutPaymentService.cs
    │   ├── FakeBackOfficeCatalogService.cs
    │   └── FakePrinterConfigurationService.cs
    ├── Helpers/
    │   └── SimulatedOperator.cs
    └── Simulations/
        ├── WaiterProfileSimulation.cs
        ├── CashierProfileSimulation.cs
        ├── KitchenStaffProfileSimulation.cs
        ├── FloorManagerProfileSimulation.cs
        └── AdminProfileSimulation.cs
```

**Structure Decision**: Single new test project under `tests/`, mirroring the existing `RestaurantPos.Client.Maui.Tests` project pattern. The `Fakes/` directory holds hand-written deterministic fakes (preferred over Moq for profile-level integration simulations to improve readability). The `Simulations/` directory holds the five test classes.
