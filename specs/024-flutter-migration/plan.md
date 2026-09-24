# Implementation Plan: Flutter / Dart Client Migration

**Branch**: `024-flutter-migration` | **Date**: 2026-09-23 | **Spec**: [spec.md](../spec.md)

**Input**: Feature specification from `specs/024-flutter-migration/spec.md`

## Summary

The `RestaurantPos.Client.Maui` project will be replaced entirely by a brand-new Flutter/Dart project utilizing the BLoC pattern for reactivity. The goal is to perfectly replicate the ergonomic tactile POS interface (fixed Keypad, Swipe-to-delete, Haptics) while enforcing offline-first capabilities (`sqflite`) and SignalR socket streaming (`signalr_netcore`).

## Technical Context

**Language/Version**: Dart 3.x, Flutter 3.x

**Primary Dependencies**: flutter_bloc, sqflite, signalr_netcore, equatable, haptic_feedback

**Storage**: SQLite local database (via sqflite plugin) for offline event-sourcing/transactions

**Testing**: flutter test (Widget testing for components, Mocktail/Mockito for BLoC testing)

**Target Platform**: iPadOS 17+ (Kiosk mode strictly enforced)

**Project Type**: Mobile Tablet Application

**Performance Goals**: Tactile response < 50ms, rendering locked at 60/120fps (ProMotion limit)

**Constraints**: Must match exact MAUI UX specifications, disconnected offline operation capability

**Scale/Scope**: Replacement of 100% of the MAUI Client scope (Views, ViewModels, Offline Journal, API Handlers)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Touch-First Ergonomics**: Replicated identically via Flutter gestures.
- [x] **Clean Architecture**: Refactored logic to isolated BLoCs away from Widget Views.
- [x] **Transactional Integrity (Offline-First)**: Ported using `sqflite`. No external network calls block UI mutations.
- [x] **mDNS Discovery & SignalR Sync**: Retained via `signalr_netcore` library bridging to .NET 9 server.
- [x] **Test-First & Tracey**: Flutter testing encompasses BLoC logic completely.

## Project Structure

### Documentation (this feature)

```text
specs/024-flutter-migration/
├── plan.md              
├── research.md          
├── data-model.md        
├── quickstart.md        
├── contracts/           
└── tasks.md             
```

### Source Code (repository root)

```text
src/RestaurantPos.Client.Flutter/
├── lib/
│   ├── core/
│   │   ├── extensions/
│   │   ├── theme/
│   │   └── utils/
│   ├── data/
│   │   ├── local_db/
│   │   ├── models/
│   │   └── network/
│   ├── domain/
│   │   ├── entities/
│   │   └── repositories/
│   └── presentation/
│       ├── blocs/
│       │   ├── pos_terminal/
│       │   └── sync/
│       ├── widgets/
│       │   ├── numeric_keypad.dart
│       │   └── cart_swipe_item.dart
│       └── pages/
│           ├── pos_terminal_page.dart
│           └── kds_page.dart
test/
├── presentation/
├── domain/
└── data/
```

**Structure Decision**: A dedicated `RestaurantPos.Client.Flutter` directory is initialized within `src/` to strictly separate it from the old MAUI code until the transition is 100% functional, enabling Clean Architecture layering within the Flutter tree.
