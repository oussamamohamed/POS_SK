import { test, expect } from '@playwright/test';

test.describe('Encaissements', () => {

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

  test('affiche les tuiles, le graphique horaire, la bascule en tableau et le lien vers la fiscalité', async ({ page }) => {
    await ensureLoggedIn(page);
    await page.click('#btnNavTakings');
    await expect(page.locator('#takingsView')).toHaveClass(/active/);
    await expect(page.locator('#takingsBody .floor-stat')).toHaveCount(4);
    await expect(page.locator('#takingsBody svg[data-hours]')).toBeVisible();

    await page.click('[data-takings-table]');
    await expect(page.locator('#takingsBody table.menu-table').first()).toBeVisible();
    await page.click('[data-takings-table]');
    await expect(page.locator('#takingsBody svg[data-hours]')).toBeVisible();

    await page.click('[data-go-z]');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);
  });
});
