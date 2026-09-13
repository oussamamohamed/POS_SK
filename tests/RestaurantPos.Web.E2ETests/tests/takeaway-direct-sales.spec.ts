import { test, expect } from '@playwright/test';

test.describe('Vente Directe & Encaissement À Emporter (Feature 018)', () => {

  async function ensureLoggedIn(page: any) {
    page.on('console', (msg: any) => console.log(`[BROWSER ${msg.type()}]: ${msg.text()}`));
    page.on('pageerror', (err: any) => console.log(`[BROWSER ERROR STACK]: ${err.stack || err.message}`));

    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');

    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      await page.click('.pin-keypad button[data-val="1"]');
      await page.click('.pin-keypad button[data-val="2"]');
      await page.click('.pin-keypad button[data-val="3"]');
      await page.click('.pin-keypad button[data-val="4"]');
      await expect(pinModal).not.toHaveClass(/active/);
    }
    await page.waitForSelector('#activeTableBadge');
    await page.waitForTimeout(500);
  }

  test('Doit ouvrir par défaut sur le comptoir en mode À Emporter', async ({ page }) => {
    await ensureLoggedIn(page);

    // Active table badge should be Comptoir
    await expect(page.locator('#activeTableBadge')).toHaveText('Comptoir');

    // Takeaway toggle should be active by default
    await expect(page.locator('#btnDestTakeaway')).toHaveClass(/active/);
    await expect(page.locator('#btnDestEatIn')).not.toHaveClass(/active/);
  });

  test('Doit basculer dynamiquement entre Sur Place et À Emporter', async ({ page }) => {
    await ensureLoggedIn(page);

    // Wait for buttons to be ready
    await page.waitForSelector('#btnDestEatIn');
    await page.click('#btnDestEatIn');
    await expect(page.locator('#btnDestEatIn')).toHaveClass(/active/);
    await expect(page.locator('#btnDestTakeaway')).not.toHaveClass(/active/);

    // Switch back to À Emporter
    await page.click('#btnDestTakeaway');
    await expect(page.locator('#btnDestTakeaway')).toHaveClass(/active/);
    await expect(page.locator('#btnDestEatIn')).not.toHaveClass(/active/);
  });

  test('Doit exécuter une vente express comptoir avec rendu de monnaie et numéro de retrait', async ({ page }) => {
    await ensureLoggedIn(page);

    // Ensure item added to cart
    await page.waitForSelector('.product-card');
    const card = page.locator('.product-card').first();
    await card.click();

    // Handle modifier modal if it appears
    const modModal = page.locator('#modifiersModal');
    if (await modModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnConfirmModifiers');
    }

    await expect(page.locator('#summaryTtc')).not.toHaveText('0.00 €');

    // Tap quick-cash 50 € button
    const btnCash50 = page.locator('.cart-fast-cash-bar button[data-cash="50"]');
    await btnCash50.click();

    // Change Overlay should appear
    const overlay = page.locator('#changeOverlayModal');
    await expect(overlay).toBeVisible();

    // Verify pickup number format (#A-XX)
    const pickupNumber = await page.locator('#changeOverlayPickupNumber').textContent();
    expect(pickupNumber).toMatch(/^#[A-D]-\d{2}$/);

    // Close change overlay
    await page.click('#btnChangeDone');
    await expect(overlay).not.toBeVisible();

    // Fresh new cart
    await expect(page.locator('#summaryTtc')).toHaveText('0.00 €');
  });

  test('Doit mettre en attente (Hold) et rappeler (Recall) un panier actif', async ({ page }) => {
    await ensureLoggedIn(page);

    // Add an item
    await page.waitForSelector('.product-card');
    const card = page.locator('.product-card').first();
    await card.click();
    const modModal = page.locator('#modifiersModal');
    if (await modModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnConfirmModifiers');
    }

    // Ensure cart has item
    await expect(page.locator('#summaryTtc')).not.toHaveText('0.00 €');

    // Put on hold
    page.once('dialog', async dialog => {
      await dialog.accept('Test Client Hold');
    });
    await page.click('#btnHoldCart');

    // Cart resets to empty
    await expect(page.locator('#summaryTtc')).toHaveText('0.00 €');

    // Held badge should show at least 1
    await expect(page.locator('#heldBadgeCount')).not.toHaveText('0');

    // Open held modal
    await page.click('#btnHeldQueue');
    const heldModal = page.locator('#heldOrdersModal');
    await expect(heldModal).toBeVisible();

    // Recall first held order
    const recallBtn = page.locator('.btn-recall-held').first();
    await recallBtn.click();

    // Modal closes and cart is restored
    await expect(heldModal).not.toBeVisible();
    await expect(page.locator('#summaryTtc')).not.toHaveText('0.00 €');
  });
});
