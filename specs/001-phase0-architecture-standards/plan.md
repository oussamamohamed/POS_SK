# Implementation Plan: Phase 0 - Architectural Framing, Offline Resilience & Technical Standards

**Branch**: `001-phase0-architecture-standards` | **Date**: 2026-08-15 | **Spec**: [specs/001-phase0-architecture-standards/spec.md](spec.md)

**Input**: Feature specification from `specs/001-phase0-architecture-standards/spec.md`

## Summary

Establish the foundational normative framework, shared Clean Architecture structure, offline-first resilience mechanisms, Apple/iOS sandboxing and network constraints, NF525 cryptographic audit ledger, and automated quality gates for the Restaurant POS system. The solution unites a centralized .NET 9 ASP.NET Core backend with an ergonomic, touch-first .NET MAUI iPadOS client sharing common domain and application libraries.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (targeting `net9.0` and `net9.0-ios17.0+`)

**Primary Dependencies**: 
- Shared: `MediatR` (12.x), `FluentValidation` (11.x)
- Central Backend: `ASP.NET Core Web API`, `Microsoft.AspNetCore.SignalR`, `Npgsql.EntityFrameworkCore.PostgreSQL`
- Tactile Client: `.NET MAUI`, `CommunityToolkit.Mvvm` (8.x), `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.Maui.Graphics`

**Storage**: 
- Edge POS Terminals: Embedded SQLite located in `FileSystem.AppDataDirectory` (iOS Sandbox)
- Central Backend: PostgreSQL 16+ (or SQL Server) for multi-station master data and reporting

**Testing**: `xUnit`, `FluentAssertions`, `Moq`, `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`)

**Target Platform**: iPadOS 17+ & iOS 17+ (Front-of-House iPad tablets, mobile handhelds, KDS), Linux / Windows Server (Central Backend)

**Project Type**: Multi-tier Enterprise POS System (Tactile .NET MAUI Client + Central ASP.NET Core Web API & SignalR Server)

**Performance Goals**:
- Touch input visual/haptic acknowledgment: $< 50\text{ms}$
- Local-First offline transaction commit: $< 10\text{ms}$
- Outbox synchronization of 100 buffered orders: $< 2\text{s}$
- Real-time multi-terminal state broadcast (SignalR): $< 200\text{ms}$
- ESC/POS thermal ticket dispatch: $< 500\text{ms}$

**Constraints**:
- 100% operational autonomy offline (Local-First pattern with immutable append-only journal)
- Strict compliance with NF525 fiscal auditability (SHA-256 chained ledger, JET log, terminal-prefixed monotonic numbering)
- Zero system keyboard popups during ordering (integrated on-screen custom keypad)
- Zero compiler warnings (`TreatWarningsAsErrors=true`) and strict static analysis

**Scale/Scope**:
- Support up to 50 concurrent POS/KDS terminals per restaurant location
- Support catalog scale of 10,000+ items with complex modifier trees and recipes
- Buffer up to 50,000 offline transactions locally without performance degradation

## Constitution Check

*GATE: Evaluated and Passed across all 5 Core Principles.*

| Principle | Status | Compliance Verification |
| :--- | :--- | :--- |
| **I. Touch-First Ergonomics & iPadOS Tactile Design** | **PASS** | Cibles tactiles $\ge 54\times 54\text{ pt}$ (et $68\times 68\text{ pt}$ pour articles coup de feu), pavé numérique personnalisé fixe sans popup clavier système, gestuelle tactile native (swipe, haptic touch, pinch-to-zoom). |
| **II. Clean Architecture & Centralized Multi-POS Backend** | **PASS** | Projets partagés `RestaurantPos.Domain` et `RestaurantPos.Application` compilés pour .NET 9 et MAUI iOS, médiation CQRS MediatR, MVVM via `CommunityToolkit.Mvvm`. |
| **III. Transactional Integrity and Offline-First Operations** | **PASS** | Journal local *append-only* en SQLite sandbox avant tout dispatch, identifiants décentralisés chronologiques UUIDv7, Outbox pattern avec fusion additive et alertes. |
| **IV. Hardware Driver Abstraction & Real-Time Sync** | **PASS** | Pilote ESC/POS sur socket TCP brut port 9100, impulsion tiroir RJ11 24V, découverte réseau mDNS/Bonjour et hub temps réel SignalR `TableHub`. |
| **V. Test-First, Immutability & Fiscal Traceability (NF525)** | **PASS** | Arithmétique monétaire stricte en centimes (`Money`), chaînage cryptographique SHA-256 inaltérable, Journal des Événements Techniques (JET), clôtures Z et suites de tests automatisées. |

## Project Structure

### Documentation (this feature)

```text
specs/001-phase0-architecture-standards/
├── plan.md              # Implementation plan (/speckit-plan output)
├── research.md          # Architectural decisions & pattern evaluations
├── data-model.md        # Entities, value objects & persistence schemas
├── quickstart.md        # Validation guide and verification scenarios
├── contracts/           # API, WebSocket, Fiscal & Hardware interface contracts
│   ├── outbox-sync-contract.md
│   ├── fiscal-audit-contract.md
│   └── hardware-drivers-contract.md
├── checklists/
│   └── requirements.md  # Specification quality validation checklist
└── spec.md              # Feature specification
```

### Source Code Architecture (Target Solution Layout)

```text
RestaurantPos.sln
├── src/
│   ├── RestaurantPos.Domain/              # Shared Domain: Entities, Value Objects (Money), Aggregates, Enums
│   │   ├── Common/                        # BaseEntity, ValueObject, UUIDv7 generator
│   │   ├── Entities/                      # TransactionJournalEntry, FiscalReceiptRecord, TechnicalEventLogEntry, TerminalProfile
│   │   └── ValueObjects/                  # Money, TaxBreakdownItem
│   │
│   ├── RestaurantPos.Application/         # Shared Application: CQRS Commands, Queries, Behaviors, DTOs
│   │   ├── Common/                        # Interfaces (IFiscalAuditService, IPrinterService, INetworkDiscoveryService)
│   │   ├── Features/                      # MediatR Handlers (Sync, Fiscal, Journaling)
│   │   └── Behaviors/                     # ValidationBehavior, LoggingBehavior
│   │
│   ├── RestaurantPos.Infrastructure/      # Shared Infrastructure: EF Core Multi-Provider, Sockets, Discovery
│   │   ├── Persistence/                   # AppDbContext (PostgreSQL / SQLite provider configurations)
│   │   ├── Fiscal/                        # NF525 SHA-256 Chaining Interceptor & JET Logger
│   │   ├── Hardware/                      # EscPosPrinterService (TCP Port 9100), CashDrawerService
│   │   └── Discovery/                     # MdnsNetworkDiscoveryService (Bonjour)
│   │
│   ├── RestaurantPos.Api/                 # Centralized Backend: ASP.NET Core 9 Web API & Real-time Hubs
│   │   ├── Controllers/                   # SyncController, FiscalController, ReportsController
│   │   └── Hubs/                          # TableHub (SignalR real-time dining room & KDS sync)
│   │
│   └── RestaurantPos.Client.Maui/         # Tactile Client: .NET MAUI for iPadOS & Touch Terminals
│       ├── Platforms/iOS/                 # Info.plist (NSLocalNetworkUsageDescription, orientation lock)
│       ├── Views/                         # Tactile Pages (PinLockPage, PosTerminalPage, FloorPlanPage, KDS)
│       ├── ViewModels/                    # MVVM ViewModels (CommunityToolkit.Mvvm)
│       ├── Controls/                      # Custom Touch Keypad, TouchTiles, 2D Canvas
│       └── Services/                      # LocalSyncWorkerService, LocalStorageService
│
└── tests/
    ├── RestaurantPos.Domain.Tests/        # Unit tests: Money arithmetic, Fiscal SHA-256 formulas
    ├── RestaurantPos.Application.Tests/   # Unit tests: CQRS command handlers & FluentValidation rules
    ├── RestaurantPos.Infrastructure.Tests/# Integration tests: EF Core SQLite/Postgres interceptors, ESC/POS socket
    └── RestaurantPos.IntegrationTests/    # End-to-End API & Outbox sync reconciliation tests
```

**Structure Decision**: A modular Clean Architecture solution sharing `Domain` and `Application` libraries across the centralized ASP.NET Core backend and the .NET MAUI iOS client, ensuring 100% logic consistency, zero code duplication, and strict compliance with our project constitution.

## Complexity Tracking

*No unjustified complexity violations. The multi-project Clean Architecture layout is required to support cross-compilation for both the central .NET 9 ASP.NET Core server and the .NET MAUI iOS iPad client.*
