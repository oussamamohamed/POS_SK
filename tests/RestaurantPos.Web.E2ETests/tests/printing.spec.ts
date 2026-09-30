import { test, expect, Page } from '@playwright/test';
import { pairTill } from './helpers/pairing';

/** Impression réelle : réglages, postes des familles, état des imprimantes, ticket à table. */
test.describe('Impression', () => {

  async function login(page: Page, pin = '1234') {
    await pairTill(page);
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of pin) await page.click(`.pin-keypad button[data-val="${d}"]`);
      await expect(pinModal).not.toHaveClass(/active/);
    }
    await page.waitForSelector('.product-card');
  }

  async function api(page: Page, method: string, path: string, body?: unknown) {
    return page.evaluate(async ({ method, path, body }) => {
      const token = localStorage.getItem('pos_jwt_token');
      const device = JSON.parse(localStorage.getItem('pos_device') || 'null');
      const res = await fetch(path, {
        method,
        headers: {
          'Content-Type': 'application/json',
          ...(token ? { Authorization: `Bearer ${token}` } : {}),
          ...(device ? { 'X-Device-Token': device.token } : {}),
        },
        body: body ? JSON.stringify(body) : undefined,
      });
      let json: any = null;
      try { json = await res.json(); } catch { /* corps vide */ }
      return { status: res.status, json };
    }, { method, path, body });
  }

  async function openAdminTab(page: Page, tab: string) {
    await page.click('#btnNavAdmin');
    await page.click(`.admin-tab-btn[data-admin-tab="${tab}"]`);
  }

  /** Crée une table dédiée, l'ouvre et y ajoute un article (enregistré au clic sur Payer). */
  async function openTableWithOneLine(page: Page, product?: string): Promise<string> {
    const name = `P${Date.now().toString().slice(-6)}`;
    expect((await api(page, 'POST', '/api/tables', { tableNumber: name, capacity: 4 })).status).toBe(201);
    await page.click('#btnNavFloor');
    await page.locator('.table-card').filter({ has: page.locator('.table-num', { hasText: new RegExp(`^${name}$`) }) }).click();
    await expect(page.locator('#activeTableBadge')).toHaveText(`Table ${name}`);
    await page.click('#btnNavPos');
    await page.locator('.product-card').filter(product ? { hasText: product } : {}).first().click();
    const modal = page.locator('#modifiersModal');
    if (await modal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnConfirmModifiers');
    }
    await expect(page.locator('#summaryTtc')).not.toHaveText('0.00 €');
    return name;
  }

  test('réglages : langue des bons cuisine enregistrée', async ({ page }) => {
    await login(page);
    await openAdminTab(page, 'tabPrinters');
    await page.locator('#settingsKitchenLanguage').selectOption('ar');
    await expect.poll(async () => (await api(page, 'GET', '/api/settings')).json.kitchenTicketLanguage).toBe('ar');
    await page.locator('#settingsKitchenLanguage').selectOption('fr');
    await expect.poll(async () => (await api(page, 'GET', '/api/settings')).json.kitchenTicketLanguage).toBe('fr');
  });

  test('famille : poste de préparation modifiable', async ({ page }) => {
    await login(page);
    const cat = (await api(page, 'GET', '/api/catalog/categories')).json[0];
    const open = async () => {
      await page.locator(`.btn-edit-cat-trigger[data-cat-id="${cat.id}"]`).click();
      await expect(page.locator('#editCategoryModal')).toHaveClass(/active/);
    };
    await open();
    await page.locator('#editCatStation').selectOption('BAR');
    await page.click('#formEditCategory [type=submit]');
    await expect(page.locator('#editCategoryModal')).not.toHaveClass(/active/);
    await expect.poll(async () => (await api(page, 'GET', '/api/catalog/categories')).json.find((c: any) => c.id === cat.id)?.preparationStationId).toBe('BAR');
    await open();
    await expect(page.locator('#editCatStation')).toHaveValue('BAR');
    // Remise en état : chaîne vide = effacer le poste.
    await page.locator('#editCatStation').selectOption('');
    await page.click('#formEditCategory [type=submit]');
    await expect.poll(async () => (await api(page, 'GET', '/api/catalog/categories')).json.find((c: any) => c.id === cat.id)?.preparationStationId ?? null).toBeNull();
  });

  test('ligne sans poste article : preparationStationId null envoyé, jamais HOT_KITCHEN', async ({ page }) => {
    await login(page);
    const original = (await api(page, 'GET', '/api/catalog/products')).json.find((p: any) => p.name.startsWith('Salade César'));
    const put = (stationId: string | null) => api(page, 'PUT', `/api/catalog/products/${original.id}`, { ...original, stationId, isQuickKey: original.isQuickKey });
    expect((await put(null)).status).toBeLessThan(300);
    try {
      await page.reload();
      await page.waitForSelector('.product-card');
      const pinModal = page.locator('#pinLockModal');
      if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
        for (const d of '1234') await page.click(`.pin-keypad button[data-val="${d}"]`);
      }
      const saved = page.waitForRequest(r => r.method() === 'POST' && /\/api\/tables\/[^/]+\/items$/.test(r.url()));
      await openTableWithOneLine(page, original.name);
      await page.click('#btnPayModal');
      const body = (await saved).postDataJSON();
      expect(body.items).toHaveLength(1);
      expect(body.items[0].preparationStationId).toBeNull();
    } finally {
      await put(original.preparationStationId);
    }
  });

  test('paiement à table : la case envoie requestReceiptPrint', async ({ page }) => {
    await login(page);
    await openTableWithOneLine(page);
    await page.click('#btnPayModal');
    await expect(page.locator('#paymentModal')).toHaveClass(/active/);
    await expect(page.locator('#paymentPrintReceiptRow')).toBeVisible();
    await page.locator('#paymentPrintReceipt').check();
    const request = page.waitForRequest(r => r.url().endsWith('/api/checkout/pay'));
    await page.click('.tender-types-grid button[data-tender="Card"]');
    expect((await request).postDataJSON().requestReceiptPrint).toBe(true);
  });

  test('vente comptoir : la case ticket à table est masquée', async ({ page }) => {
    await login(page);
    await page.click('#btnNavPos');
    await page.locator('.product-card').first().click();
    const modal = page.locator('#modifiersModal');
    if (await modal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnConfirmModifiers');
    }
    await page.click('#btnPayModal');
    await expect(page.locator('#paymentModal')).toHaveClass(/active/);
    await expect(page.locator('#paymentPrintReceiptRow')).toBeHidden();
  });

  test('état des imprimantes affiché', async ({ page }) => {
    await login(page);
    const name = `Etat ${Date.now()}`;
    const created = await api(page, 'POST', '/api/printers', { name, ipAddress: '127.0.0.1', port: 1, paperWidthMm: 80, openCashDrawerOnReceipt: false, assignedStationIds: ['RECEIPT'] });
    expect(created.status).toBeLessThan(300);
    try {
      await openAdminTab(page, 'tabPrinters');
      await expect(page.locator('#adminPrintersList .item-list-row').filter({ hasText: name }).locator('.printer-status')).toBeVisible();
    } finally {
      await api(page, 'PUT', `/api/printers/${created.json.id}`, { name, ipAddress: '127.0.0.1', port: 1, paperWidthMm: 80, hasCashDrawer: false, targetStations: ['RECEIPT'], isActive: false });
    }
  });

  test('notification hors ligne sur vraie transition', async ({ page }) => {
    // Imprimante RECEIPT injoignable (127.0.0.1:1) + vente comptoir avec ticket : le cycle de la file (5 s)
    // détecte la panne et diffuse OnPrinterStatusChanged.
    test.setTimeout(60_000);
    await login(page);
    const name = `HorsLigne ${Date.now()}`;
    const body = { name, ipAddress: '127.0.0.1', port: 1, paperWidthMm: 80, hasCashDrawer: false, targetStations: ['RECEIPT'] };
    const created = await api(page, 'POST', '/api/printers', { ...body, openCashDrawerOnReceipt: false, assignedStationIds: ['RECEIPT'] });
    expect(created.status).toBeLessThan(300);
    try {
      await page.click('#btnNavPos');
      await page.locator('.product-card').first().click();
      const modal = page.locator('#modifiersModal');
      if (await modal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
        await page.click('#btnConfirmModifiers');
      }
      await page.click('#btnPayModal');
      await page.click('.tender-types-grid button[data-tender="Card"]');
      await expect(page.locator('.toast').filter({ hasText: /hors ligne|offline/i }).first()).toBeVisible({ timeout: 20_000 });
    } finally {
      await api(page, 'PUT', `/api/printers/${created.json.id}`, { ...body, isActive: false });
    }
  });
});
