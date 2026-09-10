# Quickstart: Validating Complex E2E Tests

This guide explains how to validate the end-to-end multi-actor test scenario once implemented.

## Prerequisites

- .NET 9 SDK installed
- The `RestaurantPos.slnx` solution available

## Run the E2E Lifecycle Test

The E2E test uses a unified `SharedFakeBackend` to seamlessly pass state between the different role profiles without a network hop.

1. Open a terminal in the solution root.
2. Run the specific E2E test to validate the Waiter -> Kitchen -> Cashier flow:

```bash
dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/RestaurantPos.Client.Maui.ProfileSimulations.csproj --filter "E2E_FullRestaurantLifecycle_ShouldSucceed"
```

### Expected Outcome

The test should output `Passed` within ~500ms. If it fails, the error message will pinpoint where the chain broke (e.g., "Expected KdsViewModel.PendingTickets to have 1 item, but found 0" implies the `FakeKitchenSignalRClient` failed to broadcast).

## Run the Advanced Hospitality Tests

Validate the fractional split logic and discount capabilities:

```bash
dotnet test tests/RestaurantPos.Client.Maui.ProfileSimulations/RestaurantPos.Client.Maui.ProfileSimulations.csproj --filter "HospitalityProfileSimulation"
```

### Expected Outcome

The tests should pass, confirming that:
- A 10.01 amount split 3 ways mathematically balances.
- Applying a global discount correctly modifies the ViewModel's `TotalTtc` property.
