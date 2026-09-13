import { test, expect } from '@playwright/test';

test.describe('Encaissement & Règlement Fiscal Tactile', () => {

  async function ensureLoggedIn(page: any) {
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
  }

  test('Doit ouvrir le modal d encaissement et valider un règlement par Carte Bancaire', async ({ page }) => {
    await ensureLoggedIn(page);

    await page.click('#btnNavPos');

    // If cart is empty, add an item without modifiers
    const totalText = await page.locator('#summaryTtc').textContent();
    const currentTotal = parseFloat((totalText || '0').replace('€', '').trim());

    if (currentTotal <= 0) {
      const beerCard = page.locator('.product-card').filter({ hasText: 'Bière Artisanale' }).first();
      if (await beerCard.isVisible()) {
        await beerCard.click();
      } else {
        await page.locator('.product-card').first().click();
      }
    }

    // Verify cart has positive total
    await expect(page.locator('#summaryTtc')).not.toHaveText('0.00 €');

    // Click Encaisser
    await page.click('#btnPayModal');

    // Payment modal opens
    const payModal = page.locator('#paymentModal');
    await expect(payModal).toHaveClass(/active/);

    // Click Carte Bancaire
    await page.click('.tender-types-grid button[data-tender="Card"]');

    // Modal should close
    await expect(payModal).not.toHaveClass(/active/);

    // Cart should be reset to 0.00 €
    await expect(page.locator('#summaryTtc')).toHaveText('0.00 €');
  });
});
