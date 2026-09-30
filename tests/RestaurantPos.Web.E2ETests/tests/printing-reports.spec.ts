import { test, expect, Page } from '@playwright/test';
import { pairTill } from './helpers/pairing';

/** Mode texte des imprimantes, impression des rapports X/Z, pourboire à table. */
test.describe('Impression des rapports', () => {

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

  async function openFiscal(page: Page) {
    await page.click('#btnNavFiscal');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);
  }

  async function openTableWithOneLine(page: Page): Promise<number> {
    const name = `R${Date.now().toString().slice(-6)}`;
    expect((await api(page, 'POST', '/api/tables', { tableNumber: name, capacity: 4 })).status).toBe(201);
    await page.click('#btnNavFloor');
    await page.locator('.table-card').filter({ has: page.locator('.table-num', { hasText: new RegExp(`^${name}$`) }) }).click();
    await expect(page.locator('#activeTableBadge')).toHaveText(`Table ${name}`);
    await page.click('#btnNavPos');
    await page.locator('.product-card').first().click();
    const modal = page.locator('#modifiersModal');
    if (await modal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnConfirmModifiers');
    }
    await expect(page.locator('#summaryTtc')).not.toHaveText('0.00 €');
    return parseFloat(((await page.locator('#summaryTtc').textContent()) || '0').replace('€', '').trim());
  }

  test('imprimante : case mode texte enregistrée et conservée', async ({ page }) => {
    await login(page);
    const name = `Texte E2E ${Date.now()}`;
    await page.click('#btnNavAdmin');
    await page.click('.admin-tab-btn[data-admin-tab="tabPrinters"]');
    await page.fill('#inputPrinterName', name);
    await page.fill('#inputPrinterIp', '10.0.0.77');
    await page.check('#newPrinterTextMode');
    await page.click('#formAddPrinter [type=submit]');
    const row = page.locator('#adminPrintersList .item-list-row').filter({ hasText: name });
    await expect(row).toBeVisible();
    const printers = (await api(page, 'GET', '/api/printers')).json;
    const created = printers.find((p: any) => p.name === name);
    try {
      expect(created.textMode).toBe(true);
      await row.locator('.btn-edit-printer').click();
      await expect(page.locator('#editPrinterTextMode')).toBeChecked();
    } finally {
      await api(page, 'PUT', `/api/printers/${created.id}`, { name, ipAddress: '10.0.0.77', port: 9100, paperWidthMm: 80, hasCashDrawer: false, targetStations: ['RECEIPT'], isActive: false });
    }
  });

  test('fiscal : Imprimer le rapport X appelle la route d\'impression', async ({ page }) => {
    await login(page);
    await openFiscal(page);
    const req = page.waitForRequest(r => r.url().includes('/api/fiscal/x-report/print') && r.method() === 'POST');
    await page.locator('#btnPrintXReport').click();
    await req;
  });

  test('fiscal : Réimprimer la dernière clôture appelle la route d\'impression', async ({ page }) => {
    await login(page);
    await openFiscal(page);
    const req = page.waitForRequest(r => r.url().includes('/api/fiscal/latest-closure/print') && r.method() === 'POST');
    await page.locator('#btnReprintZ').click();
    await req;
  });

  test('fiscal : Z refusée affiche les commandes à encaisser', async ({ page }) => {
    await page.route('**/api/fiscal/z-closure', route => route.fulfill({
      status: 409, contentType: 'application/json',
      body: JSON.stringify({ code: 'open_orders', message: 'Clôture Z impossible : encaissez d\'abord 1 commande(s) en cours : T5 (12.50)', openOrders: [{ tableNumber: 'T5', remainingTtc: 12.5 }] })
    }));
    await login(page);
    await openFiscal(page);
    await page.click('#btnExecuteZ');
    await expect(page.locator('.toast').last()).toContainText('T5 (12.50)');
  });

  test('fiscal : Z réussie sans imprimante → avertissement', async ({ page }) => {
    await page.route('**/api/fiscal/z-closure', route => route.fulfill({ status: 200, contentType: 'application/json',
      body: JSON.stringify({ closureSequence: 9, totalSalesTtc: 0, totalSalesHt: 0, receiptCount: 0, vatBreakdown: {}, paymentTotals: {}, perpetualGrandTotal: 0, signatureHash: 'x', closedAtUtc: new Date().toISOString(), printQueued: false }) }));
    await login(page);
    await openFiscal(page);
    await page.click('#btnExecuteZ');
    await expect(page.locator('.toast').filter({ hasText: /imprimante/i })).toBeVisible();
  });

  test('paiement à table : le pourboire est envoyé à part et inclus dans l\'encaissement', async ({ page }) => {
    await login(page, '2468');
    const linePrice = await openTableWithOneLine(page);
    await page.click('#btnPayModal');
    await expect(page.locator('#paymentModal')).toHaveClass(/active/);
    await page.locator('[data-tip-percent="10"]').click();
    const req = page.waitForRequest(r => r.url().endsWith('/api/checkout/pay'));
    await page.click('.tender-types-grid button[data-tender="Card"]');
    const body = (await req).postDataJSON();
    expect(body.tipAmount).toBeCloseTo(linePrice * 0.1, 2);
    expect(body.tenders[0].amount).toBeCloseTo(linePrice + body.tipAmount, 2);
  });
});
