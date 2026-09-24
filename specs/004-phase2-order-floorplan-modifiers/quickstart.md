# Quickstart & Verification Guide: Phase 2 - Tactile Order Entry, 2D Floor Plan & Modifiers

**Feature**: `004-phase2-order-floorplan-modifiers`
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

### Scenario A: 2D Floor Plan Table State & Sub-100ms Transitions
Verify that opening a table, switching tables, and table status updates complete in $< 100\text{ms}$.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.FloorPlanTests"
```

### Scenario B: Rapid Catalog Navigation & Two-Tap Item Entry
Verify that items can be added to the cart in $\le 2$ taps and calculate exact taxes and totals.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.CatalogNavigationTests"
```

### Scenario C: Modifier Validation, Cooking Temperatures & Surcharges
Verify mandatory single-choice and optional multi-choice modifier selection with exact price additions.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.ModifierCalculationTests"
```
