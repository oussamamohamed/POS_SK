# Implementation Plan: Happy Hour Multi-Select Configuration (Articles & Familles)

**Branch**: `020-happy-hour-multiselect` | **Date**: 2026-09-13 | **Spec**: [spec.md](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/020-happy-hour-multiselect/spec.md)

**Input**: Feature specification from `specs/020-happy-hour-multiselect/spec.md`

## Summary

Cette fonctionnalité enrichit le module Happy Hour (Feature 019) avec une interface graphique d'administration tactile ergonomique permettant :
1. La sélection groupée de familles entières (catégories) pour leur attribuer en masse une remise en pourcentage.
2. La sélection multiple d'articles spécifiques (via cases à cocher / tags tactiles, filtre de catégorie et recherche instantanée) pour leur affecter en un clic soit un prix fixe commun en euros, soit une remise en pourcentage commune.
3. Des endpoints API batch atomiques (`POST /api/happy-hour/schedules/{id}/rules/batch` et `DELETE /api/happy-hour/schedules/{id}/rules/batch`) évitant les allers-retours réseau un par un et garantissant la réactivité tactile.

## Technical Context

**Language/Version**: C# 13 / .NET 9, Vanilla JavaScript (ES2022) / CSS3  
**Primary Dependencies**: ASP.NET Core, EF Core SQLite / PostgreSQL, SignalR Core, FluentAssertions, Playwright E2E  
**Storage**: SQLite (`AppDbContext`, `LocalAppDbContext`), tables `HappyHourSchedules` et `HappyHourPriceRules`  
**Testing**: xUnit (`tests/RestaurantPos.Infrastructure.Tests`), Playwright E2E (`tests/RestaurantPos.Web.E2ETests`)  
**Target Platform**: POS tactile Web / iPadOS Safari & WebKit, Kiosk POS  
**Project Type**: Web Application + ASP.NET Core Web API  
**Performance Goals**: Temps de réponse de l'enregistrement groupé < 300 ms, sélection tactile fluide (60 fps, touches min 48x48px)  
**Constraints**: Zero rounding drift (utilisation stricte du value object `Money`), diffusion temps réel SignalR `HappyHourStatusChanged`  
**Scale/Scope**: Prise en charge jusqu'à 500 articles et 50 catégories sélectionnables sans latence  

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [X] **I. Touch-First Ergonomics**: Cibles tactiles d'au moins 48x48px (tuiles catégories et boutons de sélection avec padding confortable, pas de popup virtuel bloquant).
- [X] **II. Clean Architecture**: DTOs placés dans `RestaurantPos.Application`, logique métier dans `IHappyHourPricingService` / `HappyHourPricingService`, endpoints minimaux dans `RestaurantPos.Api`.
- [X] **III. Transactional Integrity**: Opérations de création/mise à jour groupée exécutées dans une transaction atomique EF Core.
- [X] **IV. Real-Time Sync**: Broadcast SignalR lors de la modification des règles pour actualisation immédiate des caisses actives.
- [X] **V. Test-First & Immutability**: `Money.FromEuros()` pour les prix fixes, validation par tests unitaires xUnit et tests Playwright E2E.

## Project Structure

### Documentation (this feature)

```text
specs/020-happy-hour-multiselect/
├── plan.md              # Ce fichier (/speckit-plan)
├── research.md          # Phase 0 : Décisions architecturales et UX tactile
├── data-model.md        # Phase 1 : DTOs batch et structure des requêtes
├── quickstart.md        # Phase 1 : Guide de validation rapide
├── contracts/           # Phase 1 : Contrats d'API batch
│   └── happy-hour-batch-api.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── RestaurantPos.Domain/
│   └── Entities/
│       ├── HappyHourSchedule.cs
│       └── HappyHourPriceRule.cs
├── RestaurantPos.Application/
│   ├── Common/Interfaces/
│   │   └── IHappyHourPricingService.cs
│   └── DTOs/
│       └── HappyHourDtos.cs                 # Ajout BatchPriceRulesRequestDto, BatchDeleteRulesRequestDto
├── RestaurantPos.Infrastructure/
│   └── Services/
│       └── HappyHourPricingService.cs       # Méthodes ApplyBatchPriceRulesAsync, DeleteBatchPriceRulesAsync
├── RestaurantPos.Api/
│   ├── Endpoints/
│   │   └── HappyHourEndpoints.cs            # Endpoints POST/DELETE /api/happy-hour/schedules/{id}/rules/batch
│   └── wwwroot/
│       ├── index.html                       # Refonte onglet #tabHappyHour (2 sous-onglets : Familles & Articles)
│       ├── styles.css                       # Styles de la grille de sélection tactile et des badges
│       └── app.js                           # Logique de sélection multiple, filtres et requêtes batch
tests/
├── RestaurantPos.Infrastructure.Tests/
│   └── HappyHourPricingServiceTests.cs      # Tests unitaires batch rules
└── RestaurantPos.Web.E2ETests/
    └── tests/
        └── happy-hour-multiselect.spec.ts   # Tests E2E Playwright de sélection en masse
```

**Structure Decision**: Extension naturelle de l'architecture existante avec zéro couplage superflu.
