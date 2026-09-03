# Implementation Plan: Phase 1 - Core Domain Entities, Fast PIN Authentication & Hybrid Persistence

**Branch**: `003-phase1-core-pin-persistence` | **Date**: 2026-08-15 | **Spec**: [specs/003-phase1-core-pin-persistence/spec.md](spec.md)

**Input**: Feature specification from `specs/003-phase1-core-pin-persistence/spec.md`

## Summary

Deliver the core domain model foundations, fast offline PIN authentication with salted cryptographic hashing, and Entity Framework Core multi-provider persistence for the Restaurant POS system. This phase establishes the shared C# entities (`User`, `Product`, `Category`, `TaxRate`), the `PinLockPage` tactile authentication workflow, and the local catalog caching pipeline.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0

**Primary Dependencies**:
- Shared Domain & App: `Microsoft.Extensions.DependencyInjection`, `MediatR`
- Persistence: `Microsoft.EntityFrameworkCore.Sqlite`, `Npgsql.EntityFrameworkCore.PostgreSQL`
- Cryptography: `System.Security.Cryptography` (SHA-256 + RNGCryptoServiceProvider)

**Storage**:
- Edge Terminals: Sandboxed SQLite database in `FileSystem.AppDataDirectory`
- Central Server: PostgreSQL database for multi-store master catalogs and user credentials

**Testing**: `xUnit`, `FluentAssertions`, `Moq`

**Target Platform**: Cross-platform (iOS, Android, Windows, Linux)

**Project Type**: Multi-tier Clean Architecture Core Libraries & Tactile Auth View

**Performance Goals**:
- Operator PIN authentication and role validation: $< 10\text{ms}$ locally
- Cold-start catalog cache load (1,000 items): $< 100\text{ms}$
- Background reference data delta synchronization: $< 500\text{ms}$

**Constraints**:
- 100% offline authentication without central server dependency
- Zero floating-point rounding errors on money and tax calculations
- Strict compilation with zero compiler warnings (`TreatWarningsAsErrors=true`)

## Constitution Check

*GATE: Evaluated and Passed across all Core Principles.*

| Principle | Status | Compliance Verification |
| :--- | :--- | :--- |
| **I. Touch-First Ergonomics & Tactile Design** | **PASS** | Pavé numérique fixe à l'écran sur `PinLockPage`, cibles $\ge 54\times 54\text{ pt}$, retours visuels et haptiques instantanés. |
| **II. Clean Architecture & Centralized Multi-POS Backend** | **PASS** | Entités `User`, `Product`, `Category`, `TaxRate` centralisées dans `RestaurantPos.Domain`, consommées par le backend et le client. |
| **III. Transactional Integrity and Offline-First Operations** | **PASS** | Authentification par hash salé en cache local SQLite permettant un fonctionnement 100% autonome en mode avion. |
| **IV. Hardware Driver Abstraction & Real-Time Sync** | **PASS** | Synchronisation asynchrone non-bloquante des référentiels du serveur maître vers le cache client. |
| **V. Test-First, Immutability & Fiscal Traceability** | **PASS** | Arithmétique monétaire exacte en centimes (`Money`), tests unitaires de hachage de PIN et validation de schéma EF Core. |

## Project Structure

### Documentation (this feature)

```text
specs/003-phase1-core-pin-persistence/
├── plan.md              # Implementation plan (/speckit-plan output)
├── research.md          # Cryptographic hashing & multi-provider decisions
├── data-model.md        # User, Product, Category, TaxRate schemas
├── quickstart.md        # Build & test verification guide
├── contracts/           # PIN auth & catalog sync contracts
│   ├── pin-authentication-contract.md
│   └── catalog-sync-contract.md
├── checklists/
│   └── requirements.md  # Specification quality validation checklist
└── spec.md              # Feature specification
```

### Source Code Architecture

```text
src/
├── RestaurantPos.Domain/
│   ├── Common/                        # BaseEntity, UuidV7
│   ├── Entities/                      # User, Product, Category, TaxRate, Order, OrderItem
│   └── ValueObjects/                  # Money, TaxBreakdownItem
│
├── RestaurantPos.Application/
│   └── Common/Interfaces/             # IOperatorAuthenticationService, ICatalogSyncService
│
├── RestaurantPos.Infrastructure/
│   ├── Persistence/                   # AppDbContext (PostgreSQL multi-provider)
│   └── Security/                      # OperatorAuthenticationService (Salted SHA-256)
│
└── RestaurantPos.Client.Maui/
    ├── Persistence/                   # LocalAppDbContext (SQLite)
    ├── ViewModels/                    # PinLockViewModel
    └── Views/                         # PinLockPage.xaml
```

## Complexity Tracking

*No unjustified complexity violations.*
