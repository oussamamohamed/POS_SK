# Implementation Plan: Grille Tactile Personnalisable avec Pagination & Interface Sans Ascenseur (POS Tablette)

**Branch**: `013-customizable-touch-grid` | **Date**: 2026-09-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/013-customizable-touch-grid/spec.md`

---

## Summary

Implémenter l'extension de la grille tactile d'articles personnalisable avec deux axes majeurs :
1. **Pagination Tactile Multi-Pages par Catégorie** :
   - Prise en charge d'un nombre illimité d'articles par catégorie via un découpage en pages matricielles fixes $4 \times 4$ (16 articles par page).
   - Navigation tactile instantanée au doigt via boutons Précédent `◀`, Suivant `▶`, pastilles `● ○ ○` et libellé `Page X / Y`.
   - Éditeur Back-Office avec gestion des pages (`Page 1`, `Page 2`, `➕ Ajouter une page`) et glisser-déposer inter-pages.
2. **Interface Globale 100% Ajustée au Viewport Sans Ascenseur (Zero-Scrollbar)** :
   - Verrouillage strict de tous les écrans du système POS (Vente, Plan de salle, Cuisine KDS, Clôture Z, Paramétrage, Modales) dans la hauteur d'écran (`100vh` / `100vw`).
   - Élimination intégrale des barres d'ascenseur du navigateur (`scrollbar-width: none; -ms-overflow-style: none; ::-webkit-scrollbar { display: none; }`).
   - Préservation du défilement tactile fluide invisible (inertial touch scroll) pour les listes internes débordantes (ex: panier de caisse).

---

## Technical Context

**Language/Version**: C# 13 / .NET 9  
**Primary Dependencies**: ASP.NET Core, EF Core 9, SignalR, .NET MAUI / Vanilla Web Client  
**Storage**: SQLite (Local-First embedded terminal cache) / PostgreSQL (serveur central multi-caisses)  
**Testing**: xUnit, FluentAssertions, `WebApplicationFactory` (API Integration tests)  
**Target Platform**: iPadOS 17+ (Apple iPad 10.9" / 11" / 13"), POS Web Client Touchscreen  
**Project Type**: Multi-tier Client-Server Clean Architecture  
**Performance Goals**: Rendu de grille $< 100\text{ ms}$, transition de page $< 50\text{ ms}$, retour tactile au tap $< 50\text{ ms}$, 0 barre d'ascenseur visible  
**Constraints**: Zero-downtime offline capability, NF525 fiscal compliance (prix calculé depuis l'entité `Product`)

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- [x] **I. Touch-First Ergonomics & iPadOS Tactile Design** : Tuiles carrées de taille $> 68 \times 68\text{ pt}$, commandes de pagination tactiles ergonomiques avec zones de frappe $> 48\text{ pt}$, zéro ascenseur visible sur aucun écran.
- [x] **II. Clean Architecture & Centralized Backend** : Découpage strict Domain (`GridLayout`, `GridSlot`), Application (DTOs, Services), Infrastructure (EF Core Repositories), Presentation (Web/MAUI).
- [x] **III. Transactional Integrity & Offline-First** : Persistance locale immédiate dans SQLite/LocalStorage avant émission réseau, UUIDv7 décentralisé.
- [x] **IV. Hardware Abstraction & Real-Time Sync** : SignalR WebSocket pour la diffusion des mutations de layout multi-pages.
- [x] **V. Test-First & Fiscal Traceability** : Le layout et la pagination n'altèrent pas les entités fiscales ni les calculs de TVA en centimes entiers.

---

## Project Structure

### Documentation (this feature)

```text
specs/013-customizable-touch-grid/
├── plan.md              # Ce document (Architecture & Plan d'exécution)
├── research.md          # Phase 0 : Décisions techniques (Pagination & Zero-Scrollbar)
├── data-model.md        # Phase 1 : Schéma entités, DTOs, règles de validation
├── contracts/           # Phase 1 : Schéma d'API OpenAPI
│   └── grid-management.openapi.json
├── quickstart.md        # Phase 1 : Scénarios de validation et guide de test
├── checklists/
│   └── requirements.md  # Checklist de conformité SpecKit
└── tasks.md             # Phase 2 : Tâches détaillées d'implémentation (généré via /speckit-tasks)
```

### Source Code Impact

```text
src/
├── RestaurantPos.Domain/
│   └── Entities/
│       └── HospitalityEntities.cs        # GridLayout (PageIndex, TotalPages) & GridSlot
├── RestaurantPos.Application/
│   ├── Common/Interfaces/
│   │   └── IGridManagementService.cs      # Méthodes GetLayoutByCategoryAsync(categoryId, pageIndex)
│   └── DTOs/
│       └── GridManagementDtos.cs          # DTOs enrichis avec PageIndex et TotalPages
├── RestaurantPos.Infrastructure/
│   ├── Persistence/
│   │   └── AppDbContext.cs               # Index composite (CategoryId, PageIndex)
│   └── Services/
│       └── GridManagementService.cs      # Logique de pagination et gestion multi-pages
├── RestaurantPos.Api/
│   ├── Program.cs                         # Endpoints /api/grid-layouts/{categoryId}?page={pageIndex}
│   └── wwwroot/
│       ├── index.html                     # Barre de pagination en caisse & éditeur multi-pages
│       ├── styles.css                     # Règles Zero-Scrollbar, 100vh lock, pagination tactiles
│       └── app.js                         # Gestion de state.activeGridPage, navigation et swipe
```

---

## Complexity Tracking

Aucune dérogation constitutionnelle requise. Le design réutilise les patrons existants de l'architecture Clean Architecture et Local-First.
