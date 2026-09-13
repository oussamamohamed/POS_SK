# Tasks: Happy Hour Pricing & Schedule Management

**Input**: Design documents from `/specs/019-happy-hour-pricing/`  
**Prerequisites**: [`plan.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/019-happy-hour-pricing/plan.md), [`spec.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/019-happy-hour-pricing/spec.md), [`research.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/019-happy-hour-pricing/research.md), [`data-model.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/019-happy-hour-pricing/data-model.md), [`contracts/happy-hour-api.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/019-happy-hour-pricing/contracts/happy-hour-api.md)

---

## Phase 1: Setup (Shared Entities & DTOs)

**Purpose**: Core domain entities, value objects, and application DTOs

- [X] T001 [P] Create `HappyHourSchedule` entity with days of week, time ranges, and priority in `src/RestaurantPos.Domain/Entities/HappyHourSchedule.cs`
- [X] T002 [P] Create `HappyHourPriceRule` entity supporting `FixedPrice` and `PercentageDiscount` modes in `src/RestaurantPos.Domain/Entities/HappyHourPriceRule.cs`
- [X] T003 [P] Create `HappyHourOverrideSession` entity for supervisor overrides in `src/RestaurantPos.Domain/Entities/HappyHourOverrideSession.cs`
- [X] T004 [P] Enrich `OrderItem` entity with Happy Hour audit fields (`IsHappyHourApplied`, `OriginalUnitPrice`, `AppliedHappyHourScheduleId`, `OrderedAtUtc`) in `src/RestaurantPos.Domain/Entities/OrderItem.cs`
- [X] T005 [P] Create Happy Hour DTOs (`HappyHourStatusDto`, `HappyHourRuleDto`, `HappyHourPricingTableDto`, `ActivateOverrideRequest`, `HappyHourScheduleDto`) in `src/RestaurantPos.Application/DTOs/HappyHourDtos.cs`

---

## Phase 2: Foundational (Infrastructure, Service Contracts & EF Core)

**Purpose**: Database schema, dependency injection, service interfaces, and initial seeding

- [X] T006 Update `AppDbContext` to register `DbSet<HappyHourSchedule>`, `DbSet<HappyHourPriceRule>`, `DbSet<HappyHourOverrideSession>` and configure relational navigation in `src/RestaurantPos.Infrastructure/Persistence/AppDbContext.cs`
- [X] T007 Define `IHappyHourPricingService` interface with status detection, price resolution, and override controls in `src/RestaurantPos.Application/Common/Interfaces/IHappyHourPricingService.cs`
- [X] T008 Register `IHappyHourPricingService` in DI and seed default Happy Hour schedule ("Afterwork Standard", Lun-Ven 17h-20h, Bière 5.00 €) in `src/RestaurantPos.Api/Program.cs`

---

## Phase 3: User Story 1 - Détection Automatique et Tarification Happy Hour (Priority: P1) 🎯 MVP

**Goal**: Détection automatique des créneaux actifs et calcul des tarifs préférentiels (prix fixe ou % catégorie) sans calcul manuel

**Independent Test**: Appeler `GET /api/happy-hour/status` et `GET /api/happy-hour/pricing-table` pour un créneau actif et constater l'application du tarif Happy Hour calculé.

- [X] T009 [P] [US1] Implement unit & business tests for `HappyHourPricingService` (fixed price priority, category percentage discount, time window boundary checks) in `tests/RestaurantPos.Infrastructure.Tests/HappyHourPricingServiceTests.cs`
- [X] T010 [US1] Implement `HappyHourPricingService` core engine (schedule evaluation by day/time, priority resolution, pricing table cache) in `src/RestaurantPos.Infrastructure/Services/HappyHourPricingService.cs`
- [X] T011 [US1] Create `HappyHourEndpoints.cs` and map `GET /api/happy-hour/status` and `GET /api/happy-hour/pricing-table` in `src/RestaurantPos.Api/Endpoints/HappyHourEndpoints.cs`

---

## Phase 4: User Story 2 - Verrouillage du Prix à la Saisie de Commande (Priority: P1)

**Goal**: Garantir que les consommations commandées en Happy Hour conservent définitivement leur prix réduit, même si l'encaissement intervient après l'heure limite

**Independent Test**: Créer une table, ajouter un article en Happy Hour, simuler l'expiration du créneau, constater que l'article existant conserve son tarif Happy Hour à l'encaissement et que tout nouvel article reçoit le tarif normal.

- [X] T012 [US2] Update `AddOrUpdateTableOrderItemsAsync` in `src/RestaurantPos.Infrastructure/Services/TableManagementService.cs` to resolve and stamp `UnitPrice`, `IsHappyHourApplied`, and `OriginalUnitPrice` on order creation
- [X] T013 [US2] Update `CounterSaleEndpoints.cs` to preserve Happy Hour line item properties during direct counter sales and takeaway checkout in `src/RestaurantPos.Api/Endpoints/CounterSaleEndpoints.cs`
- [X] T014 [US2] Add integration test validating price immutability across schedule expiration in `tests/RestaurantPos.Infrastructure.Tests/HappyHourPricingServiceTests.cs`

---

## Phase 5: User Story 3 - Visibilité Tactile et Signalétique en Caisse (Priority: P2)

**Goal**: Afficher en temps réel sur l'écran tactile un bandeau Happy Hour avec décompte, et mettre en valeur les prix réduits avec prix standard barré sur les tuiles produits

**Independent Test**: Ouvrir le Web POS sur le navigateur : bandeau amber visible, tuiles bières affichant `5,00 €` en surbrillance avec `7,50 €` barré, et tag `[HH]` visible dans le panier.

- [X] T015 [P] [US3] Add Happy Hour banner, countdown timer, and status badge markup in `src/RestaurantPos.Api/wwwroot/index.html`
- [X] T016 [P] [US3] Add glowing amber theme styles, countdown pill, and strikethrough price styling (`.product-price-hh`, `.product-price-strike`) in `src/RestaurantPos.Api/wwwroot/styles.css`
- [X] T017 [US3] Update `app.js` to poll / hydrate Happy Hour pricing table, dynamically format product card price labels, and append `[HH]` indicator to cart lines in `src/RestaurantPos.Api/wwwroot/app.js`
- [X] T018 [US3] Add SignalR event `HappyHourStatusChanged` broadcast in `src/RestaurantPos.Api/Hubs/PosHub.cs` and wire client listener in `src/RestaurantPos.Api/wwwroot/app.js`

---

## Phase 6: User Story 4 - Dérogation et Forçage Manuel par Superviseur (Priority: P2)

**Goal**: Permettre au responsable (PIN FloorManager/Admin) d'activer exceptionnellement le Happy Hour ou de le prolonger de 30 minutes, avec audit JET NF525

**Independent Test**: Ouvrir le menu d'action rapide, saisir le PIN gérant `9999`, cliquer sur "Prolonger de 30 min", vérifier la mise à jour immédiate du compte à rebours et l'écriture de l'événement dans `TransactionJournalEntries`.

- [X] T019 [US4] Implement `ActivateOverrideAsync` and `DeactivateOverrideAsync` with PIN verification and JET audit logging in `src/RestaurantPos.Infrastructure/Services/HappyHourPricingService.cs`
- [X] T020 [US4] Map `POST /api/happy-hour/override/activate` and `POST /api/happy-hour/override/stop` in `src/RestaurantPos.Api/Endpoints/HappyHourEndpoints.cs`
- [X] T021 [US4] Add Supervisor Happy Hour quick actions modal (PIN keypad, "+30 min", "+60 min", "Arrêter") in `src/RestaurantPos.Api/wwwroot/index.html` and `src/RestaurantPos.Api/wwwroot/app.js`

---

## Phase 7: User Story 5 - Configuration des Règles et Plages Horaires (Priority: P3)

**Goal**: Fournir une interface et des endpoints pour créer, modifier et désactiver des plannings et règles Happy Hour

**Independent Test**: Appeler `POST /api/happy-hour/schedules` avec un planning personnalisé, vérifier son enregistrement en base et sa prise en compte dans le statut.

- [X] T022 [P] [US5] Implement CRUD endpoints for `HappyHourSchedule` and `HappyHourPriceRule` in `src/RestaurantPos.Api/Endpoints/HappyHourEndpoints.cs`
- [X] T023 [US5] Add Happy Hour management settings panel in `src/RestaurantPos.Api/wwwroot/index.html` and `src/RestaurantPos.Api/wwwroot/app.js`

---

## Phase 8: Polish, Cross-Cutting & E2E Validation

**Purpose**: Validation de bout en bout de tous les flux et couverture de tests automatisés

- [X] T024 [P] Create complete Playwright E2E test suite covering automatic detection, cart pricing, price locking on tables, and supervisor PIN overrides in `tests/RestaurantPos.Web.E2ETests/tests/happy-hour.spec.ts`
- [X] T025 Run full test suite (.NET xUnit + Playwright E2E) to verify zero regressions across all 17 existing checkout tests and new Happy Hour tests

---

## Dependencies & Completion Order

```text
Phase 1: Setup (T001-T005)
       │
       ▼
Phase 2: Foundational (T006-T008)
       │
       ├─────────────────────────┐
       ▼                         ▼
Phase 3: US1 Engine & MVP   Phase 5: US3 Tactile UI
 (T009-T011)                 (T015-T018)
       │                         │
       ▼                         │
Phase 4: US2 Price Lock          │
 (T012-T014)                     │
       │                         │
       ▼                         │
Phase 6: US4 Supervisor Override ◄
 (T019-T021)
       │
       ▼
Phase 7: US5 Admin Config (T022-T023)
       │
       ▼
Phase 8: Polish & E2E Validation (T024-T025)
```

---

## Implementation Strategy

1. **MVP First (Phases 1, 2, 3)**: Obtenir immédiatement le moteur de calcul Happy Hour fonctionnel avec les endpoints d'interrogation et le seeding par défaut.
2. **Intégrité Métier (Phase 4)**: Sécuriser le verrouillage immuable des prix sur les tables pour éviter les contestations clients lors de l'encaissement différé.
3. **Ergonomie Front (Phase 5 & 6)**: Rendre le Happy Hour immédiatement visible sur le tactile et outiller les superviseurs avec le forçage PIN.
4. **Validation E2E (Phase 8)**: Automatiser les scénarios Playwright pour garantir la non-régression avec les tests d'encaissement NF525.
