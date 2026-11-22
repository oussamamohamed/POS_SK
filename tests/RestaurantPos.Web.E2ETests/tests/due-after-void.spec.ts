import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { pairTill, managerToken } from './helpers/pairing';

// D5 : après annulation d'une part d'une note partagée, la commande rouverte propose le reste dû (la part annulée), jamais le TTC entier.

async function setupTable(request: APIRequestContext) {
  const auth = { Authorization: `Bearer ${await managerToken(request)}` };
  const number = `D${Date.now().toString().slice(-6)}${Math.floor(Math.random() * 90 + 10)}`;
  expect((await request.post('/api/tables', { headers: auth, data: { tableNumber: number, capacity: 4 } })).status()).toBe(201);
  const products = await (await request.get('/api/catalog/products', { headers: auth })).json();
  const p = products.find((x: any) => (x.modifierGroups ?? []).length === 0 && x.price > 0);
  const add = await request.post(`/api/tables/${number}/items`, {
    headers: auth,
    data: { items: [{ productId: p.id, productName: p.name, quantity: 3, unitPrice: p.price, taxRatePercent: p.taxRatePercent ?? 10, preparationStationId: p.preparationStationId ?? null, modifiers: [], modifiersPriceExtra: 0, course: 0 }] },
  });
  expect(add.ok()).toBeTruthy();
  return number;
}

async function loginAs(page: Page, pin: string) {
  await page.goto('/?nocache=' + Date.now());
  await page.waitForLoadState('domcontentloaded');
  const pinModal = page.locator('#pinLockModal');
  // Au rechargement, la session peut être encore ouverte : le PIN n'est saisi que si l'écran le demande.
  if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
    await page.click('#btnPinClear');
    for (const d of pin) await page.click(`.pin-keypad button[data-val="${d}"]`);
  }
  await expect(pinModal).not.toHaveClass(/active/);
}

async function openTable(page: Page, number: string) {
  await page.click('#btnNavFloor');
  await page.click('[data-floor-mode="cards"]');
  await page.locator('.table-card').filter({ has: page.locator('.table-num', { hasText: new RegExp(`^${number}$`) }) }).click();
  await expect(page.locator('#activeTableBadge')).toHaveText(`Table ${number}`);
  await page.click('#btnNavPos');
  await expect(page.locator('#cartItemsList .cart-item-row')).toHaveCount(1);
}

async function payCard(page: Page) {
  const sent = page.waitForResponse(r => r.url().endsWith('/api/checkout/pay'));
  await page.click('.tender-types-grid button[data-tender="Card"]');
  const res = await sent;
  expect(res.status()).toBe(200);
  return { amount: res.request().postDataJSON().tenders[0].amount as number, receiptNumber: (await res.json()).receiptNumber as string };
}

test.describe('Reste dû après annulation d une part', () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('pos_lang', 'fr'));
  });

  test('la commande rouverte propose la part annulée seulement, puis se solde', async ({ page, request }) => {
    await pairTill(page);
    const number = await setupTable(request);
    await loginAs(page, '1234');
    await openTable(page, number);

    // Note partagée en deux, les deux parts réglées par carte : la commande est soldée.
    await page.click('#btnSplitBill');
    await page.click('#btnConfirmSplit');
    await expect(page.locator('#payRemainingAmount')).toContainText('Part 1/2');
    const first = await payCard(page);
    await expect(page.locator('#payRemainingAmount')).toContainText('Part 2/2');
    const second = await payCard(page);
    await expect(page.locator('#paymentModal')).not.toHaveClass(/active/);

    // Annulation de la 1re part depuis le poste qui l'a émise : la commande est rouverte sur la table.
    const device = await page.evaluate(() => JSON.parse(localStorage.getItem('pos_device') || '{}'));
    const headers = { Authorization: `Bearer ${await managerToken(page.request)}`, 'X-Device-Token': device.token };
    const from = new Date(Date.now() - 3600_000).toISOString(), to = new Date(Date.now() + 3600_000).toISOString();
    const receipts = await (await page.request.get(`/api/dashboard/receipts?from=${from}&to=${to}`, { headers })).json();
    const id = receipts.find((r: any) => r.receiptNumber === first.receiptNumber).id;
    const voided = await page.request.post(`/api/checkout/void/${id}`, { headers, data: { terminalId: '', operatorId: '00000000-0000-0000-0000-000000000000', restock: false } });
    expect(voided.ok()).toBeTruthy();

    const order = await (await page.request.get(`/api/tables/${number}/order`, { headers })).json();
    expect(order.amountPaidTtc).toBeCloseTo(second.amount, 2);
    expect(order.amountDueTtc).toBeCloseTo(first.amount, 2);

    // Rechargement : le modal d'encaissement propose la part annulée et affiche le déjà réglé.
    await loginAs(page, '1234');
    await openTable(page, number);
    await page.click('#btnPayModal');
    await expect(page.locator('#paymentModal')).toHaveClass(/active/);
    await expect(page.locator('#payRemainingAmount')).toHaveText(`${first.amount.toFixed(2)} €`);
    await expect(page.locator('#payAlreadyPaid')).toBeVisible();
    await expect(page.locator('#payAlreadyPaid')).toHaveText(`Déjà réglé : ${second.amount.toFixed(2)} €`);

    // Solde : la part annulée est encaissée, aucune commande ouverte ne reste sur la table.
    const repaid = await payCard(page);
    expect(repaid.amount).toBeCloseTo(first.amount, 2);
    expect((await page.request.get(`/api/tables/${number}/order`, { headers })).status()).toBe(404);
  });
});
