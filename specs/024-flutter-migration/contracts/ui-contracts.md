# Flutter UI / BLoC Contracts

## 1. PosTerminalEvent
All UI actions emitted from the Views towards the BLoC.

```dart
abstract class PosTerminalEvent extends Equatable {
  const PosTerminalEvent();
}

class AddProductEvent extends PosTerminalEvent {
  final Product product;
  const AddProductEvent(this.product);
}

class NumpadDigitPressedEvent extends PosTerminalEvent {
  final int digit;
  const NumpadDigitPressedEvent(this.digit);
}

class RemoveCartItemEvent extends PosTerminalEvent {
  final OrderItem item;
  const RemoveCartItemEvent(this.item);
}
//...
```

## 2. Kiosk Configuration (Info.plist)
Even in Flutter, the underlying iOS Runner must enforce the kiosk behavior via native contracts defined in `.specify/memory/constitution.md`.

```xml
<key>UIRequiresFullScreen</key>
<true/>
<key>UISupportedInterfaceOrientations</key>
<array>
    <string>UIInterfaceOrientationLandscapeLeft</string>
    <string>UIInterfaceOrientationLandscapeRight</string>
</array>
```
