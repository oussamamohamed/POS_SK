import { test, expect } from '@playwright/test';

test.describe('Cuisine : tuiles et filtres de poste', () => {

  async function ensureLoggedIn(page: any) {
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of ['1', '2', '3', '4']) await page.click(`.pin-keypad button[data-val="${d}"]`);
      await expect(pinModal).not.toHaveClass(/active/);
    }
  }

  test('affiche les quatre tuiles, les compteurs par poste et le filtre actif', async ({ page }) => {
    await ensureLoggedIn(page);
    await page.click('#btnNavKds');
    await expect(page.locator('#kdsStats .floor-stat')).toHaveCount(4);
    await expect(page.locator('.kds-filter-btn[data-station="ALL"] small')).toBeVisible();

    await page.click('.kds-filter-btn[data-station="BAR"]');
    await expect(page.locator('.kds-filter-btn[data-station="BAR"]')).toHaveClass(/active/);
    await expect(page.locator('#kdsAllDay')).toBeVisible();
    await expect(page.locator('#kdsBumped')).toBeVisible();
  });
});
