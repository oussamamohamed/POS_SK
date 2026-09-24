# Implementation Plan: Phase 4 - Multi-Payment Checkout, Split Bill & NF525 Fiscal Audit Chain

**Branch**: `006-phase4-checkout-nf525-splitbill` | **Date**: 2026-08-15 | **Spec**: [specs/006-phase4-checkout-nf525-splitbill/spec.md](spec.md)

**Input**: Feature specification from `specs/006-phase4-checkout-nf525-splitbill/spec.md`

## Summary

Deliver legal checkout settlement and French NF525 fiscal compliance: multi-tender payments (Cash, Card, Meal Vouchers) with tactile change calculation, zero-loss split-bill partitioning, an unalterable append-only transaction ledger with cryptographic SHA-256 hash chaining, periodic X-reports, and immutable daily Z-closures.

## Technical Context

**Language/Version**: C# 13 / .NET 9.0

**Primary Dependencies**:
- Cryptography: `System.Security.Cryptography.SHA256`
- Core & Application: `CommunityToolkit.Mvvm`, `MediatR`
- Client UI: .NET MAUI (`PaymentModal.xaml`, `SplitBillModal.xaml`, `FiscalReportsPage.xaml`)
- Storage: EF Core with PostgreSQL and SQLite

**Storage**:
- `FiscalReceipts`, `DailyFiscalClosures`, `PaymentTenders`

**Testing**: `xUnit`, `FluentAssertions`, `Moq`

**Target Platform**: Cross-platform (iOS, Android, Windows)

**Performance Goals**:
- Checkout validation & receipt generation: $< 500\text{ms}$
- Split bill arithmetic: $< 5\text{ms}$
- Audit verification traversal: $< 200\text{ms}$ for 100,000 records

**Constraints**:
- Absolute mathematical zero-loss cent balance ($\sum \text{Partitions} = \text{Total}$)
- 100% cryptographic tamper detection
- Zero compiler warnings (`TreatWarningsAsErrors=true`)

## Constitution Check

*GATE: Evaluated and Passed across all Core Principles.*

| Principle | Status | Compliance Verification |
| :--- | :--- | :--- |
| **I. Touch-First Ergonomics & Tactile Design** | **PASS** | Pavé numérique grand format pour saisie espèces, suggestions de coupures rapides (10€, 20€, 50€). |
| **II. Clean Architecture & Centralized Multi-POS Backend** | **PASS** | Modèles `FiscalReceipt` et `DailyFiscalClosure` dans `Domain`, services dans `Infrastructure`, interfaces dans `Application`. |
| **III. Transactional Integrity and Offline-First Operations** | **PASS** | Numérotation préfixée par terminal (`POS01-001042`) pour scellement fiscal hors-ligne sans collision. |
| **IV. Hardware Driver Abstraction & Real-Time Sync** | **PASS** | Impression ESC/POS des mentions légales et QR code fiscal NF525 via `NetworkPrinterClient`. |
| **V. Test-First, Immutability & Fiscal Traceability** | **PASS** | Chaînage cryptographique SHA-256 vérifié à 100% par des tests unitaires d'audit et de détection de falsification. |

## Project Structure

### Documentation (this feature)

```text
specs/006-phase4-checkout-nf525-splitbill/
├── plan.md              # Implementation plan (/speckit-plan output)
├── research.md          # SHA-256 hash chaining & split bill algorithms
├── data-model.md        # FiscalReceipt, DailyFiscalClosure schemas
├── quickstart.md        # Build & test verification guide
├── contracts/           # Checkout & NF525 audit contracts
│   ├── checkout-payment-contract.md
│   └── nf525-audit-closure-contract.md
├── checklists/
│   └── requirements.md  # Specification quality validation checklist
└── spec.md              # Feature specification
```

### Source Code Architecture

```text
src/
├── RestaurantPos.Domain/
│   └── Entities/                      # FiscalReceipt, DailyFiscalClosure, PaymentTender
│
├── RestaurantPos.Application/
│   └── Common/Interfaces/             # ICheckoutPaymentService, INF525FiscalAuditService
│
├── RestaurantPos.Infrastructure/
│   └── Services/                      # CheckoutPaymentService, NF525FiscalAuditService
│
└── RestaurantPos.Client.Maui/
    ├── ViewModels/                    # CheckoutViewModel, SplitBillViewModel, FiscalReportsViewModel
    └── Views/                         # PaymentModal.xaml, SplitBillModal.xaml
```

## Complexity Tracking

*No unjustified complexity violations.*
