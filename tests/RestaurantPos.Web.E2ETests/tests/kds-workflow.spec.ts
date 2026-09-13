import { test, expect } from '@playwright/test';

test.describe('Écran Cuisine KDS & Stations de Préparation', () => {

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

  test('Doit naviguer vers le KDS et afficher les colonnes de production', async ({ page }) => {
    await ensureLoggedIn(page);

    // Navigate to KDS
    await page.click('#btnNavKds');
    await expect(page.locator('#kdsView')).toHaveClass(/active/);

    // Verify 3 columns exist
    await expect(page.locator('#colPending')).toBeVisible();
    await expect(page.locator('#colInPrep')).toBeVisible();
    await expect(page.locator('#colReady')).toBeVisible();

    // Verify column headers
    await expect(page.locator('#colPending')).toContainText('En Attente');
    await expect(page.locator('#colInPrep')).toContainText('En Préparation');
    await expect(page.locator('#colReady')).toContainText('Prêt à Servir');
  });
});
