# Feature Specification: Phase 2 - Tactile Order Entry, Interactive 2D Floor Plan & Modifiers

**Feature Branch**: `004-phase2-order-floorplan-modifiers`

**Created**: 2026-08-15

**Status**: Draft

**Input**: User description: "phase 2" (From PLAN.md Phase 2: Prise de Commande Tactile, Plan de Salle & Modificateurs)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Interactive 2D Floor Plan Navigation & Real-Time Table Status (Priority: P1)

Waitstaff and floor managers require an interactive 2D floor plan of the restaurant layout displaying tables with real-time status color coding (Free = Green, Occupied = Blue, Bill Requested = Amber, Paid = Gray), allowing staff to select or switch tables in under 100 milliseconds with a single touch.

**Why this priority**: Restaurant servers operate across dozens of dining room tables simultaneously. Visual table status prevents duplicate order taking and speeds up table turnover.

**Independent Test**: Display the 2D floor plan with various table states. Tap different tables; verify that the active table context switches in $< 100\text{ms}$ and opens the corresponding order basket.

**Acceptance Scenarios**:

1. **Given** the 2D floor plan view, **When** tapping an occupied table ("T03"), **Then** the terminal opens the active order for table T03 in $< 100\text{ms}$ with full item history.
2. **Given** a free table ("T02"), **When** tapped by a waiter, **Then** the system prompts for guest covers count (defaulting to table capacity) and creates a new active order.
3. **Given** a table with a requested bill, **When** viewed on the floor plan, **Then** it clearly displays an amber badge and the duration since the bill was printed.

---

### User Story 2 - Ultra-Fast Tactile Product Catalog & Rush-Hour Quick Keys (Priority: P1)

Cashiers and servers need an ergonomic, touch-first catalog grid featuring prominent category tabs ($\ge 80\text{px}$) and high-contrast product tiles with quick-key access for popular rush-hour items, allowing any standard dish to be added to the active basket in $\le 2$ touch interactions.

**Why this priority**: During lunch and dinner rush hours, rapid order entry directly dictates service speed and customer satisfaction.

**Independent Test**: Navigate across categories (Drinks, Mains, Desserts). Tap product tiles; verify that items appear in the order basket immediately with updated totals and VAT breakdown in $< 16\text{ms}$.

**Acceptance Scenarios**:

1. **Given** an open order, **When** tapping the "Boissons" category tab and then "Café Espresso", **Then** the item is added to the cart (total 2 taps) with quantity 1 and appropriate 10% VAT.
2. **Given** an item already present in the cart, **When** tapping the product tile again, **Then** the line item quantity increments to 2 without creating duplicate lines.
3. **Given** a high-priority "Rush Key" on the main grid, **When** tapped directly, **Then** the item is added immediately in a single tap.

---

### User Story 3 - Modal Modifier Selection, Cooking Temperatures & Extras (Priority: P2)

When an item requires preparation customization (e.g., steak cooking temperature, burger sauce, salad dressing, or extra cheese/bacon), the system displays a modal dialog with large tactile option buttons, enforcing mandatory single-choice selections (e.g., Rare / Medium / Well Done) and optional multi-choice add-ons with automatic price adjustments.

**Why this priority**: Essential for kitchen communication and accurate bill calculation for customizable dishes.

**Independent Test**: Tap a dish configured with modifiers ("Entrecôte Grillée"). Verify that the `ModifiersModal` appears with cooking options. Select "Saignant" + "Sauce Poivre (+2.00 €)". Tap Confirm; verify that the item is added with its modifiers and the total price reflects the extra charge.

**Acceptance Scenarios**:

1. **Given** a product with mandatory modifiers, **When** tapped from the catalog, **Then** the `ModifiersModal` opens automatically before adding to the cart.
2. **Given** a mandatory single-choice group (Cooking Temperature), **When** selecting an option, **Then** the Confirm button activates and allows adding to the order.
3. **Given** optional paid add-ons (+1.50 € Bacon), **When** selected, **Then** the line item price increases accordingly in integer cents.

---

### Edge Cases

- **Rapid Multi-Tapping**: Preventing accidental double-adds when staff tap a product button rapidly in under 100ms.
- **Modifier Cancellation**: Closing the modifier modal without adding the item if the customer changes their mind.
- **Custom Kitchen Comments**: Adding free-text or voice-dictated notes for allergies (e.g. "Sans gluten") directly from the modifier modal.
- **Table Transfer**: Moving an active order from one table to another (e.g. from Bar to Table 14).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide an interactive 2D floor plan view (`FloorPlanPage`) displaying tables with number, capacity, server assignment, and visual status colors (`Free`, `Occupied`, `BillRequested`, `Paid`).
- **FR-002**: System MUST transition between tables in under 100 milliseconds without freezing or dropping UI frames.
- **FR-003**: System MUST provide a responsive tactile catalog grid (`CatalogGrid`) with category navigation tabs ($\ge 80\text{px}$ touch targets) and rush-hour quick keys.
- **FR-004**: System MUST allow adding any standard catalog product to the active basket in $\le 2$ touch taps.
- **FR-005**: System MUST provide a tactile pop-up modal (`ModifiersModal`) supporting mandatory single-choice rules (e.g., cooking temps) and optional multi-choice extras with price adjustments.
- **FR-006**: System MUST update active basket quantities, unit prices, modifiers, VAT breakdown, and total TTC in memory in $< 16\text{ms}$ (60 FPS rendering).
- **FR-007**: System MUST provide distinct tactile haptic and visual animations on all table, category, product, and modifier button taps.
- **FR-008**: System MUST support transferring an active order from one table to another while preserving all line items and modifiers.

### Key Entities *(include if feature involves data)*

- **DiningTable**: Represents a physical restaurant table, including `TableNumber`, `Capacity`, `Status` (`Free`, `Occupied`, `BillRequested`, `Paid`), `PositionX`, `PositionY`, and `CurrentOrderId`.
- **ProductModifierGroup**: Associates a product with modifier rules (`MinSelections`, `MaxSelections`, `GroupName`).
- **ProductModifierOption**: Represents a specific choice, including `OptionName`, `ExtraPrice` (`Money`), and `IsDefault`.
- **OrderItemModifier**: Represents the chosen modifier attached to a specific line item on an active order.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Switching between dining room tables completes in under 100 milliseconds.
- **SC-002**: $\ge 90\%$ of standard catalog items can be added to an active order in 2 touch taps or fewer.
- **SC-003**: In-memory active order recalculation (totals, taxes, modifiers) completes in under 16 milliseconds.
- **SC-004**: 100% of floor plan navigation, catalog browsing, and modifier selections function completely offline.

## Assumptions

- Restaurant floor plans are configured with fixed 2D grid coordinates for tables.
- Products can have zero, one, or multiple modifier groups.
- Modifiers with extra charges add directly to the parent item's unit price in exact integer cents.
