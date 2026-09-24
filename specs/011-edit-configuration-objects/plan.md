# Implementation Plan: Modification des Objets dans le Module de Configuration

**Branch**: `011-edit-configuration-objects` | **Date**: 2026-08-31 | **Spec**: [specs/011-edit-configuration-objects/spec.md](spec.md)

**Input**: Feature specification from `specs/011-edit-configuration-objects/spec.md`

---

## Summary

Permettre aux gérants et administrateurs d'éditer et mettre à jour directement depuis l'interface d'administration les entités du restaurant (Articles, Familles/Catégories, Collaborateurs/Codes PIN, Imprimantes Réseau) via des modales d'édition pré-remplies, des endpoints REST `PUT /api/...`, des mises à jour atomiques dans la base de données SQLite/EF Core, et une synchronisation instantanée du catalogue de vente et du terminal tactile de caisse.

---

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: ASP.NET Core Minimal APIs, Entity Framework Core 9.0, .NET MAUI / CommunityToolkit.Mvvm, Vanilla HTML5 / Modern CSS  
**Storage**: SQLite (`AppDbContext` / `pos_offline_cache.db`) via EF Core  
**Testing**: xUnit, FluentAssertions, Moq  
**Target Platform**: iPadOS 17+ / Windows 11 / Web Browser  
**Project Type**: Clean Architecture POS  
**Performance Goals**: Rendu de la modale d'édition < 50ms, enregistrement et mise à jour de la grille de caisse < 100ms  
**Constraints**: Traçabilité des prix sans rupture sur les commandes en cours, validation stricte des PIN (4-6 chiffres) et des adresses IPv4, unicité des identifiants  
**Scale/Scope**: Catalogues jusqu'à 1000 articles, 50 familles, 50 employés, 10 imprimantes  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principe Constitutionnel | Statut | Justification / Alignement |
|---|---|---|
| **I. Touch-First Ergonomics & iPadOS** | ✅ Pass | Boutons d'édition `✏️` avec cibles tactiles confortables (≥ 54x54 pt), formulaires d'édition clairs avec sélecteurs de couleur et présélections |
| **II. Clean Architecture & Centralized Backend** | ✅ Pass | Méthodes `UpdateProductAsync`, `UpdateCategoryAsync`, `UpdateStaffAsync`, `UpdatePrinterAsync` dans les interfaces de services d'application |
| **III. Transactional Integrity & Offline-First** | ✅ Pass | Mises à jour d'entités avec EF Core `SaveChangesAsync`, gestion de la concurrence et persistance locale |
| **IV. Real-Time Sync & Hardware Abstraction** | ✅ Pass | Réactualisation réactive du catalogue de caisse, des onglets et du pool d'impression |
| **V. Test-First & Quality Gates** | ✅ Pass | Tests unitaires sur les mises à jour et contrôles de validation de chaque type d'entité |

---

## Project Structure

### Documentation (this feature)

```text
specs/011-edit-configuration-objects/
├── plan.md              # Ce document
├── research.md          # Analyse des flux d'édition et des règles de validation
├── data-model.md        # Modèles d'entités et schémas de mise à jour (PUT)
├── quickstart.md        # Guide de test et scénarios de validation pas à pas
├── contracts/           # Schémas JSON et contrats TypeScript
│   ├── edit-entities-api.json
│   └── config-entities.ts
└── tasks.md             # Tâches d'implémentation ordonnancées (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── RestaurantPos.Domain/
│   └── Entities/ (Product, Category, User, PrinterConfiguration)
├── RestaurantPos.Application/
│   └── Common/Interfaces/ (ICatalogManagementService, IStaffManagementService, IPrinterRoutingService)
├── RestaurantPos.Infrastructure/
│   └── Services/ (CatalogManagementService, StaffManagementService, PrinterRoutingService)
├── RestaurantPos.Api/
    ├── Program.cs
    └── wwwroot/
        ├── index.html
        ├── styles.css
        └── app.js

tests/
├── RestaurantPos.Infrastructure.Tests/
│   └── ConfigurationUpdateTests.cs
└── RestaurantPos.Client.Maui.Tests/
    └── AdminViewModelTests.cs
```

**Structure Decision**: Utilisation de la Clean Architecture existante avec ajout des méthodes `Update...` dans les services de l'Infrastructure et des points d'accès `PUT` dans `Program.cs`.

---

## Complexity Tracking

*Aucune dérogation constitutionnelle requise.*
