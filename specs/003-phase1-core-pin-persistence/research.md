# Research & Architecture Decisions: Phase 1 - Core Domain, Fast PIN Auth & Hybrid Persistence

**Feature**: `003-phase1-core-pin-persistence`
**Date**: 2026-08-15
**Status**: Completed

## 1. Offline-First Operator PIN Authentication & Cryptographic Hashing

### Decision
Store operator credentials in the local sandboxed database using a salted SHA-256 hash:
$$\text{StoredHash} = \text{Hex}(\text{SHA256}(\text{PinSalt} + \text{NumericPin}))$$
Where `PinSalt` is a cryptographically generated 16-byte random string unique to each operator.

### Rationale
- **Sub-50ms Offline Verification**: Local hash calculation takes $< 2\text{ms}$ on modern tablet CPUs, allowing instant login without server roundtrips.
- **Security & Separation**: If an edge device is physically compromised, rainbow table attacks are prevented by individual salts.
- **Role Validation**: The authenticated user record immediately yields the operator's assigned role (`Waiter`, `Cashier`, `Manager`, `Admin`).

### Alternatives Considered
- **Plaintext PINs**: Rejected because anyone extracting the SQLite database file would gain immediate access to all staff PINs.
- **Cloud-Only OAuth2 / JWT Login**: Rejected because it prevents staff from logging in or switching operators during internet/Wi-Fi outages.

---

## 2. Multi-Provider Entity Framework Core Strategy

### Decision
Define all core entities (`User`, `Product`, `Category`, `TaxRate`, `ModifierGroup`) in the shared `RestaurantPos.Domain` library. Use EF Core multi-provider configurations:
- **Central Master Backend**: `AppDbContext` using `Npgsql.EntityFrameworkCore.PostgreSQL` for central multi-terminal and multi-store persistence.
- **Edge POS Clients**: `LocalAppDbContext` using `Microsoft.EntityFrameworkCore.Sqlite` for zero-latency local caching and offline operations.

### Rationale
- Single source of truth for entity definitions, property validations, and business logic.
- EF Core manages schema mapping, migrations, and relationship navigation seamlessly across both database engines.

### Alternatives Considered
- **Separate Client and Server Data Models**: Rejected to prevent model drift, duplicate validation logic, and mapping overhead.
- **No ORM (Raw SQL everywhere)**: Rejected due to maintenance complexity across multiple SQL dialects (Postgres vs SQLite).

---

## 3. Asynchronous Reference Data Synchronization (Delta Sync)

### Decision
Implement catalog synchronization via a versioned HTTP endpoint (`GET /api/v1/catalog/sync?since=timestamp`). The central server returns added and updated categories, products, and user credential hashes, which the client applies in a single local database transaction.

### Rationale
- Initial synchronization transfers the entire active catalog in $< 500\text{ms}$.
- Subsequent synchronizations transfer only modified records (delta), minimizing bandwidth and local disk writes.
- Client database transaction guarantees atomic updates without putting active order entry in an inconsistent state.
