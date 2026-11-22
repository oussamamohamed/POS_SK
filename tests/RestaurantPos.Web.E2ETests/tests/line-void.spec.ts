import { test, expect, type APIRequestContext, type Page } from '@playwright/test';

// Annulation d'une ligne envoyée (manager) et correction d'une ligne enregistrée non envoyée (tout opérateur).
const XSS = '<img src=x onerror="window.__xss=1">';

async function token(request: APIRequestContext, pin: string) {
  const res = await request.post('/api/auth/login', { data: { pin } });
  expect(res.ok()).toBeTruthy();
  return (await res.json()).token as string;
}

/** Crée une table avec une ligne (quantité donnée) ; l'envoie en cuisine si `dispatch`. */
async function setupTable(request: APIRequestContext, quantity: number, dispatch: boolean, productName?: string) {
  const auth = { Authorization: `Bearer ${await token(request, '1234')}` };
  const number = `V${Date.now().toString().slice(-6)}${Math.floor(Math.random() * 90 + 10)}`;
  expect((await request.post('/api/tables', { headers: auth, data: { tableNumber: number, capacity: 4 } })).status()).toBe(201);
  const products = await (await request.get('/api/catalog/products', { headers: auth })).json();
  const p = products.find((x: any) => (productName ? x.name === productName : (x.modifierGroups ?? []).length === 0 && x.price > 0));
  const add = await request.post(`/api/tables/${number}/items`, {
    headers: auth,
    data: { items: [{ productId: p.id, productName: p.name, quantity, unitPrice: p.price, taxRatePercent: p.taxRatePercent ?? 10, preparationStationId: p.preparationStationId ?? null, modifiers: [], modifiersPriceExtra: 0, course: 0 }] },
  });
  expect(add.ok()).toBeTruthy();
  if (dispatch) expect((await request.post(`/api/tables/${number}/dispatch`, { headers: auth })).ok()).toBeTruthy();
  return { number, productName: p.name as string, auth };
}

async function loginAs(page: Page, pin: string) {
  await page.goto('/?nocache=' + Date.now());
  await page.waitForLoadState('domcontentloaded');
  await page.click('#btnPinClear');
  for (const d of pin) await page.click(`.pin-keypad button[data-val="${d}"]`);
  await expect(page.locator('#pinLockModal')).not.toHaveClass(/active/);
}

async function openTable(page: Page, number: string) {
  await page.click('#btnNavFloor');
  await page.click('[data-floor-mode="cards"]');
  await page.locator('.table-card').filter({ has: page.locator('.table-num', { hasText: new RegExp(`^${number}$`) }) }).click();
  await expect(page.locator('#activeTableBadge')).toHaveText(`Table ${number}`);
  await page.click('#btnNavPos');
  await expect(page.locator('#cartItemsList .cart-item-row')).toHaveCount(1);
}

test.describe('Annulation et correction de ligne', () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => localStorage.setItem('pos_lang', 'fr'));
  });

  test('manager voids a sent line and kitchen shows it cancelled', async ({ page, request }) => {
    const { number, productName, auth } = await setupTable(request, 1, true);
    await loginAs(page, '1234');
    await openTable(page, number);

    await page.locator('#cartItemsList [data-action="void"]').click();
    await expect(page.locator('#lineVoidModal')).toHaveClass(/active/);
    // Motif obligatoire : le formulaire ne part pas sans motif.
    await page.click('#lineVoidModal button[type="submit"]');
    await expect(page.locator('#lineVoidModal')).toHaveClass(/active/);

    await page.fill('#inputLineVoidReason', 'Erreur de saisie');
    const sent = page.waitForResponse(r => /\/items\/[^/]+\/void$/.test(r.url()) && r.request().method() === 'POST');
    await page.click('#lineVoidModal button[type="submit"]');
    expect((await sent).status()).toBe(200);
    await expect(page.locator('#lineVoidModal')).not.toHaveClass(/active/);
    await expect(page.locator('#cartItemsList .cart-item-row')).toHaveCount(0);

    const tickets = await (await request.get('/api/kds/tickets', { headers: auth })).json();
    const item = tickets.filter((k: any) => k.tableNumber === number).flatMap((k: any) => k.items).find((i: any) => i.productName === productName);
    expect(item.status).toBe(3);
    expect(item.cancelledQuantity).toBe(1);

    await page.click('#btnNavKds');
    const card = page.locator('.kds-ticket-card').filter({ hasText: number }).first();
    await expect(card.locator('.ticket-items s, .ticket-items .kds-item-cancelled').first()).toContainText(productName);
    await expect(card).toContainText('ANNULÉ');
  });

  test('waiter sees the cancel action refused on a sent line', async ({ page, request }) => {
    const { number } = await setupTable(request, 1, true);
    await loginAs(page, '2468');
    await openTable(page, number);

    await expect(page.locator('#cartItemsList [data-action="void"]')).toHaveCount(0);
    await page.locator('#cartItemsList [data-action="minus"]').click();
    await expect(page.locator('.toast.warning').last()).toContainText('réservée au manager');
    await expect(page.locator('#lineVoidModal')).not.toHaveClass(/active/);
    await expect(page.locator('#cartItemsList .cart-item-row')).toHaveCount(1);
  });

  test('waiter corrects a saved unsent line (no reason asked)', async ({ page, request }) => {
    const { number } = await setupTable(request, 2, false);
    await loginAs(page, '2468');
    await openTable(page, number);

    const sent = page.waitForResponse(r => /\/items\/[^/]+\/void$/.test(r.url()) && r.request().method() === 'POST');
    await page.locator('#cartItemsList [data-action="minus"]').click();
    const res = await sent;
    expect(res.status()).toBe(200);
    expect(res.request().postDataJSON()).toMatchObject({ quantity: 1 });
    await expect(page.locator('#lineVoidModal')).not.toHaveClass(/active/);
    await expect(page.locator('#cartItemsList .cart-item-row .cart-item-controls span').first()).toHaveText('1');
  });

  test('reason with <img onerror> is escaped', async ({ page, request }) => {
    const { number } = await setupTable(request, 2, true);
    await loginAs(page, '1234');
    await openTable(page, number);

    await page.locator('#cartItemsList [data-action="void"]').click();
    await page.fill('#inputLineVoidReason', XSS);
    await page.click('#lineVoidModal button[type="submit"]');
    await expect(page.locator('.toast.success').last()).toContainText(XSS); // texte littéral, visible
    await page.waitForTimeout(300); // onerror est asynchrone
    expect(await page.evaluate(() => (window as any).__xss)).toBeUndefined();
    expect(await page.locator('#cartItemsList img').count()).toBe(0);
  });
});
