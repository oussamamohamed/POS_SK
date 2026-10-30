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

  test('filtre par famille, tri des colonnes et fiche du plat', async ({ page }) => {
    await ensureLoggedIn(page);
    await page.click('#btnNavMenu');
    await page.click('.menu-range button[data-days="30"]');
    const chips = page.locator('#menuCats button');
    await expect(chips).not.toHaveCount(1);
    await expect(chips.first()).toHaveAttribute('aria-pressed', 'true');

    await chips.nth(1).click();
    await expect(chips.nth(1)).toHaveAttribute('aria-pressed', 'true');
    await expect(page.locator('#menuAnalysisBody .menu-card, #menuAnalysisBody .menu-empty').first()).toBeVisible();

    await chips.first().click();
    const header = page.locator('.menu-table th[data-sort="name"]');
    if (await header.count()) {
      await header.click();
      await expect(header).toHaveAttribute('aria-sort', 'ascending');
      await page.locator('.menu-table tbody tr').first().click();
      await expect(page.locator('.menu-pick')).toBeVisible();
    }
  });

  test('le champ coût de revient existe dans le formulaire de création d\'article', async ({ page }) => {
    await ensureLoggedIn(page);
    await expect(page.locator('#inputProdCost')).toHaveCount(1);
  });
});
