# Plan d'implémentation : Stock, fiches techniques et allergènes

**Branche** : `main` | **Date** : 2026-11-04 | **Spec** : [spec.md](spec.md)

## Résumé

Trois lots indépendants, livrables dans cet ordre :

1. **Lot A, allergènes et ruptures** (histoires 1 et 2) : un masque d'allergènes sur le produit, et la rupture manuelle en réutilisant `Product.IsAvailable`, qui existe déjà mais n'est contrôlé nulle part côté commande.
2. **Lot B, ingrédients, fiches techniques et coût matière** (histoire 3) : calcul du coût de revient en entiers, branché sur `MenuAnalysisService` et son miroir Swift.
3. **Lot C, stock** (histoire 4) : mouvements en ajout seulement, consommation au règlement, réceptions, inventaires, alertes, rupture automatique.

Le mode serveur (.NET + web + iPad serveur) et le mode autonome (`LocalPosAPI`) reçoivent les mêmes règles, avec des fixtures de parité générées par le .NET.

## Contexte technique

**Langage / version** : C# .NET 10 (API), JavaScript vanilla (web), Swift 6 / iPadOS 17+ (PosKit, SwiftUI)

**Dépendances** : aucune nouvelle. EF Core SQLite, SignalR, SQLite local de l'iPad déjà présents.

**Stockage** : SQLite serveur (`EnsureCreated` + SQL idempotent dans `Program.cs`) ; SQLite local iPad (migrations `LocalMigrator`, prochaines versions V13 puis V14, jamais de script publié modifié).

**Tests** : xUnit (domaine, API), Playwright (web), Swift Testing et XCUITest (iPad), journée simulée étendue.

**Plateforme cible** : serveur sur le réseau du restaurant, web tactile, iPad serveur, iPad autonome.

**Objectifs de performance** : rupture visible sur les autres postes en moins de 2 s (SC-001) ; calcul de coût d'une carte de 200 plats sans latence perceptible (< 200 ms côté API).

**Contraintes** : montants en centimes entiers, aucun `float` ni `double` ; arrondi identique .NET et Swift ; vente jamais bloquée par le stock ; ne touche à aucune chaîne NF525 ; textes FR/EN/AR partout.

**Périmètre** : un dépôt de stock par établissement, 14 allergènes, cinq unités de base.

## Vérification de la constitution

| Principe | Verdict | Remarque |
|---|---|---|
| I. Tactile, i18n, parité web/iPad | Respecté | Cibles 56 pt ; allergènes et unités traduits en en/fr/ar sur les trois supports ; écrans web et iPad dans le même lot |
| II. Couches et services | Respecté | Interfaces `IStockService`, `IRecipeCostCalculator` dans Application ; implémentation dans Infrastructure ; endpoints Minimal API ; pas de MediatR |
| III. Intégrité transactionnelle | Respecté | Consommation écrite dans la transaction de règlement, idempotente par (commande, ingrédient, type) |
| IV. Temps réel | Respecté | Un nouvel événement `OnProductAvailabilityChanged` sur `/hubs/pos`, ajouté sans renommer l'existant |
| V. Monnaie et traçabilité | Respecté | Prix en milli-centimes entiers, arrondi final unique ; mouvements en ajout seulement (déclencheurs SQL) ; aucune chaîne fiscale modifiée |
| Schéma sans migrations EF | Respecté | SQL idempotent dans `Program.cs` ; migrations V13 et V14 côté iPad |

Re-vérification après conception : aucun écart. Section de suivi de complexité vide.

## Structure du projet

### Documentation

```text
specs/023-stock-recipes-allergens/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/api.md
└── tasks.md   (étape suivante, /speckit-tasks)
```

### Code source (fichiers touchés ou créés)

```text
src/RestaurantPos.Domain/
├── Entities/Ingredient.cs, RecipeLine.cs, StockMovement.cs      (nouveaux)
├── Entities/Product.cs                                          (AllergenMask, AllergensDeclared, AutoUnavailable)
└── ValueObjects/Allergens.cs                                    (masque et noms stables)
src/RestaurantPos.Application/Common/Interfaces/
├── IStockService.cs, IRecipeCostCalculator.cs                   (nouveaux)
src/RestaurantPos.Infrastructure/Services/
├── StockService.cs, RecipeCostCalculator.cs                     (nouveaux)
├── CheckoutPaymentService.cs, TakeawayCounterService.cs         (appel de la consommation au solde)
├── TableManagementService.cs                                    (refus d'ajout d'un produit indisponible)
└── MenuAnalysisService.cs                                       (coût calculé prioritaire)
src/RestaurantPos.Api/
├── Endpoints/StockEndpoints.cs                                  (nouveau)
├── Endpoints/CatalogEndpoints.cs                                (availability, allergens, recipe)
├── Hubs/PosHub.cs                                               (OnProductAvailabilityChanged)
└── Program.cs                                                   (DI, SQL idempotent, déclencheurs)
src/RestaurantPos.Api/wwwroot/                                   (app.js, styles.css, i18n/{fr,en,ar}.json)
ios/Packages/PosKit/Sources/PosKit/
├── Core/Allergens.swift, RecipeCost.swift                       (miroirs)
├── Models/StockModels.swift, Stores/StockStores.swift
└── Local/LocalMigrator.swift (V13, V14), LocalStockRepository.swift, LocalPosAPI+Stock.swift
ios/RestaurantPOS/Features/Stock/                                (écrans) ; Localizable.xcstrings
tests/ : Domain, Infrastructure (fixture recipe_cost_reference.json), Api, Web.E2ETests, DaySimulation
```

**Décision de structure** : projet existant à quatre couches plus deux clients, aucune nouvelle brique.

## Découpage en lots

| Lot | Contenu | Livrable testable seul |
|---|---|---|
| A | Allergènes (masque, saisie, affichage, filtre), rupture manuelle avec diffusion, refus d'ajout, indicateur « non renseigné » | Rupture visible partout en moins de 2 s ; produit filtré par allergène |
| B | Ingrédients, fiches techniques, sous-recettes, coût calculé, allergènes déduits, analyse du menu | Coût d'un plat = somme exacte ; classe de l'analyse du menu mise à jour |
| C | Mouvements, consommation au règlement, avoir, réceptions, inventaires, alertes, rupture automatique, ajustements de modificateurs | Journée simulée : stock final égal à l'oracle |
