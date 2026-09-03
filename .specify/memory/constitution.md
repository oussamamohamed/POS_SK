<!--
Sync Impact Report
- Version change: 1.2.0 → 1.3.0
- List of modified principles:
  * I. Touch-First Ergonomics & iPadOS Tactile Design (Clarified touch targets, gestures, and on-screen keypad rules)
  * II. Clean Architecture & Centralized Multi-POS Backend (Specified .NET 9, shared Domain/Application assemblies, MVVM CommunityToolkit)
  * III. Transactional Integrity and Offline-First Operations (Added UUIDv7 decentralized IDs, Outbox pattern, and sandbox persistence)
  * IV. Hardware Driver Abstraction, mDNS Discovery & Real-Time Sync (Detailed raw TCP port 9100 ESC/POS, mDNS/Bonjour, SignalR TableHub)
  * V. Test-First, Immutability & Fiscal Traceability (NON-NEGOTIABLE) (Formalized NF525 SHA-256 chaining, JET logs, and Z-closure)
- Added sections:
  * ## Deployment Topologies, Apple Ecosystem & Enterprise Stack
  * ## Robust Software Engineering, Quality Gates & Fiscal Compliance
- Removed sections:
  * None
- Follow-up TODOs: None (Fully synchronized with PLAN.md)
-->

# Restaurant POS System Constitution

## Core Principles

### I. Touch-First Ergonomics & iPadOS Tactile Design
All Front-of-House user interfaces MUST be engineered exclusively for tactile screens across the Apple iPad ecosystem (iPad 10.9", iPad Pro 11"/13", iPad Mini 8.3") and touchscreen POS terminals. Touch targets MUST adhere to a minimum size of 54x54 pt (and 68x68 pt for high-velocity rush items) with generous touch padding. Workflows MUST eliminate system virtual pop-up keyboards during ordering; an integrated, fixed on-screen custom numeric keypad is mandatory to prevent obscuring the order cart or totals. Natural tactile gestures (Swipe for quick item removal/hold, Long-press/Haptic Touch for modifiers and doneness, Pinch-to-zoom for 2D floor plans) MUST be supported natively. Instant visual/haptic feedback (< 50ms) and adaptive light/dark themes (dark for dim bar atmospheres, high-contrast light for outdoor terraces) are mandatory.

### II. Clean Architecture & Centralized Multi-POS Backend
The solution MUST adhere strictly to Clean Architecture and CQRS (Command Query Responsibility Segregation via MediatR) separating Domain, Application, Infrastructure, and Presentation layers. Core business libraries (`RestaurantPos.Domain`, `RestaurantPos.Application`) MUST be shared across both the centralized .NET 9 ASP.NET Core backend and the .NET MAUI iOS/iPadOS client. The central backend serves as the authoritative orchestrator for multi-terminal and multi-outlet management: central catalog (menus, variants, recipes, VAT rates), global inventory tracking, multi-operator access controls, and consolidated financial reporting. The frontend MUST utilize modern MVVM architecture (`CommunityToolkit.Mvvm`). Core domain business logic MUST remain 100% decoupled from transport layers, databases, and UI frameworks, with strong typing and asynchronous non-blocking I/O (`async`/`await`) enforced across all boundaries.

### III. Transactional Integrity and Offline-First Operations
Every sales transaction MUST be recorded in an immutable, append-only local journal before being processed further. The application MUST support fully functional offline operations (Local-First pattern with embedded SQLite in the iOS application sandbox `FileSystem.AppDataDirectory`), safely buffering sales records locally and synchronizing them to the cloud / central backend once network connectivity is verified. Decentralized, collision-free chronological identifiers (UUIDv7) MUST be generated locally. Local and remote transaction states MUST remain consistent, idempotent, and auditable at all times with unique idempotency tokens on every mutation.

*Rationale*: Unstable network connectivity at physical retail locations must not cause business interruption or transaction loss.

### IV. Hardware Driver Abstraction, mDNS Discovery & Real-Time Sync
Physical restaurant peripherals (ESC/POS thermal receipt and kitchen printers over raw TCP sockets on port 9100, RJ11 cash drawers, barcode/RFID scanners, IP/BLE payment terminals) MUST be encapsulated behind strict hardware abstraction interfaces (`IPrinterService`, `IPaymentTerminalService`, `ICashDrawerService`). Network discovery of central servers and peripherals MUST leverage zero-configuration mDNS / Bonjour protocols with appropriate iOS local network permissions (`NSLocalNetworkUsageDescription`). Real-time floor plan table states and Kitchen Display System (KDS) order routing MUST utilize persistent SignalR / WebSocket channels featuring automatic exponential reconnection and missed-event replay buffers.

### V. Test-First, Immutability & Fiscal Traceability (NON-NEGOTIABLE)
Financial calculations (VAT breakdown, discounts, price modifiers, multi-way bill splitting) MUST use dedicated `Money` value objects represented as integer cents with zero rounding tolerance; binary floating-point types (`float`, `double`) are strictly forbidden. All transactional records MUST be cryptographically secured using SHA-256 block chaining ($Hash_n = \text{SHA256}(Hash_{n-1} + \text{HorodatageUtc} + \text{TotalTTC} + \text{VentilationTVA})$) and an immutable Technical Event Log (JET) satisfying regulatory fiscal standards (Norme NF525). Test-Driven Development (TDD) is mandatory for domain logic, complemented by automated API integration tests (`WebApplicationFactory`) and tactile UI component verification before any release.

## Deployment Topologies, Apple Ecosystem & Enterprise Stack

- **Supported Topologies**:
  1. *Multi-Terminal Restaurant (Recommended)*: Centralized local .NET 9 ASP.NET Core server (Mini-PC/Linux/Mac mini with PostgreSQL) connected over local Wi-Fi to multiple iPad POS clients (.NET MAUI), kitchen KDS terminals, network ESC/POS printers, and IP payment terminals.
  2. *Standalone iPad POS (Food-Truck / Autonomous)*: Single iPad executing .NET MAUI with embedded SQLite, offline business engine, and direct Wi-Fi/Bluetooth printer connection.
- **Client Runtime & Frontend**: .NET MAUI (C# / XAML or C# Markup) targeting iPadOS 17+ & iOS 17+, leveraging `Microsoft.Maui.Graphics` for 2D floor plans.
- **Centralized Backend Runtime**: .NET 9 (C#), ASP.NET Core Web API, SignalR Hubs (`TableHub`), MediatR for CQRS pipelines, Entity Framework Core multi-provider (PostgreSQL/SQL Server for central server, SQLite for local terminal cache).
- **Security & Multi-Operator Authentication**: Sub-50ms PIN / NFC badge operator switching; Role-Based Access Control (RBAC: Cashier, Waiter, Kitchen Staff, Floor Manager, Admin); tokenized payment gateways with zero local cardholder storage (PCI-DSS compliance); Apple Business Manager (ABM) / MDM profiles for Single App Mode (kiosk locking).
- **Performance Service Level Objectives (SLOs)**: Tactile input-to-render feedback < 50ms; transactional API latency p95 < 200ms; multi-terminal real-time state synchronization < 200ms; kitchen ticket and receipt print dispatch < 500ms.

## Robust Software Engineering, Quality Gates & Fiscal Compliance

- **Code Quality & Static Analysis**: Strict static analysis enforced at build time (`.editorconfig`, `Directory.Build.props`, Roslyn analyzers, StyleCop, Nullable reference types enabled, `TreatWarningsAsErrors` enabled in CI). Zero compiler warnings permitted.
- **Resilience & Fault Tolerance**: Implementation of Polly retry/circuit-breaker policies on external integrations; structured validation with FluentValidation before command handling; fail-fast design on invariant violations.
- **Observability & Diagnostics**: Structured logging (OpenTelemetry / Serilog in JSON format) with distributed correlation IDs tracing orders from tactile client to central backend, database, and printer queues; proactive health monitoring endpoints (`/healthz/live`, `/healthz/ready`).
- **Fiscal Compliance & Archiving**: Automated generation of immutable daily Z-reports, intermediate X-reports, and monthly fiscal archives (FEC export) with cryptographically verified hash chains and tamper-detection audits.

## Governance

This Constitution represents the authoritative architectural and operational standard for the Restaurant POS project. It supersedes all informal agreements and undocumented patterns.

- **Amendments**: Any change to principles, technical stack requirements, or governance rules requires a formal proposal, architectural review, and explicit consensus.
- **Compliance in Code Reviews**: Every Pull Request MUST be evaluated against these principles—specifically touch ergonomics, .NET clean architecture boundaries, offline resilience, and fiscal hash integrity.
- **Versioning Policy**: The Constitution follows Semantic Versioning (MAJOR for breaking governance/architectural removals, MINOR for new principles or constraints, PATCH for clarifications and typo fixes).

**Version**: 1.3.0 | **Ratified**: 2026-08-15 | **Last Amended**: 2026-08-15
