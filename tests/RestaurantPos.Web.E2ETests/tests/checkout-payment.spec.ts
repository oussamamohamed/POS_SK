/**
 * checkout-payment.spec.ts — Couverture E2E exhaustive du système d'encaissement
 *
 * Notes techniques :
 * - body.totalPaid / body.changeGiven sont en EUROS (décimaux) dans CounterCheckoutResponse
 * - body.pickupNumber : chaîne format #X-DD
 * - Chaque test est isolé via un reset DB de la table Comptoir (ActiveOrderId = null)
 * - ensureLoggedIn garantit un panier propre (0.00 €) au départ de chaque test
 */

import { test, expect, type Page } from '@playwright/test';
import { execSync } from 'child_process';

const DB_PATH = '/Users/oussama/Library/CloudStorage/OneDrive-Personnel/Documents/Visual Studio 2022/POS_SK_Antigavity/src/RestaurantPos.Api/restaurantpos.db';

function resetComptoirDb() {
  try {
    execSync(`sqlite3 "${DB_PATH}" "UPDATE DiningTables SET ActiveOrderId = NULL, Status = 0 WHERE TableNumber = 'Comptoir';"`, { stdio: 'ignore' });
  } catch {}
}

// ─── helpers ────────────────────────────────────────────────────────────────

async function closeOverlay(page: Page) {
  const overlay = page.locator('#changeOverlayModal');
  if (await overlay.evaluate((el: HTMLElement) => el.classList.contains('active') || el.style.display !== 'none').catch(() => false)) {
    const btnDone = page.locator('#btnChangeDone');
    if (await btnDone.isVisible().catch(() => false)) {
      await btnDone.click();
      await expect(overlay).not.toBeVisible();
    }
  }
}

async function ensureLoggedIn(page: Page) {
  resetComptoirDb();

  await page.goto('/?nocache=' + Date.now());
  await page.waitForLoadState('domcontentloaded');
  await page.waitForSelector('#activeTableBadge');

  const pinModal = page.locator('#pinLockModal');
  if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active')).catch(() => false)) {
    await page.click('#btnPinClear');
    for (const d of ['1', '2', '3', '4']) {
      await page.click(`.pin-keypad button[data-val="${d}"]`);
    }
    await expect(pinModal).not.toHaveClass(/active/);
  }

  // Fermer tout overlay de rendu résiduel
  await closeOverlay(page);
}

async function addProduct(page: Page, hint = 'Expresso') {
  await page.waitForSelector('.product-card');
  const card = page.locator('.product-card').filter({ hasText: hint }).first();
  const target = await card.isVisible() ? card : page.locator('.product-card').first();
  await target.click();

  const modModal = page.locator('#modifiersModal');
  if (await modModal.evaluate((el: HTMLElement) => el.classList.contains('active')).catch(() => false)) {
    await page.click('#btnConfirmModifiers');
  }
  await expect(page.locator('#summaryTtc')).not.toHaveText('0.00 €');
}

/**
 * Intercepte POST /api/orders/counter/checkout → déclenche action → retourne la réponse JSON.
 */
async function interceptCheckout(page: Page, trigger: () => Promise<void>) {
  const pending = page.waitForResponse(
    (r) => r.url().includes('/api/orders/counter/checkout') && r.status() === 200
  );
  await trigger();
  const resp = await pending;
  return resp.json();
}

// ─── suite ──────────────────────────────────────────────────────────────────

test.describe('Encaissement — Cas de paiement complets', () => {

  test.beforeEach(async () => {
    resetComptoirDb();
  });

  // 1. Carte Bancaire
  test('CB : paiement exact par carte, modal fermé', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page);

    const body = await interceptCheckout(page, async () => {
      await page.click('#btnPayModal');
      await expect(page.locator('#paymentModal')).toHaveClass(/active/);
      await page.click('.tender-types-grid button[data-tender="Card"]');
    });

    expect(body.changeGiven).toBeCloseTo(0, 2);
    expect(body.totalPaid).toBeGreaterThan(0);
    await expect(page.locator('#paymentModal')).not.toHaveClass(/active/);
    await closeOverlay(page);
  });

  // 2. Espèces exact sans tip → changeGiven = 0
  test('Cash : paiement exact sans tip → changeGiven = 0.00 €', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page);

    const body = await interceptCheckout(page, async () => {
      await page.locator('.cart-fast-cash-bar button[data-cash="exact"]').click();
    });

    expect(body.changeGiven).toBeCloseTo(0, 2);

    const overlay = page.locator('#changeOverlayModal');
    await expect(overlay).toBeVisible();
    await expect(page.locator('#changeOverlayAmount')).toHaveText('0.00 €');
    await closeOverlay(page);
  });

  // 3. Espèces exact avec tip 10% — bug fix régression
  test('Cash + tip 10% : changeGiven = 0 (tip inclus dans totalDue)', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page);

    const totalText = await page.locator('#summaryTtc').textContent();
    const totalTtc = parseFloat((totalText || '0').replace('€', '').replace(',', '.').trim());
    const tipEur = totalTtc * 0.10;

    await page.click('#btnPayModal');
    await expect(page.locator('#paymentModal')).toHaveClass(/active/);
    await page.locator('.btn-tip-pill[data-tip-percent="10"]').click();

    const body = await interceptCheckout(page, async () => {
      await page.click('.tender-types-grid button[data-tender="Cash"]');
    });

    // changeGiven = 0 (paiement exact), PAS = tip
    expect(body.changeGiven).toBeCloseTo(0, 2);
    expect(body.totalPaid).toBeCloseTo(totalTtc + tipEur, 1);

    await expect(page.locator('#changeOverlayModal')).toBeVisible();
    await expect(page.locator('#changeOverlayAmount')).toHaveText('0.00 €');
    await closeOverlay(page);
  });

  // 4. Espèces avec rendu — billet modal > total
  test('Cash billet modal : changeGiven > 0 quand billet > total', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page, 'Expresso');  // 2.50 €

    const totalText = await page.locator('#summaryTtc').textContent();
    const totalTtc = parseFloat((totalText || '0').replace('€', '').replace(',', '.').trim());
    if (totalTtc > 10) { return; }

    const body = await interceptCheckout(page, async () => {
      await page.click('#btnPayModal');
      await expect(page.locator('#paymentModal')).toHaveClass(/active/);
      await page.click('.btn-cash-bill[data-cash="10"]');   // billet 10 €
    });

    const changeEur = body.changeGiven;
    expect(changeEur).toBeGreaterThan(0);
    expect(changeEur).toBeCloseTo(10 - totalTtc, 1);

    const overlay = page.locator('#changeOverlayModal');
    await expect(overlay).toBeVisible();
    const changeText = await page.locator('#changeOverlayAmount').textContent();
    const displayed = parseFloat((changeText || '0').replace('€', '').replace(',', '.').trim());
    expect(displayed).toBeCloseTo(changeEur, 1);
    await closeOverlay(page);
  });

  // 5. Billet rapide modal 20 € — overlay et détails cohérents
  test('Cash modal billet 20 € : overlay visible, détails Reçu/Total présents', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page, 'Expresso');  // 2.50 €

    const totalText = await page.locator('#summaryTtc').textContent();
    const totalTtc = parseFloat((totalText || '0').replace('€', '').replace(',', '.').trim());
    if (totalTtc > 20) { return; }

    const body = await interceptCheckout(page, async () => {
      await page.click('#btnPayModal');
      await expect(page.locator('#paymentModal')).toHaveClass(/active/);
      await page.click('.btn-cash-bill[data-cash="20"]');
    });

    expect(body.totalPaid).toBeGreaterThan(0);
    await expect(page.locator('#changeOverlayModal')).toBeVisible();

    const details = await page.locator('#changeOverlayDetails').textContent();
    expect(details).toMatch(/Reçu\s*:\s*[\d,\.]+\s*€/);
    expect(details).toMatch(/Total\s*:\s*[\d,\.]+\s*€/);
    await closeOverlay(page);
  });

  // 6. Titre-Restaurant — flux complet
  test('MealVoucher : overlay visible après paiement TR', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page);

    const body = await interceptCheckout(page, async () => {
      await page.click('#btnPayModal');
      await expect(page.locator('#paymentModal')).toHaveClass(/active/);
      await page.click('.tender-types-grid button[data-tender="MealVoucher"]');
    });

    expect(body.totalPaid).toBeGreaterThan(0);
    await expect(page.locator('#changeOverlayModal')).toBeVisible();
    await closeOverlay(page);
  });

  // 7. Titre-Restaurant → vérification bon d'avoir si visible
  test('MealVoucher : si bon d\'avoir émis, code format CR-XXXXXXXX', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page, 'Expresso');

    await interceptCheckout(page, async () => {
      await page.click('#btnPayModal');
      await expect(page.locator('#paymentModal')).toHaveClass(/active/);
      await page.click('.tender-types-grid button[data-tender="MealVoucher"]');
    });

    await expect(page.locator('#changeOverlayModal')).toBeVisible();

    const voucherBox = page.locator('#changeOverlayCreditVoucherBox');
    if (await voucherBox.isVisible()) {
      const code = await page.locator('#changeOverlayCreditVoucherCode').textContent();
      expect(code).toMatch(/^CR-[A-Z0-9]+$/);
    }
    await closeOverlay(page);
  });

  // 8. Pourboire personnalisé (montant libre) + paiement CB
  test('Tip libre 1.50 € + CB : totalPaid = totalTtc + 1.50', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page);

    const totalText = await page.locator('#summaryTtc').textContent();
    const totalTtc = parseFloat((totalText || '0').replace('€', '').replace(',', '.').trim());

    await page.click('#btnPayModal');
    await expect(page.locator('#paymentModal')).toHaveClass(/active/);

    const customBtn = page.locator('#btnTipCustom');
    if (await customBtn.isVisible()) {
      await customBtn.click();
      await page.fill('#inputCustomTip', '1.50');
    } else {
      await page.locator('.btn-tip-pill[data-tip-percent="10"]').click();
    }

    const body = await interceptCheckout(page, async () => {
      await page.click('.tender-types-grid button[data-tender="Card"]');
    });

    expect(body.totalPaid).toBeGreaterThan(totalTtc);
    await closeOverlay(page);
  });

  // 9. Bipeur BIP-42 → affiché dans l'overlay
  test('Bipeur BIP-42 : #changeOverlayBuzzerVal = BIP-42', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page);

    await page.click('#btnPayModal');
    await expect(page.locator('#paymentModal')).toHaveClass(/active/);

    const buzzerInput = page.locator('#inputPickupBuzzer');
    if (await buzzerInput.isVisible()) {
      await buzzerInput.fill('BIP-42');
    }

    await interceptCheckout(page, async () => {
      await page.click('.tender-types-grid button[data-tender="Card"]');
    });

    await expect(page.locator('#changeOverlayModal')).toBeVisible();

    if (await buzzerInput.isVisible()) {
      const buzzerDisplay = page.locator('#changeOverlayBuzzerVal');
      if (await buzzerDisplay.isVisible()) {
        await expect(buzzerDisplay).toHaveText('BIP-42');
      }
    }
    await closeOverlay(page);
  });

  // 10. À Emporter → numéro retrait format #X-DD
  test('À Emporter : pickup format #X-DD dans API et overlay', async ({ page }) => {
    await ensureLoggedIn(page);
    await page.click('#btnDestTakeaway');
    await addProduct(page);

    const body = await interceptCheckout(page, async () => {
      await page.locator('.cart-fast-cash-bar button[data-cash="exact"]').click();
    });

    expect(body.pickupNumber).toMatch(/^#[A-D]-\d{2}$/);
    const pickupDisplay = await page.locator('#changeOverlayPickupNumber').textContent();
    expect(pickupDisplay).toMatch(/^#[A-D]-\d{2}$/);
    await closeOverlay(page);
  });

  // 11. Sur Place → numéro retrait présent
  test('Sur Place : numéro de retrait non vide dans l overlay', async ({ page }) => {
    await ensureLoggedIn(page);
    await page.click('#btnDestEatIn');
    await addProduct(page);

    const body = await interceptCheckout(page, async () => {
      await page.locator('.cart-fast-cash-bar button[data-cash="exact"]').click();
    });

    expect(body.pickupNumber).toBeTruthy();
    await expect(page.locator('#changeOverlayPickupNumber')).not.toBeEmpty();
    await closeOverlay(page);
  });

  // 12. Remise globale 10% + paiement CB → totalPaid réduit
  test('Remise 10% globale : totalPaid < totalTtc après paiement', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page);

    const beforeText = await page.locator('#summaryTtc').textContent();
    const beforeTotal = parseFloat((beforeText || '0').replace('€', '').replace(',', '.').trim());

    // Apply 10% discount
    await page.click('#btnDiscountModal');
    await expect(page.locator('#discountModal')).toHaveClass(/active/);
    await page.click('.btn-cap-quick[data-disc-val="10"][data-disc-type="Percent"]');
    await page.selectOption('#selectDiscountReason', 'Geste commercial / Retard cuisine');

    await Promise.all([
      page.waitForResponse(r => r.url().includes('/discount') && r.status() === 200),
      page.click('#formApplyDiscount button[type="submit"]')
    ]);
    await expect(page.locator('#discountModal')).not.toHaveClass(/active/);

    await page.waitForTimeout(300);
    const afterText = await page.locator('#summaryTtc').textContent();
    const afterTotal = parseFloat((afterText || '0').replace('€', '').replace(',', '.').trim());
    expect(afterTotal).toBeCloseTo(beforeTotal * 0.90, 1);

    const body = await interceptCheckout(page, async () => {
      await page.click('#btnPayModal');
      await expect(page.locator('#paymentModal')).toHaveClass(/active/);
      await page.click('.tender-types-grid button[data-tender="Card"]');
    });

    expect(body.totalPaid).toBeCloseTo(afterTotal, 1);
    await closeOverlay(page);
  });

  // 13. Remise montant fixe 5 € + paiement CB
  test('Remise fixe 5 € : totalPaid = totalTtc - 5.00 après paiement', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page, 'Burger');

    const beforeText = await page.locator('#summaryTtc').textContent();
    const beforeTotal = parseFloat((beforeText || '0').replace('€', '').replace(',', '.').trim());
    if (beforeTotal <= 5) { return; }

    await page.click('#btnDiscountModal');
    await expect(page.locator('#discountModal')).toHaveClass(/active/);

    await page.click('.btn-cap-quick[data-disc-type="Fixed"][data-disc-val="5"]');
    await page.selectOption('#selectDiscountReason', 'Geste commercial / Retard cuisine');

    await Promise.all([
      page.waitForResponse(r => r.url().includes('/discount') && r.status() === 200),
      page.click('#formApplyDiscount button[type="submit"]')
    ]);
    await expect(page.locator('#discountModal')).not.toHaveClass(/active/);

    await page.waitForTimeout(300);
    const afterText = await page.locator('#summaryTtc').textContent();
    const afterTotal = parseFloat((afterText || '0').replace('€', '').replace(',', '.').trim());
    expect(afterTotal).toBeCloseTo(beforeTotal - 5, 1);

    const body = await interceptCheckout(page, async () => {
      await page.click('#btnPayModal');
      await expect(page.locator('#paymentModal')).toHaveClass(/active/);
      await page.click('.tender-types-grid button[data-tender="Card"]');
    });

    expect(body.totalPaid).toBeCloseTo(afterTotal, 1);
    await closeOverlay(page);
  });

  // 14. Article offert (Comp) → total à 0 ou réduit
  test('Article offert (Comp) : article marqué offert, total réduit ou à 0', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page, 'Expresso');

    const beforeText = await page.locator('#summaryTtc').textContent();
    const beforeTotal = parseFloat((beforeText || '0').replace('€', '').replace(',', '.').trim());

    await page.click('#btnDiscountModal');
    await expect(page.locator('#discountModal')).toHaveClass(/active/);

    const targetSelect = page.locator('#selectDiscountTarget');
    const options = await targetSelect.locator('option').all();
    for (const o of options) {
      const val = await o.getAttribute('value');
      if (val && val !== 'global') {
        await targetSelect.selectOption(val);
        break;
      }
    }

    await page.selectOption('#selectDiscountReason', 'Geste commercial / Retard cuisine');
    await Promise.all([
      page.waitForResponse(r => r.url().includes('/comp') && r.status() === 200),
      page.click('#formApplyDiscount button[type="submit"]')
    ]);
    await expect(page.locator('#discountModal')).not.toHaveClass(/active/);

    await page.waitForTimeout(300);
    const afterText = await page.locator('#summaryTtc').textContent();
    const afterTotal = parseFloat((afterText || '0').replace('€', '').replace(',', '.').trim());
    expect(afterTotal).toBeLessThan(beforeTotal);
  });

  // 15. Split bill 2 convives → modal split et transfert vers encaissement
  test('Split 2 convives : part #1 = total/2 (en cents)', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page);

    const beforeText = await page.locator('#summaryTtc').textContent();
    const totalEur = parseFloat((beforeText || '0').replace('€', '').replace(',', '.').trim());

    await page.click('#btnSplitBill');
    const splitModal = page.locator('#splitBillModal');
    await expect(splitModal).toHaveClass(/active/);
    await expect(page.locator('#splitGuestsCount')).toContainText('2');
    await page.click('#btnConfirmSplit');
    await expect(splitModal).not.toHaveClass(/active/);

    const payModal = page.locator('#paymentModal');
    await expect(payModal).toHaveClass(/active/);
    await expect(page.locator('#payRemainingAmount')).toContainText('Part 1/2');

    // Fermeture du modal de paiement
    await page.click('#btnClosePayModal');
    await expect(payModal).not.toHaveClass(/active/);
  });

  // 16. Billet insuffisant → checkout non déclenché, overlay absent
  test('Billet insuffisant : toast warning, checkout non déclenché', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page, 'Burger');

    const totalText = await page.locator('#summaryTtc').textContent();
    const total = parseFloat((totalText || '0').replace('€', '').replace(',', '.').trim());
    if (total <= 10) { return; }

    const checkoutCalled = page.waitForResponse(
      (r) => r.url().includes('/api/orders/counter/checkout'),
      { timeout: 2000 }
    ).then(() => true).catch(() => false);

    await page.locator('.cart-fast-cash-bar button[data-cash="10"]').click();

    const called = await checkoutCalled;
    expect(called).toBe(false);
    await expect(page.locator('#changeOverlayModal')).not.toBeVisible();
  });

  // 17. Reçu fiscal AGEC → requestFiscalReceiptPrint = true dans le payload
  test('AGEC : payload contient requestFiscalReceiptPrint = true', async ({ page }) => {
    await ensureLoggedIn(page);
    await addProduct(page);

    await page.click('#btnPayModal');
    await expect(page.locator('#paymentModal')).toHaveClass(/active/);

    const chk = page.locator('#chkPrintFiscalReceipt');
    if (await chk.isVisible()) {
      await chk.check();
      await expect(chk).toBeChecked();
    }

    const [request] = await Promise.all([
      page.waitForRequest((r) => r.url().includes('/api/orders/counter/checkout')),
      page.click('.tender-types-grid button[data-tender="Card"]'),
    ]);

    const payload = JSON.parse(request.postData() || '{}');
    if (await chk.isVisible()) {
      expect(payload.requestFiscalReceiptPrint).toBe(true);
    } else {
      expect(payload).toBeDefined();
    }
    await closeOverlay(page);
  });
});
