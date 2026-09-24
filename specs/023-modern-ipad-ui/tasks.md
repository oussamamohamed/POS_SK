# Tasks: Modern Reactive iPad POS Interface

**Input**: Design documents from `/specs/023-modern-ipad-ui/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/ui-contracts.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [x] T001 [P] Extract updated `IPlatformEnvironmentService` and `INumericKeypadReceiver` interface contracts into `src/RestaurantPos.Client.Maui/Contracts/IPlatformEnvironmentService.cs` (and equivalent Keypad contract file).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

- [x] T002 [P] Update `src/RestaurantPos.Client.Maui/Platforms/iOS/Info.plist` and `MacCatalyst/Info.plist` to enforce `UIRequiresFullScreen` (Kiosk mode) and restrict `UISupportedInterfaceOrientations` to Landscape.
- [x] T003 Create `PosUiProfile` model in `src/RestaurantPos.Client.Maui/Models/PosUiProfile.cs` to hold Handedness and Theme descriptors.
- [x] T004 Create `InputBufferState` view-model record in `src/RestaurantPos.Client.Maui/ViewModels/InputBufferState.cs`.

**Checkpoint**: Foundation ready - basic configuration is locked.

---

## Phase 3: User Story 1 - High-Velocity Tactile Core Ordering (Priority: P1) 🎯 MVP

**Goal**: Highly responsive touch ordering with Swipe / Long-press interactions and Sub-50ms Haptics.

**Independent Test**: Can be independently verified by swiping items and invoking the modifier flyout using touch gestures on the Simulator.

### Implementation for User Story 1

- [x] T005 [P] [US1] Implement native Haptic Feedback using `Microsoft.Maui.Devices.HapticFeedback` in `src/RestaurantPos.Client.Maui/Services/PlatformEnvironmentService.cs`.
- [x] T006 [US1] Bind SwipeView / contextual swipe actions for Cart Item deletion in `src/RestaurantPos.Client.Maui/Views/PosTerminalPage.xaml`.
- [x] T007 [US1] Implement Long-Press recognizing (or TapGesture with multple taps if LongPress implies custom drawing) to trigger `ModifierFlyoutState.Open` in `src/RestaurantPos.Client.Maui/Views/PosTerminalPage.xaml`.
- [x] T008 [US1] Wire the Haptic Feedback service to invoke on all critical Command actions in `src/RestaurantPos.Client.Maui/ViewModels/PosTerminalViewModel.cs`.

**Checkpoint**: At this point, User Story 1 should be fully functional (tactile gestures work and trigger haptics).

---

## Phase 4: User Story 2 - Integrated POS Keypad Integration (Priority: P2)

**Goal**: Integrated on-screen numeric keypad substituting the iOS soft keyboard to avoid screen occlusion.

**Independent Test**: Clicking a quantity invokes the custom keypad and inputs calculate without bringing up the iOS keyboard.

### Implementation for User Story 2

- [x] T009 [P] [US2] Create the Custom Keypad UI component in `src/RestaurantPos.Client.Maui/Views/Components/NumericKeypad.xaml` and its code-behind.
- [x] T010 [US2] Update `src/RestaurantPos.Client.Maui/ViewModels/PosTerminalViewModel.cs` to handle `InputBufferState` operations (append digit, clear, backspace).
- [x] T011 [US2] Replace native `Entry` elements for quantities/custom prices with read-only mocked `Label` controls bound to the InputBuffer in `src/RestaurantPos.Client.Maui/Views/PosTerminalPage.xaml`.

**Checkpoint**: At this point, User Stories 1 AND 2 should work independently without any OS keyboard popping up.

---

## Phase 5: User Story 3 - Adaptive Environment Theming (Priority: P3)

**Goal**: Handedness layout toggle (left/right grid mirrored layout) and Light/Dark dynamic themes.

**Independent Test**: Toggling the Handedness switch reverses the layout Grid. OS Theme toggles switch from Light to Dark.

### Implementation for User Story 3

- [x] T012 [P] [US3] Extract hardcoded colors and setup DynamicResource variables (Light/Dark variants) in `src/RestaurantPos.Client.Maui/Resources/Styles/Colors.xaml` and `Styles.xaml`.
- [x] T013 [US3] Add `IsLeftHandedMode` toggle property and logic to `src/RestaurantPos.Client.Maui/ViewModels/PosTerminalViewModel.cs`.
- [x] T014 [US3] Update `Grid.Column` bindings and/or `FlowDirection` on the main container in `src/RestaurantPos.Client.Maui/Views/PosTerminalPage.xaml` to react to `IsLeftHandedMode`.

**Checkpoint**: The iPad layout is now fully dynamic and environmentally adaptive.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [x] T015 [P] Add unit tests for Keypad and Handedness logic updates in `tests/RestaurantPos.Client.Maui.Tests/PosTerminalViewModelTests.cs`.
- [x] T016 Run Quickstart validation scenarios defined in `specs/023-modern-ipad-ui/quickstart.md` locally via Simulator.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup completion.
- **User Stories (Phase 3+)**: All depend on Foundational phase completion.
- **Polish (Final Phase)**: Depends on all desired user stories being complete.

### User Story Dependencies

- **User Story 1 (P1)**: High velocity UI basics (can be tested manually immediately).
- **User Story 2 (P2)**: Overlays custom input directly, enhancing MVP. Independent of US1.
- **User Story 3 (P3)**: Layout structure scaling. Independent of US1/US2.

### Parallel Opportunities

- Foundational `Info.plist` changes can be done concurrently by any member while models are being drafted.
- Keypad UI (T009) and DynamicResource extraction (T012) can be accomplished concurrently as they touch completely distinct files from the main Page logic.

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 & 2.
2. Complete Phase 3 (US1).
3. Validate tactile gestures and haptics. (MVP delivered: better core UX).

### Incremental Delivery

1. Integrate the `NumericKeypad.xaml` (US2) to eliminate the OS keyboard constraint. Validate.
2. Add Handedness layout mirror and dynamic colors (US3).
3. Clean up through Polish tests.
