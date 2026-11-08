# Suivi : spec 023 — Stock, fiches techniques et allergènes

Tâches : `specs/023-stock-recipes-allergens/tasks.md`. Lots : A (US1 allergènes + US2 ruptures), B (US3 recettes), C (US4 stock).

## Décomptes de tests

| Date | Étape | .NET | PosKit | Web |
|---|---|---|---|---|
| 2026-11-05 | Départ | 842 (Domain 69, Infra 468, Api 305) | non lancé | non lancé |
| 2026-11-05 | Après serveur du lot A | 861 (Domain 76, Infra 468, Api 317), tous verts | non lancé | non lancé |

## État des tâches

Fait (cochées dans tasks.md) : T001–T005, T008, T010, T012, T027, T029–T031.

| Tâches | Statut |
|---|---|
| T003 chemins d'ajout de ligne | fait : un seul chemin réel, `TableManagementService.AddOrUpdateTableOrderItemsAsync` (le comptoir passe par lui via `/api/tables/{n}/items`). `CheckoutEndpoints` (vente à la volée) et `SyncEndpoints` (synchro hors ligne) créent des lignes sans produit du catalogue : ils ne sont pas concernés par la rupture. |
| T006, T007 miroir Swift, noms i18n des 14 allergènes | à faire |
| T009 fixture `products_with_allergens.json` | à faire (API en marche requise) |
| T011 | partiel : 4 colonnes `Products` faites ; colonnes `RestaurantSettings` (ticket cuisine) reportées avec T013/T025 |
| T013, T025, T026 option allergènes sur le ticket de cuisine | à faire (régénération de fixture ticket) |
| T014–T017 web allergènes | à faire |
| T018–T024 iPad allergènes (migration V13) | à faire |
| T028 règles iPad local de rupture | à faire |
| T032 synchro hors ligne : test que la vente reste acceptée | à faire (le code n'est pas touché) |
| T033–T035 web ruptures | à faire |
| T036–T040 iPad ruptures | à faire |
| Lots B et C | à faire |

## Fait côté iPad (lot A, partiel, 2026-11-06)

- PosKit : 746 tests verts (740 avant). Non lancés : .NET, web, UI iPad.
- `Core/Allergens.swift` (T006) + `AllergensTests`, `Product` (masque, déclaré, surcharge, `isOutOfStock`, `passesAllergenFilter`) (T018).
- `PosAPI.setAvailability` / `setAllergens` dans `HTTPPosAPI`, `InMemoryPosAPI`, `LocalPosAPI` (T019, T037 sauf événement) ; `HTTPPosAPI` n'envoie plus `isAvailable` (modifier un produit levait la rupture).
- Migration V13 `Products` seulement (T020) : colonnes `RestaurantSettings` reportées avec T013/T025 ; refus d'ajout en rupture dans `LocalPosAPI.addItems` ; `LocalAllergensAvailabilityTests` (T028).
- Écrans (2026-11-06) : `RealtimeEvent.productAvailabilityChanged`, `CatalogStore` (filtre, mode « Ruptures », `toggleAvailability`, `saveAllergens`), `AllergenViews.swift`, tuile grisée + badge + ligne d'allergènes, section d'allergènes de l'éditeur de produit ; textes FR/EN/AR (xcstrings + `Localizable.strings`) ; démo InMemory (burger gluten+lait, pizza sans allergène).
- Tests : PosKit 746 verts ; `AllergensAvailabilityUITests` (2 cas) verts. Non lancés : suite UI complète, contrat réel.
- Contrat (2026-11-06) : fixture `products_with_allergens.json` capturée sur l'API .NET (+ test de décodage) ; cas `LiveAPI.stockOutRefusesAddThenRestores` : 7 tests de contrat verts contre l'API réelle.
- Reste : T020 colonnes `RestaurantSettings`, T025/T026 ticket cuisine, lots B et C. Non couvert : édition des allergènes dans l'éditeur (UI).

## Lot B (2026-11-06, démarré)

- Fait : `Ingredient`, `RecipeLine` (Domain), `Domain/Recipes/RecipeCostCalculator` (entiers, `Int128`, un seul arrondi, préparations avec rendement, cycle, profondeur 5, ingrédient inactif) + `RecipeCostCalculatorTests` (9 cas). Domain.Tests : 85 verts. T041 coché.
- Convention de calcul (à reprendre en Swift, T055) : centimes = Σ(quantité milli × prix milli-centimes) / 1 000 000, demi supérieur ; prix d'une préparation = Σ / rendement milli, demi supérieur (milli-centimes par unité de base).
- Reste du lot B : T042–T044 tests API/fixture, T045 (DbContext), T046 SQL, T048–T051 serveur, web (T052–T054), iPad (T055–T061).

## Fait côté web (lot A)

- Cartes produit : ligne d'allergènes (`⚠ Gluten, Lait`) ou `?` si non renseignés, badge « Rupture », carte grisée ; ticket en cours : ligne d'allergènes.
- Boutons d'outils au début de la barre de familles : filtre d'allergènes (masque aussi les plats non renseignés, par prudence) et mode « Ruptures » (toucher un plat bascule sa disponibilité).
- Abonnement à `OnProductAvailabilityChanged` sur `/hubs/pos` : le plat se grise sans rechargement.
- Éditeur de produit : cases d'allergènes + « renseignés » ; l'enregistrement n'envoie plus `isAvailable` (ne lève plus une rupture) ; `overridden: true` à la saisie manuelle.
- Textes `allergen.*`, `order.*`, `admin.allergens_*` en FR, EN, AR (fin de `wwwroot/i18n/*.json`, fichiers non triés).
- `tests/allergens-availability.spec.ts` (4 cas, rupture visible en moins de 2 s).
- Playwright complet : 105 verts, 1 échec `hotel-room-charge` (déjà en échec avant ce travail, voir le suivi du 2026-11-03 : 101/102).
- Non couvert par un test e2e : l'éditeur de produit (cases d'allergènes) et la ligne d'allergènes du ticket en cours.

## Fait côté serveur (lot A)

- `Allergens` (14 bits) + `AllergensTests`.
- `Product` : `AllergenMask`, `AllergensDeclared`, `AllergensOverridden`, `AutoUnavailable` ; ALTER idempotents dans `Program.cs`.
- `PUT /api/catalog/products/{id}/availability` (tout opérateur authentifié, diffuse `OnProductAvailabilityChanged` sur `/hubs/pos`), `PUT …/allergens` (responsable), `GET /products` sert `allergenMask`, `allergensDeclared`, `allergensOverridden`.
- Refus d'ajout : `ProductUnavailableException` → `409 {"code":"product_unavailable","productId"}`.
- Correctif : `PUT /api/catalog/products/{id}` sans `IsAvailable` ne lève plus la rupture (`bool?` dans `UpdateProductAsync`).
- Textes `errors.product_unavailable` et `errors.allergen_mask_invalid` en FR, EN, AR.
- Tests : `ProductAvailabilityTests` (6), `ProductAllergensTests` (6 cas).

## Constats ouverts

- `PUT /api/catalog/products/{id}` (`UpdateProductRequest.IsAvailable ?? true`) remet un produit en disponible quand l'éditeur de produit n'envoie pas le champ : à corriger avec la rupture manuelle (US2).
- Représentation des allergènes : masque + « déclaré » (hypothèse du plan) ; le tri « absent » / « non renseigné » par allergène reste à valider.
- Carte client (FR-002, FR-004) : dépend de la spec 022.

## Lot B, serveur (2026-11-07)

Fait : T042–T050.
- `IRecipeService` / `RecipeService` : CRUD ingrédients (unicité sans casse, unité immuable si utilisé, 409 `ingredient_in_use` avec la liste des produits), fiche produit (`GET/PUT /api/catalog/products/{id}/recipe` → `lines`, `costCents`, `allergenMask`), fiche de préparation (`GET/PUT /api/stock/ingredients/{id}/recipe`, recalcule `UnitCostMilliCents`), `ProductCostsCentsAsync`.
- Allergènes déduits à l'enregistrement de la fiche (sauf `AllergensOverridden` ; fiche vide : produit inchangé).
- `MenuAnalysisService(db, IRecipeService)` : coût de la fiche prioritaire sur `Product.Cost`. Une fiche devenue invalide (ingrédient désactivé) retombe sur `Product.Cost`.
- Tables `Ingredients`, `RecipeLines` (+ index unique `(OwnerKind, OwnerId, IngredientId)`) dans `Program.cs` et `AppDbContext`. Le nom unique est contrôlé en mémoire (`OrdinalIgnoreCase`, donc « Crème » ≠ « creme »).
- `recipe_cost_reference.json` généré (41 cas dont 4 refus) : `WRITE_RECIPE_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter RecipeCostReferenceFixtureTests`.
- Tests : .NET 85 (Domain) / 470 (Infrastructure) / 323 (Api), tous verts.

Reste : T051 (capturer `ingredients.json`, `recipe.json` sur l'API), web T052–T054, iPad T055–T061 (migration V14).
Constat ouvert : une fiche de préparation modifiée ne recalcule pas `UnitCostMilliCents` des préparations qui l'utilisent (le coût de fiche, lui, est toujours recalculé à la volée).

## Lot B terminé (2026-11-07)

- Web : onglet Gestion → Ingrédients, éditeur de fiche dans l'éditeur d'article (coût en direct), textes FR/EN/AR, `recipes.spec.ts` (2 cas). La modale d'article défile désormais (`max-height: 92vh`).
- Fixtures capturées : `ingredients.json`, `recipe.json` ; générée : `recipe_cost_reference.json`.
- iPad : `Core/RecipeCost.swift` (somme sur 128 bits maison, `Int128` absent d'iOS 17), `RecipeRules`, `StockModels`, `StockStore`, migration V14, `LocalStockRepository`, `LocalPosAPI+Stock`, `InMemoryPosAPI`, `HTTPPosAPI` (les 400/409 portant un code connu lèvent `RecipeError`), analyse du menu avec coût de fiche prioritaire, écrans `Features/Stock/StockScreens.swift`, `StockUITests` (2 cas).
- Tests : .NET 85 / 470 / 323 ; PosKit 755 ; `StockUITests` 2/2.
- Non lancés après ces changements : suite UI complète, Playwright complet.

## Lot C et finitions (2026-11-07)

Fait : T062–T085 (hors T086–T089 ci-dessous).
- Serveur : `StockMovement`, `ModifierOptionConsumption`, `StockSchema` (index unique partiel, déclencheurs d'ajout seulement, vue `IngredientStock`), `StockConsumption` (appelée par `CheckoutPaymentService` à l'encaissement soldé), `StockService` (réception à prix moyen pondéré, inventaire valorisé, pertes, historique, alertes, rupture automatique, réglages), `StockEndpoints`, `StockAvailabilityFilter` (rafraîchit et diffuse `OnProductAvailabilityChanged` après un règlement), `restock` sur `POST /api/checkout/void/{id}`.
- Bug trouvé et corrigé : `RecipeService` et `StockService` changeaient allergènes et disponibilité sans invalider le catalogue en cache (`catalog_products`).
- Ticket de cuisine : ligne d'allergènes + mention légale (option `PrintAllergensOnKitchenTicket`, défaut faux), 3 scénarios dans `ticket_document_reference.json`, miroir `TicketBuilder.swift`, dispatch iPad.
- Web : opérations Réception / Inventaire / Perte / Historique au pavé numérique, réglages (rupture automatique, allergènes sur le ticket), cloche `lowStock`, `stock.spec.ts`.
- iPad : migration V15, consommation dans `closeOrder(.paid)`, `restoreSale`, alertes, rupture automatique, `StockStore`, `StockOpSheet`, `NeedsStore` (`lowStock`), `StockMovementsUITests`.
- Journée de stock : `StockDay` + `StockDaySimulationTests` (oracle indépendant, chaînes fiscales valides) et `LocalStockDaySimulationTests`, fixture `stock_day_reference.json`. Scénario dédié : `day_scenario.json` et les fixtures fiscales n'ont pas changé.
- Écarts : pas d'écran d'annulation de reçu (web, iPad) donc pas de case « produit non consommé » dans l'UI ; pas d'écran iPad pour les ajustements de modificateurs ; test .NET du répartiteur (option activée) couvert seulement par la fixture du constructeur.
- Tests : .NET 85 / 479 / 330 ; PosKit 766.

## Suites complètes (T088, 2026-11-07)

- .NET : 85 / 479 / 330, verts. PosKit : 766, vert. Contrat contre l'API réelle : 7/7.
- Playwright : 107 passés, 2 en échec **sans rapport** avec le stock : `hotel-room-charge` (plafond de la chambre de test épuisé par les exécutions répétées sur la base de dev) et `auth-flow` « verrouiller et déverrouiller » (passe isolé, flaky en suite complète).
- UI iPad : dans la suite complète, seuls mes 3 cas de stock échouaient ; deux défauts réels corrigés (libellé d'unité affiché en clé brute faute de clés dans le catalogue de l'app ; identifiant de ligne qui masquait celui du menu), cas de la fiche rendu robuste au défilement. `StockUITests` et `StockMovementsUITests` passent seuls ; la suite UI complète n'a pas été relancée après ces corrections.
- T089 (quickstart à la main sur simulateur et web) : **non fait**.

## Suite UI iPad complète (2026-11-07, 23 h 39)

81 tests, 78 verts, 3 en échec : `BookingsUITests` (liste d'attente ×2, mise à table). Les réservations de démonstration sont posées « maintenant + 60 min » (`InMemoryPosAPI`, `at(60)`) : lancées à 23 h 39 elles tombent après minuit, hors de « aujourd'hui ». Dépendance à l'heure sans rapport avec le stock (non reproduit en journée). Tous les tests de stock passent.

## Quickstart (T089)

Commandes du quickstart relancées : `Availability`, `DaySimulation`, `LocalizationTests` (.NET, 46 verts), `RecipeCostReferenceFixtureTests`, `swift test --filter RecipeCost|LocalDaySimulation|LocalStockDay` (6 verts). Pas de parcours visuel manuel sur simulateur : la couverture vient des tests automatisés.

## Écrans ajoutés (2026-11-08)

Annulation de reçu avec case « produit non consommé » (web, iPad) et édition de la consommation des options de modificateur (web, iPad). Tests : Playwright `stock.spec.ts` (2 cas), `StockUITests` (4 cas, dont `testVoidReceiptWithTheRestockCheckbox` et `testModifierOptionConsumptionIsSavedWithTheProduct`), PosKit +2 (`modifierOptionConsumptionIsEditableAndConsumedBySales`). Constat : `NeedsStoreTests.collectsLateBookingsAndOverdueWaitlistFromTheDemoBook` échoue autour de minuit (réservations de démo à « maintenant + 60 min »), comme `BookingsUITests`.

## Complétude (2026-11-08)

- Playwright complet sur base neuve jetable (`ConnectionStrings__DefaultConnection=Data Source=/tmp/….db`, `--seed-test-data` puis lancement normal) : **110 passés, 0 échec**. Les 2 échecs précédents (`hotel-room-charge`, `auth-flow`) venaient de l'état accumulé de la base de dev (plafond de la chambre de test épuisé).
- UI iPad complète : 83 / 83. PosKit : 768. .NET : 85 / 480 / 330 (+ test du répartiteur : allergènes sur le ticket de cuisine seulement si l'option est active).
