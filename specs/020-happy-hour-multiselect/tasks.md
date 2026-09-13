# Tasks: Happy Hour Multi-Select Configuration (Articles & Familles)

**Input**: Design documents from `/specs/020-happy-hour-multiselect/`  
**Prerequisites**: [`plan.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/020-happy-hour-multiselect/plan.md), [`spec.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/020-happy-hour-multiselect/spec.md), [`research.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/020-happy-hour-multiselect/research.md), [`data-model.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/020-happy-hour-multiselect/data-model.md), [`contracts/happy-hour-batch-api.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/020-happy-hour-multiselect/contracts/happy-hour-batch-api.md)

---

## Phase 1: Setup & DTOs

**Purpose**: Data Transfer Objects pour le transport groupé des règles

- [x] T001 [P] Create batch DTOs (`BatchPriceRulesRequestDto`, `BatchPriceRulesResponseDto`, `BatchDeleteRulesRequestDto`, `BatchDeleteRulesResponseDto`) in `src/RestaurantPos.Application/DTOs/HappyHourDtos.cs`

---

## Phase 2: Foundational (Service Contract, Core Engine & Batch Endpoints)

**Purpose**: Méthodes du moteur de tarification pour traiter les opérations par lot et endpoints API correspondants

- [x] T002 Add `ApplyBatchPriceRulesAsync` and `DeleteBatchPriceRulesAsync` method contracts to `IHappyHourPricingService` in `src/RestaurantPos.Application/Common/Interfaces/IHappyHourPricingService.cs`
- [x] T003 Implement `ApplyBatchPriceRulesAsync` (upsert atomique, conversion Money en cents, diffusion SignalR) and `DeleteBatchPriceRulesAsync` in `src/RestaurantPos.Infrastructure/Services/HappyHourPricingService.cs`
- [x] T004 Map batch endpoints `POST /api/happy-hour/schedules/{scheduleId}/rules/batch` and `DELETE /api/happy-hour/schedules/{scheduleId}/rules/batch` in `src/RestaurantPos.Api/Endpoints/HappyHourEndpoints.cs`
- [x] T005 [P] Add unit tests for batch rule upsert, category percentage validation, and batch deletion in `tests/RestaurantPos.Infrastructure.Tests/HappyHourPricingServiceTests.cs`

---

## Phase 3: User Story 1 - Sélection et Application Groupée par Famille / Catégorie (Priority: P1) 🎯 MVP

**Goal**: Permettre au gérant de sélectionner en masse des familles de produits (catégories) et d'appliquer une remise en % commune en un clic

**Independent Test**: Ouvrir l'onglet "🏷️ Familles éligibles", cocher 2 familles (ex: "Boissons" et "Desserts"), saisir `20%`, cliquer sur "Appliquer au lot", et vérifier la création des règles correspondantes.

- [x] T006 [P] [US1] Add HTML markup for "🏷️ Familles éligibles" sub-tab (categories multi-select grid, checkbox chips, batch discount input, and select all button) in `src/RestaurantPos.Api/wwwroot/index.html`
- [x] T007 [P] [US1] Add CSS styles for category multi-select cards, selection badges, and batch action toolbar in `src/RestaurantPos.Api/wwwroot/styles.css`
- [x] T008 [US1] Implement category multi-select state handling, select/deselect all, and batch apply API integration in `src/RestaurantPos.Api/wwwroot/app.js`

---

## Phase 4: User Story 2 - Sélection Tactile Multiple d'Articles avec Prix Fixe ou Remise (Priority: P1)

**Goal**: Permettre de sélectionner plusieurs articles simultanément (avec filtres de catégorie et recherche rapide) et d'appliquer en un clic un prix fixe ou une remise en %

**Independent Test**: Ouvrir l'onglet "🍺 Articles spécifiques", filtrer par une catégorie, cocher 3 articles, sélectionner le mode "Prix Fixe", saisir `5.00 €`, valider, et vérifier l'affichage des 3 articles avec leur tarif réduit.

- [x] T009 [P] [US2] Add HTML markup for "🍺 Articles spécifiques" sub-tab (category pills, search bar, product checkbox grid, price mode toggle buttons, and batch action footer) in `src/RestaurantPos.Api/wwwroot/index.html`
- [x] T010 [P] [US2] Add CSS styling for product multi-select grid, selection indicator badges, and price mode toggles in `src/RestaurantPos.Api/wwwroot/styles.css`
- [x] T011 [US2] Implement product search filtering, multi-selection state, price mode toggle (FixedPrice vs DiscountPercent), and batch save API integration in `src/RestaurantPos.Api/wwwroot/app.js`

---

## Phase 5: User Story 3 - Vue Détaillée et Gestion Collective des Règles Actives (Priority: P2)

**Goal**: Afficher séparément les règles familles et articles, et permettre la suppression unitaire ou par lot directement depuis l'écran tactile

**Independent Test**: Cocher 2 règles d'articles dans la liste récapitulative, cliquer sur "Supprimer la sélection", et vérifier leur retrait immédiat en base et dans l'interface.

- [x] T012 [P] [US3] Update active rules display markup in `index.html` with separate Familles and Articles sections and batch delete checkboxes in `src/RestaurantPos.Api/wwwroot/index.html`
- [x] T013 [US3] Implement batch delete action handler calling `DELETE /api/happy-hour/schedules/{scheduleId}/rules/batch` and re-render rules in `src/RestaurantPos.Api/wwwroot/app.js`

---

## Phase 6: Polish & E2E Validation

**Purpose**: Validation de bout en bout et tests de non-régression

- [x] T014 [P] Create Playwright E2E test suite covering category batch discount, product multi-select pricing, and batch rule deletion in `tests/RestaurantPos.Web.E2ETests/tests/happy-hour-multiselect.spec.ts`
- [x] T015 Run full test suite (.NET xUnit + Playwright E2E) to verify zero regressions across all checkout and Happy Hour tests

---

## Dependencies & Completion Order

```text
Phase 1: Setup DTOs (T001)
       │
       ▼
Phase 2: Foundational Engine & Batch API (T002-T005)
       │
       ├─────────────────────────┐
       ▼                         ▼
Phase 3: US1 Familles (P1)   Phase 4: US2 Articles (P1)
 (T006-T008)                  (T009-T011)
       │                         │
       └───────────┬─────────────┘
                   ▼
Phase 5: US3 Active Rules & Batch Delete (P2)
 (T012-T013)
                   │
                   ▼
Phase 6: Polish & E2E Validation (T014-T015)
```

---

## Parallel Opportunities

- **T006 & T007 & T009 & T010**: Les ajouts HTML et CSS pour les sous-onglets Familles et Articles peuvent être réalisés en parallèle.
- **T001 & T005**: La création des DTOs et l'écriture des tests unitaires peuvent s'exécuter en parallèle une fois les contrats définis.
- **US1 & US2**: Les deux interfaces (Familles et Articles) partagent le même endpoint backend batch (`POST .../rules/batch`) et peuvent être développées indépendamment.

---

## Implementation Strategy

1. **MVP First (Phases 1, 2, 3)**: Obtenir immédiatement le backend batch opérationnel et l'interface de sélection groupée de familles (catégories) avec remise en %.
2. **Expansion Articles (Phase 4)**: Ajouter la grille tactile d'articles multi-sélection avec recherche et double mode (prix fixe / remise %).
3. **Gestion & Suppression (Phase 5)**: Permettre le nettoyage et la suppression groupée des règles.
4. **Validation E2E (Phase 6)**: Automatiser les scénarios Playwright pour garantir la robustesse tactile.
