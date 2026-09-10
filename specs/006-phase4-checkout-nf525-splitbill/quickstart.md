# Quickstart & Verification Guide: Phase 4 - Multi-Payment, Split Bill & NF525 Fiscal Audit

**Feature**: `006-phase4-checkout-nf525-splitbill`
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

### Scenario A: Multi-Tender Payment & Real-Time Change Calculation
Verify multi-payment (Cash + Meal Voucher + Card) with exact change calculation in $< 500\text{ms}$.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.PaymentCheckoutTests"
```

### Scenario B: Tactile Split-Bill Remainder Cents Balancing
Verify that dividing 100.00 € by 3 yields 33.34 €, 33.33 €, 33.33 € with $\sum = 100.00\text{ \euro}$.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.SplitBillTests"
```

### Scenario C: NF525 Cryptographic Hash Chain & Audit Validation
Verify SHA-256 chain integrity, tampering detection, and daily Z-closure perpetual grand totals.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.NF525FiscalAuditTests"
```
