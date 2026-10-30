import { test, expect } from '@playwright/test';

test.describe('Plan de salle dessiné', () => {

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

  test('affiche le plan, les tuiles et le volet de détail, puis la grille de cartes', async ({ page }) => {
    await page.addInitScript(() => localStorage.removeItem('pos_floor_mode'));
    await ensureLoggedIn(page);
    await page.click('#btnNavFloor');

    // 4 tuiles, plus l'encaissé du jour pour un responsable.
    await expect(page.locator('#floorStats .floor-stat')).toHaveCount(5);
    const first = page.locator('#floorPlanIn .floor-tbl').first();
    await expect(first).toBeVisible();

    await first.click();
    await expect(page.locator('#floorPane [data-act="open"]')).toBeVisible();
    await page.click('#floorPane [data-act="close"]');
    await expect(page.locator('#floorPane [data-act="open"]')).toHaveCount(0);

    await page.click('[data-floor-mode="cards"]');
    await expect(page.locator('#floorTablesGrid .table-card').first()).toBeVisible();
    await expect(page.locator('#floorSplit')).toBeHidden();
  });

  test('un clic sur « Ouvrir la table » ramène à la caisse', async ({ page }) => {
    await page.addInitScript(() => localStorage.removeItem('pos_floor_mode'));
    await ensureLoggedIn(page);
    await page.click('#btnNavFloor');
    await page.locator('#floorPlanIn .floor-tbl').first().click();
    await page.click('#floorPane [data-act="open"]');
    await expect(page.locator('#posView')).toHaveClass(/active/);
  });
});
