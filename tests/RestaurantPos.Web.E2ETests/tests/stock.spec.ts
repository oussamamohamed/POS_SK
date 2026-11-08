import { test, expect, type Page } from '@playwright/test';
import { pairTill, managerToken } from './helpers/pairing';

const uniq = () => Math.random().toString(36).slice(2, 7);

// La baisse de stock à la vente et la restitution par avoir sont couvertes par Api.Tests/StockEndpointsTests.cs (API réelle, poste appairé).
test.describe('Stock : réception, inventaire, pertes, alerte', () => {
  async function openStockTab(page: Page) {
    await pairTill(page);
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of '1234') await page.click(`.pin-keypad button[data-val="${d}"]`);
      await expect(pinModal).not.toHaveClass(/active/);
    }
    await page.click('button[data-view="adminView"]');
    await page.click('button[data-admin-tab="tabStock"]');
  }

  async function type(page: Page, digits: string) {
    for (const k of digits) await page.locator(`#stockOpModal .stock-keypad [data-k="${k}"]`).click();
  }

  test('réception, perte, inventaire et historique au pavé numérique, puis alerte sous le seuil', async ({ page }) => {
    const name = `Farine ${uniq()}`;
    const token = await managerToken(page.request);
    const res = await page.request.post('/api/stock/ingredients', {
      headers: { Authorization: `Bearer ${token}` },
      data: { name, unit: 'g', unitCostMilliCents: 1000, isPreparation: false, allergenMask: 0, lowStockMilli: 5000000 },
    });
    expect(res.ok()).toBeTruthy();
    const id = (await res.json()).id;

    await openStockTab(page);
    const row = page.locator('#stockList .stock-row', { hasText: name });
    await expect(row).toContainText('stock : 0 g');

    await row.locator('[data-op="receipt"]').click();
    await type(page, '10000');
    await page.click('#stockOpModal [data-act="ok"]');
    await expect(row).toContainText('stock : 10000 g');

    await row.locator('[data-op="loss"]').click();
    await type(page, '2000');
    await page.click('#stockOpModal [data-act="ok"]');
    await expect(row).toContainText('stock : 8000 g');

    await row.locator('[data-op="count"]').click();
    await type(page, '4000');
    await page.click('#stockOpModal [data-act="ok"]');
    await expect(row).toContainText('stock : 4000 g');
    await expect(row.locator('.stock-level')).toHaveClass(/low/);

    await row.locator('[data-op="history"]').click();
    await expect(page.locator('#stockHistoryRows')).toContainText('Réception');
    await expect(page.locator('#stockHistoryRows')).toContainText('Perte');
    await expect(page.locator('#stockHistoryRows')).toContainText('Inventaire');
    await page.click('#stockOpModal [data-act="cancel"]');

    const alerts = await (await page.request.get('/api/stock/alerts', { headers: { Authorization: `Bearer ${token}` } })).json();
    expect(alerts.lowStock.some((a: any) => a.ingredientId === id)).toBeTruthy();
  });

  test('consommation d\'une option de modificateur et annulation d\'un reçu avec la case « produit non consommé »', async ({ page }) => {
    const name = `Beurre ${uniq()}`;
    const token = await managerToken(page.request);
    const headers = { Authorization: `Bearer ${token}` };
    const ing = await (await page.request.post('/api/stock/ingredients', { headers, data: { name, unit: 'g', unitCostMilliCents: 900, isPreparation: false, allergenMask: 0 } })).json();
    const products = await (await page.request.get('/api/catalog/products', { headers })).json();
    const burger = products.find((p: any) => p.name.startsWith('Burger Gourmet'));
    const option = burger.modifierGroups[1].options[0];

    await openStockTab(page);
    await page.click('button[data-admin-tab="tabCatalog"]');
    await page.evaluate((n) => {
      for (const b of document.querySelectorAll<HTMLElement>('#tabCatalog .btn-edit-product')) {
        let el: HTMLElement | null = b;
        while (el && !el.innerText.includes(n)) el = el.parentElement;
        if (el && el.querySelectorAll('.btn-edit-product').length === 1) { b.click(); return; }
      }
      throw new Error('ligne produit introuvable');
    }, burger.name);
    const block = page.locator(`.opt-cons[data-option="${option.id}"]`);
    await block.locator('.opt-ing').selectOption({ label: name });
    await block.locator('.opt-qty').fill('15');
    await block.locator('[data-add]').click();
    await page.click('#formEditProduct button[type="submit"]');
    await expect(page.locator('#editProductModal')).not.toHaveClass(/active/);
    const saved = await (await page.request.get(`/api/catalog/modifier-options/${option.id}/consumption`, { headers })).json();
    expect(saved).toContainEqual({ ingredientId: ing.id, deltaQuantityMilli: 15000 });

    // Annulation d'un reçu du poste du navigateur, depuis la vue Encaissements.
    const device = await page.evaluate(() => JSON.parse(localStorage.getItem('pos_device') || '{}'));
    const sale = await page.request.post('/api/sync/receipt', {
      headers: { ...headers, 'X-Device-Token': device.token },
      data: { orderId: '00000000-0000-0000-0000-000000000000', tableNumber: 'ANN' + uniq(), totalTtcCents: 1234, tenders: [{ method: 0, amountCents: 1234, tenderedCents: 1234 }] },
    });
    expect(sale.ok()).toBeTruthy();
    await page.click('#btnNavTakings');
    const voidBtn = page.locator('[data-void]').first();
    await voidBtn.click();
    await page.locator('#voidRestock').check();
    await page.click('#voidReceiptModal [data-act="ok"]');
    await expect(page.locator('#voidReceiptModal')).toHaveCount(0);
  });
});
