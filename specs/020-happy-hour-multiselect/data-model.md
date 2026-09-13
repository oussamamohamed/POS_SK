# Data Model: Happy Hour Multi-Select Configuration

## 1. Nouveaux DTOs Applicatifs

### `BatchPriceRulesRequestDto`
Requête de création ou mise à jour groupée de règles pour un planning donné.

```csharp
namespace RestaurantPos.Application.DTOs;

public record BatchPriceRulesRequestDto(
    HappyHourTargetType TargetType,       // 0 = Product, 1 = Category
    List<string> TargetIds,               // Liste des IDs cibles (Guids produits en string ou IDs catégories)
    HappyHourPricingMode PricingMode,     // 0 = FixedPrice, 1 = PercentageDiscount
    decimal? FixedPrice,                  // Montant fixe en euros (si FixedPrice)
    decimal? DiscountPercent              // Pourcentage 0-100 (si PercentageDiscount)
);
```

### `BatchPriceRulesResponseDto`
Réponse après application du lot.

```csharp
public record BatchPriceRulesResponseDto(
    bool Success,
    int AppliedCount,
    Guid ScheduleId,
    string Message
);
```

### `BatchDeleteRulesRequestDto`
Requête de suppression groupée de règles existantes.

```csharp
public record BatchDeleteRulesRequestDto(
    List<Guid> RuleIds                    // Liste des identifiants uniques des règles à supprimer
);
```

### `BatchDeleteRulesResponseDto`
Réponse après suppression du lot.

```csharp
public record BatchDeleteRulesResponseDto(
    bool Success,
    int DeletedCount,
    Guid ScheduleId,
    string Message
);
```

---

## 2. Entités Existantes Mobilisées

### `HappyHourSchedule` (src/RestaurantPos.Domain/Entities/HappyHourSchedule.cs)
- `Id`: Guid
- `Name`: string
- `DaysOfWeek`: List<DayOfWeek>
- `StartTime`: TimeOnly
- `EndTime`: TimeOnly
- `Priority`: int
- `PriceRules`: ICollection<HappyHourPriceRule>

### `HappyHourPriceRule` (src/RestaurantPos.Domain/Entities/HappyHourPriceRule.cs)
- `Id`: Guid
- `ScheduleId`: Guid
- `TargetType`: HappyHourTargetType (`Product = 0`, `Category = 1`)
- `TargetId`: string (Product Guid ou Category string)
- `TargetName`: string
- `PricingMode`: HappyHourPricingMode (`FixedPrice = 0`, `PercentageDiscount = 1`)
- `FixedPrice`: Money?
- `DiscountPercent`: decimal?

---

## 3. Règles de Validation & Invariants Métier

1. `TargetIds` ne doit pas être vide (minimum 1 élément).
2. Si `PricingMode == FixedPrice` :
   - `FixedPrice` doit être strictement supérieur à `0.00 €`.
   - `DiscountPercent` est ignoré / mis à `null`.
3. Si `PricingMode == PercentageDiscount` :
   - `DiscountPercent` doit être strictement compris entre `0.01` et `100.00`.
   - `FixedPrice` est ignoré / mis à `null`.
4. Comportement **Upsert** :
   - Si une règle existe déjà pour le couple `(ScheduleId, TargetType, TargetId)`, ses champs `PricingMode`, `FixedPrice`, `DiscountPercent` sont mis à jour sans créer de nouvelle ligne en base.
   - Si elle n'existe pas, une nouvelle entité `HappyHourPriceRule` est ajoutée.
