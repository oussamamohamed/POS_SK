# Quickstart & Verification Guide: Configurable POS Management

**Feature**: `007-pos-configuration-management`  
**Date**: 2026-08-30  
**Status**: Ready for Verification

---

## 1. Prerequisites & Environment Setup

Ensure the solution builds cleanly without warnings:

```powershell
dotnet build RestaurantPos.slnx
```

Run existing regression test suites:

```powershell
dotnet test
```

---

## 2. Validation Scenarios

### Scenario 1: Catalog & Article Creation (User Story 1)
1. **Action**: Initialize `IBackOfficeCatalogService`.
2. **Execute**:
   - Create category `"Pizzas Artisanales"` with `#E74C3C` and order `1`.
   - Create product `"Pizza Margherita"` at `12.50 €` with `10%` VAT bracket and station `HotKitchen`.
3. **Assert**:
   - Category exists and contains the article.
   - Adding `"Pizza Margherita"` to an active POS order calculates exact total of `12.50 €` with `1.14 €` VAT.

### Scenario 2: Staff Member Creation & PIN Authentication (User Story 2)
1. **Action**: Initialize `IStaffManagementService` and `IOperatorAuthenticationService`.
2. **Execute**:
   - Create staff member `"Amélie"` with role `Server` and PIN `"1357"`.
   - Attempt login with incorrect PIN `"0000"` $\to$ Fails.
   - Attempt login with PIN `"1357"` $\to$ Authenticates successfully in $<50\text{ms}$ with `UserRole.Server`.

### Scenario 3: Printer Configuration & Diagnostic Test Print (User Story 3)
1. **Action**: Initialize `IPrinterConfigurationService`.
2. **Execute**:
   - Register network printer `"Imprimante Bar"` at `192.168.1.180:9100` with role `PreparationStation.Bar` and drawer trigger enabled.
   - Trigger `SendTestPrintAsync()`.
3. **Assert**:
   - Valid ESC/POS byte sequence (`ESC @`, text diagnostic header, `ESC p 0 25 250`, `GS V 66 0`) is dispatched.

### Scenario 4: Terminal Layout & Quick-Keys Customization (User Story 4)
1. **Action**: Initialize `ITerminalLayoutService`.
2. **Execute**:
   - Create profile `"Bar Fast POS"` pinning top 4 beverage product IDs in `QuickKeyProductIds`.
   - Load sales terminal with this profile: verify quick keys are displayed and addable in 1 tap.

---

## 3. Automated PowerShell Verification

Execute the feature verification suite:

```powershell
powershell -File scripts/powershell/verify-phase5-configuration.ps1
```
