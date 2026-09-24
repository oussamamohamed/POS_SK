# Phase 1: Data Model Updates

The core domain transactional models (Orders, Subtotals, VAT) remain untouched to preserve business logic integrity. The changes for this feature introduce specialized UI state models in the Presentation layer.

## 1. PosUiProfile (UI Layer Entity)
Represents the operator's personalized display preferences stored locally.
- `HandednessMode` (Enum: `RightHanded` [default], `LeftHanded`) - Drives the Grid column logic.
- `ThemePreference` (Enum: `System`, `Light`, `Dark`) - Drives the active `AppThemeBinding` overrides.

## 2. InputBufferState (ViewModel State)
Represents the state of the custom numeric keypad.
- `CurrentBuffer` (string): The raw numerical input currently typed.
- `ActiveField` (Enum): Context of what the keypad is targeting (e.g., `Quantity`, `CustomPrice`, `Barcode`).

## 3. Gestural State Transitions
- **Swipe-to-Delete**: Transitioning `CartItem` -> `IsSwipePending` -> `True` (reveals delete button).
- **Long-Press**: Transitioning `SelectedProduct` -> Triggers `ModifierFlyoutState.Open`.
