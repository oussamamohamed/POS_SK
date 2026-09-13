import { test, expect } from '@playwright/test';

test.describe('Happy Hour Pricing & Schedule Management', () => {

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

  test('US1 & US4: Forçage Superviseur Happy Hour active le bandeau, le compte à rebours et applique les tarifs préférentiels', async ({ page, request }) => {
    await ensureLoggedIn(page);

    // 1. Activer une dérogation Happy Hour via l'API superviseur (Alexandre Dupont PIN 1234 ou 9999)
    const overrideRes = await request.post('/api/happy-hour/override/activate', {
      data: {
        terminalId: 'POS_A',
        supervisorPin: '9999',
        durationMinutes: 45,
        reason: 'Test E2E Forçage Happy Hour'
      }
    });
    expect(overrideRes.ok()).toBeTruthy();

    // S'assurer qu'un planning a une règle pour la Bière
    const schedsRes = await request.get('/api/happy-hour/schedules');
    const scheds = await schedsRes.json();
    if (scheds && scheds.length > 0) {
      const prodsRes = await request.get('/api/catalog/products');
      const products = await prodsRes.json();
      const beer = products.find((p: any) => p.name.includes('Bière'));
      if (beer) {
        await request.post(`/api/happy-hour/schedules/${scheds[0].id}/rules/batch`, {
          data: {
            targetType: 0,
            targetIds: [beer.id],
            pricingMode: 0,
            fixedPrice: 5.00,
            discountPercent: null
          }
        });
      }
    }

    // 2. Recharger la caisse tactile en attendant le statut
    await Promise.all([
      page.waitForResponse((resp: any) => resp.url().includes('/api/happy-hour/status') && resp.status() === 200),
      page.goto('/?nocache=' + Date.now())
    ]);
    await ensureLoggedIn(page);

    // 3. Vérifier que le bandeau Happy Hour est visible avec son compte à rebours
    const banner = page.locator('#happyHourBanner');
    await expect(banner).toBeVisible();
    await expect(page.locator('#hhBannerTitle')).toContainText('DÉROGATION');
    await expect(page.locator('#hhCountdownPill')).toBeVisible();

    // 4. Vérifier que les tuiles produits affichent la signalétique Happy Hour
    const hhPriceCards = page.locator('.product-price-hh-container');
    await expect(hhPriceCards.first()).toBeVisible();

    // 5. Basculer en mode Sur Place (le Happy Hour s'applique sur place par défaut)
    await page.click('#btnDestEatIn');

    // 6. Cliquer sur un produit en Happy Hour pour l'ajouter au panier
    const hhCard = page.locator('.product-card').filter({ has: page.locator('.product-price-hh') }).first();
    await hhCard.click();

    // 7. Vérifier que la ligne de panier porte le badge [HH]
    const hhCartBadge = page.locator('.cart-item-badge-hh');
    await expect(hhCartBadge).toBeVisible();
    await expect(hhCartBadge).toContainText('[HH]');

    // 7. Arrêter la dérogation
    const stopRes = await request.post('/api/happy-hour/override/stop', {
      data: {
        terminalId: 'POS_A',
        supervisorPin: '9999',
        reason: 'Fin test E2E'
      }
    });
    expect(stopRes.ok()).toBeTruthy();

    // 8. Recharger et vérifier que le bandeau se masque
    await Promise.all([
      page.waitForResponse((resp: any) => resp.url().includes('/api/happy-hour/status') && resp.status() === 200),
      page.goto('/?nocache=' + Date.now())
    ]);
    await ensureLoggedIn(page);
    await expect(banner).not.toBeVisible();
  });

  test('US4: Modal tactile de dérogation Superviseur avec PIN erroné refuse l accès', async ({ page }) => {
    await ensureLoggedIn(page);

    // Ouvrir le modal dérogation
    const btnOverride = page.locator('#btnHhOverrideQuickAction');
    // Forcer l'affichage du bouton si le bandeau n'est pas actif
    await page.evaluate(() => {
      const banner = document.getElementById('happyHourBanner');
      if (banner) banner.style.display = 'block';
    });

    await btnOverride.click();
    const modal = page.locator('#hhOverrideModal');
    await expect(modal).toHaveClass(/active/);

    // Saisir un PIN erroné
    await page.fill('#inputHhPin', '0000');
    await page.click('#btnHhExtend30');

    // Vérifier notification d'erreur et modal reste ouvert
    const toast = page.locator('.toast.error');
    await expect(toast).toBeVisible();

    // Fermer le modal
    await page.click('#btnCloseHhOverrideModal');
    await expect(modal).not.toHaveClass(/active/);
  });

});
