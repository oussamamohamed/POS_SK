# Quickstart & Validation Guide: Flutter Migration

## Prerequisites
- Flutter SDK 3.x installed (`flutter doctor` must report green for iOS)
- Xcode 15+ installed for iOS compilation
- The ASP.NET Core Mock Backend running locally (`dotnet run --project src/RestaurantPos.Application`)

## 1. Bootstrap and Launch

Run the freshly scaffolded Flutter Client:

```bash
cd src/RestaurantPos.Client.Flutter
flutter pub get
flutter run -d "iPad Pro"
```

## 2. Validation Scenarios

### Scenario A: Kiosk & Haptics Verification
1. Observe the app launches in Fullscreen Landscape.
2. Tap numeric keypad digits.
3. **Expected**: Sub-50ms haptic feedback is felt (or logged in console if on simulator) cleanly separated from the logical `NumpadDigitPressedEvent`.

### Scenario B: Offline Cart Operations (BLoC)
1. Add an item using the tactical menu.
2. Swipe right-to-left on the Cart record.
3. Tap "Supprimer".
4. **Expected**: The BLoC emits a new state perfectly recalculating `totalTtcCents` down to 0, validating that state mutations exactly mirror the old `ObservableObject` architecture.

### Scenario C: Handedness Toggle
1. Toggle Handedness mode in the mock settings.
2. **Expected**: The Flutter `Row` or `Grid` swaps children dynamically, placing the cart on the right and products on the left.
