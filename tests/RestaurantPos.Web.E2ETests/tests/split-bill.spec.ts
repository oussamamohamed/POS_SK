import { test, expect } from '@playwright/test';

test.describe('Partage de l Addition (Split Bill)', () => {

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

  test('Doit ouvrir le modal Split Bill, ajuster le nombre de convives et transférer la part vers l encaissement', async ({ page }) => {
    await ensureLoggedIn(page);

    await page.click('#btnNavPos');

    // Ensure cart has items
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

    await expect(page.locator('#summaryTtc')).not.toHaveText('0.00 €');

    // Click Split Bill button
    await page.click('#btnSplitBill');

    const splitModal = page.locator('#splitBillModal');
    await expect(splitModal).toHaveClass(/active/);

    // Initial count is 2
    await expect(page.locator('#splitGuestsCount')).toContainText('2');

    // Increase to 3
    await page.click('#btnSplitPlus');
    await expect(page.locator('#splitGuestsCount')).toContainText('3');

    // Decrease back to 2
    await page.click('#btnSplitMinus');
    await expect(page.locator('#splitGuestsCount')).toContainText('2');

    // Confirm split
    await page.click('#btnConfirmSplit');

    // Split modal closes, payment modal opens with fractional amount
    await expect(splitModal).not.toHaveClass(/active/);
    const payModal = page.locator('#paymentModal');
    await expect(payModal).toHaveClass(/active/);
    await expect(page.locator('#payRemainingAmount')).toContainText('Part 1/2');

    // Close payment modal
    await page.click('#btnClosePayModal');
    await expect(payModal).not.toHaveClass(/active/);
  });
});
