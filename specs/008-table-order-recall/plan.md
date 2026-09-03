# Implementation Plan: Rappel & Restauration du Contenu de Table (Table Order Recall & Cart Hydration)

**Branch**: `008-table-order-recall` | **Date**: 2026-08-30 | **Spec**: [specs/008-table-order-recall/spec.md](spec.md)  
**Input**: Feature specification from `specs/008-table-order-recall/spec.md`  

---

## Summary

Permettre la synchronisation, la persistance et le rechargement réactif immédiat du contenu d'une table occupée (lignes de commande, quantités, modificateurs, statut d'envoi en cuisine, sous-total HT, TVA multi-taux et total TTC) lorsque l'opérateur sélectionne une table dans le plan de salle 2D, la liste des tables ou le terminal de vente.

---

## Technical Context

**Language/Version**: C# 13 / .NET 9.0 (MAUI + ASP.NET Core Minimal APIs + Vanilla JS)  
**Primary Dependencies**: EF Core 9.0 (SQLite Local-First + InMemory/PostgreSQL Master), `CommunityToolkit.Mvvm`, `Microsoft.AspNetCore.SignalR`  
**Storage**: Dual Persistence (Sandboxed SQLite `LocalAppDbContext` offline-first + Master `AppDbContext`)  
**Testing**: xUnit, FluentAssertions, Moq (`TreatWarningsAsErrors=true`)  
**Target Platform**: iPadOS / macOS / Android / Windows (Touch-Optimized) + Web Tactile Test App  
**Project Type**: Hybrid MAUI Client + Clean Architecture Core + Embedded Web POS  
**Performance Goals**: Restauration du panier en `< 50ms` sur rappel de table  
**Constraints**: Zero-rounding cents (`Money` struct), Traçabilité inaltérable NF525, Aucun doublon d'envoi en cuisine lors des ajouts incrémentaux  
**Scale/Scope**: Support de 50+ tables, commandes multi-lignes illimitées avec modificateurs imbriqués  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principe Constitutionnel | Exigence | Statut | Justification |
| :--- | :--- | :---: | :--- |
| **I. Clean Architecture** | Domaine pur, contrats Application, persistance Infrastructure | ✅ PASS | Les entités `Order`, `OrderLine`, `DiningTable` sont dans le Domaine ; les contrats dans `Application` |
| **II. Offline-First** | Fonctionnement sans connexion obligatoire au serveur | ✅ PASS | Hydratation depuis SQLite local (`LocalAppDbContext`) avec synchronisation Outbox |
| **III. Précision Monétaire** | Type `Money` en centimes entiers (`long AmountInCents`) | ✅ PASS | Tous les calculs de lignes et totaux utilisent la structure `Money` |
| **IV. Tactile & Ergonomie** | Cibles tactiles $\ge 48\times 48\,\text{dp}$, feedback haptique | ✅ PASS | Sélection 1-tap de table avec rechargement fluide et instantané |
| **V. NF525 & Fiscalité** | Intégrité inaltérable des encaissements | ✅ PASS | Les modifications en cours de table ne corrompent aucun reçu fiscal scellé |

---

## Project Structure

### Documentation (this feature)

```text
specs/008-table-order-recall/
├── spec.md              # Feature specification
├── plan.md              # This implementation plan
├── research.md          # Phase 0 research & technical decisions
├── data-model.md        # Phase 1 data model & state machine
├── quickstart.md        # Phase 1 verification and run guide
├── contracts/           # Phase 1 API and ViewModel contracts
│   ├── table-order-api.json
│   └── cart-hydration.ts
└── checklists/
    └── requirements.md  # Quality validation checklist
```

### Source Code Impact

```text
src/
├── RestaurantPos.Domain/
│   └── Entities/
│       ├── DiningTable.cs             # Table entity with ActiveOrderId & Covers
│       └── Order.cs                   # Order & OrderLine models with IsDispatched flag
├── RestaurantPos.Application/
│   └── Common/Interfaces/
│       ├── ITableManagementService.cs  # Extended with GetActiveOrderForTableAsync
│       └── IOrderManagementService.cs  # Extended with AddLinesToOrderAsync & DispatchLinesAsync
├── RestaurantPos.Infrastructure/
│   ├── Persistence/AppDbContext.cs
│   └── Services/
│       └── TableManagementService.cs  # Order retrieval & hydration implementation
├── RestaurantPos.Client.Maui/
│   ├── Persistence/LocalAppDbContext.cs
│   ├── ViewModels/
│   │   ├── PosTerminalViewModel.cs    # Table recall & cart hydration logic
│   │   └── FloorPlanViewModel.cs      # Selection trigger & navigation binding
│   └── Views/
└── RestaurantPos.Api/
    ├── Program.cs                     # API endpoints for /api/tables/{number}/order & /api/tables/{number}/items
    └── wwwroot/
        ├── app.js                     # Web client table recall & cart hydration
        └── index.html                 # Visual badges for dispatched vs new items

tests/
├── RestaurantPos.Domain.Tests/
├── RestaurantPos.Infrastructure.Tests/
│   └── TableManagementServiceTests.cs # Unit & integration tests for order recall
└── RestaurantPos.Client.Maui.Tests/
    └── PosTerminalViewModelTests.cs   # Unit tests for cart hydration from table order
```

---

## Complexity Tracking

*Aucune déviation ni complexité superflue introduite.*
