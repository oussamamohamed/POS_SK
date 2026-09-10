# Implementation Plan: Phase 3 - Kitchen Display System (KDS) & Real-Time Order Routing

**Branch**: `005-phase3-kds-realtime-routing` | **Date**: 2026-08-15 | **Spec**: [specs/005-phase3-kds-realtime-routing/spec.md](spec.md)

**Input**: Feature specification from `specs/005-phase3-kds-realtime-routing/spec.md`

## Summary

Deliver bidirectional real-time communication between POS order terminals and kitchen preparation areas: an ASP.NET Core SignalR hub (`KitchenHub`) streaming new tickets and state changes, a dedicated full-screen tactile KDS Kanban interface (`KdsPage`), and an intelligent station routing service (`KitchenRoutingService`) splitting dishes by preparation workstation (Bar, Hot Kitchen, Pastry).

## Technical Context

**Language/Version**: C# 13 / .NET 9.0

**Primary Dependencies**:
- Real-Time Communication: `Microsoft.AspNetCore.SignalR`, `Microsoft.AspNetCore.SignalR.Client`
- Core & Application: `CommunityToolkit.Mvvm`, `MediatR`
- Client UI: .NET MAUI (`Grid`, `Border`, `ScrollView`)

**Storage**:
- PostgreSQL / SQLite: `KitchenTickets`, `KitchenTicketItems`, `PreparationStations`

**Testing**: `xUnit`, `FluentAssertions`, `Moq`

**Target Platform**: Cross-platform (iOS, Android, Windows)

**Performance Goals**:
- Order transmission latency (POS dispatch $\to$ KDS display): $< 200\text{ms}$
- Ticket state bump transition: $< 50\text{ms}$
- Background state reconnection after network drop: $< 1\text{s}$

**Constraints**:
- Zero ticket loss during momentary Wi-Fi interruptions
- Real-time elapsed preparation timers updating every second without UI lag
- Zero compiler warnings (`TreatWarningsAsErrors=true`)

## Constitution Check

*GATE: Evaluated and Passed across all Core Principles.*

| Principle | Status | Compliance Verification |
| :--- | :--- | :--- |
| **I. Touch-First Ergonomics & Tactile Design** | **PASS** | Interface Kanban plein écran avec boutons de bump larges, minuteurs d'attente à code couleur (Vert/Ambre/Rouge). |
| **II. Clean Architecture & Centralized Multi-POS Backend** | **PASS** | `KitchenHub` hébergé dans l'API backend, logique de routage dans `Application`, vues dans `Client.Maui`. |
| **III. Transactional Integrity and Offline-First Operations** | **PASS** | Les bons de cuisine sont persistés de manière immutable avec ID chronologiques UUIDv7. |
| **IV. Hardware Driver Abstraction & Real-Time Sync** | **PASS** | WebSockets SignalR bidirectionnels avec souscription par groupe de poste (`Group_Station_HotKitchen`). |
| **V. Test-First, Immutability & Fiscal Traceability** | **PASS** | Tests unitaires sur le découpage des commandes par poste et les transitions de statuts. |

## Project Structure

### Documentation (this feature)

```text
specs/005-phase3-kds-realtime-routing/
├── plan.md              # Implementation plan (/speckit-plan output)
├── research.md          # SignalR grouping & timer heatmap decisions
├── data-model.md        # KitchenTicket, KitchenTicketItem, PreparationStation schemas
├── quickstart.md        # Build & test verification guide
├── contracts/           # SignalR KitchenHub & routing contracts
│   ├── kitchen-hub-contract.md
│   └── station-routing-contract.md
├── checklists/
│   └── requirements.md  # Specification quality validation checklist
└── spec.md              # Feature specification
```

### Source Code Architecture

```text
src/
├── RestaurantPos.Domain/
│   └── Entities/                      # KitchenTicket, KitchenTicketItem, PreparationStation
│
├── RestaurantPos.Application/
│   └── Common/Interfaces/             # IKitchenRoutingService, IKitchenHubClient
│
├── RestaurantPos.Infrastructure/
│   └── Services/                      # KitchenRoutingService
│
├── RestaurantPos.Api/
│   └── Hubs/                          # KitchenHub.cs
│
└── RestaurantPos.Client.Maui/
    ├── ViewModels/                    # KdsViewModel.cs
    └── Views/                         # KdsPage.xaml
```

## Complexity Tracking

*No unjustified complexity violations.*
