import { test, expect, type Page, type APIRequestContext } from '@playwright/test';
import { pairTill, managerToken } from './helpers/pairing';

const PRODUCT = 'Bière Artisanale';
const GLUTEN_AND_MILK = 1 | 64; // bits 0 et 6 (Allergens.cs)

test.describe('Allergènes et ruptures', () => {

  async function ensureLoggedIn(page: Page) {
    await pairTill(page);
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of '1234') await page.click(`.pin-keypad button[data-val="${d}"]`);
      await expect(pinModal).not.toHaveClass(/active/);
    }
    await page.click('#btnNavPos');
  }

  async function productId(request: APIRequestContext): Promise<string> {
    const list = await (await request.get('/api/catalog/products')).json();
    return list.find((p: any) => p.name.startsWith(PRODUCT)).id;
  }

  async function setAllergens(request: APIRequestContext, declared: boolean, mask: number) {
    const token = await managerToken(request);
    const res = await request.put(`/api/catalog/products/${await productId(request)}/allergens`, {
      data: { declared, mask, overridden: true },
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
  }

  async function setAvailability(request: APIRequestContext, isAvailable: boolean) {
    const token = await managerToken(request);
    const res = await request.put(`/api/catalog/products/${await productId(request)}/availability`, {
      data: { isAvailable },
      headers: { Authorization: `Bearer ${token}` },
    });
    expect(res.ok()).toBeTruthy();
  }

  const card = (page: Page) => page.locator('.product-card').filter({ hasText: PRODUCT }).first();

  test('affiche les allergènes du plat et le filtre masque ce qui en contient', async ({ page }) => {
    try {
      await setAllergens(page.request, true, GLUTEN_AND_MILK);
      await ensureLoggedIn(page);

      await expect(card(page).locator('.product-allergens')).toContainText('Gluten');
      await expect(card(page).locator('.product-allergens')).toContainText('Lait');

      await page.locator('.catalog-tool').first().click();
      await page.locator('#allergenFilterModal .allergen-chip', { hasText: 'Gluten' }).click();
      await page.locator('#allergenFilterModal [data-act="done"]').click();
      await expect(page.locator('.product-card').filter({ hasText: PRODUCT })).toHaveCount(0);

      await page.locator('.catalog-tool').first().click();
      await page.locator('#allergenFilterModal [data-act="clear"]').click();
      await expect(card(page)).toBeVisible();
    } finally {
      await setAllergens(page.request, false, 0);
    }
  });

  test('un plat dont les allergènes ne sont pas renseignés est signalé, et masqué par le filtre', async ({ page }) => {
    await setAllergens(page.request, false, 0);
    await ensureLoggedIn(page);

    await expect(card(page).locator('.product-allergens.unknown')).toBeVisible();

    await page.locator('.catalog-tool').first().click();
    await page.locator('#allergenFilterModal .allergen-chip', { hasText: 'Lait' }).click();
    await page.locator('#allergenFilterModal [data-act="done"]').click();
    await expect(page.locator('.product-card').filter({ hasText: PRODUCT })).toHaveCount(0);

    await page.locator('.catalog-tool').first().click();
    await page.locator('#allergenFilterModal [data-act="clear"]').click();
  });

  test('une rupture saisie ailleurs grise le plat en moins de 2 secondes et son ajout est refusé', async ({ page }) => {
    try {
      await ensureLoggedIn(page);
      await expect(card(page)).not.toHaveClass(/product-card-unavailable/);

      await setAvailability(page.request, false);
      await expect(card(page)).toHaveClass(/product-card-unavailable/, { timeout: 2000 });
      await expect(card(page).locator('.stockout-badge')).toBeVisible();

      await card(page).click();
      await expect(page.locator('#cartItems .cart-item-row').filter({ hasText: PRODUCT })).toHaveCount(0);

      await setAvailability(page.request, true);
      await expect(card(page)).not.toHaveClass(/product-card-unavailable/, { timeout: 2000 });
    } finally {
      await setAvailability(page.request, true);
    }
  });

  test('le mode Ruptures bascule le plat touché', async ({ page }) => {
    try {
      await ensureLoggedIn(page);

      await page.locator('.catalog-tool').nth(1).click();
      await card(page).click();
      await expect(card(page)).toHaveClass(/product-card-unavailable/);

      await card(page).click();
      await expect(card(page)).not.toHaveClass(/product-card-unavailable/);
    } finally {
      await setAvailability(page.request, true);
    }
  });
});
