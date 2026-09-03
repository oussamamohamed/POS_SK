# Implementation Plan: Règles de Vidage du Panier et Réinitialisation post-Envoi Cuisine

**Branch**: `009-table-cart-rules` | **Date**: 2026-08-30 | **Spec**: [specs/009-table-cart-rules/spec.md](spec.md)

**Input**: Feature specification from `specs/009-table-cart-rules/spec.md`

---

## Summary

Implémenter la logique métier et les comportements d'interface pour le vidage sélectif du panier de table (suppression exclusive des articles en attente d'envoi `isDispatched == false` tout en protégeant les articles déjà envoyés `isDispatched == true`) ainsi que la réinitialisation automatique du panier et la transition vers la vue Plan de Salle lors de la validation de l'envoi en cuisine.

---

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: ASP.NET Core Minimal APIs, Entity Framework Core 9.0, .NET MAUI / CommunityToolkit.Mvvm, Vanilla HTML5 / Modern CSS  
**Storage**: SQLite local (`pos_offline_cache.db` / `AppDbContext`) via EF Core  
**Testing**: xUnit, FluentAssertions, Moq  
**Target Platform**: iPadOS 17+ / Windows 11 / Web Browser  
**Project Type**: Clean Architecture POS (.NET 9 Web API + Web Client + MAUI)  
**Performance Goals**: Vidage sélectif du panier < 10ms, transition post-envoi < 100ms sans saccade visuelle  
**Constraints**: Préservation absolue des articles en cuisine, recalcul exact des centimes HT/TVA/TTC via `Money`, notification explicite sans modale bloquante  
**Scale/Scope**: Paniers jusqu'à 50 lignes d'articles par table  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principe Constitutionnel | Statut | Justification / Alignement |
|---|---|---|
| **I. Touch-First Ergonomics & iPadOS** | ✅ Pass | Clic sur la corbeille réactif avec toast d'explication immédiat, bascule automatique vers le plan de salle sans étape superflue |
| **II. Clean Architecture & Centralized Backend** | ✅ Pass | Logique de filtrage implémentée dans `PosTerminalViewModel` et client web, API de dispatch cohérente |
| **III. Transactional Integrity & Offline-First** | ✅ Pass | Les articles déjà envoyés restent immuables en base ; seuls les ajouts temporaires non validés sont purgés |
| **IV. Real-Time Sync & Hardware Abstraction** | ✅ Pass | KDS et plan de salle rafraîchis en temps réel après chaque envoi cuisine |
| **V. Test-First & Quality Gates** | ✅ Pass | Tests unitaires ciblés sur le vidage sélectif et la transition de vue |

---

## Project Structure

### Documentation (this feature)

```text
specs/009-table-cart-rules/
├── plan.md              # Ce document
├── research.md          # Analyse des règles de panier et de transition
├── data-model.md        # Modèle d'état du panier et flags de dispatch
├── quickstart.md        # Guide de test et scénarios de validation pas à pas
├── contracts/           # Schémas JSON et interfaces TypeScript
│   ├── cart-rules-api.json
│   └── cart-state.ts
└── tasks.md             # Tâches d'implémentation ordonnancées (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── RestaurantPos.Domain/
│   └── Entities/Order.cs
├── RestaurantPos.Application/
│   └── Common/Interfaces/ITableManagementService.cs
├── RestaurantPos.Client.Maui/
│   └── ViewModels/PosTerminalViewModel.cs
└── RestaurantPos.Api/
    ├── Program.cs
    └── wwwroot/
        ├── app.js
        ├── styles.css
        └── index.html

tests/
├── RestaurantPos.Infrastructure.Tests/
│   └── TableManagementServiceTests.cs
└── RestaurantPos.Client.Maui.Tests/
    └── PosTerminalViewModelTests.cs
```

**Structure Decision**: La règle de vidage sélectif est appliquée au niveau du `PosTerminalViewModel` (MAUI) et du module `app.js` (Web Client), tandis que l'envoi cuisine déclenche la persistance des nouveaux articles, le dispatch KDS et la réinitialisation de session.

---

## Complexity Tracking

*Aucune dérogation constitutionnelle requise.*
