import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { pairTill, managerToken, loginWithPin } from './helpers/pairing';

// Clé d'idempotence de l'encaissement : un nouvel essai après une réponse perdue ne débite pas deux fois.
const PAY_URL = /\/api\/checkout\/pay$/;

async function setupTable(request: APIRequestContext) {
  const auth = { Authorization: `Bearer ${await managerToken(request)}` };
  const number = `K${Date.now().toString().slice(-6)}${Math.floor(Math.random() * 90 + 10)}`;
  expect((await request.post('/api/tables', { headers: auth, data: { tableNumber: number, capacity: 4 } })).status()).toBe(201);
  const products = await (await request.get('/api/catalog/products', { headers: auth })).json();
  const p = products.find((x: any) => (x.modifierGroups ?? []).length === 0 && x.price > 0);
  const add = await request.post(`/api/tables/${number}/items`, {
    headers: auth,
    data: { items: [{ productId: p.id, productName: p.name, quantity: 2, unitPrice: p.price, taxRatePercent: p.taxRatePercent ?? 10, preparationStationId: p.preparationStationId ?? null, modifiers: [], modifiersPriceExtra: 0, course: 0 }] },
  });
  expect(add.ok()).toBeTruthy();
  return number;
}

async function openTable(page: Page, number: string) {
  await page.click('#btnNavFloor');
  await page.click('[data-floor-mode="cards"]');
  await page.locator('.table-card').filter({ has: page.locator('.table-num', { hasText: new RegExp(`^${number}$`) }) }).click();
  await expect(page.locator('#activeTableBadge')).toHaveText(`Table ${number}`);
  await page.click('#btnNavPos');
  await expect(page.locator('#cartItemsList .cart-item-row')).toHaveCount(1);
}

async function receiptCount(page: Page) {
  const device = await page.evaluate(() => JSON.parse(localStorage.getItem('pos_device') || '{}'));
  const headers = { Authorization: `Bearer ${await managerToken(page.request)}`, 'X-Device-Token': device.token };
  const from = new Date(Date.now() - 3600_000).toISOString(), to = new Date(Date.now() + 3600_000).toISOString();
  return (await (await page.request.get(`/api/dashboard/receipts?from=${from}&to=${to}`, { headers })).json()).length as number;
}

test.describe('Idempotency-Key des encaissements', () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('pos_lang', 'fr'));
  });

  test('réponse perdue : le nouvel essai reprend la clé et n\'écrit qu\'un reçu ; la part suivante a sa propre clé', async ({ page, request }) => {
    await pairTill(page);
    const number = await setupTable(request);
    await loginWithPin(page, '1234');
    await openTable(page, number);
    const before = await receiptCount(page);

    // Note partagée en deux parts égales : deux demandes identiques, qui ne doivent pas partager de clé.
    await page.click('#btnSplitBill');
    await page.click('#btnConfirmSplit');
    await expect(page.locator('#payRemainingAmount')).toContainText('Part 1/2');

    // Le serveur encaisse, mais la réponse n'arrive jamais au navigateur (délai dépassé).
    const keys: string[] = [];
    await page.route(PAY_URL, async route => {
      keys.push(route.request().headers()['idempotency-key']);
      if (keys.length === 1) {
        await route.fetch();
        return route.abort('failed');
      }
      return route.continue();
    });
    await page.click('.tender-types-grid button[data-tender="Card"]');
    await expect(page.locator('.toast.error').last()).toBeVisible();
    await expect(page.locator('#paymentModal')).toHaveClass(/active/);
    expect(await receiptCount(page)).toBe(before + 1); // l'encaissement a bien eu lieu côté serveur

    const retried = page.waitForResponse(r => PAY_URL.test(r.url()));
    await page.click('.tender-types-grid button[data-tender="Card"]');
    expect((await retried).status()).toBe(200);
    expect(keys).toHaveLength(2);
    expect(keys[1]).toBe(keys[0]);
    expect(await receiptCount(page)).toBe(before + 1); // rejeu : pas de second reçu
    await expect(page.locator('#payRemainingAmount')).toContainText('Part 2/2');

    // Part 2 : même demande que la part 1, réponse reçue entre-temps => nouvelle clé, vrai second encaissement.
    const second = page.waitForResponse(r => PAY_URL.test(r.url()));
    await page.click('.tender-types-grid button[data-tender="Card"]');
    expect((await second).status()).toBe(200);
    expect(keys).toHaveLength(3);
    expect(keys[2]).not.toBe(keys[0]);
    expect(await receiptCount(page)).toBe(before + 2);
    await page.unroute(PAY_URL);
  });
});
