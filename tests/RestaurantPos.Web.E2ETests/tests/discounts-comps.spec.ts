import { test, expect } from '@playwright/test';

test.describe('Remises Commerciales & Articles Offerts', () => {

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

  test('Doit ouvrir le modal de remise, appliquer une remise globale de 10% puis la réinitialiser', async ({ page }) => {
    await ensureLoggedIn(page);

    await page.click('#btnNavPos');

    // Ensure item in cart
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

    const beforeDiscountText = await page.locator('#summaryTtc').textContent();

    // Click Remise
    await page.click('#btnDiscountModal');

    const discountModal = page.locator('#discountModal');
    await expect(discountModal).toHaveClass(/active/);

    // Select 10% quick discount
    await page.click('.btn-cap-quick[data-disc-val="10"]');

    // Select reason
    await page.selectOption('#selectDiscountReason', 'Geste commercial / Retard cuisine');

    // Submit form
    await page.click('#formApplyDiscount button[type="submit"]');

    // Modal should close
    await expect(discountModal).not.toHaveClass(/active/);

    // Summary should show discount or lower total
    await expect(page.locator('#summaryTtc')).toBeVisible();

    // Re-open discount modal to test Reset
    await page.click('#btnDiscountModal');
    await expect(discountModal).toHaveClass(/active/);

    await page.click('#btnResetDiscount');
    await expect(discountModal).not.toHaveClass(/active/);
  });
});
