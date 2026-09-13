# Quickstart: Happy Hour Pricing & Schedule Management

**Feature**: [`specs/019-happy-hour-pricing/spec.md`](file:///Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual%20Studio%202022/POS_SK_Antigavity/specs/019-happy-hour-pricing/spec.md)  
**Phase**: Phase 1 — Validation Guide  
**Status**: Ready  

---

## 1. Prerequisites & Environment

1. **Backend Running**:
   ```bash
   dotnet run --project src/RestaurantPos.Api
   # Running on http://localhost:5000
   ```
2. **Database Initialized**: SQLite database `src/RestaurantPos.Api/restaurantpos.db` with seeded products and tables.

---

## 2. Validation Scenarios

### Scenario 1: Automatic Detection & Pricing in Cart
1. **Setup**:
   - Verify that an active Happy Hour schedule exists for the current day and time (or activate an override for testing).
2. **Execution**:
   - Open Web POS at `http://localhost:5000`.
   - Log in with PIN `1234`.
   - Observe header: verify amber banner `🎉 Happy Hour en cours`.
   - In catalog, find "Bière Artisanale": verify price displays the Happy Hour rate (e.g. `5.00 €` with `7.50 €` strikethrough).
   - Click the product to add to cart: verify unit price in cart is `5.00 €`.
3. **Expected Outcome**:
   - Cart calculates total based on `5.00 €`.
   - NF525 tax calculation reflects VAT based on `5.00 €`.

---

### Scenario 2: Price Lock on Table Orders
1. **Execution**:
   - Open table `T01`.
   - Add 2 pints of beer during Happy Hour (unit price `5.00 €`, total `10.00 €`).
   - Click "Envoyer en Cuisine" to dispatch.
   - Deactivate Happy Hour (or fast-forward time past end time).
   - Re-open table `T01`:
     - Verify existing pints are STILL `5.00 €`.
     - Add 1 additional pint of beer: verify the new pint is charged at the standard price `7.50 €`.
     - Total table note: `2 * 5.00 + 1 * 7.50 = 17.50 €`.
   - Proceed to checkout and validate payment.
2. **Expected Outcome**:
   - Total paid is `17.50 €`.
   - Ticket prints itemized receipt with `[HH]` tag on the first two beers.

---

### Scenario 3: Supervisor PIN Override
1. **Execution**:
   - In Web POS, click supervisor quick action: "Déclencher Happy Hour (+60 min)".
   - Enter invalid PIN `1111`: verify access is denied with error message.
   - Enter manager PIN `9999`: verify success toast.
   - Observe header: badge updates immediately with countdown.
2. **Expected Outcome**:
   - SignalR event `HappyHourStatusChanged` updates all connected screens.
   - Database table `TransactionJournalEntries` logs `EVENT_HAPPY_HOUR_OVERRIDE` with operator `Marc`.

---

## 3. Automated Test Commands

Run automated tests validating Happy Hour business rules and E2E flows:

```bash
# 1. Backend Service & Domain Tests
dotnet test tests/RestaurantPos.Infrastructure.Tests --filter "FullyQualifiedName~HappyHour"

# 2. Web E2E Tests (Playwright)
npx playwright test tests/happy-hour.spec.ts --reporter=line
```
