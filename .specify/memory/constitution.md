<!--
Sync Impact Report
- Version change: 1.3.0 → 2.0.0 (MAJOR: removal of principles/stack elements that the code never adopted or dropped)
- Modified principles:
  * II. Layered Architecture & Centralized Backend (MediatR/CQRS, .NET MAUI, CommunityToolkit.Mvvm removed → plain services, Minimal API, native SwiftUI + PosKit)
  * III. Transactional Integrity and Operational Resilience (MAUI sandbox SQLite removed → server is the only fiscal signer, iPad keeps a local draft, outbox for sync)
  * IV. Peripherals, Discovery & Real-Time Sync (IPrinterService/IPaymentTerminalService/ICashDrawerService and TableHub removed → PrintDispatcher/PrintWorker, Bonjour beacon, PosHub/KitchenHub)
  * V. Test-First, Immutability & Fiscal Traceability (hash formula aligned with NF525FiscalAuditService; chains per terminal; append-only)
- Modified sections:
  * Deployment (PostgreSQL, standalone MAUI topology removed; SQLite on the server)
  * Quality Gates (Polly, FluentValidation, Serilog/OpenTelemetry, /healthz removed: not present in the code)
- Removed sections: none
- Follow-up TODOs: spec 021 (NF525) will add the JET chain, period closures, archives and an immutability interceptor; amend principle V when they ship.
- Source of truth: the code and CLAUDE.md. This file states what is actually enforced.
-->

# Restaurant POS System Constitution

## Core Principles

### I. Touch-First Ergonomics & iPadOS Tactile Design
All Front-of-House user interfaces (native SwiftUI iPad app, web touch client) MUST be engineered for tactile screens. Touch targets MUST be at least 54x54 pt for primary actions (the iPad design system uses 56 pt; 44 pt is allowed only for dense secondary controls). Ordering workflows MUST avoid the system virtual keyboard where a fixed on-screen numeric keypad can be used, so the cart and totals stay visible. Natural gestures (swipe for quick item removal/hold, long-press for modifiers, pinch-to-zoom for floor plans) SHOULD be supported. Both light and dark themes MUST be supported. Every user-facing string MUST exist in en, fr and ar (web `i18n/*.json`, server `SharedResource*.resx`, iOS `Localizable.xcstrings`). A feature MUST exist on both the web client and the iPad (parity).

### II. Layered Architecture & Centralized Backend
The backend MUST keep the layering Domain ← Application ← Infrastructure ← Api (.NET 9, ASP.NET Core). Application holds service interfaces and DTOs; Infrastructure holds EF Core, service implementations, security and printing; Api holds Minimal API endpoint groups that call the injected `I*Service` interfaces directly. There is no MediatR/CQRS layer: do not introduce one without an amendment. All DI registration happens in `Program.cs`. The central server is the authoritative orchestrator for catalog, orders, operators, fiscal data and reporting. On iPad, all logic lives in the `PosKit` Swift package; SwiftUI views talk only to stores, never to the network. The web client, the iOS `PosKit` models and the .NET DTOs MUST stay in sync; captured API fixtures back the Swift contract tests. Business logic MUST stay decoupled from transport and UI, with `async`/`await` end to end.

### III. Transactional Integrity and Operational Resilience
Every sale MUST be recorded server-side in append-only fiscal records. The server is the only fiscal signer: no client computes a fiscal signature. Receipt-writing routes MUST require a paired device (`X-Device-Token`); the server takes the terminal identity from the device. The iPad keeps a local draft ticket and sends it only on defined actions (kitchen send, payment, discount, hold, transfer, table change); offline sales are submitted through `/api/sync/receipt` and signed by the server. Identifiers use UUIDv7 (`Domain/Common/UuidV7.cs`). Sync messages go through the outbox. Mutations MUST be idempotent and auditable.

*Rationale*: network loss at the venue must not lose a transaction or let a client forge fiscal data.

### IV. Peripherals, Discovery & Real-Time Sync
Receipt and kitchen printing MUST go through the print queue: `PrintDispatcher` enqueues `PrintJobs` after payment or kitchen send and MUST never fail the business operation; `PrintWorker` renders raster ESC/POS and sends it over TCP port 9100. Station resolution follows item → product → category → `HOT_KITCHEN`. The server MUST announce itself on the LAN over Bonjour (`_restaurantpos._tcp`, `NetworkDiscoveryBeaconService`), and the iOS app MUST declare `NSLocalNetworkUsageDescription`. Real-time table, kitchen and printer state MUST use the SignalR hubs `/hubs/pos` (authorized) and `/hubs/kitchen`; hub event names are a contract shared by both clients and MUST NOT be renamed unilaterally.

### V. Test-First, Immutability & Fiscal Traceability (NON-NEGOTIABLE)
Money MUST be integer cents via the `Money` value object (`Domain/ValueObjects/Money.cs`); `float`/`double` are forbidden for amounts, and rounding rules MUST be identical on the .NET and PosKit sides. Fiscal receipts are chained per terminal with SHA-256 over `previousHash|terminalId|sequenceNumber|amountCents|timestampUtc:O|taxBreakdownJson`, starting from `GenesisHash`; changing field order, formatting or the tax-breakdown serialization breaks existing chains and is forbidden. Daily Z closures are chained and carry a perpetual grand total that is never reset. Fiscal receipts, closures and journal entries are append-only: corrections go through voids and credit notes, never updates. Any new fiscal chain MUST have its own documented formula and MUST NOT alter existing chains (Norme NF525). Test-Driven Development is mandatory for domain and fiscal logic, backed by API integration tests, Playwright web E2E tests and Swift Testing/XCUITest on iPad.

## Deployment, Stack & Security

- **Topology**: one local .NET 9 server (SQLite via EF Core) on the restaurant network, serving the web client as static files and the SignalR hubs, with iPad clients, kitchen displays and network ESC/POS printers connected over Wi-Fi. There is no standalone-iPad topology and no MAUI client.
- **Schema management**: no EF migrations. `Program.cs` calls `EnsureCreated()` then runs idempotent SQL (`ALTER TABLE` / `CREATE TABLE IF NOT EXISTS`) for later additions; every new column or table MUST add its SQL there. The `Testing` environment uses the EF InMemory provider and skips that block.
- **Clients**: native SwiftUI iPad app (Swift 6, iPadOS 17+) in `ios/`, with `.xcodeproj` generated from `project.yml`; vanilla JS touch web client in `wwwroot/`.
- **Authentication**: operator PIN login issuing a JWT, with PIN rate limiting; `Jwt:Secret` is required outside Development. Roles: Waiter, Cashier, KitchenStaff, FloorManager, Admin. Manager/admin routes MUST be protected by policy.
- **Performance targets**: touch feedback < 50 ms; API p95 < 200 ms on the LAN; multi-terminal state sync < 200 ms; receipt/kitchen print dispatch < 500 ms.

## Quality Gates & Fiscal Compliance

- **Static analysis**: `Directory.Build.props` enables nullable reference types, `TreatWarningsAsErrors`, `EnforceCodeStyleInBuild` and `AnalysisLevel=latest-recommended`; any analyzer or `.editorconfig` violation fails the build. Run `dotnet format` before building.
- **Logging**: `ILogger` with source-generated `[LoggerMessage]`.
- **Fiscal compliance**: Z reports, X reports and FEC export exist. Verification, period closures, archives, duplicates and the chained technical event log (JET) are specified in `specs/021-nf525-implementation` and become requirements of this constitution once delivered.
- **Specs and docs**: features are specified with spec-kit (`specs/NNN-*`). Where a spec, plan or comment conflicts with the code, the code and `CLAUDE.md` win until the document is fixed.

## Governance

This Constitution is the authoritative standard for the Restaurant POS project and MUST describe what the code actually enforces; an aspiration that the code does not follow belongs in a spec, not here.

- **Amendments**: any change to principles, stack requirements or governance requires a written proposal and review, and MUST update the Sync Impact Report above.
- **Compliance in code review**: every Pull Request MUST be checked against these principles, in particular touch ergonomics, layering boundaries, cross-client contract, fiscal hash integrity and append-only data.
- **Versioning**: Semantic Versioning (MAJOR for removals or incompatible governance changes, MINOR for new principles or constraints, PATCH for clarifications).

**Version**: 2.0.0 | **Ratified**: 2026-08-15 | **Last Amended**: 2026-10-01
