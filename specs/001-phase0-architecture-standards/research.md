# Research & Architecture Decisions: Phase 0 - Architectural Framing & Technical Standards

**Feature**: `001-phase0-architecture-standards`
**Date**: 2026-08-15
**Status**: Completed

## 1. Clean Architecture & Code Sharing Model (.NET 9 & .NET MAUI)

### Decision
Structure the solution into modular projects where `RestaurantPos.Domain` and `RestaurantPos.Application` target `.net9.0` (with cross-compatibility for `.net9.0-ios` in .NET MAUI). The centralized server runs ASP.NET Core (.NET 9), and the tactile client runs .NET MAUI iOS/iPadOS 17+.

### Rationale
- 100% of core domain rules (taxes, currency arithmetic, discount policies, fiscal chaining) and application handlers (CQRS commands/queries) are shared without duplication.
- Strong typing and compile-time verification across the entire solution.
- Clean Architecture ensures business domain rules remain decoupled from frameworks, databases, and UI implementations.

### Alternatives Considered
- **Blazor Hybrid**: Evaluated for rapid web-like UI development, but rejected due to higher input latency on touch gestures, suboptimal canvas rendering for 2D floor plans, and clunky iOS hardware peripheral bridging compared to native .NET MAUI handlers.
- **Separate Client and Server Codebases**: Rejected due to duplicate model maintenance, high risk of serialization drift, and double effort for validation logic.

---

## 2. Local-First Architecture & Outbox Synchronization

### Decision
Implement a Local-First data persistence strategy using embedded SQLite inside the iOS application sandbox (`FileSystem.AppDataDirectory`). All order mutations, payments, and fiscal events are written to an append-only local journal and transactional Outbox queue before dispatching to the central server via an idempotent HTTP/SignalR sync service.

### Rationale
- **Zero Interruption**: Floor operations (waiters taking orders, bartenders punching drinks, cashiers printing receipts) continue unabated during Wi-Fi drops or server restarts.
- **Conflict-Free Chronology**: Local generation of **UUIDv7** identifiers guarantees globally unique, chronologically sortable keys without server roundtrips.
- **Idempotency & Reconciliation**: Every sync payload carries an idempotency key. Concurrent offline edits on the same table are reconciled using an additive merge strategy with an on-screen alert banner.

### Alternatives Considered
- **Direct REST-only calls**: Rejected because any network packet loss halts POS operations and frustrates staff.
- **CouchDB / PouchDB sync**: Evaluated but rejected due to poor .NET MAUI ecosystem integration and lack of strong C# type safety compared to EF Core SQLite.

---

## 3. Cryptographic Ledger & Fiscal Compliance (Norme NF525)

### Decision
Implement an unbroken SHA-256 cryptographic chain linking every fiscal receipt to the previous transaction:
$$\text{Hash}_n = \text{SHA256}(\text{Hash}_{n-1} + \text{HorodatageUtc} + \text{TotalTTC} + \text{VentilationTVA})$$
In addition, capture all operational events (drawer openings, line voids, ticket reprints, operator switches) in an immutable Technical Event Log (JET), and enforce terminal-prefixed monotonic numbering (`POS01-001042`).

### Rationale
- Guarantees complete compliance with European/French cash register certification standards (NF525 / BOI-TVA-DECLA-30-10-30).
- Immutability is enforced via database-level triggers and EF Core interceptors that reject any SQL `UPDATE` or `DELETE` on finalized fiscal tables.
- Enables mathematical audit verification of 100,000+ receipts in seconds.

### Alternatives Considered
- **External Hardware Fiscal Dongles**: Rejected due to high hardware cost, platform lock-in, and maintenance fragility.
- **Blockchain / Distributed Ledger**: Rejected due to unnecessary storage overhead, high transaction latency, and regulatory misalignment with standard POS fiscal audits.

---

## 4. Hardware Peripheral Abstraction & Zero-Configuration Networking

### Decision
Encapsulate all peripheral drivers behind clean interfaces (`IPrinterService`, `ICashDrawerService`, `IPaymentTerminalService`). Implement direct raw TCP socket communication (port 9100) for ESC/POS thermal printers and RJ11 24V drawer trigger (`ESC p 0 25 250`). Utilize zero-configuration mDNS / Bonjour (`_pos-server._tcp`, `_printer._tcp`) for automated device pairing on the local subnet.

### Rationale
- Raw TCP sockets on port 9100 bypass OS spooler delays, achieving sub-500ms ticket print times and direct control over paper cut commands (`GS V 66 0`).
- mDNS eliminates manual IP address configuration on tablet fleets, reducing maintenance and onboarding overhead.
- Meets iOS `Info.plist` requirements (`NSLocalNetworkUsageDescription`, `NSBonjourServices`).

### Alternatives Considered
- **Apple AirPrint**: Rejected because AirPrint does not support thermal receipt roll formatting, auto-cutting, or RJ11 cash drawer kicks.
- **Bluetooth-only peripherals**: Kept as an optional fallback for food-truck standalone topology, but Ethernet/Wi-Fi is primary for multi-terminal restaurants due to physical range.

---

## 5. Touch-First Ergonomics & UI Architecture

### Decision
Develop the user interface using .NET MAUI with `CommunityToolkit.Mvvm` (compiled bindings, source generators) and `Microsoft.Maui.Graphics` for 2D room layouts. Enforce minimum touch target sizing (54x54 pt standard, 68x68 pt rush items), custom on-screen numeric keypad (no virtual system keyboard popups), and manual operator session locking.

### Rationale
- Sub-50ms visual and haptic feedback keeps input responsive during rush periods.
- Fixed on-screen keypad ensures the order basket and ticket total remain visible at all times.
- Manual lock mode prevents unwanted lockouts while allowing 1-tap operator switching.

### Alternatives Considered
- **Standard Entry Controls with System Keyboard**: Rejected because the iOS keyboard obstructs half the screen, slows down entry, and causes orientation glitches.
