# Tasks: Grille Tactile Personnalisable avec Pagination & Interface Sans Ascenseur

**Feature**: `013-customizable-touch-grid`  
**Date**: 2026-09-03  
**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md)

---

## Phase 1: Setup & Data Foundation (Multi-Page Schema)

**Purpose**: Mise à niveau du modèle de données de domaine et mappings EF Core pour supporter la pagination multi-pages

- [X] T001 [P] Mettre à jour l'entité de domaine `GridLayout` avec `PageIndex` (défaut 0) dans `src/RestaurantPos.Domain/Entities/HospitalityEntities.cs`
- [X] T002 [P] Enrichir les DTOs `GridLayoutDto`, `UpdateGridLayoutRequest` avec `PageIndex` et `TotalPages` dans `src/RestaurantPos.Application/DTOs/GridManagementDtos.cs`
- [X] T003 Configurer l'index composite unique `(CategoryId, PageIndex)` sur `GridLayout` dans `src/RestaurantPos.Infrastructure/Persistence/AppDbContext.cs` et `src/RestaurantPos.Client.Maui/Persistence/LocalAppDbContext.cs`

---

## Phase 2: Foundational (Service Layer & Multi-Page API Endpoints)

**Purpose**: Core infrastructure bloquant toutes les User Stories

**⚠️ CRITICAL**: Aucun développement front-end ne commence avant la validation de cette phase

- [X] T004 Définir `GetLayoutByCategoryAsync(string categoryId, int pageIndex = 0)` et `GetAllPagesByCategoryAsync(string categoryId)` dans `src/RestaurantPos.Application/Common/Interfaces/IGridManagementService.cs`
- [X] T005 Implémenter la gestion multi-pages et l'auto-seeding par page dans `src/RestaurantPos.Infrastructure/Services/GridManagementService.cs`
- [X] T006 [P] Mapper les routes Minimal API `/api/grid-layouts/{categoryId}` (avec query param `page`) et `/api/grid-layouts/{categoryId}/pages` dans `src/RestaurantPos.Api/Program.cs`
- [X] T007 [P] Écrire et exécuter les tests unitaires du service multi-pages dans `tests/RestaurantPos.Infrastructure.Tests/GridManagementServiceTests.cs`

**Checkpoint**: Fondations multi-pages validées — les User Stories peuvent débuter.

---

## Phase 3: User Story 4 - Interface Globale 100% Ajustée au Viewport Sans Ascenseur (Priority: P1) 🎯 MVP

**Goal**: Éliminer toute barre de défilement (ascenseur) sur tous les écrans du système POS (Vente, Tables, KDS, Admin, Modales) avec verrouillage viewport 100vh.

**Independent Test**: Parcourir chaque écran de l'application et chaque modale : vérifier qu'aucune scrollbar n'apparaît et qu'aucun défilement vertical global n'est possible.

- [X] T008 [US4] Appliquer le verrouillage viewport global (`height: 100vh; overflow: hidden;`) sur `html`, `body` et les vues principales (`.main-content`, `.pos-view`, `.admin-layout`) dans `src/RestaurantPos.Api/wwwroot/styles.css`
- [X] T009 [P] [US4] Définir les règles de suppression globale des barres d'ascenseur (`scrollbar-width: none; ::-webkit-scrollbar { display: none; }`) sur tous les sélecteurs dans `src/RestaurantPos.Api/wwwroot/styles.css`
- [X] T010 [US4] Adapter la zone défilante interne du panier de vente (`.order-items-scroll`) et la liste KDS pour un défilement tactile fluide sans ascenseur visible dans `src/RestaurantPos.Api/wwwroot/styles.css`
- [X] T011 [US4] Ajuster les modales (`.modal-card`, encaissement, division, clôture Z, édition slot) pour s'adapter à la hauteur maximale du viewport sans débordement dans `src/RestaurantPos.Api/wwwroot/styles.css`

---

## Phase 4: User Story 1 - Saisie Tactile sur Grille Homogène Multi-Pages (Priority: P1) 🎯 MVP

**Goal**: Fournir une matrice carrée 1:1 $4 \times 4$ fixe avec pagination tactile par boutons `◀` / `▶` et pastilles `● ○` sans défilement vertical.

**Independent Test**: Sélectionner une catégorie dense, naviguer entre les pages 1 et 2 en moins de 50ms et ajouter un article de la page 2 au panier.

- [X] T012 [US1] Ajouter la barre de pagination tactile (`#gridPaginationBar`, `#btnPrevGridPage`, `#btnNextGridPage`, `#gridPageDots`, `#gridPageLabel`) sous la grille des articles dans `src/RestaurantPos.Api/wwwroot/index.html`
- [X] T013 [P] [US1] Styliser la barre de pagination tactile (boutons ergonomiques $\ge 48\text{px}$, pastilles actives) dans `src/RestaurantPos.Api/wwwroot/styles.css`
- [X] T014 [US1] Gérer l'état `state.activeGridPage` (avec réinitialisation automatique à la Page 1 lors du changement de catégorie) et implémenter `loadGridLayout(categoryId, pageIndex)` et `renderProductsGridPagination()` dans `src/RestaurantPos.Api/wwwroot/app.js`
- [X] T015 [US1] Connecter les écouteurs de clics et swipe horizontal pour la navigation de page (`goToGridPage`, `nextGridPage`, `prevGridPage`) dans `src/RestaurantPos.Api/wwwroot/app.js`

**Checkpoint**: MVP fonctionnel — grille carrée $4 \times 4$ paginée sans aucun ascenseur sur l'écran de vente.

---

## Phase 5: User Story 2 - Configuration Visuelle & Pagination dans l'Éditeur Back-Office (Priority: P2)

**Goal**: Permettre aux administrateurs de naviguer entre les pages d'une catégorie dans l'éditeur de grille, d'ajouter de nouvelles pages et d'organiser les articles par glisser-déposer.

**Independent Test**: Ouvrir Paramétrage > Disposition de l'Écran, changer de page d'édition, ajouter une page 2, assigner un article et vérifier sa présence en caisse sur la page 2.

- [X] T016 [US2] Ajouter la barre d'onglets de pages d'édition (`#adminGridPageTabs`, boutons `Page 1`, `Page 2`, `➕ Ajouter Page`) dans `src/RestaurantPos.Api/wwwroot/index.html`
- [X] T017 [US2] Implémenter la gestion de l'onglet de page active `state.activeAdminGridPage` et la fonction `renderAdminGridPageTabs()` dans `src/RestaurantPos.Api/wwwroot/app.js`
- [X] T018 [US2] Adapter `renderAdminGridEditor(categoryId, pageIndex)` pour charger et éditer la page sélectionnée dans `src/RestaurantPos.Api/wwwroot/app.js`
- [X] T019 [US2] Implémenter l'ajout d'une nouvelle page de grille (`addNewGridPage`) et la suppression d'une page vide dans `src/RestaurantPos.Api/wwwroot/app.js`

---

## Phase 6: User Story 3 & User Story 5 - Personnalisation & Synchronisation Multi-Terminaux (Priority: P3 / P4)

**Goal**: Assurer la personnalisation des tuiles sur toutes les pages et la synchronisation SignalR / Local-First multi-pages.

**Independent Test**: Modifier un libellé ou couleur sur la page 2, vérifier la persistance après rechargement et la synchronisation en temps réel sur les autres terminaux.

- [X] T020 [US3] Mettre à jour la sauvegarde des slots personnalisés (`customLabel`, `customColorHex`) pour préserver le `pageIndex` dans `src/RestaurantPos.Api/wwwroot/app.js`
- [X] T021 [US5] Diffuser et écouter l'événement SignalR `OnGridLayoutUpdated` incluant `pageIndex` et `totalPages` dans `src/RestaurantPos.Api/wwwroot/app.js`
- [X] T022 [P] [US5] Mettre à jour le cache local `localStorage` multi-pages (`grid_layout_${categoryId}_page_${pageIndex}`) dans `src/RestaurantPos.Api/wwwroot/app.js`

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Validation de bout en bout, conformité des SLOs et non-régression

- [X] T023 Exécuter l'ensemble des scénarios de validation décrits dans `specs/013-customizable-touch-grid/quickstart.md`
- [X] T024 [P] Vérifier la non-régression de l'ensemble des 90+ tests de la solution (`dotnet test`)
- [X] T025 Valider l'absence complète d'ascenseur et la fluidité tactile sur les résolutions de tablettes cibles (iPad 10.9", 12.9", Android 1080p)

---

## Dependencies & Execution Order

### Phase Dependencies
- **Phase 1 (Setup)**: Complétée.
- **Phase 2 (Foundational)**: Complétée.
- **Phase 3 (User Story 4 - Zero-Scrollbar MVP)**: Complétée.
- **Phase 4 (User Story 1 - Multi-Page Sales Grid MVP)**: Complétée.
- **Phase 5 (User Story 2 - Back-Office Multi-Page Editor)**: Complétée.
- **Phase 6 (User Story 3 & 5 - Customization & Sync)**: Complétée.
- **Phase 7 (Polish)**: Complétée.
