# Feature Specification: Modern Reactive iPad POS Interface

**Feature Branch**: `023-modern-ipad-ui`

**Created**: 2026-09-23

**Status**: Draft

**Input**: User description: "je veux une nouvelle interface Ipad moderne et reactive"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - High-Velocity Tactile Core Ordering (Priority: P1)

As a Waiter or Cashier, I want to use a highly responsive, touch-optimized ordering interface on the iPad so that I can process customer orders rapidly without UI lag or mis-taps during rush hours.

**Why this priority**: Fundamental to POS operations. If the core ordering experience isn't fast, tactile, and reliable, the whole application loses its primary value.

**Independent Test**: Can be fully tested by simulating a 10-item complex order during rush hour using only touch interactions directly on the iPad screen.

**Acceptance Scenarios**:

1. **Given** I am in the main POS view, **When** I tap a product category then a product (e.g., "Mains" -> "Burger"), **Then** the product appears instantly in the cart with visual/haptic feedback (< 50ms response).
2. **Given** a product is in the cart, **When** I swipe left on the item, **Then** it is instantly removed/voided from the cart without opening secondary menus.
3. **Given** a product in the cart that supports doneness or modifiers, **When** I long-press (Haptic Touch) the item, **Then** the modifier flyout appears.

---

### User Story 2 - Integrated POS Keypad Integration (Priority: P2)

As a POS Operator, I want to use an integrated on-screen custom numeric keypad for all numerical inputs (quantities, custom prices, PIN codes) so that the native iPadOS virtual keyboard does not pop up and obscure my cart or totals.

**Why this priority**: The native iOS keyboard disrupts the visual flow and covers crucial financial totals, leading to frustrating multi-step interactions to dismiss it.

**Independent Test**: Can be tested by placing focus on a quantity or custom price field and verifying the internal keypad handles input without triggering the OS keyboard.

**Acceptance Scenarios**:

1. **Given** the active order cart, **When** I tap to change an item's quantity, **Then** the custom POS numeric keypad is presented (or natively integrated) without invoking the system keyboard.
2. **Given** a custom price or discount entry, **When** I use the integrated keypad, **Then** the cart recalculates in real-time as I type.

---

### User Story 3 - Adaptive Environment Theming (Priority: P3)

As a Waiter moving between different environments (dimly lit bar vs bright outdoor terrace), I want the interface to support both high-contrast Light mode and immersive Dark mode, so that screen glare is minimized and readability is preserved.

**Why this priority**: iPads are used in variable lighting conditions in restaurants; hardcoded themes cause eye strain and usability issues.

**Independent Test**: Fully tested by toggling iPadOS theme settings and verifying the POS interface instantly applies the corresponding color palette without a restart.

**Acceptance Scenarios**:

1. **Given** the POS running in Light mode, **When** the environment changes into dim light or the OS triggers Dark Mode, **Then** the UI transitions smoothly to the Dark theme palette.
2. **Given** an outdoor glare environment, **When** using Light mode, **Then** high-contrast text and prominent element borders ensure critical data (totals, buttons) remain legible.

### Edge Cases

- What happens when the user types rapidly on the integrated keypad faster than UI updates? (Inputs must buffer and never drop characters).
- How does system handle long-pressing an item that has no modifiers configured? (Provide default feedback, e.g., a mild haptic 'bloop' with no flyout).
- What happens if the iPad is rotated rapidly (Portrait vs Landscape)? ([NEEDS CLARIFICATION: Does the UI fully support both Portrait and Landscape, or is it strictly locked to Landscape mode?])

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide an interface optimized entirely for tactile touch (minimum touch targets 54x54 pt, 68x68 pt for high-velocity buttons like payment validation).
- **FR-002**: System MUST completely suppress system virtual keyboards for numerical POS inputs, surfacing a custom integrated numeric keypad instead.
- **FR-003**: System MUST implement tactile gestures globally across the cart workflow (Swipe to remove, Long-press for modifiers).
- **FR-004**: System MUST emit sub-50ms visual and haptic feedback upon every primary FOH interaction.
- **FR-005**: System MUST support dynamic light/dark theming reacting to OS-level environment triggers or manual toggles.
- **FR-006**: System MUST dynamically adapt the layout for left-handed and right-handed operators, easily mirroring the cart and numeric keypad position to match user dominance.
- **FR-007**: System MUST enforce an exclusive Full Screen (Kiosk) mode, disregarding or restricting iOS multitasking (Split View / Slide Over) to maximize stability and prevent accidental exits during ordering.

### Key Entities *(include if feature involves data)*

- **PosThemeProfile**: Represents the active color palette, spacing, and typography multipliers (Light/Dark/High Contrast).
- **OperatorSession**: Tracks active operator context, including UI layout preferences (handedness) if applicable.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 95% of FOH button interactions register visual/haptic feedback in under 50ms.
- **SC-002**: Operators can process a standard 5-item order 15% faster compared to the previous GUI iteration.
- **SC-003**: Zero appearances of the native iOS virtual keyboard during core ordering and checkout flows.
- **SC-004**: The application maintains a buttery smooth 60fps (or 120fps on ProMotion iPad Pro displays) during intense cart scrolling and animations.

## Assumptions

- We are targeting iPadOS 17+ and .NET MAUI as specified by the Constitution.
- Physical Apple keyboards are not attached by FOH operators during standard service.
- iPads utilized are standard consumer or enterprise models (10.9" to 13"), not miniaturized smartphone screens.
