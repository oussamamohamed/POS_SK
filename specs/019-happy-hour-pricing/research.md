# Research: Happy Hour Pricing & Schedule Management

**Feature**: [`specs/019-happy-hour-pricing/spec.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/019-happy-hour-pricing/spec.md)  
**Phase**: Phase 0 — Outline & Research  
**Status**: Completed  

---

## 1. Executive Summary & Core Decisions

| Domain / Problem | Decision | Rationale | Alternatives Considered |
|---|---|---|---|
| **Pricing Calculation Model** | **Hybrid Rule Engine**: Dedicated Fixed Price (`Money`) per product OR Category Percentage Discount (`decimal`), with priority given to Fixed Price. | Maximizes flexibility for bar owners: rounded prices for drafts (e.g. 5.00 €) and category-wide discounts on cocktails (-20%). | Fixed price only (inflexible for large menus); Percentage only (causes non-round pricing like 5.12 €). |
| **Order Price Lock Lifecycle** | **Lock on Order Creation/Dispatch**: The effective unit price is captured when the item is added / dispatched. The price is immutable on the `OrderItem`. | Industry standard for hospitality. Avoids checkout disputes when guests order during Happy Hour but pay after 20:00. | Recomputing on bill print (causes heavy customer friction); Session-wide discount (causes revenue loss on late rounds). |
| **Time & Timezone Authority** | **Local Timezone with TimeOnly ranges**: Schedules use local standard restaurant time (`TimeOnly`) evaluated against `DateTimeOffset.Now` in restaurant time zone. | Happy hours are strictly local events tied to wall-clock time (e.g. 17h-20h Paris time). | Pure UTC ranges (fails when Daylight Saving Time changes); Client-provided timestamp (insecure, clock tampering). |
| **Supervisor Override & JET Audit** | **Explicit Override Session with Jet Log**: Stored in DB with supervisor ID, expiry time, and logged to the NF525 `TransactionJournalEntry` (JET). | Satisfies NF525 non-repudiation and traceability while giving managers operational leeway for events. | Unaudited client-side toggle (non-compliant); Permanent schedule change (requires tedious admin cleanup). |
| **Offline Resilience** | **Autonomous Local State Evaluation**: In-memory cache of schedules + periodic check (every 30s) running in the terminal. | Adheres to Constitution Principle III (Offline-First). The bar can continue operating and pricing during Wi-Fi outages. | Server-only RPC per click (violates offline-first and touch latency SLO < 50ms). |
| **Real-Time Distribution** | **SignalR Event Broadcast**: `PosHub.SendHappyHourStatusChanged` pushes changes to all connected POS and KDS screens. | Ensures all terminals show the badge, update tile prices, and synchronize remaining time instantly (< 200ms). | HTTP polling only (higher latency, unnecessary server load); No sync (discrepancy between terminals). |

---

## 2. Technical Research Details

### 2.1 Pricing Resolution Pipeline

```text
Product added to Cart
       │
       ▼
Is Happy Hour Active? (Check Active Override OR Recurring Schedule)
       │
       ├─ No  ──► Apply Standard Price (product.Price)
       │
       └─ Yes ──► Check HappyHourPriceRule:
                   1. Specific Product Rule (Fixed Price or % Discount)
                   2. Category Rule (% Discount)
                   3. If multiple: Select Lowest Price (Best Value)
                   4. Stamp OrderItem:
                      - UnitPrice = HappyHourPrice
                      - OriginalUnitPrice = StandardPrice
                      - IsHappyHourApplied = true
```

### 2.2 Financial & NF525 Compliance

1. **Integer Cents Representation**:
   All monetary calculations strictly use the `Money` value object (`AmountInCents`), avoiding IEEE-754 floating-point rounding errors.
   - Example: 7.50 € (-20%) -> `Math.Round(750 * 0.80) = 600` cents (6.00 €).
2. **VAT Allocation**:
   VAT remains compliant with the product's assigned tax rate:
   - On-site consumption (10% food/soft, 20% alcohol):
     - `TotalTtc = 600` cents
     - `TotalHt = Round(600 / 1.20) = 500` cents
     - `TaxAmount = 100` cents
3. **Receipt & Journal Traceability**:
   - `OrderItem` records both `OriginalUnitPrice` (7.50 €) and `UnitPrice` (6.00 €).
   - Fiscal receipt lines indicate promotional status `[HH]`.

### 2.3 Supervisor Overrides & Security

- Requires role `FloorManager` or `Admin`.
- PIN authenticated via `IOperatorAuthenticationService.AuthenticatePinAsync`.
- Audit record emitted to `TransactionJournalEntry`:
  - `EventType`: `"EVENT_HAPPY_HOUR_OVERRIDE"`
  - `Payload`: `{ "Action": "Activate", "DurationMinutes": 60, "OperatorId": "...", "Reason": "Match prolongation" }`
  - Chained with JET cryptographic hash.

---

## 3. Best Practices & Design Constraints

1. **Tactile Screen Performance**:
   Pre-calculate the active Happy Hour pricing table in client state so catalog clicks execute synchronously in < 5ms without awaiting API round-trips.
2. **Visual Ergonomics**:
   - Amber / Golden banner on the header: `🎉 Happy Hour (17:00 - 20:00) — Reste 42 min`.
   - On product cards: standard price strikethrough (~~7,50 €~~) and Happy Hour price highlighted in bold amber (`5,00 €`).
   - In cart items: subtle tag `[Happy Hour]`.
