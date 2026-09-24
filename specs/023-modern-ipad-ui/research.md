# Phase 0: Research & Technical Approach

## 1. System OS Virtual Keyboard Suppression
**Decision**: Use read-only bound `Label` controls styled to look like inputs, or use MAUI Handler modifications to natively suppress the soft keyboard on `Entry` fields while keeping them focused (via `UIKeyInput` dummy tricks in iOS).
**Rationale**: The iPadOS virtual keyboard covers half the screen, ruining POS ergonomics. A custom XAML numeric keypad (`NumericKeypad.xaml`) binds directly to the ViewModel's `ActiveInputBuffer`, rendering OS keyboards completely unnecessary.
**Alternatives considered**: Setting `IsReadOnly="True"` on `Entry` fields (can break focus workflows), or dismissing the keyboard constantly (causes visual glitching / bouncing).

## 2. Kiosk Mode and Multitasking Prevention
**Decision**: Configure iOS `UIRequiresFullScreen` to `true` in `Info.plist` to disable Split View and Slide Over multitasking. Full MDM Single App Mode (Guided Access) will be documented as the deployment standard.
**Rationale**: `UIRequiresFullScreen` is the official Apple way to opt out of iPad multitasking natively in the app bundle. This satisfies FR-007 (Exclusive Full Screen mode).
**Alternatives considered**: Complex programmatic window resizing blockers (brittle on iPadOS).

## 3. Landscape Orientation Lock
**Decision**: Set `UISupportedInterfaceOrientations` strictly to `UIInterfaceOrientationLandscapeLeft` and `UIInterfaceOrientationLandscapeRight` in the iOS `Info.plist`.
**Rationale**: Adheres to the user's explicit clarification that the UI is strictly locked to Landscape mode to preserve layout stability during rushes.

## 4. Left-Right Handed Dynamic Layout
**Decision**: Use standard MAUI `Grid` column definitions bound to a ViewModel property (`IsLeftHandedMode`), which dynamically swaps the `Grid.Column` attachments of the primary Cart/Keypad column and the Product Catalog column. Alternatively, utilize `FlowDirection="RightToLeft"` on the main container where culturally and practically appropriate.
**Rationale**: Grid column swapping is extremely cheap operationally on MAUI and allows instant mirroring of the interface without reloading the page.
**Alternatives considered**: Maintaining two separate XAML pages (too much maintenance overhead). 

## 5. Sub-50ms Haptic Feedback
**Decision**: Use `Microsoft.Maui.Devices.HapticFeedback.Perform(HapticFeedbackType.Click)` inside the platform environment abstraction securely bound to the `ICommand` execution pipeline.
**Rationale**: Built-in native API wrapper providing zero-latency integration with the iPad's Taptic Engine.
