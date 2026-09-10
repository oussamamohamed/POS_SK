# Quickstart & Verification Guide: Cross-Platform Tactile Frontend

**Feature**: `002-cross-platform-frontend`
**Date**: 2026-08-15
**Status**: Ready

## 1. Prerequisites

- **.NET 9 SDK** (`dotnet --version` $\ge$ `9.0.100`)
- **.NET MAUI Workloads**:
  ```powershell
  dotnet workload install maui-android maui-ios maui-windows
  ```
- **Android SDK & Emulator** (API 29+)
- **Windows App SDK / Windows Developer Mode** enabled

---

## 2. Multi-Target Build Verification

Verify that `RestaurantPos.Client.Maui` compiles cleanly across all three operating systems:

### Android Build
```powershell
dotnet build src/RestaurantPos.Client.Maui -f net9.0-android -c Release /p:TreatWarningsAsErrors=true
```

### iOS / iPadOS Build
```powershell
dotnet build src/RestaurantPos.Client.Maui -f net9.0-ios -c Release /p:TreatWarningsAsErrors=true
```

### Windows Build (WinUI 3)
```powershell
dotnet build src/RestaurantPos.Client.Maui -f net9.0-windows10.0.19041.0 -c Release /p:TreatWarningsAsErrors=true
```

**Expected Outcome**:
All 3 platform targets build successfully with 0 errors and 0 compiler warnings.

---

## 3. Automated Cross-Platform Test Execution

Run cross-platform unit and UI component tests:

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Client.Maui.Tests"
```

**Expected Outcome**:
All responsive breakpoint tests, numeric keypad touch input tests, and platform storage path resolver tests pass on all targets.
