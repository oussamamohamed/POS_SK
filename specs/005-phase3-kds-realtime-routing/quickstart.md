# Quickstart & Verification Guide: Phase 3 - Kitchen Display System (KDS) & Real-Time Routing

**Feature**: `005-phase3-kds-realtime-routing`
**Date**: 2026-08-15
**Status**: Ready

## 1. Prerequisites

- **.NET 9 SDK** installed (`dotnet --version` $\ge$ `9.0.100`)

---

## 2. Build Verification

```powershell
dotnet build RestaurantPos.slnx -c Release /p:TreatWarningsAsErrors=true
```

---

## 3. Automated Verification Scenarios

### Scenario A: Real-Time Order Splitting & Station Routing
Verify that an order with drinks, hot dishes, and desserts splits into separate station tickets (Bar, HotKitchen, Pastry).

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.KitchenRoutingTests"
```

### Scenario B: SignalR KitchenHub Streaming & Broadcast
Verify that `KitchenHub` broadcasts ticket lifecycle events to correct station subscriber groups in $< 200\text{ms}$.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.KitchenHubTests"
```

### Scenario C: KDS Ticket Bump & Recall Transitions
Verify that bumping a ticket moves it from Pending $\to$ InPreparation $\to$ Ready $\to$ Served, and recall restores the previous state.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.KdsViewModelTests"
```
