# Quickstart & Verification Guide: Direct Sales & Takeaway Checkout

**Feature**: `018-takeaway-direct-sales`  
**Date**: 2026-09-13  
**Status**: Ready for Verification  

---

## 1. Prerequisites & Environment Setup

Ensure the .NET 9 API and client dev environments are running:

```bash
# 1. Verify build and database state
dotnet build --configuration Debug

# 2. Run automated test suite
dotnet test tests/RestaurantPos.Api.Tests --filter "Category=DirectSales|Category=Takeaway"

# 3. Start local API server (if running manual E2E/Playwright verification)
dotnet run --project src/RestaurantPos.Api
```

---

## 2. Runnable Verification Scenarios

### Scenario 1: Default Direct Sale Express Checkout
*Objective*: Validate that opening the POS presents an active direct cart by default, requiring no table selection, and closes a cash sale in < 3 taps.

1. Navigate to the POS screen: `http://localhost:5000`
2. Authenticate as Cashier (PIN: `1234`).
3. **Verify**: The cart is immediately open in mode `[À EMPORTER]` by default. No table floorplan is prompted.
4. Tap product `Burger Classic` (12.00 €).
5. Tap quick cash shortcut `[Billet 20 €]`.
6. **Expected Outcome**:
   - Order is paid and sealed in NF525 ledger.
   - Cash change of `8.00 €` is displayed in large tactile overlay.
   - Cash drawer open signal is dispatched.
   - Screen resets to a fresh, active direct cart ready for the next customer.

---

### Scenario 2: Dynamic VAT Recalculation (Sur Place $\leftrightarrow$ À Emporter)
*Objective*: Verify that toggling consumption destination recalculates VAT lines instantly (5.5% vs 10%).

1. Add 1 `Canette Soda 33cl` (scellée, 3.00 €) and 1 `Pizza Margherita` (11.00 €).
2. By default (`À Emporter`), observe the tax breakdown:
   - Soda: Taxed at **5.5%** (HT: 2.84 €, TVA: 0.16 €)
   - Pizza: Taxed at **10.0%** (HT: 10.00 €, TVA: 1.00 €)
   - Total TTC: 14.00 € (Total HT: 12.84 €, Total TVA: 1.16 €).
3. Tap the header toggle `[Sur Place]`:
   - Soda instantly switches to **10.0%** (HT: 2.73 €, TVA: 0.27 €)
   - Pizza remains at **10.0%** (HT: 10.00 €, TVA: 1.00 €)
   - Total TTC: 14.00 € (Total HT: 12.73 €, Total TVA: 1.27 €).
4. Tap `[À Emporter]` again: the tax breakdown immediately reverts to 5.5% on the soda.
5. **Expected Outcome**: Latency is $< 50\text{ms}$; sum of HT + TVA equals exact TTC to the cent.

---

### Scenario 3: Split Tender with Meal Voucher Ceiling & Policy A
*Objective*: Verify meal voucher eligibility, daily ceiling (25.00 €), and zero-change policy.

1. Create a cart with:
   - 2 `Menu Midi Burger` (24.00 €, food voucher eligible)
   - 1 `Bière Artisanale 33cl` (6.00 €, alcohol, not voucher eligible).
   - Total: 30.00 €.
2. Tap `[Paiement]` $\to$ `[Titre-Restaurant]`.
3. Verify the suggested eligible max is **24.00 €** (not 30.00 €).
4. Enter a paper meal voucher of **25.00 €** (surplus of 1.00 € over the 24.00 € eligible amount).
   - Under Policy A (`CapAtBalance`): The POS accepts the voucher, caps the imputed amount at 24.00 €, displays **0.00 € monnaie rendue**, and updates remaining due to **6.00 €**.
5. Tap `[Carte Bancaire]` for the remaining 6.00 € and add `1.00 €` tip.
6. **Expected Outcome**: Order is sealed at 30.00 € TTC + 1.00 € tip. No cash change is given on meal vouchers.

---

### Scenario 4: Counter Queue Management (Hold & Recall)
*Objective*: Verify that parking a cart frees the register and that recalled carts restore exact state.

1. Add 3 items to a direct cart (Total: 21.50 €).
2. Tap `[Mettre en attente]` (Hold).
3. Verify:
   - The active cart is cleared and ready for a new sale.
   - Top bar displays indicator badge: `En attente (1)`.
4. Process and pay another quick order of 1 coffee (2.50 €).
5. Tap the `En attente (1)` badge $\to$ the drawer opens showing the parked order (21.50 €).
6. Tap `[Reprendre]` (Recall).
7. **Expected Outcome**: All 3 items, options, and taxes are completely restored to the active cart.

---

### Scenario 5: Anti-Waste Printing Prompt (Loi AGEC)
*Objective*: Validate pickup coupon vs. fiscal receipt printing behavior.

1. Complete a takeaway order.
2. The modal prompt appears: *"Le client souhaite-t-il sa facturette ?"*
   - Test A: Tap `[Non]` (or wait 3 seconds) $\to$ Only the compact **Coupon de Retrait** (`#A-01`) is sent to the printer.
   - Test B: Tap `[Oui]` $\to$ Full combined NF525 fiscal receipt with detachable header `#A-02` is printed.

---

### Scenario 6: Supervisor PIN Security Gate
*Objective*: Verify that deleting parked orders or voiding partially paid carts is restricted to managers.

1. Put an order on hold.
2. Open the hold drawer and tap `[Supprimer / Annuler]`.
3. A tactile PIN modal appears: *"Code PIN Superviseur Requis"*.
4. Enter cashier PIN (`1234`) $\to$ Refused: *"Autorisation insuffisante"*.
5. Enter supervisor PIN (`9999`) $\to$ Accepted: Order is voided, and JET fiscal log records `EVENT_HELD_ORDER_VOIDED`.
