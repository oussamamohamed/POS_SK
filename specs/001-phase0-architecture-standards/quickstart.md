# Quickstart & Verification Guide: Phase 0 - Architectural Framing & Technical Standards

**Feature**: `001-phase0-architecture-standards`
**Date**: 2026-08-15
**Status**: Ready

## 1. Prerequisites

- **.NET 9 SDK** installed (`dotnet --version` $\ge$ `9.0.100`)
- **.NET MAUI Workload** installed (`dotnet workload install maui-ios maui`)
- **Visual Studio 2022** (v17.12+) with .NET MAUI & Mobile Development workloads

---

## 2. Solution Structure & Build Verification

Run clean build across all shared, backend, and client projects to verify zero compiler warnings:

```powershell
dotnet clean
dotnet build -c Release /p:TreatWarningsAsErrors=true
```

**Expected Outcome**:
Build succeeds with 0 errors and 0 warnings across all targeted frameworks (`net9.0`, `net9.0-ios`).

---

## 3. Automated Verification Scenarios

### Scenario A: Offline Journaling & UUIDv7 Chronology
Verify that when network is disconnected, mutations are written to the local SQLite append-only journal in strict UUIDv7 order.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.OfflineJournalTests"
```
**Expected Outcome**:
All tests pass. Records are written locally in $< 10\text{ms}$ with valid incremental sequence numbers.

---

### Scenario B: Outbox Synchronization & Concurrent Conflict Alert
Verify that buffered offline messages synchronize with the central backend with additive table merging and on-screen alert notifications.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.SyncOutboxTests"
```
**Expected Outcome**:
100 offline messages reconcile in $< 1.5\text{s}$. Concurrent table modifications trigger additive merge and raise `ConcurrentModificationAlert`.

---

### Scenario C: NF525 Cryptographic Hash Chaining Integrity
Verify that SHA-256 block chaining calculates correct receipt signatures and that tampering with historical records triggers an immediate validation failure.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.FiscalChainingTests"
```
**Expected Outcome**:
10,000 chained receipts validate with 100% mathematical integrity. Tampered payloads fail verification immediately.

---

### Scenario D: ESC/POS Thermal Printing & mDNS Zero-Config Discovery
Verify raw TCP socket (port 9100) ESC/POS command generation and mDNS service discovery on the local network.

```powershell
dotnet test --filter "FullyQualifiedName~RestaurantPos.Tests.HardwareDriverTests"
```
**Expected Outcome**:
ESC/POS byte generator produces exact byte sequences for text, cutting (`GS V 66 0`), and RJ11 cash drawer pulse (`ESC p 0 25 250`).
