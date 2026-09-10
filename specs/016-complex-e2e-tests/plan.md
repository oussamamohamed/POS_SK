# Implementation Plan: Complex E2E Tests

**Branch**: `016-complex-e2e-tests` | **Date**: 2026-09-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/016-complex-e2e-tests/spec.md`

## Summary

Expand the existing UI simulation test suite to support true multi-actor end-to-end testing by introducing a stateful `SharedFakeBackend` and `FakeKitchenSignalRClient`. Also, implement tests for advanced hospitality features like discounts, comping, and room billing.

## Technical Context

**Language/Version**: C# 13, .NET 9

**Primary Dependencies**: xUnit, FluentAssertions, Moq, `RestaurantPos.Client.Maui`

**Storage**: Pure In-Memory (No databases)

**Testing**: Headless ViewModel Simulation (xUnit)

**Target Platform**: .NET 9 Test Runner

**Project Type**: Test Suite (MAUI ViewModels)

**Performance Goals**: E2E test suite completes in under 2 seconds.

**Constraints**: Must run entirely without networking, requiring deterministic mocking of SignalR events.

**Scale/Scope**: 1 multi-stage E2E test, 3 advanced hospitality test cases.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Passes II. Clean Architecture**: Tests interact exclusively with Application layer contracts (`ITableManagementService`, `IKitchenSignalRClient`) and ViewModels.
- **Passes V. Fiscal Traceability**: Verifies exact decimal/cent accuracy on 3-way fractional split check.

## Project Structure

### Documentation (this feature)

```text
specs/016-complex-e2e-tests/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
└── tasks.md             # Phase 2 output (generated via /speckit-tasks)
```

### Source Code (repository root)

```text
tests/RestaurantPos.Client.Maui.ProfileSimulations/
├── Fakes/
│   ├── FakeKitchenSignalRClient.cs
│   ├── FakeHospitalityServices.cs
│   └── SharedFakeBackend.cs
└── Simulations/
    ├── HospitalityProfileSimulation.cs
    └── EndToEndRestaurantSimulation.cs
```

**Structure Decision**: Files will be added to the existing `RestaurantPos.Client.Maui.ProfileSimulations` xUnit project, preserving the separation between unit/integration tests and headless simulation tests.
