# Guide de validation : Stock, fiches techniques et allergènes

Prérequis : API en `Development` sur le port 5080 avec les données de test (`--seed-test-data`) ; codes PIN 1234 (responsable) et 2468 (serveur).

## Lot A

1. Gestion → Produits : cocher « gluten » et « lait » sur un produit ; il porte les icônes sur le web et sur l'iPad.
2. Filtrer la carte « sans gluten » : le produit disparaît.
3. Un produit sans déclaration affiche « allergènes non renseignés ».
4. Passer un produit en rupture depuis un iPad ; le web le grise en moins de 2 s ; ajouter ce produit renvoie `product_unavailable`.
5. Tests : `dotnet test tests/RestaurantPos.Api.Tests --filter "FullyQualifiedName~Availability"`, Playwright `allergens-availability.spec.ts`, `cd ios && ./scripts/test.sh unit`.

## Lot B

1. Créer 3 ingrédients (farine, beurre, œufs) et la fiche d'un plat ; le coût affiché est la somme exacte arrondie une fois.
2. Changer un prix d'achat : le coût du plat suit ; ajouter une sous-recette l'inclut ; un cycle est refusé.
3. Analyse du menu : la classe du plat tient compte du coût calculé.
4. Parité : `WRITE_RECIPE_FIXTURE=1 dotnet test tests/RestaurantPos.Infrastructure.Tests --filter RecipeCostReferenceFixtureTests` puis `swift test --filter RecipeCost`.

## Lot C

1. Réception de 5 kg de farine ; vendre 3 plats de 200 g chacun et solder ; le stock baisse de 600 g.
2. Annuler par avoir avec « produit non consommé » : le stock revient ; sans la case : inchangé.
3. Passer sous le seuil : une alerte apparaît dans la cloche ; avec la rupture automatique, le produit se grise.
4. Inventaire avec écart : un mouvement `count` et sa valeur sont enregistrés ; tenter un `UPDATE` direct en base échoue.
5. Journée simulée : `dotnet test tests/RestaurantPos.Api.Tests --filter DaySimulation` (stock final égal à l'oracle), rejouée par `LocalDaySimulationTests`.

## Critères de sortie (checklist du projet)

.NET, PosKit, Playwright et UI iPad complets ; textes FR/EN/AR cohérents (`LocalizationTests`) ; XSS échappé dans l'affichage des noms d'ingrédients ; `docs/plans/stock-progress.md` à jour avec les décomptes de tests.
