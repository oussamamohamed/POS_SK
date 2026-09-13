# Research & Architecture Decisions: Direct Sales and Takeaway Checkout with Complex Scenarios

**Feature**: `018-takeaway-direct-sales`  
**Date**: 2026-09-13  
**Status**: Completed  

---

## 1. Differentiated VAT Re-calculation Engine (Eat-In vs. Takeaway)

### Context & Challenge
Under French tax law (CGI art. 278-0 bis, 279), catering sales are taxed according to the consumption destination:
- **Eat-In (Sur Place)**: Standard catering rate of 10.0% for all food and non-alcoholic drinks served, 20.0% for alcoholic beverages.
- **Takeaway (À Emporter)**: 
  - Prepared food and drinks for immediate consumption (hot sandwiches, salads, cooked dishes, poured sodas, espresso): **10.0%**.
  - Sealed, packaged food and non-alcoholic beverages allowing deferred storage/consumption (canned sodas, sealed bottles, pre-packaged pastries, yogurts, unopened snacks): **5.5%**.
  - Alcoholic beverages: **20.0%**.

### Decision
Enhance catalog product definitions and order line pricing logic with an explicit tax mapping:
1. Each `CatalogItem` defines:
   - `TaxRateEatIn` (e.g. 10.0% or 20.0%)
   - `TaxRateTakeaway` (e.g. 5.5%, 10.0%, or 20.0%)
   - `IsFoodVoucherEligible` (bool)
2. When the user toggles destination mode (`Sur Place` $\leftrightarrow$ `À Emporter`), a domain method `Order.SetDestination(OrderDestination destination)` triggers an instantaneous re-taxation of every active line item in the cart:
   $$\text{LineTaxRate} = (\text{Destination} == \text{Takeaway}) ? \text{Item.TaxRateTakeaway} : \text{Item.TaxRateEatIn}$$
   $$\text{LineHt} = \text{RoundCents}\left(\frac{\text{LineTtc}}{1 + \text{LineTaxRate}}\right)$$
   $$\text{LineVat} = \text{LineTtc} - \text{LineHt}$$
3. All calculations maintain exact zero-cent rounding sum invariance: $\sum \text{LineHt} + \sum \text{LineVat} = \text{OrderTotalTtc}$.

### Rationale
- Guaranteed NF525 fiscal compliance without requiring cashiers to memorize tax classifications.
- Instant sub-50ms recalculation on client side without blocking network calls.

---

## 2. Meal Voucher (Titres-Restaurant) Overpayment Policies

### Context & Challenge
French law strictly forbids giving cash change on paper meal vouchers (*interdiction formelle de rendu de monnaie*). Furthermore, meal vouchers can only be spent on eligible food items, subject to a daily ceiling (default: 25.00 €). If a customer hands a 10.00 € voucher for an 8.50 € bill, how should the POS behave?

### Decision
Implement a terminal-level configurable setting `MealVoucherOverpaymentPolicy` with three selectable strategies:
- **Policy A (Default - `CapAtBalance`)**: The voucher is consumed for the transaction, the order is settled, exactly 0.00 € change is given, and the 1.50 € non-refundable surplus is fiscally recorded as non-refundable voucher gain (`VoucherSurplusLossToCustomer`).
- **Policy B (`StrictRejection`)**: The POS rejects any voucher input value strictly greater than the remaining balance due, requiring the cashier to enter an exact or smaller amount.
- **Policy C (`CustomerCreditVoucher`)**: The order is settled and the system automatically generates an immutable store credit voucher (`CustomerCredit`) with an alphanumeric barcode token for future redemption.

### Rationale
- Complies with French commercial and fiscal requirements while providing restaurant owners operational flexibility based on their customer service policies.

---

## 3. Decentralized Collision-Free Pickup Numbering (`#A-01` .. `#A-99`)

### Context & Challenge
In multi-terminal environments operating offline-first (Local-First), two cashiers entering takeaway orders simultaneously during a network disconnect must not generate duplicate customer pickup numbers (e.g. both handing `#12` to waiting customers).

### Decision
Implement terminal-prefixed monotonic daily sequences:
$$\text{PickupIdentifier} = \text{TerminalPrefix} + \text{"-"} + \text{DailySequenceNumber (01..99)}$$
- Terminal 1 $\to$ `#A-01` to `#A-99`
- Terminal 2 $\to$ `#B-01` to `#B-99`
- Terminal 3 $\to$ `#C-01` to `#C-99`
- Each terminal manages its local atomic counter in SQLite, resetting to `01` upon daily Z-closure or first sale of the local calendar day.

### Rationale
- 100% autonomous operation without requiring central server lock or distributed consensus.
- Clear, readable acoustic and visual calling at the pickup counter.

---

## 4. Counter Hold & Recall Queue Architecture

### Context & Challenge
At rush hour, customers frequently hesitate or look for payment. To avoid blocking the checkout lane, the active cart must be safely parked in memory and local SQLite storage, freeing the register for the next customer.

### Decision
1. Introduce a `HeldOrdersManager` service:
   - Quick "Mettre en attente" (Hold) parks the active cart into a lightweight `HeldOrder` entity with a unique `HoldId`, timestamp, optional customer note, and terminal ID.
   - The UI displays an active badge: `En attente (N)`.
   - Tapping the badge opens a quick drawer showing parked orders with items and elapsed time.
   - Selecting a parked order restores all lines, modifiers, and taxes into the active cart.
2. Security & Fraud Prevention:
   - Discarding an unstarted draft cart is free.
   - Deleting a parked order or an order with already registered partial payments requires supervisor PIN authentication and logs a JET event (`HELD_ORDER_VOIDED`).

---

## 5. Anti-Waste (Loi AGEC) Receipt Printing Flow

### Context & Challenge
French law AGEC (effective 2023) prohibits systematic printing of sales receipts unless requested by the customer. However, takeaway operations require a physical order identifier so the customer can collect their order when ready.

### Decision
At checkout completion:
1. The POS displays a swift tactile prompt: *"Le client souhaite-t-il sa facturette ?"* with a 3-second auto-default timer to **Non**.
2. **If Non (or timeout)**: The printer generates solely the compact **Pickup Voucher** (displaying `#A-XX`, scheduled pickup time, and order summary for packing verification).
3. **If Oui**: The printer outputs the **Combined Receipt** (NF525 fiscal receipt with legal mentions + detachable header pickup voucher `#A-XX`).
