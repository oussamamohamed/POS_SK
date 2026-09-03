# Quickstart & Verification Guide: Phase 1 - Core Domain, Fast PIN Auth & Hybrid Persistence

**Feature**: `003-phase1-core-pin-persistence`
**Date**: 2026-08-15
**Status**: Ready

## 1. Prerequisites

- **.NET 9 SDK** installed (`dotnet --version` $\ge$ `9.0.100`)

---

## 2. Build Verification

Build the shared domain, application, and persistence packages:

```powershell
dotnet build RestaurantPos.slnx -c Release /p:TreatWarningsAsErrors=true
```

---

## 3. Automated Verification Scenarios

### Scenario A: Operator PIN Hashing & Offline Authentication
Verify that PIN validation with cryptographic salt executes in $< 10\text{ms}$ and correctly distinguishes valid vs invalid credentials.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.OperatorAuthTests"
```

### Scenario B: Shared Domain Tax & Decimal Cent Arithmetic
Verify that `Product`, `Category`, `TaxRate`, and `Money` calculate exact VAT splits (5.5%, 10%, 20%) with zero rounding error.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.DomainEntityTests"
```

### Scenario C: EF Core Multi-Provider Mapping & Local Database Creation
Verify that `AppDbContext` and `LocalAppDbContext` initialize schemas and perform CRUD operations seamlessly.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.PersistenceTests"
```
