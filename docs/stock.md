# Stock, fiches techniques et allergènes (spec 023)

## Allergènes et ruptures
- 14 allergènes en bits 0 à 13 (`Allergens`, miroir `Allergens.swift`) : `gluten, crustaceans, eggs, fish, peanuts, soy, milk, nuts, celery, mustard, sesame, sulphites, lupin, molluscs`. `AllergensDeclared` faux = « non renseigné » (≠ « aucun »). `AllergensOverridden` : saisie manuelle, jamais écrasée par la déduction depuis une fiche.
- Rupture manuelle = `Product.IsAvailable` (`PUT /api/catalog/products/{id}/availability`, tout opérateur) ; ajout refusé en 409 `product_unavailable` ; événement SignalR `OnProductAvailabilityChanged(productId, isAvailable)` sur `/hubs/pos`.
- Rupture **automatique** (option `AutoUnavailableOnStockOut`) : un plat dont un ingrédient suivi (au moins un mouvement) a un stock ≤ 0 passe `IsAvailable = 0` et `AutoUnavailable = 1` ; il redevient disponible quand le stock revient. Une rupture manuelle n'est jamais levée par le stock. Un ingrédient jamais reçu n'éteint rien.
- Option « allergènes sur le ticket de cuisine » (`PrintAllergensOnKitchenTicket`, défaut faux) : une ligne par plat (« non renseignés » s'imprime, « aucun » n'ajoute rien) et une mention légale facultative en bas.

## Unités et arrondis
- Quantités en **millièmes** d'unité de base (`g`, `ml`, `piece`), prix en **milli-centimes** par unité de base. Saisie : € par kg, l ou pièce (`× 100` pour g et ml, `× 100 000` pour une pièce).
- Coût d'une fiche = Σ quantité × prix / 1 000 000, arrondi **une seule fois** au demi supérieur. Prix d'une préparation = coût de sa fiche / rendement, arrondi au demi supérieur par unité de base. Profondeur 5 au plus, cycle refusé (`recipe_cycle`, `recipe_depth`).
- Sommes sur 128 bits (`Int128` côté .NET, `U128` maison côté Swift : `Int128` manque à iOS 17). Fixture `recipe_cost_reference.json` générée par le .NET.

## Consommation
- À l'**encaissement soldé** (point unique : `order.Status = Paid` dans `CheckoutPaymentService`, `closeOrder(.paid)` côté iPad), dans la transaction du règlement. Un mouvement `sale` par (commande, ingrédient) — index unique partiel, donc idempotent. Les lignes offertes ou remisées sont consommées. La consommation ne lève jamais d'erreur qui annulerait la vente, et ne touche à aucune chaîne fiscale.
- Les préparations ne sont pas suivies : la vente consomme les ingrédients **bruts** (`RecipeExpander`, part = Q × qi / rendement arrondie).
- Modificateurs : `ModifierOptionConsumption` ajoute (ou retire si négatif) une quantité par portion ; l'option est retrouvée par son nom parmi les groupes du produit.
- Avoir : le stock n'est restitué que si `restock` est vrai (mouvement `saleReversal`). Sans la case, l'avoir ne change rien (produit jeté ou consommé).
- Stock négatif toléré (signalé par l'alerte). Un ingrédient qui a des mouvements ne se supprime pas (409 `ingredient_has_movements`) : on le désactive.

## Mouvements (ajout seulement)
`StockMovements` : `sale`, `saleReversal`, `receipt` (prix moyen pondéré reporté sur l'ingrédient), `count` (écart = comptage − stock théorique, valorisé), `loss`. Déclencheurs SQL `BEFORE UPDATE/DELETE` → `stock_movements_append_only` (`StockSchema.Statements`, serveur et iPad V15). Stock courant = somme des mouvements (vue `IngredientStock`).

## API
`/api/stock/ingredients` (CRUD, `stockMilli`), `/api/stock/ingredients/{id}/recipe` (préparation), `/api/catalog/products/{id}/recipe`, `/api/stock/{receipts,counts,losses,movements,alerts}`, `/api/stock/settings` (**hors** `/api/settings`, dont le corps est signé dans le journal fiscal), `/api/catalog/modifier-options/{id}/consumption`, `POST /api/checkout/void/{id}` avec `restock`. Tout est réservé aux responsables sauf la rupture manuelle.

## Périmètre
Un seul dépôt (pas de multi-stock), pas de commandes fournisseur, pas de DLC/lots. La consommation a lieu au règlement, pas à l'envoi en cuisine : le stock baisse avec le retard d'un repas (limite connue).

## Écrans
- **Annulation de reçu** avec la case « produit non consommé » (`restock`) : web (Encaissements → liste des tickets, bouton « Annuler », `openVoidReceipt`) et iPad (`VoidReceiptSheet`, `PosAPI.voidReceipt(receiptId:restock:)`, `TakingsStore.void`). Un reçu ne s'annule que depuis le poste qui l'a émis (règle serveur).
- **Consommation des options de modificateur** : web (éditeur d'article, `openOptionConsumption`) et iPad (`OptionConsumptionSection` dans l'éditeur d'article), via `GET/PUT /api/catalog/modifier-options/{id}/consumption` (`PosAPI.modifierConsumption` / `setModifierConsumption`).
