# Feature Specification: Phase 0 - Architectural Framing, Offline Resilience & Technical Standards

**Feature Branch**: `001-phase0-architecture-standards`

**Created**: 2026-08-15

**Status**: Draft

**Input**: User description: "read Plan.dm and create a specification for phase 0"

## Clarifications

### Session 2026-08-15

- Q: How should the system reconcile concurrent offline modifications made to the same dining table by different staff members? → A: Option C: Additive merge combining uniquely identified items with an on-screen conflict alert notifying staff of concurrent offline edits.
- Q: How should consecutive fiscal receipt numbers be generated and structured when multiple terminals are issuing sales tickets offline simultaneously? → A: Option A: Terminal-prefixed monotonic sequential counters (e.g., POS01-001042) ensuring collision-free, unbroken audit numbering per terminal.
- Q: How should operator session locking and PIN re-authentication be handled on shared touchscreen terminals? → A: Option C: Manual lock only — The terminal remains on the active operator session continuously until staff explicitly taps the "Lock / Switch Operator" button.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Zero-Disruption Offline POS Operations & Eventual Consistency (Priority: P1)

Waiters and cashiers must be able to continuously take orders, apply item modifiers, seat guests, and process cash or offline payments on tactile mobile tablets without any disruption or delay, even during intermittent Wi-Fi drops or total local server disconnects. When connectivity is restored, all locally buffered operations must automatically synchronize with the central backend in chronological order with zero lost orders, zero duplicated tickets, and automatic conflict resolution.

**Why this priority**: Unstable Wi-Fi is inevitable in busy restaurant environments (terrace zones, dead spots, peak service hours). Front-of-house operations must never halt or lose revenue due to network problems.

**Independent Test**: Put a mobile terminal in offline mode (e.g., Airplane mode), create 10 orders with modifiers and process payments locally, verify instant UI responsiveness and local persistence, reconnect to the network, and verify that all 10 transactions reconcile with the central server with exact totals and zero duplicates.

**Acceptance Scenarios**:

1. **Given** a POS terminal with no active Wi-Fi or server connection, **When** an operator inputs items and completes an order, **Then** the system records the transaction into an immutable local append-only journal, updates the local table state immediately, and marks the payload for background synchronization.
2. **Given** one or more buffered offline transactions on a terminal, **When** network connectivity is restored, **Then** the synchronization engine dispatches the buffered events in strict chronological order with unique idempotency keys, updates remote state, and marks local records as synchronized without operator intervention.
3. **Given** simultaneous offline mutations on separate terminals for the same table, **When** both terminals reconnect and synchronize, **Then** the system performs an additive merge of all uniquely identified items, updates the unified table tab, and generates an on-screen conflict notification alert informing staff that concurrent offline edits were merged.

---

### User Story 2 - Regulatory Fiscal Inalterability & Cryptographic Audit Trail (Priority: P1)

Restaurant owners, managers, and fiscal auditors require absolute proof of sales integrity. Every sales transaction, void, cancellation, discount, price override, and cash drawer opening must be irreversibly recorded with an unbroken SHA-256 cryptographic chain, preventing any retrospective modification or deletion of fiscal data.

**Why this priority**: Compliance with strict fiscal regulations (such as NF525) is a mandatory legal and business requirement. Failure to guarantee immutable auditability exposes the business to heavy legal and financial penalties.

**Independent Test**: Execute a series of sales transactions, cancellations, and cash drawer openings, verify that each entry contains a valid SHA-256 hash chaining back to the previous entry, and verify that modifying any historical database record causes an immediate audit chain validation failure.

**Acceptance Scenarios**:

1. **Given** a completed sales transaction, **When** the receipt is saved and closed, **Then** the system assigns a terminal-prefixed monotonic consecutive number (e.g., `POS01-001042`), calculates a sequential cryptographic hash combining the previous record's hash, UTC timestamp, total amount, and VAT breakdown, and writes it to an immutable fiscal log.
2. **Given** multiple POS terminals operating offline simultaneously, **When** issuing receipts, **Then** each terminal increments its dedicated terminal-prefixed sequence independently without collision or numbering gaps.
3. **Given** an operational event such as a line item void, ticket reprint, or manual drawer opening, **When** the action is performed, **Then** a structured event is appended to the Technical Event Log (JET) recording the operator identity, timestamp, and action reason.
4. **Given** an end-of-day closure request (Z-Report), **When** generated by an authorized manager, **Then** cumulative sales figures and perpetual grand totals are calculated, sealed with a cryptographic signature, and archived permanently.

---

### User Story 3 - High-Velocity Touchscreen Ergonomics & Zero-Configuration Hardware (Priority: P2)

Front-of-house staff operating tactile terminals under high-pressure rush conditions require an interface optimized for speed and single-hand or dual-hand finger taps. The interface must feature large touch targets, natural gestures, and a fixed on-screen numeric keypad that never triggers an obstructive operating system keyboard. In addition, new or rebooted terminals must discover network thermal printers and central servers automatically without manual network configuration.

**Why this priority**: Order entry speed directly dictates table turnover and customer satisfaction. Obstructive system keyboards or manual IP configurations slow down staff and cause operational friction.

**Independent Test**: Navigate ordering workflows on tablet hardware, verify touch target sizing and on-screen keypad response, trigger local network discovery, and verify automated detection of local ESC/POS printers and backend servers.

**Acceptance Scenarios**:

1. **Given** an operator navigating the catalog or entering prices/quantities on a tactile screen, **When** tapping interactive elements, **Then** touch targets measure at least 54x54 pt with immediate visual/haptic feedback (< 50ms) and numeric entry occurs via an integrated on-screen pad without popping up the system virtual keyboard.
2. **Given** an active operator session on a shared terminal, **When** orders are entered or completed, **Then** the terminal stays on the active session continuously without automatic timeout locking until the operator explicitly presses the "Lock / Switch Operator" button.
3. **Given** an operator working in varying lighting conditions (dim dining room vs. bright outdoor terrace), **When** switching modes, **Then** the interface provides high-contrast light and dark themes tailored to environment readability.
4. **Given** a POS terminal connected to the local restaurant network, **When** the application initializes, **Then** it automatically discovers available network receipt/kitchen printers and the local central server using zero-configuration network protocols (mDNS/Bonjour).

---

### User Story 4 - Modular Architecture & Robust Engineering Standards (Priority: P3)

Software engineers and operations teams require a clean, modular multi-project structure adhering to Clean Architecture and CQRS, with shared core domain rules across backend and client platforms, unified static analysis rules, and zero compiler warnings.

**Why this priority**: High software quality, clear architectural boundaries, and strong engineering constraints prevent regressions, reduce maintenance overhead, and guarantee that the system remains extensible across multiple operating topologies.

**Independent Test**: Build the entire solution from clean source, verify that all projects compile with zero warnings, execute automated architectural boundary checks, and verify that domain business logic has zero external dependencies on presentation or persistence layers.

**Acceptance Scenarios**:

1. **Given** the core domain and application libraries, When referenced by both the centralized backend and the tactile client application, Then all business entities, money arithmetic, and command handlers execute identically without platform-specific divergence.
2. **Given** the continuous integration pipeline, When analyzing code, Then strict static analysis rules (`TreatWarningsAsErrors`, nullable reference types, and consistent code formatting) are enforced across all components.

---

### Edge Cases

- **Network Flapping**: Network rapidly drops and reconnects during the transmission of a batch of buffered offline transactions.
- **Concurrent Table Mutations**: Two mobile waitstaff modify the same table offline; the system applies additive merge and surfaces an on-screen alert banner on active terminals indicating concurrent modifications occurred.
- **Sudden Power/Battery Loss**: The tablet loses power abruptly during an active sales calculation or receipt commit before network transmission.
- **Local Network Permission Denied**: The host operating system restricts local network discovery permissions, requiring clear user guidance and fallback manual entry.
- **Corrupted Local Storage**: Local database file corruption detection with automated recovery from secure backup or server state rebuild.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST record every sales mutation and transaction in an immutable, append-only local journal prior to any external network dispatch.
- **FR-002**: System MUST assign chronological, decentralized, collision-free unique identifiers (UUIDv7) to all orders, order lines, payments, and audit entries.
- **FR-003**: System MUST provide an automated Outbox pattern synchronization engine that safely buffers local records during offline periods and synchronizes them idempotently upon network restoration.
- **FR-004**: System MUST reconcile concurrent offline table modifications using an additive merge strategy while triggering an on-screen visual alert notifying operators of concurrent offline changes.
- **FR-005**: System MUST maintain the active operator session continuously without automatic idle locking, providing an explicit one-tap "Lock / Switch Operator" button to return to the PIN screen.
- **FR-006**: System MUST generate terminal-prefixed monotonic sequential receipt numbers (e.g., `POS01-001042`) for both online and offline transactions, guaranteeing unbroken chronological series without collision across multiple stations.
- **FR-007**: System MUST calculate an unbroken SHA-256 cryptographic hash chain for all sales receipts and cumulative fiscal totals in compliance with NF525 regulations.
- **FR-008**: System MUST log all non-fiscal operational events (line voids, receipt reprints, drawer openings, operator sign-ins) in a tamper-evident Technical Event Log (JET).
- **FR-009**: System MUST represent all monetary values and financial calculations using exact integer cents (`Money` Value Object) with zero rounding discrepancies.
- **FR-010**: System MUST provide a touch-first interface adhering to minimum interactive target dimensions of 54x54 pt (and 68x68 pt for rush items) with an integrated on-screen keypad that never summons the platform virtual keyboard.
- **FR-011**: System MUST support native tactile gestures including swipe for quick item deletion/hold, long-press for modifier popups, and pinch-to-zoom for 2D room layouts.
- **FR-012**: System MUST discover central servers and network thermal printers automatically using zero-configuration mDNS/Bonjour protocols.
- **FR-013**: System MUST encapsulate all physical peripheral operations (ESC/POS socket printing on port 9100, RJ11 drawer pulse, payment terminals) behind modular hardware abstraction interfaces.
- **FR-014**: System MUST broadcast and synchronize real-time dining room table states and kitchen order updates across all connected terminals using persistent real-time channels (SignalR) with automatic reconnection.
- **FR-015**: System MUST enforce Clean Architecture boundaries where domain entities and application logic remain completely independent of UI frameworks, database providers, and external SDKs.

### Key Entities *(include if feature involves data)*

- **TransactionJournalEntry**: Represents an immutable recorded mutation, containing sequential local index, UUIDv7 identifier, UTC timestamp, idempotency token, cryptographic signature, and serialized payload.
- **FiscalReceiptRecord**: Represents a closed customer transaction with terminal-prefixed sequential receipt number (e.g., `POS01-001042`), immutable total amounts (cents), detailed VAT breakdown, and cryptographic hash chain link.
- **TechnicalEventLogEntry**: Represents an operational system event (e.g., drawer trigger, void, cancellation, reprint) capturing timestamp, operator identity, terminal identifier, and context.
- **OutboxSyncMessage**: Represents a locally buffered event awaiting remote acknowledgment, tracking delivery status, retry attempts, and payload hash.
- **TerminalProfile**: Represents a connected POS station, including station role (Fixed Counter, Mobile Waiter, Kitchen KDS), terminal prefix identifier, hardware peripherals mapping, and network identifiers.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of order creation, item customization, and cash checkout capabilities remain fully functional without degradation during complete network disconnection.
- **SC-002**: Automatic reconciliation synchronizes a queue of 100 buffered offline transactions to the central server in under 2 seconds upon network reconnection with zero data loss, zero duplicate records, and on-screen alert flagging on concurrent modifications.
- **SC-003**: Tactile user inputs (keypad presses, tile selections, gesture triggers) produce visual or haptic confirmation in under 50 milliseconds.
- **SC-004**: Verification of an audit trail containing over 10,000 chained fiscal transactions achieves 100% mathematical integrity with zero hash mismatches.
- **SC-005**: Automatic network discovery detects available network printers and central servers within 3 seconds of terminal startup on a local network.
- **SC-006**: The shared solution compiles across all target configurations with zero compiler warnings and 100% compliance with defined static analysis quality gates.

## Assumptions

- Terminals run on dedicated restaurant local networks (Wi-Fi 6 or Ethernet) with local broadcast/multicast discovery enabled.
- Touchscreen devices have adequate sandboxed local storage to buffer thousands of transactions offline indefinitely.
- Peripheral receipt and kitchen printers support standard ESC/POS command sets and raw TCP socket communication over port 9100.
- All monetary transactions operate in standard currency sub-units (e.g., Euro cents) to eliminate floating-point calculation errors.
