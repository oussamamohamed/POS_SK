# Implementation Plan: Happy Hour Pricing & Schedule Management

**Branch**: `019-happy-hour-pricing` | **Date**: 2026-09-13 | **Spec**: [`specs/019-happy-hour-pricing/spec.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/019-happy-hour-pricing/spec.md)

**Input**: Feature specification from `/specs/019-happy-hour-pricing/spec.md`

---

## Summary

Implement recurring and ad-hoc Happy Hour pricing for bar and restaurant sales with automatic schedule detection, tactile caisse visibility, supervisor PIN-protected overrides, immutable price locking at order dispatch time, and strict NF525 fiscal audit compliance. The design relies on a hybrid rule engine (product fixed price or category percentage discount) evaluated locally and synchronized in real-time across terminals via SignalR.

---

## Technical Context

**Language/Version**: C# 13 / .NET 9, JavaScript (ES2022 Vanilla)  
**Primary Dependencies**: ASP.NET Core Minimal APIs, Microsoft.EntityFrameworkCore 9.0, Microsoft.AspNetCore.SignalR, FluentAssertions, Playwright Test  
**Storage**: SQLite with EF Core (`AppDbContext`), local offline memory cache  
**Testing**: xUnit (.NET Unit & Integration), Playwright (E2E browser tests)  
**Target Platform**: Web POS (Tactile iPadOS / Desktop Chrome), .NET MAUI POS Client  
**Project Type**: Multi-tier Hospitality POS (Backend API + Web Front-of-House + MAUI client)  
**Performance Goals**: Cart price resolution < 50ms, multi-terminal status synchronization < 200ms  
**Constraints**: Zero rounding drift (integer cents `Money` value object), 100% offline-capable price resolution, immutable JET audit logs for supervisor overrides  
**Scale/Scope**: ~15 product categories, up to 10 overlapping or sequential schedule rules, 1-10 connected POS terminals  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Requirement | Compliance Status | Notes |
|---|---|:---:|---|
| **I. Touch-First Ergonomics** | Targets >= 54x54 pt, high-visibility badges, zero virtual keyboards | **PASS** | Visual amber badge, direct pricing on product tiles with strikethrough, supervisor PIN on custom numeric keypad. |
| **II. Clean Architecture** | Clean separation of Domain, Application, Infrastructure, Presentation | **PASS** | `HappyHourSchedule` & `HappyHourPriceRule` in Domain; `IHappyHourPricingService` in Application; implementation in Infrastructure; endpoints in API. |
| **III. Offline-First & Integrity** | Local evaluation, UUIDv7 identifiers, local SQLite persistence | **PASS** | Schedules cached in local state, terminal evaluates TimeOnly locally during network cuts, UUIDv7 on all entities. |
| **IV. Peripherals & Real-Time Sync** | SignalR persistent hub, ESC/POS receipt traceability | **PASS** | SignalR `HappyHourStatusChanged` broadcast; receipt lines print `[HH]` promotional indicators. |
| **V. Fiscal Traceability & NF525** | `Money` integer cents, SHA-256 chaining, JET logs for overrides | **PASS** | Override logged to `TransactionJournalEntry`; all calculations use `Money`; price locked at dispatch time. |

*Gate Evaluation*: **All 5 Core Constitutional Principles PASS with zero violations.**

---

## Project Structure

### Documentation (this feature)

```text
specs/019-happy-hour-pricing/
├── spec.md              # Feature specification
├── plan.md              # This implementation plan
├── research.md          # Technical decisions & research
├── data-model.md        # Entities, relationships & lifecycle
├── quickstart.md        # Validation scenarios & test commands
├── contracts/
│   └── happy-hour-api.md # REST endpoints & SignalR events
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code (repository root)

```text
src/
├── RestaurantPos.Domain/
│   └── Entities/
│       ├── HappyHourSchedule.cs       # [NEW] Schedule entity
│       ├── HappyHourPriceRule.cs      # [NEW] Pricing rule entity
│       ├── HappyHourOverrideSession.cs # [NEW] Supervisor override entity
│       └── OrderItem.cs               # [MODIFY] Added IsHappyHourApplied, OriginalUnitPrice
│
├── RestaurantPos.Application/
│   ├── Common/Interfaces/
│   │   └── IHappyHourPricingService.cs # [NEW] Pricing service contract
│   └── DTOs/
│       └── HappyHourDtos.cs           # [NEW] Status, Rule, and Override DTOs
│
├── RestaurantPos.Infrastructure/
│   ├── Persistence/
│   │   ├── AppDbContext.cs            # [MODIFY] DbSets & entity configurations
│   │   └── Migrations/                # [NEW] HappyHour schema migration
│   └── Services/
│       └── HappyHourPricingService.cs # [NEW] Service implementation & pricing engine
│
├── RestaurantPos.Api/
│   ├── Endpoints/
│   │   └── HappyHourEndpoints.cs      # [NEW] Minimal API endpoints
│   ├── Hubs/
│   │   └── PosHub.cs                  # [MODIFY] HappyHour broadcast helpers
│   └── wwwroot/
│       ├── index.html                 # [MODIFY] Happy hour banner & supervisor buttons
│       ├── styles.css                 # [MODIFY] Amber badge & strikethrough price styling
│       └── app.js                     # [MODIFY] Real-time state, tile render, cart pricing
│
tests/
├── RestaurantPos.Infrastructure.Tests/
│   └── HappyHourPricingServiceTests.cs # [NEW] Unit & business logic tests
├── RestaurantPos.Api.Tests/
│   └── HappyHourEndpointsTests.cs     # [NEW] API integration tests
└── RestaurantPos.Web.E2ETests/
    └── tests/
        └── happy-hour.spec.ts         # [NEW] Playwright E2E test suite
```

---

## Complexity Tracking

> **Zero constitutional violations detected. No complexity exceptions required.**
