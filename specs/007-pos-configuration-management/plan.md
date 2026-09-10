# Implementation Plan: Configurable POS Management & Administration

**Branch**: `007-pos-configuration-management` | **Date**: 2026-08-30 | **Spec**: [specs/007-pos-configuration-management/spec.md](spec.md)

**Input**: Feature specification from `specs/007-pos-configuration-management/spec.md`

## Summary

Deliver a modular, tactile-first back-office and administration module allowing managers to configure menu categories/articles, staff operators with salted PINs, thermal network printers, and sales screen layout profiles. All configurations operate under a Local-First pattern, persisted in local sandboxed SQLite with immediate Outbox event replication across multi-terminal setups.

## Technical Context

**Language/Version**: C# 13 / .NET 9 (`net9.0`, `net9.0-ios`, `net9.0-android`, `net9.0-windows10.0.19041.0`)  
**Primary Dependencies**: `CommunityToolkit.Mvvm`, `Microsoft.EntityFrameworkCore`, `Microsoft.AspNetCore.SignalR`, `FluentValidation`, `MediatR`, `xUnit`, `FluentAssertions`, `Moq`  
**Storage**: Multi-provider EF Core (`AppDbContext` / `LocalAppDbContext` targeting SQLite in sandbox and PostgreSQL/InMemory on master)  
**Testing**: xUnit, FluentAssertions, Moq (`RestaurantPos.Domain.Tests`, `RestaurantPos.Infrastructure.Tests`, `RestaurantPos.Client.Maui.Tests`)  
**Target Platform**: iOS/iPadOS 17+, Android 12+, Windows 10/11 Desktop  
**Project Type**: Multi-project Clean Architecture solution (.NET 9 Web API + .NET MAUI tactile POS client + Shared Domain & Infrastructure)  
**Performance Goals**: Tactile administrative UI response $<50\text{ms}$, local catalog queries $<10\text{ms}$, cross-terminal layout sync $<200\text{ms}$  
**Constraints**: 100% offline-capable (Local-First), strict decimal cent math (`Money`), immutable audit trail (NF525), zero compiler warnings (`TreatWarningsAsErrors=true`)  
**Scale/Scope**: 1-10 concurrent POS/KDS terminals, 500-2000 articles, 50-100 categories, 1-20 staff operators, 1-5 network thermal printers  

## Constitution Check

*GATE: All principles validated and satisfied.*

- [X] **Principle I (Touch-First Ergonomics)**: Administration screens designed with large tactile buttons ($\ge 54\times 54\text{ pt}$), numeric keypads for PIN and price entry, and high-contrast color badges.
- [X] **Principle II (Clean Architecture)**: Entities in `Domain`, interfaces in `Application`, EF Core implementations and discovery in `Infrastructure`, UI/ViewModels in `Client.Maui`.
- [X] **Principle III (Offline-First Integrity)**: Configuration changes update local SQLite immediately and generate UUIDv7 Outbox synchronization records.
- [X] **Principle IV (Hardware Abstraction)**: Network printers declared with IP/port, ESC/POS formatting, and diagnostic test routines over raw TCP port 9100.
- [X] **Principle V (Fiscal Traceability & Immutability)**: Archiving or updating products never mutates or corrupts historical closed fiscal receipts.

## Project Structure

### Documentation (this feature)

```text
specs/007-pos-configuration-management/
├── spec.md              # Feature specification
├── plan.md              # This implementation plan
├── research.md          # Phase 0 architectural analysis
├── data-model.md        # Phase 1 domain entities & schemas
├── quickstart.md        # Phase 1 verification scenarios
├── contracts/           # Phase 1 service interfaces
│   ├── IBackOfficeCatalogService.cs
│   ├── IStaffManagementService.cs
│   └── IPrinterAndLayoutServices.cs
└── checklists/
    └── requirements.md  # Quality validation checklist
```

### Source Code Mapping

```text
src/
├── RestaurantPos.Domain/
│   └── Entities/
│       ├── ProductCategory.cs (Enhance with colors, icons, active flag)
│       ├── Product.cs (Enhance with station, quick-key flag, archive)
│       ├── PrinterConfiguration.cs (NEW)
│       └── TerminalLayoutProfile.cs (NEW)
├── RestaurantPos.Application/
│   └── Common/Interfaces/
│       ├── IBackOfficeCatalogService.cs (NEW)
│       ├── IStaffManagementService.cs (NEW)
│       ├── IPrinterConfigurationService.cs (NEW)
│       └── ITerminalLayoutService.cs (NEW)
├── RestaurantPos.Infrastructure/
│   ├── Persistence/
│   │   └── AppDbContext.cs (Map PrinterConfigurations & TerminalLayoutProfiles)
│   └── Services/
│       ├── BackOfficeCatalogService.cs (NEW)
│       ├── StaffManagementService.cs (NEW)
│       ├── PrinterConfigurationService.cs (NEW)
│       └── TerminalLayoutService.cs (NEW)
└── RestaurantPos.Client.Maui/
    ├── Persistence/
    │   └── LocalAppDbContext.cs (Map client SQLite configuration tables)
    ├── ViewModels/
    │   ├── CatalogAdminViewModel.cs (NEW)
    │   ├── StaffAdminViewModel.cs (NEW)
    │   ├── PrinterAdminViewModel.cs (NEW)
    │   └── LayoutAdminViewModel.cs (NEW)
    └── Views/
        └── AdminHubPage.xaml (NEW)

tests/
├── RestaurantPos.Domain.Tests/
│   └── ConfigurationEntityTests.cs (NEW)
├── RestaurantPos.Infrastructure.Tests/
│   ├── BackOfficeCatalogServiceTests.cs (NEW)
│   └── StaffManagementServiceTests.cs (NEW)
└── RestaurantPos.Client.Maui.Tests/
    └── ConfigurationViewModelsTests.cs (NEW)
```

## Complexity Tracking

> No violations of the Constitution. Standard Clean Architecture design adhered to across all layers.
