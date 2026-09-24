# Tasks: Flutter / Dart Client Migration

**Input**: Design documents from `specs/024-flutter-migration/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contracts.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [x] T001 Create project directory `src/RestaurantPos.Client.Flutter`
- [x] T002 Initialize Flutter project targeting iOS (`flutter create --platforms=ios --project-name=restaurantpos_client src/RestaurantPos.Client.Flutter`)
- [x] T003 [P] Add dependencies to `pubspec.yaml` (flutter_bloc, equatable, sqflite, signalr_netcore, haptic_feedback)
- [x] T004 [P] Apply Kiosk configuration (Landscape + Fullscreen) to `src/RestaurantPos.Client.Flutter/ios/Runner/Info.plist`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

- [x] T005 [P] Setup core architecture folders in `src/RestaurantPos.Client.Flutter/lib/` (core, data, domain, presentation)
- [x] T006 [P] Create domain entities from data-model.md in `src/RestaurantPos.Client.Flutter/lib/domain/entities/order_item.dart`
- [x] T007 [P] Create state model `PosTerminalState` in `src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal_state.dart`
- [x] T008 [P] Create base events from ui-contracts.md in `src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal_event.dart`
- [x] T009 Build foundational `PosTerminalBloc` mapping events to state in `src/RestaurantPos.Client.Flutter/lib/presentation/blocs/pos_terminal_bloc.dart`

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Bootstrap Flutter Client Application (Priority: P1) 🎯 MVP

**Goal**: A base Flutter project builds and launches successfully on an iPad simulator enforcing orientation and injecting the BLoC structure.

**Independent Test**: The app launches perfectly in Landscape mode with an empty BLoC observer tracking state.

### Tests for User Story 1

- [x] T010 [P] [US1] Unit test for initial `PosTerminalBloc` state in `src/RestaurantPos.Client.Flutter/test/presentation/pos_terminal_bloc_test.dart`

### Implementation for User Story 1

- [x] T011 [US1] Implement custom `BlocObserver` for tracking transitions in `src/RestaurantPos.Client.Flutter/lib/core/utils/pos_bloc_observer.dart`
- [x] T012 [US1] Wrap `MaterialApp` with `BlocProvider` in `src/RestaurantPos.Client.Flutter/lib/main.dart`
- [x] T013 [US1] Build skeleton `PosTerminalPage` listening to `PosTerminalBloc` in `src/RestaurantPos.Client.Flutter/lib/presentation/pages/pos_terminal_page.dart`

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently on the iPad Simulator.

---

## Phase 4: User Story 2 - UI & Component Translation (Priority: P2)

**Goal**: Reproducing the tactile layout, numeric keypad, and haptics translated from MAUI.

**Independent Test**: The user can see the split layout, hit keypad buttons, and swipe items in the cart with haptic feedback logging.

### Tests for User Story 2

- [x] T014 [P] [US2] Widget test for numeric keypad layout in `src/RestaurantPos.Client.Flutter/test/presentation/widgets/numeric_keypad_test.dart`
- [x] T015 [P] [US2] Widget test for Swipe-to-delete gesture in `src/RestaurantPos.Client.Flutter/test/presentation/widgets/cart_swipe_item_test.dart`

### Implementation for User Story 2

- [x] T016 [P] [US2] Implement `NumericKeypad` widget in `src/RestaurantPos.Client.Flutter/lib/presentation/widgets/numeric_keypad.dart`
- [x] T017 [P] [US2] Implement `CartSwipeItem` widget wrapping `Dismissible` in `src/RestaurantPos.Client.Flutter/lib/presentation/widgets/cart_swipe_item.dart`
- [x] T018 [US2] Integrate `NumericKeypad` and `CartSwipeItem` within `PosTerminalPage` layout.
- [x] T019 [US2] Wire native `HapticFeedback.lightImpact()` into BLoC presentation listeners for tactile response.

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently.

---

## Phase 5: User Story 3 - Data Persistence & Backend Integration (Priority: P3)

**Goal**: Introduce SQLite offline persistence and SignalR synchronization for table states.

**Independent Test**: Local SQLite tables are seeded successfully, and the app attempts to open a SignalR connection.

### Tests for User Story 3

- [ ] T020 [P] [US3] Unit test for SQLite initialization in `src/RestaurantPos.Client.Flutter/test/data/local_db_test.dart`
- [ ] T021 [P] [US3] Mock test for SignalR hub connection in `src/RestaurantPos.Client.Flutter/test/data/network_hub_test.dart`

### Implementation for User Story 3

- [x] T022 [P] [US3] Implement `LocalJournalDatabase` using `sqflite` in `src/RestaurantPos.Client.Flutter/lib/data/local_db/local_journal_database.dart`
- [x] T023 [P] [US3] Implement `TableHubClient` using `signalr_netcore` in `src/RestaurantPos.Client.Flutter/lib/data/network/table_hub_client.dart`
- [x] T024 [US3] Create `SyncBloc` to handle background push/pull bridging SQLite to SignalR in `src/RestaurantPos.Client.Flutter/lib/presentation/blocs/sync_bloc.dart`
- [x] T025 [US3] Inject repositories into `main.dart` and bind to UI actions.

**Checkpoint**: All user stories should now be independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [x] T026 [P] Cleanup unused code and enforce dart lint rules (`flutter analyze`)
- [x] T027 Code cleanup and refactoring (resolving layout warnings for older iPads)
- [x] T028 Run quickstart.md validation scenarios strictly

---

## Dependencies & Execution Order

### Phase Dependencies
- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup
- **User Stories (Phase 3+)**: All depend on Foundational phase
- **Polish (Final Phase)**: Depends on all user stories being complete

### User Story Dependencies
- **User Story 1 (P1)**: Independent
- **User Story 2 (P2)**: Integrates UI over US1 structure
- **User Story 3 (P3)**: Integrates persistence behind the UI in US2

