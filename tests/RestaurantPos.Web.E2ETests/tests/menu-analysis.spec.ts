import { test, expect } from '@playwright/test';

test.describe('Analyse du menu', () => {

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

  test('affiche les cartes de classe et le sélecteur de période', async ({ page }) => {
    await ensureLoggedIn(page);

    await page.click('#btnNavMenu');
    await expect(page.locator('#menuView')).toHaveClass(/active/);
    await expect(page.locator('.menu-range button[data-days="7"]')).toHaveClass(/active/);

    await page.click('.menu-range button[data-days="30"]');
    await expect(page.locator('.menu-range button[data-days="30"]')).toHaveClass(/active/);

    // Soit un état vide, soit les quatre cartes de classe : jamais un corps vide.
    const body = page.locator('#menuAnalysisBody');
    await expect(body.locator('.menu-card, .menu-empty')).not.toHaveCount(0);
  });

  test('le champ coût de revient existe dans le formulaire de création d\'article', async ({ page }) => {
    await ensureLoggedIn(page);
    await expect(page.locator('#inputProdCost')).toHaveCount(1);
  });
});
