# Quickstart: iPad UI Validation

This guide explains how to validate the new Modern Reactive iPad POS interface locally using the iOS Simulator.

## Prerequisites
- macOS host machine with Xcode installed.
- .NET 9 SDK and MAUI Workloads (`dotnet workload install maui-ios`).
- A configured iPad Simulator (e.g., "iPad Pro (11-inch) (M4)").

## 1. Launching the Simulator
Build and run the MAUI Client specifically targeting the iPad simulator:

```bash
cd src/RestaurantPos.Client.Maui
dotnet build -t:Run -f net9.0-ios -p:_DeviceName="iPad Pro (11-inch) (M4)"
```

## 2. Validation Scenarios

### A. Kiosk Mode & Orientation
1. Attempt to rotate the iPad simulator to Portrait (`Cmd + Left Arrow`).
   - **Expected**: The POS interface remains locked in Landscape orientation.
2. Attempt to open a second app in Split View (using the multitasking dots at the top).
   - **Expected**: Split View and Slide Over are unavailable (multitasking restricted).

### B. Input Suppression & Keypad
1. Tap on the quantity multiplier or a custom price input field in the cart.
   - **Expected**: The system grey digital keyboard **does not** pop up from the bottom.
2. Tap digits on the integrated POS Numeric Keypad.
   - **Expected**: The field updates instantly.

### C. Handedness Toggle
1. Navigate to the POS Settings panel.
   - **Expected**: "Left-Handed Mode" switch is visible.
2. Toggle the switch.
   - **Expected**: The main cart and keypad instantly jump to the left side of the screen, and the product catalog moves to the right. 

### D. Tactile Gestures
1. Add an item to the cart. Swipe left on the item row.
   - **Expected**: A void/delete action is revealed instantly.
2. Long-press (click and hold) a product catalog item.
   - **Expected**: A modifier or doneness flyout appears with haptic feedback (macOS tracks pad feedback or visible log depending on simulator settings).
