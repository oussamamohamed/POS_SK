# Feature Specification: Phase 4 - Multi-Payment Checkout, Split Bill & NF525 Fiscal Audit Chain

**Feature Branch**: `006-phase4-checkout-nf525-splitbill`

**Created**: 2026-08-15

**Status**: Draft

**Input**: User description: "phase 4" (From PLAN.md Phase 4: Caisse, Encaissement & Clôtures Fiscales NF525)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Multi-Payment Settlement & Change Calculation (Priority: P1)

Waitstaff need a high-speed tactile payment interface supporting multiple payment methods (Cash with real-time change calculation, Credit Card / TPE, Meal Vouchers / Titres Restaurant), allowing partial multi-tender payments and settling transactions in $< 500\text{ms}$.

**Why this priority**: Fast table turnover requires frictionless, error-free payment handling during rush hours.

**Independent Test**: Open an unpaid table with a 50.00 € note. Pay 20.00 € with a Meal Voucher and 30.00 € with Cash (tendering a 50.00 € bill). Verify that the interface shows 20.00 € change due, closes the table, and records the exact tenders in $< 500\text{ms}$.

**Acceptance Scenarios**:

1. **Given** an active order totaling 45.00 €, **When** the server selects "Espèces" and taps "50 €", **Then** the screen displays "Rendu monnaie : 5,00 €" with oversized tactile buttons and finalizes the transaction.
2. **Given** an order totaling 80.00 €, **When** a customer pays 40.00 € by Credit Card and the remaining 40.00 € in Cash, **Then** both payment tenders are recorded under the same receipt with exact amounts.
3. **Given** a payment entry, **When** processed, **Then** the active table status automatically changes to `Paid` on the 2D floor plan.

---

### User Story 2 - Tactile Split Bill & Seat-Based Partitioning (Priority: P1)

Waitstaff need to divide dining checks easily—either equally across $N$ guests (e.g. 4 people sharing equally) or by selecting specific dishes and drinks per guest—settling each partition independently without table locking.

**Why this priority**: Restaurant guests frequently request divided bills; fast splitting prevents front-of-house bottlenecks.

**Independent Test**: Take an order with 3 items (20 €, 15 €, 25 € = 60 € total). Split equally among 3 guests (20 € each). Settle the first 20 €; verify that remaining balance displays 40 € and table remains open until all parts are settled.

**Acceptance Scenarios**:

1. **Given** a 100.00 € check with 4 guests, **When** the server taps "Partager en 4 parts égales", **Then** 4 payment tabs of 25.00 € each are created.
2. **Given** an itemized order, **When** the server taps "Par article" and selects 2 beers and 1 steak for Guest A, **Then** Guest A's subtotal is calculated and settled separately while remaining items stay on the table note.
3. **Given** rounding when dividing uneven amounts (e.g. 10.00 € divided by 3), **Then** the algorithm distributes cents (3.34 €, 3.33 €, 3.33 €) guaranteeing exact balance $\sum = 10.00\text{ \euro}$.

---

### User Story 3 - NF525 Cryptographic Audit Hash Chain & Fiscal Closures (Priority: P1)

The restaurant owner and tax auditor require an immutable, append-only audit ledger compliant with French NF525 fiscal regulations, featuring sequential receipt numbering (`POS01-001042`), cryptographic SHA-256 chaining ($\text{Hash}_n = \text{SHA-256}(\text{Hash}_{n-1} \parallel \text{Data}_n)$), on-demand X-reports, and immutable daily Z-closures.

**Why this priority**: Legal requirement under French finance law (article 286 du CGI / NF525 / LNE) to prevent tax fraud and tampering.

**Independent Test**: Record 5 sequential transactions. Verify that each record contains the SHA-256 hash of the previous record. Verify that running the audit verification tool validates 100% chain integrity. Generate a Daily Z-Report; verify that daily counters seal and cumulative grand totals increment.

**Acceptance Scenarios**:

1. **Given** a new transaction, **When** persisted, **Then** its signature $\text{SHA-256}(\text{PrevHash} \parallel \text{Seq} \parallel \text{Amount} \parallel \text{Taxes} \parallel \text{Timestamp})$ is computed and saved immutably.
2. **Given** a request for an X-report during a shift, **When** generated, **Then** total sales by VAT category (5.5%, 10%, 20%) and payment method are printed without resetting counters.
3. **Given** end-of-day closure (Z-report), **When** confirmed by a manager PIN, **Then** daily counters reset to zero, perpetual cumulative sales are updated, and a cryptographic closure signature is recorded.

---

### Edge Cases

- **Exact Cent Distribution on Split Bills**: Dividing 100.00 € by 3 yields parts of 33.34 €, 33.33 €, 33.33 €, strictly summing to 100.00 €.
- **Chain Tampering Detection**: If any row in the SQLite or PostgreSQL journal is altered manually, the verification algorithm immediately detects a broken signature link and raises a fiscal integrity alert.
- **Offline Receipt Generation**: Terminal creates sequential terminal-prefixed numbers (`POS01-001042`) offline, preventing duplicate sequence collisions.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support multi-tender checkout per transaction (Cash, Credit Card, Meal Voucher / Titres Restaurant, Gift Card).
- **FR-002**: System MUST compute change due in real-time when cash tendered exceeds remaining balance.
- **FR-003**: System MUST provide tactile split-bill modes: Equal division across $N$ guests, or Itemized selection of dishes per sub-group.
- **FR-004**: System MUST maintain an append-only cryptographic hash chain complying with French NF525 requirements: $\text{CurrentHash} = \text{SHA-256}(\text{PreviousHash} \parallel \text{Sequence} \parallel \text{AmountCents} \parallel \text{TimestampUtc} \parallel \text{TaxesJson})$.
- **FR-005**: System MUST assign monotonic sequential receipt numbers formatted per terminal (e.g. `POS01-001042`).
- **FR-006**: System MUST generate intermediate X-reports (non-resetting shift turnover and tax breakdown) on demand.
- **FR-007**: System MUST perform immutable daily Z-closures, resetting active counters and sealing the fiscal period with cumulative perpetual grand totals.
- **FR-008**: System MUST format legal thermal receipt ESC/POS print commands including French statutory VAT breakdown (5.5%, 10%, 20%), fiscal receipt signature, and QR code payload in $< 500\text{ms}$.

### Key Entities *(include if feature involves data)*

- **FiscalReceipt**: Immutable receipt with `ReceiptNumber`, `OrderId`, `TotalAmount` (`Money`), `TaxBreakdown`, `PaymentTenders`, `PreviousSignatureHash`, `SignatureHash`, `SequenceNumber`, `CreatedAtUtc`.
- **PaymentTender**: Represents a payment part (`TenderType` [Cash, Card, MealVoucher], `AmountTendered`, `ChangeGiven`).
- **DailyFiscalClosure (Z-Report)**: Represents a daily period closure with `ClosureSequence`, `OpeningAtUtc`, `ClosingAtUtc`, `TotalTtcAmount`, `TotalHtAmount`, `TaxesSummary`, `PaymentMethodTotals`, `CumulativeGrandTotal`, `PeriodHashSignature`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Payment validation and fiscal receipt generation completes in under 500 milliseconds.
- **SC-002**: Split bill arithmetic guarantees $\sum \text{Partitions} = \text{TotalOrderAmount}$ with 0 cent rounding errors.
- **SC-003**: Cryptographic hash chain audit validation yields 100% integrity verification with zero altered or missing links.
- **SC-004**: Daily Z-closure computes cumulative perpetual grand totals accurately across all payment methods.

## Assumptions

- French VAT rates (5.5% food, 10% restaurants/prepared food, 20% alcoholic beverages) are configured per product.
- Each POS terminal has a unique prefix identifier (`POS01`, `POS02`).
