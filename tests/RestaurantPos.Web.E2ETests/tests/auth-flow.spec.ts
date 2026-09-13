import { test, expect } from '@playwright/test';

test.describe('Authentification Tactile & Verrouillage Caisse (PIN)', () => {

  test.beforeEach(async ({ page }) => {
    // Navigate with nocache parameter
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
  });

  test('Doit afficher l écran de verrouillage PIN et rejeter un code invalide', async ({ page }) => {
    const pinModal = page.locator('#pinLockModal');

    // If modal is not active initially, lock it
    if (!(await pinModal.evaluate(el => el.classList.contains('active')))) {
      await page.click('#btnLockTerminal');
      await expect(pinModal).toHaveClass(/active/);
    }

    // Enter wrong PIN: 0000
    for (let i = 0; i < 4; i++) {
      await page.click('.pin-keypad button[data-val="0"]');
    }

    // Verify error toast or that modal stays active
    await expect(pinModal).toHaveClass(/active/);
  });

  test('Doit déverrouiller la caisse avec le code PIN valide 1234', async ({ page }) => {
    const pinModal = page.locator('#pinLockModal');

    if (await pinModal.evaluate(el => el.classList.contains('active'))) {
      // Clear any partial input
      await page.click('#btnPinClear');

      // Type PIN 1234
      await page.click('.pin-keypad button[data-val="1"]');
      await page.click('.pin-keypad button[data-val="2"]');
      await page.click('.pin-keypad button[data-val="3"]');
      await page.click('.pin-keypad button[data-val="4"]');
    }

    // Modal should close and operator badge should be visible
    await expect(pinModal).not.toHaveClass(/active/);
    await expect(page.locator('#currentOperatorName')).toContainText('Alexandre');
  });

  test('Doit pouvoir verrouiller et déverrouiller à nouveau', async ({ page }) => {
    const pinModal = page.locator('#pinLockModal');

    // Unlock if locked
    if (await pinModal.evaluate(el => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      await page.click('.pin-keypad button[data-val="1"]');
      await page.click('.pin-keypad button[data-val="2"]');
      await page.click('.pin-keypad button[data-val="3"]');
      await page.click('.pin-keypad button[data-val="4"]');
      await expect(pinModal).not.toHaveClass(/active/);
    }

    // Click lock button
    await page.click('#btnLockTerminal');
    await expect(pinModal).toHaveClass(/active/);

    // Re-unlock
    await page.click('.pin-keypad button[data-val="1"]');
    await page.click('.pin-keypad button[data-val="2"]');
    await page.click('.pin-keypad button[data-val="3"]');
    await page.click('.pin-keypad button[data-val="4"]');
    await expect(pinModal).not.toHaveClass(/active/);
  });
});
