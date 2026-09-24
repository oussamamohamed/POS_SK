# Implementation Plan: Modern Reactive iPad POS Interface

**Branch**: `023-modern-ipad-ui` | **Date**: 2026-09-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/023-modern-ipad-ui/spec.md`

## Summary

Build a highly responsive, touch-optimized, Landscape-locked iPadOS POS interface using .NET MAUI and MVVM. It integrates a custom numeric keypad (suppressing the OS virtual keyboard), enforces iPad kiosk (Single App) mode, supports dynamic Light/Dark theming, and enables left/right-handed layout mirroring to ensure <50ms response times during high-velocity FOH rush hours.

## Technical Context

**Language/Version**: C# 13, .NET 9

**Primary Dependencies**: .NET MAUI, CommunityToolkit.Mvvm

**Storage**: SQLite (embedded in iOS sandbox), via Entity Framework Core

**Testing**: xUnit, FluentAssertions, Moq

**Target Platform**: iPadOS 17+ (Apple iPad ecosystem 10.9" - 13")

**Project Type**: mobile-app (Tablet-first UI)

**Performance Goals**: < 50ms tactile/visual feedback, 60fps/120fps display rendering during cart scrolling.

**Constraints**:
- Strict Landscape orientation lock.
- Touch targets min 54x54 pt (68x68 pt for critical actions).
- No system virtual pop-up keyboards (requires custom numeric keypad).
- Kiosk mode enforcement (disabling Split View / Slide Over multitasking).

**Scale/Scope**: FOH Waiters and Cashiers processing hundreds of rush-hour physical transactions per day.

## Constitution Check

*GATE: Passed*

- **I. Touch-First Ergonomics & iPadOS Tactile Design**: 100% compliant. Implements required minimum touch targets, haptic feedback, and custom on-screen keypad to avoid virtual keyboards.
- **II. Clean Architecture**: Compliant. Uses MAUI with `CommunityToolkit.Mvvm`, logic decoupled from platform-specific UI.
- **III, IV, V**: Existing transaction, offline, and fiscal engines remain unchanged and orchestrate beneath this UI layer seamlessly.

## Project Structure

### Documentation (this feature)

```text
specs/023-modern-ipad-ui/
├── plan.md              # This file
├── research.md          # Implementation strategies for UI constraints
├── data-model.md        # UI profiles and active session states
├── quickstart.md        # Running UI tests and iPad Simulator
├── contracts/           # UI Interfaces and layout templates
└── tasks.md             # (To be created via /speckit-tasks)
```

### Source Code (repository root)

```text
src/
└── RestaurantPos.Client.Maui/
    ├── Views/
    │   ├── PosTerminalPage.xaml       # Responsive Grid layout (Handedness aware)
    │   └── Components/
    │       ├── NumericKeypad.xaml     # Custom touch keypad
    │       └── ModifierFlyout.xaml    # Haptic/Long-press modifier sheet
    ├── ViewModels/
    │   └── PosTerminalViewModel.cs    # Command handling, Layout direction states
    └── Services/
        └── PlatformEnvironmentService.cs # Haptics and Kiosk locking APIs

tests/
└── RestaurantPos.Client.Maui.Tests/
    └── PosTerminalViewModelTests.cs
```

**Structure Decision**: Integration into the existing `RestaurantPos.Client.Maui` project, specifically enhancing the `Views`, `ViewModels`, and platform-specific `Services` (iOS/MacCatalyst).
