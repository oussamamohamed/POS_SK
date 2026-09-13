import { test, expect } from '@playwright/test';

test.describe('Transfert & Fusion de Tables Tactile', () => {

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

  test('Doit ouvrir le modal de transfert, afficher les tables cibles et permettre le transfert', async ({ page }) => {
    await ensureLoggedIn(page);

    await page.click('#btnNavPos');

    // Click Transfer button
    await page.click('#btnTransferModal');

    const transferModal = page.locator('#transferTableModal');
    await expect(transferModal).toHaveClass(/active/);

    // Verify source table is populated
    await expect(page.locator('#transferSourceTable')).not.toHaveValue('');

    // Verify target tables select is visible and has options
    await expect(page.locator('#selectTargetTable')).toBeVisible();
    const options = page.locator('#selectTargetTable option');
    await expect(options.first()).toBeAttached();
    const count = await options.count();
    expect(count).toBeGreaterThan(0);

    // Cancel modal
    await page.click('#btnCancelTransfer');
    await expect(transferModal).not.toHaveClass(/active/);
  });
});
