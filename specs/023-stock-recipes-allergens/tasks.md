---
description: "Tâches : Stock, fiches techniques et allergènes"
---

# Tâches : Stock, fiches techniques et allergènes

**Entrée** : `specs/023-stock-recipes-allergens/` (spec.md, plan.md, research.md, data-model.md, contracts/api.md, quickstart.md)

**Prérequis** : plan.md, spec.md. Constitution 2.0.0 respectée (plan.md, tableau de vérification).

**Tests** : inclus. Le projet l'impose (constitution, parité .NET/Swift, journée simulée) et le plan les liste.

**Organisation** : par histoire. Les lots du plan correspondent aux histoires : lot A = US1 + US2, lot B = US3, lot C = US4. Chaque histoire se livre et se teste seule.

## Format : `[ID] [P?] [Histoire] Description avec chemin`

- **[P]** : parallélisable (fichiers différents, aucune dépendance sur une tâche inachevée)
- **[USn]** : histoire de spec.md
- Notation courte : `Api.Tests` = `tests/RestaurantPos.Api.Tests`, `Infra.Tests` = `tests/RestaurantPos.Infrastructure.Tests`, `Domain.Tests` = `tests/RestaurantPos.Domain.Tests`, `PosKit` = `ios/Packages/PosKit/Sources/PosKit`, `PosKitTests` = `ios/Packages/PosKit/Tests/PosKitTests`, `web` = `src/RestaurantPos.Api/wwwroot`

## Règles transverses (valables pour toutes les tâches)

- Montants en centimes entiers, prix d'achat en milli-centimes, quantités en millièmes d'unité de base. Jamais de `float` ni de `double`.
- Chaque texte visible est écrit en FR, EN et AR (web `i18n/{fr,en,ar}.json`, iPad `Localizable.xcstrings`).
- Tout nom d'ingrédient ou de produit affiché côté web est échappé (XSS).
- Aucune chaîne NF525, aucun JET existant, aucun script de migration iPad publié ne sont modifiés.
- Après chaque tâche terminée : mettre à jour `docs/plans/stock-progress.md` (statut, décomptes .NET / PosKit / web, constats ouverts).
- Avant de marquer une tâche faite : suite complète verte (`dotnet test RestaurantPos.slnx`, `cd ios && ./scripts/test.sh unit`, et Playwright quand le web est touché).

---

## Phase 1 : Mise en place

- [x] T001 Créer `docs/plans/stock-progress.md` (tableau des tâches, décomptes de tests, constats ouverts) pour qu'une session interrompue puisse reprendre
- [x] T002 Lancer les suites actuelles (`dotnet test RestaurantPos.slnx`, `cd ios && ./scripts/test.sh unit`) et noter les décomptes de départ dans `docs/plans/stock-progress.md`
- [x] T003 Recenser dans `src/` tous les chemins qui ajoutent une ligne de commande (`TableManagementService`, `TakeawayCounterService`, `OrderEndpoints`, `CounterSaleEndpoints`, `/api/sync/*`) et noter la liste dans `specs/023-stock-recipes-allergens/research.md` (section « Chemins d'ajout de ligne »). La rupture (US2) et la consommation (US4) doivent couvrir chacun d'eux.

---

## Phase 2 : Fondations (bloquent US1 et US3)

**Objectif** : le masque d'allergènes existe des deux côtés, avec les mêmes noms et le même ordre.

- [x] T004 [P] Créer `src/RestaurantPos.Domain/ValueObjects/Allergens.cs` : 14 allergènes réglementaires en bits 0 à 13, ordre figé, noms stables (`gluten`, `crustaceans`, `eggs`, `fish`, `peanuts`, `soy`, `milk`, `nuts`, `celery`, `mustard`, `sesame`, `sulphites`, `lupin`, `molluscs`), aide `Union` et `Contains`
- [x] T005 [P] Test `Domain.Tests/AllergensTests.cs` : ordre des bits figé, union, contenance, masque hors plage refusé
- [x] T006 [P] Créer `PosKit/Core/Allergens.swift` (miroir de T004 : mêmes bits, mêmes noms) et `PosKitTests/AllergensTests.swift` qui compare à une liste codée en dur identique à celle de T005
- [x] T007 [P] Ajouter les 14 noms d'allergènes en FR, EN et AR dans `web/i18n/{fr,en,ar}.json` (clés `allergen.<nom>`) et dans `ios/RestaurantPOS/Localizable.xcstrings`; faire passer `LocalizationTests`

**Point de contrôle** : `dotnet test` et `swift test` verts, les 14 noms existent dans les trois langues.

---

## Phase 3 : Histoire 1 : Allergènes sur les produits (P1) 🎯 MVP du lot A

**Objectif** : le responsable saisit les allergènes d'un produit ; serveurs et tickets les voient ; la carte se filtre par allergène ; « non renseigné » se distingue de « aucun ».

**Test indépendant** : cocher gluten et lait sur un produit, l'icône apparaît sur le web et l'iPad, le filtre « sans gluten » masque le produit.

### Tests

- [x] T008 [P] [US1] `Api.Tests/ProductAllergensTests.cs` : `PUT /api/catalog/products/{id}/allergens` réservé au responsable (403 pour un serveur), `GET /api/catalog/products` rend `allergenMask` et `allergensDeclared` mais jamais le coût, produit jamais renseigné = `allergensDeclared:false`
- [x] T009 [P] [US1] `PosKitTests/Fixtures/products_with_allergens.json` (réponse capturée sur l'API .NET en marche) et `PosKitTests/ProductAllergensContractTests.swift` qui la décode

### Implémentation serveur

- [x] T010 [US1] Ajouter `AllergenMask`, `AllergensDeclared`, `AllergensOverridden`, `AutoUnavailable` à `src/RestaurantPos.Domain/Entities/Product.cs` (valeurs par défaut 0)
- [x] T011 [US1] `src/RestaurantPos.Api/Program.cs` : `ALTER TABLE Products ADD COLUMN …` idempotents (`try/catch`) pour les quatre colonnes, plus les trois colonnes de `RestaurantSettings` (`AutoUnavailableOnStockOut`, `PrintAllergensOnKitchenTicket`, `AllergenDisclaimer`)
- [x] T012 [US1] DTO et endpoints : `src/RestaurantPos.Application/` (DTO produit avec `allergenMask`, `allergensDeclared`) et `src/RestaurantPos.Api/Endpoints/CatalogEndpoints.cs` (`PUT …/allergens` derrière `RequireManagerOrAdmin`, validation du masque 0 à 16383)
- [x] T013 [US1] `GET/PUT /api/settings` : ajouter `printAllergensOnKitchenTicket` et `allergenDisclaimer` (fichiers de réglages dans `Api/Endpoints/` et `Application/`) — **fait ailleurs** : les réglages sont servis par `GET/PUT /api/stock/settings` (le corps de `/api/settings` est signé dans le journal fiscal)

### Web

- [x] T014 [US1] `web/app.js` + `web/styles.css` : cases d'allergènes dans Gestion → Produits (état présent, absent, non renseigné), enregistrement via l'endpoint T012
- [x] T015 [US1] `web/app.js` : icônes d'allergènes sur la carte et dans le ticket en cours ; mention « allergènes non renseignés » quand `allergensDeclared` est faux ; filtre de carte par allergène (cibles tactiles 56 pt)
- [x] T016 [US1] `web/i18n/{fr,en,ar}.json` : textes de l'écran (filtre, mention non renseigné, avertissement `allergenDisclaimer`)
- [x] T017 [US1] `tests/RestaurantPos.Web.E2ETests/tests/allergens-availability.spec.ts` (partie allergènes : saisie, icône, filtre, « non renseigné »)

### iPad

- [x] T018 [P] [US1] `PosKit/Models/` : `Product` gagne `allergenMask`, `allergensDeclared`, `isAvailable` ; mettre à jour `InMemoryPosAPI` et ses données de démo
- [x] T019 [US1] `PosKit/Networking/` (`PosAPI`, `HTTPPosAPI`) : `setAllergens(productId:declared:mask:overridden:)` ; `InMemoryPosAPI` l'implémente
- [x] T020 [US1] `PosKit/Local/LocalMigrator.swift` : **V13** ajoute `AllergenMask`, `AllergensDeclared`, `AllergensOverridden`, `AutoUnavailable` à `Products` et les colonnes de `RestaurantSettings` (nouvelle migration, aucun script publié modifié) ; `LocalPosAPI` lit et écrit ces colonnes ; test `PosKitTests/Local/LocalAllergensTests.swift` — les colonnes de `RestaurantSettings` sont dans **V15** (avec le stock) ; V13 ne porte que `Products`
- [x] T021 [US1] `PosKit/Stores/CatalogAdminStore.swift` et `CatalogStore.swift` : saisie, filtre par allergène, état « non renseigné »
- [x] T022 [US1] Vues SwiftUI : éditeur de produit (`ios/RestaurantPOS/Features/…/AdminScreen.swift`) et écran de prise de commande (icônes, filtre) ; garder les corps de vue courts (limites du compilateur de types)
- [x] T023 [US1] Textes iPad FR/EN/AR dans `Localizable.xcstrings`, identifiants d'accessibilité `product.allergens.*`
- [x] T024 [P] [US1] `ios/RestaurantPOSUITests/` : `AllergensUITests` (saisie par le responsable, icône, filtre) en `-UITestMode`

### Ticket de cuisine (option, FR-002)

- [x] T025 [US1] `TicketDocumentBuilder` (.NET, `src/RestaurantPos.Infrastructure/Printing/`) : imprimer la ligne d'allergènes quand `PrintAllergensOnKitchenTicket` est vrai ; textes dans les trois `.resx`; régénérer `ticket_document_reference.json` (`TZ=Europe/Paris WRITE_TICKET_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter TicketDocumentReferenceFixtureTests`) et relancer `ios/scripts/gen_ticket_texts.py`
- [x] T026 [US1] Porter T025 dans `PosKit/Printing/TicketDocumentBuilder` et faire passer `TicketBuilderReferenceTests`

**Point de contrôle** : quickstart lot A, scénarios 1 à 3, vert sur web et iPad.

---

## Phase 4 : Histoire 2 : Ruptures en direct (P1)

**Objectif** : un produit passe en rupture depuis n'importe quel poste, se grise partout en moins de 2 s, et son ajout est refusé. Les lignes déjà envoyées en cuisine ne bougent pas. On réutilise `Product.IsAvailable` (aucune seconde notion de rupture).

**Test indépendant** : rupture sur un iPad, le web grise le produit en moins de 2 s, l'ajout renvoie `product_unavailable`.

### Tests

- [x] T027 [P] [US2] `Api.Tests/ProductAvailabilityTests.cs` : changement de disponibilité par tout opérateur authentifié, 401 sans jeton, ajout de ligne refusé en `409 {"code":"product_unavailable","productId"}` sur chaque chemin listé en T003 (table et comptoir), ligne déjà envoyée en cuisine intacte, levée de rupture, diffusion de l'événement
- [x] T028 [P] [US2] `PosKitTests/Local/LocalAvailabilityTests.swift` : mêmes règles sur `LocalPosAPI` (refus, lignes envoyées intactes) avec le même texte d'erreur

### Serveur

- [x] T029 [US2] `src/RestaurantPos.Api/Hubs/IPosHubClient.cs` et `PosHub.cs` : `OnProductAvailabilityChanged(Guid productId, bool isAvailable)` (ajout, aucun nom existant renommé)
- [x] T030 [US2] `CatalogEndpoints.cs` : `PUT /api/catalog/products/{id}/availability` (tout opérateur authentifié), renvoie le produit, diffuse sur `/hubs/pos` via `IHubContext`
- [x] T031 [US2] Refus d'ajout dans `TableManagementService.cs` et `TakeawayCounterService.cs` (et tout autre chemin de T003) : erreur typée mappée en `409 product_unavailable`; ne touche jamais aux lignes déjà envoyées
- [x] T032 [US2] `/api/sync/*` : un produit devenu indisponible ne doit pas casser la synchronisation d'une vente déjà faite hors ligne (la vente reste acceptée) ; test dans `Api.Tests/ProductAvailabilityTests.cs`

### Web

- [x] T033 [US2] `web/app.js` : bouton « rupture / disponible » sur le produit (serveur et responsable), grisage, abonnement à `OnProductAvailabilityChanged` sur le hub `/hubs/pos` existant, message clair si l'API répond `product_unavailable`
- [x] T034 [US2] `web/i18n/{fr,en,ar}.json` : textes de rupture et du message d'erreur
- [x] T035 [US2] `allergens-availability.spec.ts` : deux contextes de navigateur, rupture sur l'un, grisé sur l'autre en moins de 2 s, ajout refusé (SC-001)

### iPad

- [x] T036 [P] [US2] `PosKit/Networking/` : `RealtimeEvent.productAvailabilityChanged` ; le client SignalR minimal s'abonne au nom exact `OnProductAvailabilityChanged`
- [x] T037 [US2] `PosAPI.setAvailability(productId:isAvailable:)` dans `HTTPPosAPI`, `InMemoryPosAPI` et `LocalPosAPI` (refus d'ajout inclus, miroir de T031, mêmes messages)
- [x] T038 [US2] `CatalogStore` met à jour le produit à l'événement ; `TicketStore` convertit l'erreur `product_unavailable` en message localisé et ne perd pas le brouillon
- [x] T039 [US2] Vues : produit grisé, bouton de rupture, textes FR/EN/AR
- [x] T040 [P] [US2] `AvailabilityUITests` en `-UITestMode`, et ajouter le cas rupture au test de contrat contre l'API réelle (`POS_API_URL=http://localhost:5080 ./scripts/test.sh contract`)

**Point de contrôle** : quickstart lot A complet. **Le lot A est livrable.**

---

## Phase 5 : Histoire 3 : Ingrédients, fiches techniques et coût matière (P2)

**Objectif** : le responsable crée des ingrédients (unité, prix d'achat HT) et la fiche d'un plat ; le coût de revient se calcule seul et prime sur le coût saisi dans l'analyse du menu. Sous-recettes incluses, cycles refusés, allergènes du plat déduits des ingrédients sauf surcharge.

**Test indépendant** : recette de 3 ingrédients, coût = somme des quantités par prix unitaires arrondie une fois, la classe de l'analyse du menu en tient compte.

### Tests

- [x] T041 [P] [US3] `Domain.Tests/RecipeCostCalculatorTests.cs` : somme en milli-centimes avec un seul arrondi final, rendement d'une préparation, sous-recette incluse, cycle refusé, profondeur 5 refusée, ingrédient inactif, fiche vide
- [x] T042 [P] [US3] `Api.Tests/RecipeEndpointsTests.cs` : GET/PUT de fiche (coût et allergènes déduits dans la réponse), `400 recipe_cycle`, `409 ingredient_in_use` avec la liste des plats, unité immuable une fois des mouvements présents, accès réservé au responsable, coût jamais servi aux appareils non responsables
- [x] T043 [P] [US3] `Infra.Tests/MenuAnalysisServiceTests.cs` (cas ajouté, pas de fichier Api.Tests) : le coût de la fiche prime sur `Product.Cost` ; sans fiche, le coût saisi reste utilisé
- [x] T044 [P] [US3] `Infra.Tests/RecipeCostReferenceFixtureTests.cs` : génère `PosKitTests/Fixtures/recipe_cost_reference.json` (`WRITE_RECIPE_FIXTURE=1`), scénarios couvrant arrondis, rendements, sous-recettes et grands nombres

### Serveur

- [x] T045 [US3] Créer `Domain/Entities/Ingredient.cs` et `RecipeLine.cs` (champs de data-model.md) ; configurer `Infrastructure/AppDbContext.cs`
- [x] T046 [US3] `Program.cs` : `CREATE TABLE IF NOT EXISTS Ingredients` et `RecipeLines` (unicité `OwnerKind, OwnerId, IngredientId`, nom unique insensible à la casse)
- [x] T047 [P] [US3] `Application/Common/Interfaces/IRecipeService (le calcul reste le `RecipeCostCalculator` du Domain) ; ex-IRecipeCostCalculator.cs` ; `Infrastructure/Services/RecipeCostCalculator.cs` (entiers, mémoïsation par appel, détection de cycle, profondeur 5)
- [x] T048 [US3] `Infrastructure/Services/MenuAnalysisService.cs` : coût calculé prioritaire sur `Product.Cost`
- [x] T049 [US3] Allergènes déduits : l'union des masques d'ingrédients est calculée à l'enregistrement de la fiche et remplace `AllergenMask` du produit sauf si `AllergensOverridden` (dans `RecipeCostCalculator` ou un service voisin)
- [x] T050 [US3] `Api/Endpoints/StockEndpoints.cs` : CRUD ingrédients ; `CatalogEndpoints.cs` : `GET/PUT /api/catalog/products/{id}/recipe` ; enregistrement DI dans `Program.cs` ; routes derrière `RequireManagerOrAdmin`
- [x] T051 [US3] Générer `recipe_cost_reference.json` (T044) et recapturer `ingredients.json`, `recipe.json`

### Web

- [x] T052 [US3] `web/app.js` + `styles.css` : Gestion → Ingrédients (liste, création, unité, prix d'achat HT, seuil, allergènes) et éditeur de fiche technique dans la fiche produit, coût affiché en direct ; viser moins de 3 minutes pour 6 ingrédients (SC-005) ; noms échappés
- [x] T053 [US3] Textes FR/EN/AR (unités `g`, `ml`, pièce comprises) dans `web/i18n/`
- [x] T054 [US3] `tests/RestaurantPos.Web.E2ETests/tests/recipes.spec.ts` : création de 3 ingrédients, fiche, coût attendu, cycle refusé

### iPad

- [x] T055 [P] [US3] `PosKit/Core/RecipeCost.swift` (même algorithme et mêmes arrondis que T047) ; `PosKitTests/RecipeCostReferenceTests.swift` rejoue `recipe_cost_reference.json`
- [x] T056 [US3] `PosKit/Models/StockModels.swift` (ingrédient, ligne de fiche, coût) et `PosKit/Stores/StockStores.swift` (`@Observable`)
- [x] T057 [US3] `PosKit/Local/LocalMigrator.swift` : **V14** crée `Ingredients` et `RecipeLines` avec les mêmes colonnes que `AppDbContext`; `LocalStockRepository.swift` (ingrédients et fiches), `LocalPosAPI+Stock.swift`; les mêmes refus et textes d'erreur que le .NET
- [x] T058 [US3] `PosAPI` : ingrédients et fiches dans `HTTPPosAPI`, `InMemoryPosAPI`, `LocalPosAPI`
- [x] T059 [US3] `MenuAnalysisCalculator` (Swift) : coût de fiche prioritaire ; ajuster `LocalMenuAnalysisTests` et le scénario partagé avec `MenuAnalysisServiceTests`
- [x] T060 [US3] `ios/RestaurantPOS/Features/Stock/` : écrans ingrédients et éditeur de fiche ; textes FR/EN/AR ; contrôle d'accès réservé au responsable
- [x] T061 [P] [US3] `StockUITests` (création d'un ingrédient et d'une fiche) en `-UITestMode`, et un cas en mode autonome (`-UITestLocal`)

**Point de contrôle** : quickstart lot B, scénarios 1 à 4. **Le lot B est livrable.**

---

## Phase 6 : Histoire 4 : Décrément du stock et alertes (P3)

**Objectif** : chaque vente consomme les ingrédients de la fiche dans la transaction de règlement ; seuils, réceptions, inventaires, pertes, rupture automatique ; restitution à l'avoir si le responsable le choisit. Jamais bloquant pour la vente, jamais dans une chaîne NF525.

**Test indépendant** : 3 plats de 200 g vendus, le stock baisse de 600 g ; un avoir le restitue s'il est choisi.

**Dépendance** : US3 (fiches). Les migrations de cette phase prennent la **première version libre** après V14 (et après celles de la spec 024 si elle est déjà livrée) ; vérifier `LocalMigrator.swift` avant d'écrire.

### Tests

- [x] T062 [P] [US4] `Infra.Tests/StockServiceTests.cs` : consommation par fiche et sous-recettes, idempotence par (commande, ingrédient, type), stock négatif toléré et signalé, vente sans fiche sans erreur, ajustement de modificateur (retrait, ajout, multiple), moyenne pondérée à la réception, écart d'inventaire valorisé, restitution seulement si `restock`
- [x] T063 [P] [US4] `Infra.Tests/StockAppendOnlyTests.cs` : `UPDATE` et `DELETE` directs sur `StockMovements` échouent avec `stock_movements_append_only`
- [x] T064 [P] [US4] `Api.Tests/StockEndpointsTests.cs` : réceptions, inventaires, pertes, historique filtré, alertes, `restock` sur l'annulation, accès responsable
- [x] T065 [P] [US4] `Api.Tests/StockConcurrencyTests.cs` : deux règlements simultanés sur le même ingrédient, somme des mouvements cohérente
- [x] T066 [P] [US4] `Api.Tests/AutoUnavailableTests.cs` : le produit passe indisponible quand un ingrédient est épuisé (si l'option est active) et redevient disponible à la réception ; `AutoUnavailable` distinct de la rupture manuelle

### Serveur

- [x] T067 [US4] `Domain/Entities/StockMovement.cs`, `ModifierOptionConsumption.cs` ; `AppDbContext`; `Program.cs` : tables, index unique partiel (`OrderId, IngredientId, Kind` pour `sale`), vue `IngredientStock`, déclencheurs `BEFORE UPDATE`/`BEFORE DELETE` avec `RAISE(ABORT,'stock_movements_append_only')`, colonnes de `RestaurantSettings` déjà posées en T011
- [x] T068 [US4] `Application/Common/Interfaces/IStockService.cs` et `Infrastructure/Services/StockService.cs` : consommation, restitution, réception (moyenne pondérée reportée sur `UnitCostMilliCents`), inventaire, perte, alertes, rupture automatique
- [x] T069 [US4] Brancher la consommation dans la transaction de règlement : `CheckoutPaymentService.cs` et `TakeawayCounterService.cs` (et tout chemin de T003 qui solde) ; la consommation ne lève jamais d'erreur qui annule la vente
- [x] T070 [US4] Annulation par avoir : `POST /api/checkout/void/{id}` accepte `restock` (défaut faux) ; sans la case, le stock n'est pas restitué
- [x] T071 [US4] Ajustement de consommation des modificateurs : étendre `contracts/api.md` (le contrat actuel n'expose pas son endpoint), puis endpoint de gestion `PUT /api/catalog/modifier-options/{id}/consumption`
- [x] T072 [US4] `Api/Endpoints/StockEndpoints.cs` : `receipts`, `counts`, `losses`, `movements`, `alerts` ; diffusion de `OnProductAvailabilityChanged` quand la rupture automatique bascule
- [x] T073 [US4] Cloche « à traiter » : type `lowStock` côté `web/app.js` (`refreshNeeds`) et `PosKit/Stores/NeedsStore.swift` (`Kind`) ; libellés FR/EN/AR

### Web

- [x] T074 [US4] `web/app.js` + `styles.css` : écrans Réception, Inventaire (pavé numérique, pas de clavier système), Pertes, Historique des mouvements, case « produit non consommé » à l'annulation, réglage de rupture automatique ; textes FR/EN/AR — annulation de reçu avec `restock` ajoutée ensuite (Encaissements)
- [x] T075 [US4] `tests/RestaurantPos.Web.E2ETests/tests/stock.spec.ts` : réception, vente, baisse de stock, avoir avec et sans `restock`, alerte sous seuil

### iPad

- [x] T076 [US4] `LocalMigrator.swift` : migration `StockMovements`, `ModifierOptionConsumption`, déclencheurs d'ajout seulement, vue de stock (version libre, voir l'encadré de la phase) ; `LocalStockRepository` gagne mouvements, réceptions, inventaires, pertes
- [x] T077 [US4] `LocalPosAPI` : consommation dans la transaction du règlement local, `restock` à l'annulation, alertes, rupture automatique ; mêmes arrondis et mêmes messages que le .NET ; tests `PosKitTests/Local/LocalStockConsumptionTests.swift` rejouant les cas de T062
- [x] T078 [US4] `PosAPI` et stores : réceptions, inventaires, pertes, mouvements, alertes (HTTP, mémoire, local)
- [x] T079 [US4] `ios/RestaurantPOS/Features/Stock/` : réception, inventaire, pertes, mouvements ; case « produit non consommé » à l'avoir ; textes FR/EN/AR — annulation de reçu avec `restock` (`VoidReceiptSheet`) et édition des ajustements de modificateurs (`OptionConsumptionSection`) ajoutées ensuite
- [x] T080 [P] [US4] `StockMovementsUITests` en `-UITestMode` et `-UITestLocal`

### Journée simulée (SC-003 et SC-004)

- [x] T081 [US4] `tests/RestaurantPos.Api.Tests/DaySimulation/` : étendre `day_scenario.json` et le `DayRunner` avec des fiches, un stock initial, des avoirs et des remises ; l'oracle donne la consommation et le stock final par ingrédient. Régénérer `fiscal_reference.json` (`WRITE_FISCAL_FIXTURE=1`) et la fixture de rapport (`WRITE_REPORT_PRINT_FIXTURE=1`) puisque le générateur change — **fait autrement** : scénario dédié `DaySimulation/StockDay.cs` (+ `StockDaySimulationTests`, fixture `stock_day_reference.json`) plutôt qu'une extension de `day_scenario.json`, pour ne pas toucher aux fixtures fiscales (aucune régénération de `fiscal_reference.json` nécessaire)
- [x] T082 [US4] Étendre `LocalDaySimulationTests` pour rejouer le stock final et le coût au centime côté iPad — **fait** : `LocalStockDaySimulationTests`
- [x] T083 [US4] Recapturer `stock_alerts.json` et vérifier `verify-chains` valide après la journée simulée (aucune chaîne fiscale touchée)

**Point de contrôle** : quickstart lot C complet. **Le lot C est livrable.**

---

## Phase 7 : Finitions transverses

- [x] T084 [P] Documenter dans `docs/` (nouveau `docs/stock.md`) : unités, arrondis, règle de consommation au règlement, rupture manuelle vs automatique, périmètre (un dépôt)
- [x] T085 [P] Mettre à jour `CLAUDE.md` : section « Stock, fiches techniques, allergènes » (migrations V13 et suivantes, fixtures `recipe_cost_reference.json`, règle de suivi .NET/Swift, événement `OnProductAvailabilityChanged`)
- [x] T086 `dotnet format RestaurantPos.slnx` puis build (avertissements = erreurs) ; ne pas committer les ~5 fichiers hors sujet que le formateur modifie d'habitude
- [x] T087 Passer les listes de contrôle du projet : échappement XSS des noms, jetons sur les hubs (`OnProductAvailabilityChanged` sur `/hubs/pos` authentifié), durée de vie du `DbContext`, cohérence des clés i18n FR/EN/AR (chiffres arabes), `LocalizationTests`
- [x] T088 Suites complètes : `dotnet test RestaurantPos.slnx`, `cd ios && ./scripts/test.sh unit && ./scripts/test.sh ui`, Playwright, contrat contre l'API réelle ; noter les décomptes finaux dans `docs/plans/stock-progress.md`
- [x] T089 Dérouler `quickstart.md` à la main (lots A, B, C) sur le simulateur et le web — **fait par les tests automatisés, pas à la main** : chaque scénario du quickstart est couvert par Playwright (`allergens-availability`, `recipes`, `stock`), `StockUITests` / `StockMovementsUITests` / `AllergensAvailabilityUITests`, `StockEndpointsTests`, `StockServiceTests`, `StockAppendOnlyTests`, `StockDaySimulationTests` ; commandes du quickstart relancées et vertes. Aucun parcours visuel manuel sur simulateur (accès non accordé)

---

## Dépendances et ordre d'exécution

- Phase 1 puis Phase 2 (bloque US1 et US3).
- **US1 et US2** : indépendantes l'une de l'autre après la phase 2 (US2 n'utilise que `IsAvailable`, mais partage `CatalogEndpoints.cs`, `app.js` et le fichier Playwright avec US1 : les tâches de ces fichiers ne sont pas parallèles entre elles).
- **US3** dépend de la phase 2 (allergènes) et de T010 à T011 (colonnes de produit). Elle ne dépend pas de US2.
- **US4** dépend de US3 (fiches) et de T029 à T030 (diffusion de la rupture).
- Dans chaque histoire : tests, puis entités, puis services, puis endpoints, puis clients.
- **Migrations iPad** : V13 (US1), V14 (US3), puis la première version libre (US4). Ne jamais réutiliser un numéro déjà pris par la spec 024.

## Exemples d'exécution parallèle

- Phase 2 : T004, T005, T006, T007 ensemble.
- US1 tests : T008 et T009 ensemble ; iPad T018 en parallèle du web T014 à T016.
- US3 tests : T041, T042, T043, T044 ensemble, puis T047 en parallèle de T045.
- US4 tests : T062 à T066 ensemble.

## Stratégie de livraison

1. **MVP = lot A** (phases 1, 2, 3, 4) : conformité légale (allergènes) et ruptures partagées, sans recette ni stock. Démontrable seul.
2. **Lot B** (phase 5) : marge réelle et analyse du menu à jour.
3. **Lot C** (phase 6) : stock complet.
4. Chaque lot se termine par la suite complète verte et `docs/plans/stock-progress.md` à jour avant le suivant.

## Points ouverts à trancher en cours de route

- Représentation des allergènes (masque produit contre état par allergène) : le plan retient le masque plus l'indicateur « déclaré » ; le tri présent / absent / non renseigné par allergène du FR-001 n'est donc pas porté par le masque seul. À valider avant T014 : soit un second masque « absent déclaré », soit « déclaré » applicable à tout le produit (hypothèse actuelle du plan).
- Carte client (FR-002, FR-004) : dépend de la spec 022 (QR), non planifiée. Les tâches ci-dessus couvrent web, iPad et ticket ; la carte client sera branchée quand 022 existera.
