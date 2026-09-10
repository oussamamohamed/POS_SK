# Contract: Dish Modifiers, Cooking Options & Extra Charges

**Feature**: `004-phase2-order-floorplan-modifiers`
**Domain**: Product Modifiers & Kitchen Instructions

## 1. Modifier Selection Models & Contract

```csharp
namespace RestaurantPos.Domain.ValueObjects;

public readonly record struct SelectedModifier(
    Guid OptionId,
    string OptionName,
    long ExtraPriceCents);

public interface IModifierValidationService
{
    bool ValidateSelection(
        int minSelections, 
        int maxSelections, 
        IReadOnlyList<SelectedModifier> selectedOptions, 
        out string? validationError);
}
```

## 2. Line Item Pricing Formula with Modifiers

$$\text{LineTotalCents} = (\text{BasePriceCents} + \sum \text{ExtraPriceCents}) \times \text{Quantity}$$
$$\text{TaxAmountCents} = \text{LineTotalCents} - \left\lfloor \frac{\text{LineTotalCents}}{1 + \frac{\text{TaxRatePercent}}{100}} + 0.5 \right\rfloor$$
