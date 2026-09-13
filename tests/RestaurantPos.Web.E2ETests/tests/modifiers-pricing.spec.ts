import { test, expect } from '@playwright/test';

test.describe('Modificateurs & Suppléments Payants sur Produits', () => {

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

  test('Doit ouvrir le modal d options sur le Burger Gourmet Rossini et calculer les suppléments', async ({ page }) => {
    await ensureLoggedIn(page);

    await page.click('#btnNavPos');

    // Find Burger card
    const burgerCard = page.locator('.product-card').filter({ hasText: 'Burger Gourmet Rossini' }).first();
    await expect(burgerCard).toBeVisible();

    // Click Burger card to open modal
    await burgerCard.click();

    const modal = page.locator('#modifiersModal');
    await expect(modal).toHaveClass(/active/);

    // Select Cooking: Saignant button
    const saignantBtn = modal.locator('.btn-modifier-option').filter({ hasText: 'Saignant' }).first();
    await saignantBtn.click();

    // Check Extras: Double Cheddar (+2.00 €) and Bacon (+1.50 €) buttons
    const cheddarBtn = modal.locator('.btn-modifier-option').filter({ hasText: 'Double Cheddar' }).first();
    await cheddarBtn.click();

    const baconBtn = modal.locator('.btn-modifier-option').filter({ hasText: 'Bacon Fumé' }).first();
    await baconBtn.click();

    // Verify dynamic totals in modal
    await expect(page.locator('#lblModifiersExtraTotal')).toContainText('+3.50');
    await expect(page.locator('#lblModifiersEffectivePrice')).toContainText('23.00');

    // Type kitchen note
    await page.fill('#inputModifiersComment', 'Bien croustillant');

    // Confirm
    await page.click('#btnConfirmModifiers');
    await expect(modal).not.toHaveClass(/active/);

    // Verify item in cart
    const cartList = page.locator('#cartItemsList');
    await expect(cartList).toContainText('Burger Gourmet Rossini');
    await expect(cartList).toContainText('23.00');
    await expect(cartList).toContainText('Saignant');
    await expect(cartList).toContainText('Double Cheddar');
    await expect(cartList).toContainText('Bacon');
  });
});
