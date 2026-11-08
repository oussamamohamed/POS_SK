# Modèle de données : Stock, fiches techniques et allergènes

Tous les montants et quantités sont des entiers. Les tables sont créées par SQL idempotent dans `Program.cs` et par les migrations V13 et V14 de l'iPad.

## Products (colonnes ajoutées)

| Colonne | Type | Règle |
|---|---|---|
| `AllergenMask` | INTEGER NOT NULL DEFAULT 0 | bits 0 à 13, ordre figé (voir `Allergens`) |
| `AllergensDeclared` | INTEGER NOT NULL DEFAULT 0 | 0 = non renseigné ; 1 = renseigné |
| `AllergensOverridden` | INTEGER NOT NULL DEFAULT 0 | 1 = saisie manuelle, ignore la déduction des ingrédients |
| `AutoUnavailable` | INTEGER NOT NULL DEFAULT 0 | 1 = `IsAvailable` éteint par le stock (lot C) |

`IsAvailable` existe déjà.

## Ingredients

| Champ | Type | Règle |
|---|---|---|
| `Id` | UUIDv7 | |
| `Name` | texte, unique insensible à la casse | 1 à 80 caractères |
| `Unit` | `g`, `ml` ou `piece` | immuable dès qu'un mouvement existe |
| `UnitCostMilliCents` | INTEGER NOT NULL | ≥ 0, prix d'achat HT par unité de base ; pour une préparation, calculé |
| `IsPreparation` | INTEGER | 1 = a sa propre fiche |
| `YieldMilli` | INTEGER NULL | rendement d'une préparation, > 0 |
| `AllergenMask` | INTEGER | |
| `LowStockMilli` | INTEGER NULL | seuil d'alerte |
| `IsActive` | INTEGER | suppression logique ; suppression refusée si utilisé dans une fiche |

## RecipeLines

| Champ | Type | Règle |
|---|---|---|
| `Id` | UUIDv7 | |
| `OwnerKind` | `product` ou `ingredient` | |
| `OwnerId` | UUID | produit ou préparation |
| `IngredientId` | UUID | composant ; pas de cycle, profondeur 5 au plus |
| `QuantityMilli` | INTEGER | > 0, en millièmes d'unité de base |

Unicité (`OwnerKind`, `OwnerId`, `IngredientId`).

## StockMovements (ajout seulement)

| Champ | Type | Règle |
|---|---|---|
| `Id` | UUIDv7 | |
| `IngredientId` | UUID | |
| `Kind` | `sale`, `saleReversal`, `receipt`, `count`, `loss` | |
| `QuantityMilli` | INTEGER signé | négatif pour une sortie |
| `UnitCostMilliCents` | INTEGER NULL | pour une réception et une valorisation d'écart |
| `OrderId` | UUID NULL | vente et restitution |
| `OperatorId` | UUID | |
| `Note` | texte NULL | |
| `CreatedAtUtc` | texte ISO à la milliseconde | |

Unicité partielle (`OrderId`, `IngredientId`, `Kind`) pour `sale` : garantit l'idempotence.
Déclencheurs `BEFORE UPDATE` et `BEFORE DELETE` : `RAISE(ABORT, 'stock_movements_append_only')`.

Stock courant = somme des `QuantityMilli` (vue `IngredientStock`). Prix moyen pondéré recalculé à chaque réception et reporté sur `UnitCostMilliCents`.

## ModifierOptionConsumption (lot C)

`OptionId`, `IngredientId`, `DeltaQuantityMilli` (signé). Ajoute ou retire une quantité pour chaque option choisie.

## RestaurantSettings (colonnes ajoutées)

`AutoUnavailableOnStockOut` (0), `PrintAllergensOnKitchenTicket` (0), `AllergenDisclaimer` (texte facultatif).

## Transitions

- Produit : disponible ↔ indisponible (manuel) ; disponible → indisponible auto (`AutoUnavailable = 1`) → disponible à la réception.
- Ingrédient : actif → inactif (jamais supprimé s'il a des mouvements).
