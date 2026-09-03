# Research: Configurable POS Management & Administration

**Feature**: `007-pos-configuration-management`  
**Date**: 2026-08-30  
**Status**: Completed

## Executive Summary & Architectural Decisions

This research document analyzes the technical patterns, data synchronization mechanisms, tactile UX paradigms, and hardware discovery strategies required to deliver a comprehensive, offline-first configuration back-office for articles, categories, staff, network printers, and tactile screen layouts.

---

## 1. Catalog & Menu Hierarchy Architecture (Articles, Categories & Modifiers)

### Decision
Model catalog entities as a hierarchical graph where `ProductCategory` contains sort orders, color badges, and icons; `Product` belongs to a primary category with exact integer-cent pricing (`Money`) and tax rate linkage (`TaxRate`); and `ProductModifierGroup` binds to products with minimum/maximum selection bounds and optional surcharge options (`ProductModifierOption`).

### Rationale
1. **Zero-Floating Point Ambiguity**: Prices, taxes, and modifier surcharges are stored as exact decimal cents (`decimal` / `Money`) to comply with accounting standards.
2. **Archival over Deletion**: To ensure NF525 fiscal immutability, products and categories are never hard-deleted from the database when referenced by historical transactions; instead, an `IsActive` flag archives them from POS selection while preserving historical integrity.
3. **Tactile Visual Cues**: Categories carry hex color strings and icon glyph identifiers to render high-contrast category tabs and buttons for rapid rush-hour navigation.

### Alternatives Considered
- *Hard deletion with cascading NULL*: Rejected because deleting a product would break historical sales reports and receipt reprinting.
- *Unstructured JSON blob for modifiers*: Rejected because structured relational models enable strict validation of mandatory/optional modifier constraints and station routing.

---

## 2. Staff Operator Management, RBAC & Salted PIN Cryptography

### Decision
Implement `StaffMember` entity with a role enumeration (`UserRole`: `Admin`, `Manager`, `Server`, `Bartender`, `Cook`), coupled with `IOperatorAuthenticationService` using SHA-256 with per-user cryptographic salt. The administration UI is strictly gated by a Manager/Admin permission guard (`AuthorizeManager` check).

### Rationale
1. **Sub-50ms Authentication**: Computing `SHA256(PIN + Salt)` executes in $<1\text{ms}$ locally on iPad/Android/Windows devices without network latency.
2. **Offline Role Verification**: User credentials and role claims are stored in the local sandboxed SQLite database (`LocalAppDbContext`), allowing instant operator authorization even during network outages.
3. **PIN Format & Collision Prevention**: PINs are strictly constrained to 4-6 digits, and an index with unique constraint per outlet prevents collisions across active operators.

### Alternatives Considered
- *Bcrypt/Argon2 with high work factor*: Rejected for tactile POS fast switching because work factor delays of 200-500ms degrade the rush-hour operator experience. SHA-256 with individual cryptographic salts provides optimal balance of security and instantaneous response for sandboxed POS terminals.
- *Password-only login*: Rejected because full alpha keyboards obscure the touchscreen and slow down waiter operations.

---

## 3. Network Thermal Printer Discovery, Assignment & Diagnostics

### Decision
Define a `PrinterConfiguration` domain model storing IP address, TCP port (default 9100), device name, paper width (80mm/58mm), cash drawer kick enabled flag, and an array/flags of assigned `PreparationStation` roles (`ReceiptPrinter`, `HotKitchen`, `ColdKitchen`, `Bar`). Provide asynchronous TCP test socket execution directly from the client.

### Rationale
1. **Decoupled Routing**: Orders dispatched by the POS evaluate the category and station tags of each line item to route preparation tickets to the designated physical printer(s) automatically.
2. **Zero-Configuration mDNS Integration**: Integrates with `ICrossPlatformDiscoveryService` (`_printer._tcp`, `_pdl-datastream._tcp`, and `_raw._tcp`) to detect network printers automatically on local Wi-Fi.
3. **Real-time Diagnostic Slip**: The "Test Print" action emits a standardized ESC/POS diagnostic slip (`ESC @`, `GS V 66 0`) with date, IP, station roles, and optional drawer pulse (`ESC p 0 25 250`).

### Alternatives Considered
- *Windows Spooler / CUPS Print Dialog*: Rejected because OS-level print dialogs block the screen, require manual interaction, and cannot drive raw ESC/POS cut/drawer pulses directly from iOS sandboxed apps.

---

## 4. Screen Layout Customization & Station Profiles

### Decision
Introduce a `TerminalLayoutProfile` model that encapsulates:
- Category display order (sequence array).
- Quick-Keys bar article bindings (array of pinned product IDs).
- Grid density (Columns count: 3, 4, 5, or 6 depending on screen width breakpoint).
- Default landing station (Sales Terminal, Floor Plan, KDS Kanban).

### Rationale
1. **Ergonomic Versatility**: Allows a Bar terminal to pin beers and cocktails on the quick bar, while a Dining Room terminal opens directly to the 2D floor plan.
2. **Dynamic UI Adaptation**: The MAUI `ResponsiveGridContainer` and `PosTerminalViewModel` subscribe to profile changes and reload layout settings reactively without restarting the application.
3. **Local-First Caching**: Profiles are stored in local SQLite and synchronized across the cluster via Outbox events.

---

## 5. Offline-First Synchronization Pipeline

### Decision
All administrative updates (new article, edited price, new staff PIN, modified printer IP) emit an `OutboxSyncMessage` with an event type (`CatalogUpdated`, `StaffUpdated`, `PrinterConfigured`, `LayoutUpdated`) and UUIDv7 chronological ID, persisted atomically in the local SQLite transaction.

### Rationale
- Guaranteed eventual consistency across all iPads and terminals in the restaurant.
- Works 100% offline on standalone terminals.
- Automatic broadcast via SignalR hubs when connected to the central backend.
