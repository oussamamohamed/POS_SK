import { test, expect } from '@playwright/test';

test.describe('Réservations', () => {

  async function ensureLoggedIn(page: any) {
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');

    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of ['1', '2', '3', '4']) {
        await page.click(`.pin-keypad button[data-val="${d}"]`);
      }
      await expect(pinModal).not.toHaveClass(/active/);
    }
  }

  test('valide le formulaire, ajoute un client en liste d\'attente puis l\'annule', async ({ page }) => {
    await ensureLoggedIn(page);
    await page.click('#btnNavBookings');
    await expect(page.locator('#bookingsView')).toHaveClass(/active/);

    await page.click('#bkNew');
    await page.click('#bkSave');
    await expect(page.locator('#bkError')).toBeVisible();

    const name = 'E2E Attente ' + Date.now();
    await page.fill('#bkName', name);
    await page.fill('#bkFormPhone', 'abc');
    await page.click('#bkSave');
    await expect(page.locator('#bkError')).toBeVisible();

    await page.fill('#bkFormPhone', '');
    await page.check('#bkFormWaitlist');
    await expect(page.locator('#bkFormTable')).toBeDisabled();
    await page.click('#bkSave');
    await expect(page.locator('#bookingModal')).not.toHaveClass(/active/);

    const entry = page.locator('.bk-list .bk-item', { hasText: name });
    await expect(entry).toBeVisible();

    await entry.click();
    await page.click('#bkCancel');
    await expect(page.locator('.bk-list .bk-item', { hasText: name })).toHaveCount(0);
  });
});
