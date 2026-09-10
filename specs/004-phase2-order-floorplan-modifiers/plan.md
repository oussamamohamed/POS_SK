# Implementation Plan: Phase 2 - Tactile Order Entry, Interactive 2D Floor Plan & Modifiers

**Branch**: `004-phase2-order-floorplan-modifiers` | **Date**: 2026-08-15 | **Spec**: [specs/004-phase2-order-floorplan-modifiers/spec.md](spec.md)

**Input**: Feature specification from `specs/004-phase2-order-floorplan-modifiers/spec.md`

## Summary

Deliver the core touch-first order taking workflow: an interactive 2D floor plan with real-time table status color coding (`FloorPlanPage`), a high-speed tactile catalog grid with quick keys (`CatalogGrid`), and a pop-up modifier selection modal (`ModifiersModal`) for cooking temperatures and paid extra toppings.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0

**Primary Dependencies**:
- Core & Application: `CommunityToolkit.Mvvm`, `MediatR`
- Client UI: .NET MAUI (`AbsoluteLayout`, `Grid`, `DataTemplate`)
- Persistence: `Microsoft.EntityFrameworkCore.Sqlite`

**Storage**:
- Client Local SQLite: `DiningTables`, `ProductModifierGroups`, `ProductModifierOptions`

**Testing**: `xUnit`, `FluentAssertions`, `Moq`

**Target Platform**: Cross-platform (iOS/iPadOS, Android, Windows)

**Performance Goals**:
- Table selection and dining room transition: $< 100\text{ms}$
- Item addition from catalog to cart: $\le 2$ touch interactions
- In-memory cart recalculation & UI render: $< 16\text{ms}$ (60 FPS)

**Constraints**:
- 100% offline capability for table selection, catalog browsing, and modifier selections
- Strict arithmetic on money and modifier surcharges in integer cents (`Money`)
- Zero compiler warnings (`TreatWarningsAsErrors=true`)

## Constitution Check

*GATE: Evaluated and Passed across all Core Principles.*

| Principle | Status | Compliance Verification |
| :--- | :--- | :--- |
| **I. Touch-First Ergonomics & Tactile Design** | **PASS** | Cibles tactiles larges ($\ge 54\text{pt}$, onglets $\ge 80\text{px}$), retour haptique instantané, bascule de table en $< 100\text{ms}$. |
| **II. Clean Architecture & Centralized Multi-POS Backend** | **PASS** | Modèles `DiningTable`, `ProductModifierGroup` dans `Domain`, consommés par `Application` et `Client.Maui`. |
| **III. Transactional Integrity and Offline-First Operations** | **PASS** | Toutes les mutations de table et de panier sont calculées en mémoire et enregistrées dans le journal *append-only* local. |
| **IV. Hardware Driver Abstraction & Real-Time Sync** | **PASS** | Les statuts de table sont préparés pour la diffusion temps réel SignalR (`TableHub`). |
| **V. Test-First, Immutability & Fiscal Traceability** | **PASS** | Tests unitaires sur les calculs de suppléments, validation des choix obligatoires/optionnels et ventilation de TVA. |

## Project Structure

### Documentation (this feature)

```text
specs/004-phase2-order-floorplan-modifiers/
├── plan.md              # Implementation plan (/speckit-plan output)
├── research.md          # 2D layout, cart reactivity & modifier decisions
├── data-model.md        # DiningTable, ModifierGroup, ModifierOption schemas
├── quickstart.md        # Build & test verification guide
├── contracts/           # Floor plan & modifier validation contracts
│   ├── floor-plan-contract.md
│   └── order-modifiers-contract.md
├── checklists/
│   └── requirements.md  # Specification quality validation checklist
└── spec.md              # Feature specification
```

### Source Code Architecture

```text
src/
├── RestaurantPos.Domain/
│   ├── Entities/                      # DiningTable, ProductModifierGroup, ProductModifierOption
│   └── ValueObjects/                  # SelectedModifier, Money
│
├── RestaurantPos.Application/
│   └── Common/Interfaces/             # ITableManagementService, IModifierValidationService
│
└── RestaurantPos.Client.Maui/
    ├── Controls/                      # CatalogGrid.xaml, ModifiersModal.xaml
    ├── ViewModels/                    # FloorPlanViewModel, ModifiersViewModel, PosTerminalViewModel
    └── Views/                         # FloorPlanPage.xaml, PosTerminalPage.xaml
```

## Complexity Tracking

*No unjustified complexity violations.*
