import { test, expect, type APIRequestContext, type Page } from '@playwright/test';
import { managerToken, loginWithPin, pairTill } from './helpers/pairing';

// Annulation d'une ligne envoyée (manager) et correction d'une ligne enregistrée non envoyée (tout opérateur).
// Chaque test vide ou solde sa table (afterEach) : aucune commande ouverte ne bloque la Z de daily-summary.
const XSS = '<img src=x onerror="window.__xss=1">';
const VOID_URL = /\/items\/[^/]+\/void$/;

const createdTables: string[] = [];

/** Crée une table avec une ligne (quantité donnée) ; l'envoie en cuisine si `dispatch`. */
async function setupTable(request: APIRequestContext, quantity: number, dispatch: boolean, productName?: string) {
  const auth = { Authorization: `Bearer ${await managerToken(request)}` };
  const number = `V${Date.now().toString().slice(-6)}${Math.floor(Math.random() * 90 + 10)}`;
  expect((await request.post('/api/tables', { headers: auth, data: { tableNumber: number, capacity: 4 } })).status()).toBe(201);
  createdTables.push(number);
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

/** Vide la table : annule chaque ligne restante (sans reçu) ; une commande en partie réglée est soldée par le poste appairé. */
async function clearTable(request: APIRequestContext, number: string, deviceToken?: string) {
  const headers: Record<string, string> = { Authorization: `Bearer ${await managerToken(request)}` };
  const res = await request.get(`/api/tables/${encodeURIComponent(number)}/order`, { headers });
  if (!res.ok()) return;
  const order = await res.json();
  if ((order.amountPaidTtc ?? 0) > 0) {
    expect(deviceToken, 'commande réglée en partie : solde par le poste appairé').toBeTruthy();
    const due = order.amountDueTtc;
    const pay = await request.post('/api/checkout/pay', {
      headers: { ...headers, 'X-Device-Token': deviceToken! },
      data: { orderId: order.orderId, tableNumber: number, tenders: [{ method: 0, amount: due, tendered: due, changeGiven: 0 }] },
    });
    expect(pay.ok()).toBeTruthy();
    return;
  }
  for (const line of order.lines ?? []) {
    const v = await request.post(`/api/orders/${order.orderId}/items/${line.lineId}/void`, { headers, data: { quantity: line.quantity, reason: 'Nettoyage E2E' } });
    expect(v.ok()).toBeTruthy();
  }
}

async function openTable(page: Page, number: string) {
  await page.click('#btnNavFloor');
  await page.click('[data-floor-mode="cards"]');
  await page.locator('.table-card').filter({ has: page.locator('.table-num', { hasText: new RegExp(`^${number}$`) }) }).click();
  await expect(page.locator('#activeTableBadge')).toHaveText(`Table ${number}`);
  await page.click('#btnNavPos');
  await expect(page.locator('#cartItemsList .cart-item-row')).toHaveCount(1);
}

/** Compte les appels POST …/void de la page. */
function countVoidRequests(page: Page) {
  const counter = { count: 0 };
  page.on('request', r => { if (VOID_URL.test(r.url()) && r.method() === 'POST') counter.count++; });
  return counter;
}

test.describe('Annulation et correction de ligne', () => {
  let deviceToken: string | undefined;

  test.beforeEach(async ({ page }) => {
    deviceToken = undefined;
    await page.addInitScript(() => localStorage.setItem('pos_lang', 'fr'));
  });

  test.afterEach(async ({ request }) => {
    while (createdTables.length) await clearTable(request, createdTables.pop()!, deviceToken);
  });

  test('manager voids a sent line and kitchen shows it cancelled', async ({ page, request }) => {
    const { number, productName, auth } = await setupTable(request, 1, true);
    await loginWithPin(page, '1234');
    await openTable(page, number);

    await page.locator('#cartItemsList [data-action="void"]').click();
    await expect(page.locator('#lineVoidModal')).toHaveClass(/active/);
    // Motif obligatoire : le formulaire ne part pas sans motif.
    await page.click('#lineVoidModal button[type="submit"]');
    await expect(page.locator('#lineVoidModal')).toHaveClass(/active/);

    await page.fill('#inputLineVoidReason', 'Erreur de saisie');
    const sent = page.waitForResponse(r => VOID_URL.test(r.url()) && r.request().method() === 'POST');
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

  test('partial void: KDS keeps the dish and shows (ANNULÉ n)', async ({ page, request }) => {
    const { number, productName } = await setupTable(request, 3, true);
    await loginWithPin(page, '1234');
    await openTable(page, number);

    await page.locator('#cartItemsList [data-action="void"]').click();
    await page.fill('#inputLineVoidQty', '2');
    await page.fill('#inputLineVoidReason', 'Client parti');
    await page.click('#lineVoidModal button[type="submit"]');
    await expect(page.locator('#lineVoidModal')).not.toHaveClass(/active/);
    await expect(page.locator('#cartItemsList .cart-item-row .cart-item-controls span').first()).toHaveText('1');

    await page.click('#btnNavKds');
    const card = page.locator('.kds-ticket-card').filter({ hasText: number }).first();
    const line = card.locator('.ticket-items div', { hasText: productName }).first();
    await expect(line).toContainText(`3x ${productName}`);
    await expect(line).toContainText('(ANNULÉ 2)');
    await expect(line.locator('s')).toHaveCount(0); // pas barré : il reste une unité à préparer
  });

  test('manager minus on a sent line opens the modal; quantity is bounded', async ({ page, request }) => {
    const { number } = await setupTable(request, 2, true);
    await loginWithPin(page, '1234');
    await openTable(page, number);
    const calls = countVoidRequests(page);

    await page.locator('#cartItemsList [data-action="minus"]').click();
    await expect(page.locator('#lineVoidModal')).toHaveClass(/active/);
    const qty = page.locator('#inputLineVoidQty');
    await expect(qty).toHaveValue('1');
    await expect(qty).toHaveAttribute('min', '1');
    await expect(qty).toHaveAttribute('max', '2');
    await page.fill('#inputLineVoidReason', 'Erreur');
    for (const bad of ['3', '0', '-1']) {
      await qty.fill(bad);
      await page.click('#lineVoidModal button[type="submit"]');
      await expect(page.locator('#lineVoidModal')).toHaveClass(/active/);
    }
    expect(calls.count).toBe(0);

    await qty.fill('2');
    await page.click('#lineVoidModal button[type="submit"]');
    await expect(page.locator('#lineVoidModal')).not.toHaveClass(/active/);
    await expect(page.locator('#cartItemsList .cart-item-row')).toHaveCount(0);
    expect(calls.count).toBe(1);
  });

  test('double tap sends one void and one correction', async ({ page, request }) => {
    const sentTable = await setupTable(request, 3, true);
    await loginWithPin(page, '1234');
    await openTable(page, sentTable.number);
    const calls = countVoidRequests(page);

    await page.locator('#cartItemsList [data-action="void"]').click();
    await page.fill('#inputLineVoidReason', 'Erreur');
    await page.locator('#lineVoidModal button[type="submit"]').dblclick();
    await expect(page.locator('#lineVoidModal')).not.toHaveClass(/active/);
    await expect(page.locator('#cartItemsList .cart-item-row .cart-item-controls span').first()).toHaveText('2');
    expect(calls.count).toBe(1);

    const unsent = await setupTable(request, 3, false);
    await openTable(page, unsent.number);
    calls.count = 0;
    await page.locator('#cartItemsList [data-action="minus"]').dblclick();
    await expect(page.locator('#cartItemsList .cart-item-row .cart-item-controls span').first()).toHaveText('2');
    await page.waitForTimeout(300);
    expect(calls.count).toBe(1);
  });

  test('reason: 256 max in the modal, trimmed, server refuses 257', async ({ page, request }) => {
    const { number, auth } = await setupTable(request, 2, true);
    await loginWithPin(page, '1234');
    await openTable(page, number);

    await page.locator('#cartItemsList [data-action="void"]').click();
    const reason = page.locator('#inputLineVoidReason');
    await expect(reason).toHaveAttribute('maxlength', '256');
    await reason.fill('x'.repeat(300));
    await expect(reason).toHaveValue('x'.repeat(256));

    await reason.fill('   Erreur de saisie   ');
    const sent = page.waitForResponse(r => VOID_URL.test(r.url()) && r.request().method() === 'POST');
    await page.click('#lineVoidModal button[type="submit"]');
    const res = await sent;
    expect(res.status()).toBe(200);
    expect(res.request().postDataJSON()).toMatchObject({ quantity: 1, reason: 'Erreur de saisie' });
    await expect(page.locator('.toast.success').last()).toContainText('1 × ');
    await expect(page.locator('.toast.success').last()).toContainText(': Erreur de saisie');

    // Au-delà de la limite (hors navigateur) : 400 et message du serveur ; 256 entourés d'espaces passent (rognés d'abord).
    const order = await (await request.get(`/api/tables/${number}/order`, { headers: auth })).json();
    const url = `/api/orders/${order.orderId}/items/${order.lines[0].lineId}/void`;
    const tooLong = await request.post(url, { headers: { ...auth, 'Accept-Language': 'fr' }, data: { quantity: 1, reason: 'y'.repeat(257) } });
    expect(tooLong.status()).toBe(400);
    expect((await tooLong.json()).message).toBe('Le motif ne peut pas dépasser 256 caractères.');
    const padded = await request.post(url, { headers: auth, data: { quantity: 1, reason: `  ${'y'.repeat(256)}  ` } });
    expect(padded.status()).toBe(200);
  });

  test('409 order_has_receipts: toast names the receipt, line kept', async ({ page, request }) => {
    await pairTill(page);
    const { number } = await setupTable(request, 2, true);
    await loginWithPin(page, '1234');
    const device = await page.evaluate(() => JSON.parse(localStorage.getItem('pos_device') || '{}'));
    deviceToken = device.token;
    const headers = { Authorization: `Bearer ${await managerToken(request)}`, 'X-Device-Token': device.token };
    const order = await (await request.get(`/api/tables/${number}/order`, { headers })).json();
    const pay = await request.post('/api/checkout/pay', {
      headers, data: { orderId: order.orderId, tableNumber: number, tenders: [{ method: 0, amount: 1, tendered: 1, changeGiven: 0 }] },
    });
    expect(pay.ok()).toBeTruthy();
    const receiptNumber = (await pay.json()).receiptNumber as string;
    expect(receiptNumber).toBeTruthy();

    await openTable(page, number);
    await page.locator('#cartItemsList [data-action="void"]').click();
    await page.fill('#inputLineVoidReason', 'Erreur');
    const sent = page.waitForResponse(r => VOID_URL.test(r.url()) && r.request().method() === 'POST');
    await page.click('#lineVoidModal button[type="submit"]');
    const res = await sent;
    expect(res.status()).toBe(409);
    expect((await res.json()).code).toBe('order_has_receipts');
    await expect(page.locator('.toast.error').last()).toContainText(receiptNumber);
    await expect(page.locator('.toast.error').last()).toContainText('annulez-les');
    await expect(page.locator('#lineVoidModal')).toHaveClass(/active/);
    await expect(page.locator('#cartItemsList .cart-item-controls span').first()).toHaveText('2');
  });

  test('waiter sees the cancel action refused on a sent line', async ({ page, request }) => {
    const { number } = await setupTable(request, 1, true);
    await loginWithPin(page, '2468');
    await openTable(page, number);

    await expect(page.locator('#cartItemsList [data-action="void"]')).toHaveCount(0);
    await page.locator('#cartItemsList [data-action="minus"]').click();
    await expect(page.locator('.toast.warning').last()).toContainText('réservée au manager');
    await expect(page.locator('#lineVoidModal')).not.toHaveClass(/active/);
    await expect(page.locator('#cartItemsList .cart-item-row')).toHaveCount(1);
  });

  test('waiter corrects a saved unsent line (no reason asked)', async ({ page, request }) => {
    const { number } = await setupTable(request, 2, false);
    await loginWithPin(page, '2468');
    await openTable(page, number);

    const sent = page.waitForResponse(r => VOID_URL.test(r.url()) && r.request().method() === 'POST');
    await page.locator('#cartItemsList [data-action="minus"]').click();
    const res = await sent;
    expect(res.status()).toBe(200);
    expect(res.request().postDataJSON()).toMatchObject({ quantity: 1 });
    await expect(page.locator('#lineVoidModal')).not.toHaveClass(/active/);
    await expect(page.locator('#cartItemsList .cart-item-row .cart-item-controls span').first()).toHaveText('1');
  });

  test('reason with <img onerror> is escaped', async ({ page, request }) => {
    const { number } = await setupTable(request, 2, true);
    await loginWithPin(page, '1234');
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
