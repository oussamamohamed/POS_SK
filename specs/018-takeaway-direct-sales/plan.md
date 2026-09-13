# Implementation Plan: Direct Sales and Takeaway Checkout with Complex Scenarios

**Branch**: `018-takeaway-direct-sales` | **Date**: 2026-09-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/018-takeaway-direct-sales/spec.md`

## Summary

Deliver direct counter sales and takeaway checkout capabilities engineered for high-velocity rush hours:
- **Default Direct Mode**: POS opens directly on a fresh, active counter cart without requiring table selection.
- **Dynamic Differentiated VAT**: Instant sub-50ms recalculation between *Sur Place* (10% standard catering) and *À Emporter* (5.5% on sealed foods/drinks, 10% on prepared, 20% on alcohol).
- **Complex Multi-Tender Split**: Meal vouchers (Titres-Restaurant) with regulatory ceilings, food-eligibility filters, and configurable overpayment policies (A: Cap & zero change; B: Strict reject; C: Store credit voucher).
- **Hold & Recall Queue**: One-touch cart parking (`En attente`) to liberate checkout lanes, with supervisor PIN required for voiding parked carts or partially paid sales.
- **Decentralized Pickup Numbering**: Monotonic daily sequences `#A-01` .. `#A-99` per terminal ensuring collision-free operation offline.
- **Anti-Waste Printing (Loi AGEC)**: Tactile prompt offering fiscal receipt printing on demand while systematically generating the compact pickup voucher.

---

## Technical Context

**Language/Version**: C# 13 / .NET 9.0  
**Primary Dependencies**: ASP.NET Core, MediatR, Entity Framework Core, SignalR (`TableHub` & `KdsHub`)  
**Storage**: SQLite (embedded local-first database on POS terminals) & PostgreSQL/SQLite multi-provider backend  
**Testing**: xUnit, FluentAssertions, `WebApplicationFactory<Program>`, Playwright E2E (`tests/RestaurantPos.Web.E2ETests/`)  
**Target Platform**: iPadOS 17+ (Safari Web & .NET MAUI), Touch POS terminals (1080p, 1200x900 tactile touchscreens)  
**Project Type**: Full-Stack Restaurant POS Web/Client + Centralized Multi-Terminal API  
**Performance Goals**: Sub-50ms tactile feedback, sub-200ms API p95 latency, 0-cent rounding discrepancy  
**Constraints**: NF525 French fiscal compliance (SHA-256 block chaining, immutable JET audit logs), 100% offline-resilient local operation  
**Scale/Scope**: Rush peak capacity > 300 orders/hr per register, up to 10 concurrent parked carts per terminal  

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

- **Principle I: Touch-First Ergonomics & iPadOS Tactile Design**  
  *Status*: **PASS**. All counter workflows use fixed on-screen numpads, $\ge 54\text{pt}$ touch targets, one-touch destination toggles, and eliminate system pop-up keyboards.
- **Principle II: Clean Architecture & Centralized Multi-POS Backend**  
  *Status*: **PASS**. Core domain logic (`OrderDestination`, `DirectOrder`, `MealVoucherPolicy`) resides in `RestaurantPos.Domain`, business CQRS in `RestaurantPos.Application`, persistence and ESC/POS drivers in `RestaurantPos.Infrastructure`.
- **Principle III: Transactional Integrity and Offline-First Operations**  
  *Status*: **PASS**. UUIDv7 identifiers, local SQLite persistence of held orders, and terminal-prefixed pickup sequences (`#A-01`) ensure zero cross-terminal collisions when disconnected.
- **Principle IV: Hardware Driver Abstraction, mDNS Discovery & Real-Time Sync**  
  *Status*: **PASS**. Peripheral interfaces (`IPrinterService`, `ICashDrawerService`) encapsulate ticket formatting and drawer pulses; SignalR transmits takeaway packing tickets to kitchen KDS.
- **Principle V: Test-First, Immutability & Fiscal Traceability (NON-NEGOTIABLE)**  
  *Status*: **PASS**. Financial math strictly employs `Money` (integer cents); transactions are chained via SHA-256; daily Z-closure validates unclosed held carts.

---

## Project Structure

### Documentation (this feature)

```text
specs/018-takeaway-direct-sales/
├── spec.md              # Feature specification & user stories
├── plan.md              # Implementation plan (this file)
├── research.md          # Architectural decisions & legal/fiscal analysis
├── data-model.md        # Entities, enums, state machines & relationships
├── quickstart.md        # Runnable verification scenarios
├── contracts/           # API and SignalR contracts
│   └── direct-takeaway-api.yaml
└── checklists/          # Quality checklists
    └── requirements.md
```

### Source Code Mapping

```text
src/
├── RestaurantPos.Domain/
│   ├── Entities/
│   │   ├── DirectOrder.cs                    # Direct sale & takeaway order aggregate
│   │   ├── HeldOrder.cs                      # Parked counter cart entity
│   │   └── CustomerCreditVoucher.cs          # Store credit issued under Voucher Policy C
│   ├── Enums/
│   │   ├── OrderDestination.cs               # Takeaway, EatIn, Delivery
│   │   └── MealVoucherOverpaymentPolicy.cs   # CapAtBalance, StrictRejection, CustomerCredit
│   └── ValueObjects/
│       └── TaxBreakdown.cs                   # Multi-rate VAT breakdown with 0-cent invariance
│
├── RestaurantPos.Application/
│   ├── Orders/Commands/
│   │   ├── SwitchOrderDestinationCommand.cs  # Dynamic VAT recalculation handler
│   │   ├── HoldCounterOrderCommand.cs        # Park cart handler
│   │   ├── RecallCounterOrderCommand.cs      # Restore parked cart handler
│   │   ├── VoidCounterOrderCommand.cs        # Supervisor PIN void handler
│   │   └── CounterCheckoutCommand.cs         # Split payment & NF525 sealing handler
│   └── Common/Interfaces/
│       └── ITakeawayCounterService.cs        # Numbering & packaging routing service
│
├── RestaurantPos.Infrastructure/
│   ├── Services/
│   │   ├── TakeawayCounterService.cs         # Atomic pickup sequence (#A-01) management
│   │   └── HeldOrderStorageService.cs        # SQLite/PostgreSQL storage for parked carts
│   └── Printing/
│       └── TakeawayTicketFormatter.cs        # Compact pickup voucher & combined AGEC receipts
│
├── RestaurantPos.Api/
│   ├── Endpoints/
│   │   └── CounterSaleEndpoints.cs           # Minimal API endpoints for counter & takeaway
│   └── wwwroot/
│       ├── app.js                            # Tactile counter cart, hold drawer & AGEC prompt
│       └── index.html                        # Counter header toggle, hold badge & large overlays
│
tests/
├── RestaurantPos.Api.Tests/
│   ├── CounterCheckoutTests.cs               # Unit & integration tests for multi-split & VAT
│   ├── MealVoucherPolicyTests.cs             # Policy A, B, C verification tests
│   └── HeldOrderQueueTests.cs                # Hold & recall queue lifecycle tests
└── RestaurantPos.Web.E2ETests/
    └── tests/
        └── takeaway-direct-sales.spec.ts     # Playwright tactile end-to-end tests
```

---

## Complexity Tracking

> No violations of the Constitution identified. Design strictly adheres to Clean Architecture, integer cent `Money`, NF525 immutability, and touch ergonomics.
