# Feature Specification: Flutter / Dart Client Migration

**Feature Directory**: `specs/024-flutter-migration`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "Créer la spécification technique pour isoler et migrer l'application iOS MAUI vers un projet Flutter / Dart avec BLoC."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Bootstrap Flutter Client Application (Priority: P1)

The development team must initialize a clean Flutter project structure tailored for the iPad POS application. This includes setting up the fundamental Flutter architecture using the BLoC (Business Logic Component) pattern for robust, predictable state management. The foundational project must run successfully on an iPad simulator in landscape mode.

**Why this priority**: Without the base Flutter project scaffolding and state management paradigm (BLoC) locked in, no view or data migration can occur.

**Acceptance Criteria**:
- A base Flutter project builds and launches successfully on an iPad simulator.
- The root layout enforces Landscape orientation and Kiosk mode (fullscreen).
- A base BLoC observer is implemented to track state transitions.

### User Story 2 - UI & Component Translation (Priority: P2)

The user (waiter/cashier) interacts with the exact same visual components on the Flutter application as they did on the MAUI application. The cart layout, the custom tactile numeric keypad, and the touch-first gestures (Swipe-to-delete, Haptic feedback) are flawlessly translated into Flutter Widgets.

**Why this priority**: Replicating the established UI logic (previously verified in MAUI) guarantees that the ergonomic and tactile guidelines of the project constitution are respected during the technological switch.

**Acceptance Criteria**:
- The main ordering split-view (Categories on the right, Cart on the left) is visually identical.
- The custom numeric keypad behaves as an overlay/widget substituting default iOS keyboards.
- Swiping items on the cart triggers deletion gestures.
- Sub-50ms haptic feedback executes on button presses.

### User Story 3 - Data Persistence & Backend Integration (Priority: P3)

The application synchronizes data securely with the central POS backend. It utilizes local SQLite (via `sqflite`) for the offline-first transactional integrity and connects to real-time KDS (Kitchen Display) via WebSockets/SignalR equivalents in Dart.

**Why this priority**: Completes the functional parity of the POS by bridging the visual components to the critical backend architecture rules (Offline-first, immutable journals, SignalR table limits).

**Acceptance Criteria**:
- Offline cart additions are persisted to the internal Flutter SQLite database.
- Synchronization works reliably when backend mock server status is set to online.
- All offline/online boundaries strictly replicate the identical MVVM workflows originally written in C#.

## Functional Requirements *(mandatory)*

1. The client POS application must be completely decoupled from the C# `.NET` MAUI build chain. It must compile via `flutter build ios`.
2. State management must strictly implement the BLoC pattern (separating UI widgets from business logic streams).
3. The system must process real-time communication events via a Dart/Flutter SignalR client package to integrate with the existing `.NET 9` TableHub.
4. The system must abstract native platform hooks (Haptics, Device profiles, Screen sleep constraints) through Dart platform channels or community plugins.

## Non-Functional Requirements *(optional)*

- **Performance**: Tactile response times (touch-to-render) must remain strictly under 50ms, conforming to the project standard.
- **Portability**: Code structure should maintain multiplatform compatibility (Flutter) natively prioritizing iPadOS.
- **Maintainability**: Provide strict mapping documentation explaining how concepts from the old C# Clean Architecture map to the new Dart/BLoC structure.

## Assumptions & Dependencies *(optional)*

- **Dependency**: The backend C# ASP.NET Core project remains exactly as is. We rely exclusively on its REST / SignalR endpoints.
- **Dependency**: Dart ecosystem plugins (`sqflite` for DB, `signalr_netcore` or similar for WebSockets) fully support the intricate requirements of the POS system.
- **Assumption**: The existing C# MAUI test suites can be deprecated or strictly translated into Flutter Widget/Mock tests (`flutter test`).

## Success Criteria *(mandatory)*

1. Flutter application successfully launches, identical to the MAUI iteration on an iPad Pro 13-inch Simulator.
2. 100% of the User Stories defined in the original iPad UI specification function perfectly.
3. The Flutter application operates fully offline (local orders saved/persisted without crash).
4. The `RestaurantPos.Client.Maui` project is safely archivable without disrupting the backend solution compilation.

