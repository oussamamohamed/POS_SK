# Feature Specification: Phase 3 - Kitchen Display System (KDS) & Real-Time Order Routing

**Feature Branch**: `005-phase3-kds-realtime-routing`

**Created**: 2026-08-15

**Status**: Draft

**Input**: User description: "phase 3" (From PLAN.md Phase 3: Écran Cuisine (KDS) & Routage des Bons en Temps Réel)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Real-Time Kitchen Order Streaming & SignalR Hub (Priority: P1)

Line cooks, chefs, and bartenders must receive new food and drink orders on their kitchen screens in under 200 milliseconds after waitstaff tap "Send to Kitchen" on their POS terminals, delivered over a resilient real-time SignalR WebSocket channel.

**Why this priority**: Eliminates paper ticket delays and ensures kitchen preparation starts the instant food is ordered.

**Independent Test**: Connect a KDS client and a POS client to the local network. Place an order on the POS and tap "Send to Kitchen". Verify that the KDS displays the new ticket in $< 200\text{ms}$ with full table number, covers count, and item modifiers.

**Acceptance Scenarios**:

1. **Given** an active KDS screen connected to `KitchenHub`, **When** a waiter validates an order for Table 12, **Then** the KDS displays the new ticket in $< 200\text{ms}$ with an audible chime and visual highlight.
2. **Given** an order containing special modifier instructions ("Steak Saignant", "Sans sel"), **When** rendered on the KDS ticket, **Then** modifiers appear in prominent high-contrast text directly below each dish.
3. **Given** a momentary Wi-Fi drop, **When** connectivity recovers, **Then** the KDS reconnects automatically and fetches the latest state of all active tickets in $< 1\text{s}$.

---

### User Story 2 - Touch-First KDS Ticket Lifecycle & State Transitions (Priority: P1)

Kitchen staff working on 15" to 24" touchscreens need a high-visibility Kanban interface with status columns (`En attente`, `En préparation`, `Prêt`, `Servi`), allowing cooks to bump items or entire tickets to the next state with a single touch tap.

**Why this priority**: Fast kitchen pace requires minimal touch interaction, oversized buttons, and clear visual timers to maintain kitchen flow.

**Independent Test**: Display multiple active tickets on the KDS. Tap an item to mark it "In Prep"; tap the ticket header to mark the entire ticket "Ready". Verify that the ticket moves to the "Ready" column in $< 50\text{ms}$ and notifies waitstaff.

**Acceptance Scenarios**:

1. **Given** a new ticket in the "En attente" column, **When** a cook taps the ticket header, **Then** the ticket moves to "En préparation", the timer begins tracking elapsed time, and a confirmation click sounds.
2. **Given** a dish ready for pickup, **When** tapped, **Then** its status changes to "Prêt" and sends a notification to the waiter's handheld POS.
3. **Given** an accidentally bumped ticket, **When** staff tap "Recall" within 60 seconds, **Then** the ticket restores to its previous column and preparation state.

---

### User Story 3 - Multi-Station Preparation Routing (Priority: P2)

Restaurant management requires automatic order splitting so that drinks route to the "Bar" KDS, hot meals route to the "Cuisine Chaude" KDS, and desserts route to the "Pâtisserie" KDS, while consolidating table updates for servers.

**Why this priority**: Prevents station clutter and allows specialized stations to focus exclusively on their relevant prep lines.

**Independent Test**: Submit a multi-course order containing 2 Cocktails, 2 Steaks, and 1 Tiramisu. Verify that the Bar screen receives only the Cocktails, the Hot Kitchen screen receives only the Steaks, and the Dessert screen receives only the Tiramisu.

**Acceptance Scenarios**:

1. **Given** a KDS terminal configured with the "Bar" station profile, **When** a mixed order is placed, **Then** the Bar KDS displays only the drink items.
2. **Given** a KDS terminal configured with the "Cuisine Chaude" profile, **When** the order is placed, **Then** it receives only hot kitchen line items.

---

### Edge Cases

- **Order Modification After Kitchen Dispatch**: When a waiter adds or voids an item on an active table already sent to the kitchen, the KDS highlights the modified ticket with an amber alert badge ("Article modifié / annulé").
- **Prolonged Ticket Delays**: When a ticket's elapsed time exceeds 15 minutes, its header turns amber; at 20 minutes, it flashes red with an urgency indicator.
- **Multiple KDS Stations Bumping Simultaneously**: Concurrent state transitions on the same ticket across different stations resolve additively without race conditions.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide an ASP.NET Core SignalR hub (`KitchenHub`) streaming real-time order lifecycle events (`OrderReceived`, `ItemBumped`, `OrderCompleted`, `OrderRecalled`).
- **FR-002**: KDS screens MUST receive and render newly dispatched kitchen tickets in under 200 milliseconds over local Wi-Fi/LAN.
- **FR-003**: System MUST provide a dedicated full-screen KDS view (`KdsPage`) optimized for tactile counter displays with status columns (`Pending`, `InPreparation`, `Ready`, `Served`).
- **FR-004**: System MUST allow kitchen staff to bump individual items or entire tickets with a single touch tap in $< 50\text{ms}$.
- **FR-005**: System MUST support preparation station routing (`Bar`, `HotKitchen`, `ColdKitchen`, `Pastry`) based on category mappings.
- **FR-006**: System MUST display an elapsed timer on each ticket, with visual threshold alerts (Green $< 10\text{ min}$, Amber $10-20\text{ min}$, Red $> 20\text{ min}$).
- **FR-007**: System MUST provide audible chime notifications and visual pulse animations upon receiving new tickets.
- **FR-008**: System MUST provide a ticket recall button allowing staff to undo accidental bumps within 60 seconds.

### Key Entities *(include if feature involves data)*

- **KitchenTicket**: Represents an order batch sent to the kitchen, including `TicketId` (UUIDv7), `OrderNumber`, `TableNumber`, `CoversCount`, `StationId`, `Status` (`Pending`, `InPreparation`, `Ready`, `Served`), `DispatchedAtUtc`, `CompletedAtUtc`, and `Items`.
- **KitchenTicketItem**: Represents a line item on the ticket, including `ItemId`, `ProductName`, `Quantity`, `ModifiersJson`, `KitchenComment`, and `ItemStatus`.
- **PreparationStation**: Represents a kitchen station (e.g. "Bar", "Cuisine Chaude", "Pâtisserie"), including `StationId`, `Name`, and `AssociatedCategoryIds`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Order transmission from POS dispatch to KDS screen rendering completes in under 200 milliseconds.
- **SC-002**: Kitchen ticket bump transitions execute in under 50 milliseconds with visual/audio confirmation.
- **SC-003**: Preparation timers update accurately every second across all active tickets with zero UI stutter.
- **SC-004**: Automatic reconnection and state resynchronization restores active tickets in $< 1\text{s}$ upon network reconnection.

## Assumptions

- KDS screens have network connectivity to the local POS backend / master server.
- Kitchen display devices use 10" to 24" touchscreens mounted in preparation areas.
- Station routing is determined by product category configuration.
