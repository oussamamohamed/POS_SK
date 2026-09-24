# Data Model & BLoC Entities

## Domain Entities (Dart)

```dart
// Enforces exactly the same mapping as the old C# MVVM
class OrderItem extends Equatable {
  final String id; // UUIDv7
  final String productId;
  final String productName;
  final int unitPriceCents;
  final int quantity;
  final List<String> selectedModifiers;
  final String? kitchenComment;
  final bool isDispatched;

  // Equatable overrides to trigger UI rebuilds on property shifts
  @override
  List<Object?> get props => [id, quantity, isDispatched, selectedModifiers, kitchenComment];
}
```

## BLoC State representation

The entire `PosTerminalPage` renders purely as a function of this state.

```dart
class PosTerminalState extends Equatable {
  final List<OrderItem> cartItems;
  final String activeTable;
  final InputBufferState numericBuffer;
  final bool isLeftHandedMode;
  final int totalTtcCents;
  
  const PosTerminalState({
    this.cartItems = const [],
    this.activeTable = '',
    this.numericBuffer = const InputBufferState.empty(),
    this.isLeftHandedMode = false,
    this.totalTtcCents = 0,
  });

  @override
  List<Object?> get props => [cartItems, activeTable, numericBuffer, isLeftHandedMode, totalTtcCents];
}
```
