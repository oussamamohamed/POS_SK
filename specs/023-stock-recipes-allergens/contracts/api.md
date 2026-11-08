# Contrats : Stock, fiches techniques et allergènes

Tous les montants en centimes ; les prix d'achat en milli-centimes par unité de base ; les quantités en millièmes d'unité de base. Les routes de gestion sont derrière `RequireManagerOrAdmin`. Les lectures du catalogue restent ouvertes à tout opérateur authentifié.

## Catalogue (lots A)

- `GET /api/catalog/products` : ajoute `allergenMask` (entier), `allergensDeclared` (booléen), `isAvailable` (déjà servi). Jamais le coût.
- `PUT /api/catalog/products/{id}/availability` : corps `{ "isAvailable": false }`, tout opérateur authentifié. 200 avec le produit ; diffuse `OnProductAvailabilityChanged`.
- `PUT /api/catalog/products/{id}/allergens` (responsable) : `{ "declared": true, "mask": 5, "overridden": true }`.
- Ajout de ligne sur un produit indisponible : `409 {"code":"product_unavailable","productId":"…"}` (route existante d'ajout de ligne, aussi pour `POST /api/orders/counter`). Les lignes déjà envoyées ne changent pas.

## Temps réel

`/hubs/pos` : `OnProductAvailabilityChanged(productId, isAvailable)`. Web et iPad s'abonnent ; le nom ne doit pas être renommé.

## Ingrédients et fiches (lot B)

- `GET /api/stock/ingredients` : `[{ id, name, unit, unitCostMilliCents, isPreparation, yieldMilli, allergenMask, lowStockMilli, stockMilli, isActive }]` (`stockMilli` rempli au lot C).
- `POST /api/stock/ingredients`, `PUT /api/stock/ingredients/{id}`, `DELETE /api/stock/ingredients/{id}` (409 `{"code":"ingredient_in_use","products":[…]}`).
- `GET /api/catalog/products/{id}/recipe` et `PUT` : `{ "lines": [{ "ingredientId": "…", "quantityMilli": 200000 }] }`. 400 `{"code":"recipe_cycle"}` si cycle ou profondeur dépassée. La réponse inclut `costCents` et `allergenMask` déduit.
- `GET /api/menu-analysis` : même forme ; le coût utilisé est celui de la fiche quand elle existe.

## Stock (lot C)

- `POST /api/stock/receipts` : `{ "ingredientId", "quantityMilli", "unitCostMilliCents", "note" }`. Crée un mouvement `receipt` et recalcule le prix moyen.
- `POST /api/stock/counts` : `{ "ingredientId", "countedMilli", "note" }`. Crée un mouvement `count` égal à l'écart (comptage moins stock théorique) ; 200 avec l'écart et sa valeur.
- `POST /api/stock/losses` : `{ "ingredientId", "quantityMilli", "note" }`.
- `GET /api/stock/movements?ingredientId=&from=&to=` : historique.
- `GET /api/stock/alerts` : ingrédients sous le seuil et écarts d'inventaire récents, utilisé par la cloche « à traiter » (type `lowStock`).
- Annulation d'un reçu : le corps de `POST /api/checkout/void/{id}` gagne `restock` (booléen, défaut faux).

- `GET/PUT /api/catalog/modifier-options/{id}/consumption` : `{ "items": [{ "ingredientId", "deltaQuantityMilli" }] }` (responsable). Ajoute (ou retire si négatif) une quantité par portion vendue avec cette option.
- `POST /api/checkout/void/{id}` : corps `{ operatorId, restock }`.
- Un ingrédient qui a des mouvements ne se supprime pas (409 `ingredient_has_movements`) : le désactiver. Seuls les ingrédients bruts sont suivis (400 `ingredient_invalid` pour une préparation).

## Réglages

`GET/PUT /api/stock/settings` : `{ autoUnavailableOnStockOut, printAllergensOnKitchenTicket, allergenDisclaimer }` (responsable). Ils ne passent **pas** par `/api/settings`, dont le corps est signé dans le journal fiscal.

## Fixtures capturées (à recapturer si les formes changent)

`ios/Packages/PosKit/Tests/PosKitTests/Fixtures/` : `products_with_allergens.json`, `ingredients.json`, `recipe.json`, `stock_alerts.json` (capturées) et `recipe_cost_reference.json` (générée, `WRITE_RECIPE_FIXTURE=1`).
