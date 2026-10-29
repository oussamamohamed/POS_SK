import { test, expect, type Page } from '@playwright/test';
import { loginWithPin, managerToken } from './helpers/pairing';

// C2 : une donnée serveur/opérateur ne doit jamais être interprétée comme du HTML.
const P = '<img src=x onerror="window.__xss=1">';

test.describe('XSS stockée : données affichées via innerHTML', () => {
  let auth: Record<string, string>;
  let categoryId: string;
  let productId: string;
  let staffId: string;

  test.beforeAll(async ({ request }) => {
    auth = { Authorization: `Bearer ${await managerToken(request)}` };
    const cat = await request.post('/api/catalog/categories', {
      headers: auth,
      data: { name: P, colorHex: '#ff0000', displayOrder: 9999, iconName: null },
    });
    expect(cat.ok()).toBeTruthy();
    categoryId = (await cat.json()).id;
    const prod = await request.post('/api/catalog/products', {
      headers: auth,
      data: { name: P, categoryId, price: 5, taxRatePercent: 10, description: null, colorHex: null, displayOrder: 1, isQuickKey: true, stationId: null },
    });
    expect(prod.ok()).toBeTruthy();
    productId = (await prod.json()).id;
    const staff = await request.post('/api/staff', {
      headers: auth,
      data: { name: P, role: 'Waiter', pin: String(1000 + Math.floor(Math.random() * 8000)) },
    });
    expect(staff.ok()).toBeTruthy();
    staffId = (await staff.json()).id;
  });

  test.afterAll(async ({ request }) => {
    await request.delete(`/api/catalog/products/${productId}`, { headers: auth });
    await request.put(`/api/catalog/categories/${categoryId}`, {
      headers: auth,
      data: { name: 'xss-nettoyage', colorHex: '#ff0000', displayOrder: 9999, iconName: null, isActive: false },
    });
    await request.delete(`/api/staff/operators/${staffId}`, { headers: auth });
  });

  // Le texte malveillant doit être affiché tel quel (sinon le test passerait parce que la donnée n'apparaît jamais).
  async function assertSafeAndShown(page: Page, selector: string) {
    await page.waitForTimeout(300); // onerror est asynchrone
    expect(await page.evaluate(() => (window as any).__xss), `window.__xss après ${selector}`).toBeUndefined();
    await expect
      .poll(() => page.evaluate((s) => document.querySelector(s)?.textContent ?? '', selector), { message: `texte littéral dans ${selector}` })
      .toContain(P);
  }

  test('aucune donnée ne s exécute dans les vues', async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('pos_lang', 'fr'));

    // Données non nettoyables par l'API (pas de suppression) : réponses simulées.
    await page.route('**/api/catalog/products', async (route) => {
      const res = await route.fetch();
      const products = await res.json();
      for (const p of products) {
        if (p.id === productId) {
          p.modifierGroups = [{
            id: 'g-xss', groupName: P, minSelections: 0, maxSelections: 2, isMandatory: false, isSingleChoice: false,
            options: [{ id: 'o-xss', name: P, extraPrice: 1.5, isDefault: false }],
          }];
        }
      }
      await route.fulfill({ response: res, json: products });
    });
    await page.route(/\/api\/tables$/, (route) => route.fulfill({
      json: [{ tableNumber: P, capacity: 2, status: 1, assignedWaiterName: P, coversCount: 2, activeOrderTotalTtc: 12 }],
    }));
    await page.route(/\/api\/kds\/tickets$/, (route) => route.fulfill({
      json: [{ id: '11111111-1111-1111-1111-111111111111', tableNumber: P, stationId: P, status: 0, items: [{ quantity: 2, productName: P }] }],
    }));
    await page.route(/\/api\/orders\/counter\/held\?/, (route) => route.fulfill({
      json: [{ id: '22222222-2222-2222-2222-222222222222', customerLabel: P, itemCount: 1, totalTtc: 5, heldAtUtc: new Date().toISOString() }],
    }));
    await page.route(/\/api\/printers$/, (route) => route.fulfill({
      json: [{ id: '33333333-3333-3333-3333-333333333333', name: P, ipAddress: P, port: 9100, paperWidthMm: 80, openCashDrawerOnReceipt: false, assignedStationIds: [P], isActive: true }],
    }));
    await page.route(/\/api\/devices$/, (route) => route.fulfill({
      json: [{ id: '44444444-4444-4444-4444-444444444444', name: P, terminalId: P, role: P, isRevoked: false }],
    }));
    await page.route(/\/api\/happy-hour\/schedules$/, (route) => route.fulfill({
      json: [{
        id: '55555555-5555-5555-5555-555555555555', name: P, startTime: P, endTime: P, priority: 1, daysOfWeek: [1], appliesToTakeaway: true,
        priceRules: [
          { id: '66666666-6666-6666-6666-666666666666', targetType: 'Category', targetName: P, discountPercent: 10 },
          { id: '77777777-7777-7777-7777-777777777777', targetType: 'Product', targetName: P, fixedPrice: 3, discountPercent: 0 },
        ],
      }],
    }));

    await loginWithPin(page);

    // Comptoir : onglet de catégorie, touche rapide, grille.
    await page.locator('.btn-cat-tab').last().click({ position: { x: 10, y: 10 } });
    await assertSafeAndShown(page, '#categoryTabsBar');
    await assertSafeAndShown(page, '#quickKeysBar');
    await assertSafeAndShown(page, '#productsGrid');

    // Fenêtre des options (groupe, option) puis panier (produit, option, commentaire cuisine).
    await page.locator('#productsGrid .product-card').first().click();
    await expect(page.locator('#modifiersModal')).toHaveClass(/active/);
    await assertSafeAndShown(page, '#modifiersGroupsContainer');
    await page.locator('#modifiersGroupsContainer .btn-modifier-option').first().click();
    await page.fill('#inputModifiersComment', P);
    await page.click('#btnConfirmModifiers');
    await assertSafeAndShown(page, '#cartItemsList');
    await expect.poll(() => page.evaluate(() => document.querySelector('#cartItemsList')?.textContent?.match(/💬/) !== null)).toBe(true);

    // File d'attente.
    await page.click('#btnHeldQueue');
    await assertSafeAndShown(page, '#heldOrdersList');
    await page.click('#btnCloseHeldModal');

    // Salle, KDS.
    await page.click('#btnNavFloor');
    await assertSafeAndShown(page, '#floorTablesGrid');
    await page.click('#btnNavKds');
    await assertSafeAndShown(page, '#kdsPendingCards');

    // Gestion.
    await page.click('#btnNavAdmin');
    await page.click('[data-admin-tab="tabCatalog"]');
    await assertSafeAndShown(page, '#adminCatalogList');
    await page.click('[data-admin-tab="tabStaff"]');
    await assertSafeAndShown(page, '#adminStaffList');
    await page.click('[data-admin-tab="tabPrinters"]');
    await assertSafeAndShown(page, '#adminPrintersList');
    await page.click('[data-admin-tab="tabDevices"]');
    await assertSafeAndShown(page, '#adminDevicesList');
    await page.click('[data-admin-tab="tabHappyHour"]');
    await assertSafeAndShown(page, '#selectHhActiveSchedule');
    await assertSafeAndShown(page, '#adminHappyHourList');
    await assertSafeAndShown(page, '#hhArticlesGrid');
    await assertSafeAndShown(page, '#hhFamiliesGrid');
    await assertSafeAndShown(page, '#hhArticleCategoryFilters');
    await page.click('#btnSubtabHhActiveRules');
    await assertSafeAndShown(page, '#hhActiveCategoryRulesList');
    await assertSafeAndShown(page, '#hhActiveProductRulesList');
  });
});
