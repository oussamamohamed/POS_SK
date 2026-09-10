# Feature Specification: Phase 1 - Core Domain Entities, Fast PIN Authentication & Hybrid Persistence

**Feature Branch**: `003-phase1-core-pin-persistence`

**Created**: 2026-08-15

**Status**: Draft

**Input**: User description: "phase 1" (From PLAN.md Phase 1: Socle Technique, Authentification PIN & Persistance Hybride)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Express Operator PIN Authentication & Role-Based Access (Priority: P1)

Waitstaff, bartenders, and managers must be able to log in, unlock their session, and switch operators in under 50 milliseconds by tapping a 4-to-6 digit numeric PIN code on the tactile touchscreen keypad, with immediate visual and haptic feedback, functioning 100% offline without requiring central server connectivity.

**Why this priority**: Fast-paced restaurant operations require staff to switch operators dozens of times per hour. Any authentication delay, slow network check, or intrusive keyboard creates operational bottlenecks.

**Independent Test**: Disconnect network connectivity (Airplane mode). Attempt to authenticate using valid and invalid operator PINs on the tactile keypad. Verify that valid PINs unlock the session within 50ms with a success haptic pulse, while invalid PINs show an error and clear the input immediately.

**Acceptance Scenarios**:

1. **Given** an operator at the `PinLockPage` screen, **When** tapping a valid 4-digit PIN code on the integrated numeric keypad, **Then** the system authenticates the user locally in $< 50\text{ms}$, assigns the appropriate role (`Waiter`, `Cashier`, `Manager`), and transitions to the main ordering terminal view.
2. **Given** an incorrect PIN entry, **When** the 4th digit is entered, **Then** the system displays a clear error message ("Code PIN invalide"), triggers an error haptic buzz, and resets the input field.
3. **Given** a terminal completely offline (no Wi-Fi/server), **When** staff enter their PIN, **Then** the terminal authenticates the operator against the local salted hash cache with zero latency.

---

### User Story 2 - Shared Master Data & Entity Domain Modeling (Priority: P1)

Software developers and backend services require strongly typed, immutable domain models for `User` (Staff), `Product`, `Category`, and `TaxRate`, ensuring that all price calculations, tax breakdowns, and modifier dependencies are uniform between the .NET 9 ASP.NET Core central server and tactile client terminals.

**Why this priority**: Eliminates domain model duplication, schema divergence, and calculation discrepancies across client terminals and master backend.

**Independent Test**: Execute unit tests instantiating products, calculating item taxes with varying VAT rates (5.5%, 10%, 20%), and verifying exact integer cent arithmetic with zero rounding errors.

**Acceptance Scenarios**:

1. **Given** a product catalog with categorized items and assigned VAT rates, **When** calculating line item totals and taxes, **Then** the domain model computes exact taxable bases (HT) and tax amounts in integer cents.
2. **Given** an operator entity with assigned role permissions, **When** performing role checks (e.g., voiding items or generating Z-reports), **Then** the domain model enforces role-based authorization boundaries.

---

### User Story 3 - Multi-Provider Hybrid Persistence & Local Catalog Synchronization (Priority: P2)

POS terminals must asynchronously download and cache the central restaurant catalog (categories, products, prices, modifier groups, and operator credential hashes) from the master server into the local sandboxed database (SQLite) so that the terminal remains fully functional offline.

**Why this priority**: Enables rapid offline startups and zero network latency when navigating large product catalogs during rush hours.

**Independent Test**: Populate master database with 500 products, start a client terminal, trigger catalog sync, verify that the local SQLite database contains all records, disconnect network, and verify that the catalog loads instantly from local cache.

**Acceptance Scenarios**:

1. **Given** an active network connection between the POS client and central server, **When** the application starts up or receives a catalog update signal, **Then** it synchronizes reference data into the local SQLite database in the background.
2. **Given** an updated product price on the central backend, **When** synchronized to the terminal, **Then** the local catalog cache updates atomically without interrupting active orders.

---

### Edge Cases

- **First-Time App Launch with No Network**: Handling the initial deployment before the first catalog synchronization has occurred (displaying a setup prompt to connect to the restaurant network).
- **Concurrent Operator PIN Collisions**: Ensuring that all operator PINs within a restaurant organization are unique across staff members.
- **PIN Change Synchronization**: Propagating PIN updates from the central back-office to offline terminals upon their next reconnection.
- **Corrupted Local Cache**: Detecting schema version mismatches and re-seeding the local SQLite database from the central backend.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide an express operator authentication screen (`PinLockPage`) with an integrated fixed numeric keypad supporting 4-6 digit PIN codes.
- **FR-002**: System MUST validate operator credentials locally in under 50 milliseconds using salted cryptographic hashes (e.g., PBKDF2 or SHA-256 with salt) stored in the local database.
- **FR-003**: System MUST enforce Role-Based Access Control (RBAC) with predefined roles: `Cashier`, `Waiter`, `KitchenStaff`, `FloorManager`, and `Admin`.
- **FR-004**: System MUST define shared C# domain entities in `RestaurantPos.Domain` for `User`, `Product`, `Category`, `TaxRate`, and `ModifierGroup`.
- **FR-005**: System MUST configure Entity Framework Core multi-provider persistence (`AppDbContext`) supporting SQLite for client edge terminals and PostgreSQL/SQL Server for the master server.
- **FR-006**: System MUST provide an asynchronous catalog synchronization service that refreshes local product, category, and staff records from the master backend.
- **FR-007**: System MUST provide instant visual and haptic feedback for every keypad tap, successful login, and authentication error.

### Key Entities *(include if feature involves data)*

- **User / Operator**: Represents a staff member, including `Id` (UUIDv7), `Name`, `PinHash`, `PinSalt`, `Role`, and `IsActive`.
- **Product**: Represents a sellable menu item, including `Id`, `Name`, `CategoryId`, `Price` (Money), `TaxRatePercent`, and `ColorHex`.
- **Category**: Represents a menu section (e.g., Drinks, Starters, Mains, Desserts), including `Id`, `Name`, `ColorHex`, and `DisplayOrder`.
- **TaxRate**: Represents a statutory tax rate (e.g., 5.5%, 10.0%, 20.0%), including `Id`, `Code`, `RatePercent`, and `Description`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Operator PIN authentication and role validation completes in under 50 milliseconds on touchscreen terminals.
- **SC-002**: 100% of staff login and terminal unlock operations succeed without network connectivity.
- **SC-003**: Background synchronization of a catalog containing 1,000 items and categories completes in under 1.5 seconds over local Wi-Fi.
- **SC-004**: Domain and persistence test suites achieve 100% pass rate with zero compiler warnings.

## Assumptions

- Operator PINs are 4 to 6 digits numeric codes assigned by the restaurant manager.
- Client devices have sufficient local sandboxed storage to cache product catalogs and operator hashes indefinitely.
- The master central server exposes RESTful API endpoints for catalog and staff synchronization.
