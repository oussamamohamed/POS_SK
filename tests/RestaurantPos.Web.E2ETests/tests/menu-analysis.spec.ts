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

  test('un plat sans coût se renseigne directement dans le tableau', async ({ page }) => {
    const id = '11111111-1111-1111-1111-111111111111';
    let sent: any = null;
    const item = (cost: number | null) => ({
      productId: id, name: 'Plat test sans coût', categoryName: 'Test', quantity: 5, share: 1, revenueHt: 50,
      unitCost: cost, unitMarginHt: cost == null ? null : 10 - cost, marginHt: null, popular: true, profitable: cost == null ? null : true,
      class: cost == null ? null : 0, missingCost: cost == null
    });
    await page.route('**/api/menu-analysis?**', route => route.fulfill({
      json: { fromUtc: new Date().toISOString(), toUtc: new Date().toISOString(), popularityThreshold: 0.7, averageUnitMarginHt: sent ? 6 : null, missingCostCount: sent ? 0 : 1, items: [item(sent ? sent.cost : null)] }
    }));
    await page.route('**/api/catalog/products', route => route.fulfill({
      json: [{ id, name: 'Plat test sans coût', categoryId: 'CAT_X', price: 11, taxRatePercent: 10, description: '', colorHex: '#3B82F6', displayOrder: 1, isQuickKey: false, preparationStationId: 'COLD', cost: null }]
    }));
    await page.route(`**/api/catalog/products/${id}`, route => {
      sent = route.request().postDataJSON();
      return route.fulfill({ json: {} });
    });

    await ensureLoggedIn(page);
    await page.click('#btnNavMenu');
    const form = page.locator('.menu-table .menu-cost-form');
    await expect(form).toHaveCount(1);
    await form.locator('input').fill('4,25'.replace(',', '.'));
    await form.locator('button').click();

    await expect.poll(() => sent?.cost).toBe(4.25);
    expect(sent.name).toBe('Plat test sans coût');
    expect(sent.price).toBe(11);
    expect(sent.clearCost).toBeUndefined();
    // Le plat est reclassé : plus de formulaire, plus de bandeau, et le clic dans le champ n'avait pas ouvert la fiche.
    await expect(page.locator('.menu-cost-form')).toHaveCount(0);
    await expect(page.locator('.menu-warn')).toHaveCount(0);
  });

  test('le champ coût de revient existe dans le formulaire de création d\'article', async ({ page }) => {
    await ensureLoggedIn(page);
    await expect(page.locator('#inputProdCost')).toHaveCount(1);
  });
});
