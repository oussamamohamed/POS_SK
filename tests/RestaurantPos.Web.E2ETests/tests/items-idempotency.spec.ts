import { test, expect, type Page } from '@playwright/test';
import { pairTill, managerToken, loginWithPin } from './helpers/pairing';

// Clé d'idempotence de l'ajout de lignes : un nouvel envoi après une réponse perdue n'ajoute pas la ligne deux fois.
const ITEMS_URL = /\/api\/tables\/[^/]+\/items$/;

async function openFreshTable(page: Page): Promise<string> {
  const auth = { Authorization: `Bearer ${await managerToken(page.request)}` };
  const number = `I${Date.now().toString().slice(-6)}${Math.floor(Math.random() * 90 + 10)}`;
  expect((await page.request.post('/api/tables', { headers: auth, data: { tableNumber: number, capacity: 4 } })).status()).toBe(201);
  await page.click('#btnNavFloor');
  await page.click('[data-floor-mode="cards"]');
  await page.locator('.table-card').filter({ has: page.locator('.table-num', { hasText: new RegExp(`^${number}$`) }) }).click();
  await expect(page.locator('#activeTableBadge')).toHaveText(`Table ${number}`);
  return number;
}

async function quantity(page: Page, number: string) {
  const headers = { Authorization: `Bearer ${await managerToken(page.request)}` };
  const order = await (await page.request.get(`/api/tables/${number}/order`, { headers })).json();
  return order.lines.reduce((s: number, l: any) => s + l.quantity, 0) as number;
}

test('réponse perdue sur l\'envoi du ticket : le nouvel essai reprend la clé et n\'ajoute la ligne qu\'une fois', async ({ page }) => {
  await page.addInitScript(() => localStorage.setItem('pos_lang', 'fr'));
  await pairTill(page);
  await loginWithPin(page, '1234');
  const number = await openFreshTable(page);
  await page.click('#btnNavPos');
  await page.locator('.product-card').first().click();
  const modal = page.locator('#modifiersModal');
  if (await modal.evaluate((el: HTMLElement) => el.classList.contains('active'))) await page.click('#btnConfirmModifiers');
  await expect(page.locator('#cartItemsList .cart-item-row')).toHaveCount(1);

  // Le serveur ajoute la ligne, mais la réponse n'arrive jamais au navigateur.
  const keys: string[] = [];
  await page.route(ITEMS_URL, async route => {
    keys.push(route.request().headers()['idempotency-key']);
    if (keys.length === 1) {
      await route.fetch();
      return route.abort('failed');
    }
    return route.continue();
  });
  await page.click('#btnSendKitchen');
  await expect(page.locator('.toast.error').last()).toBeVisible();
  expect(await quantity(page, number)).toBe(1); // l'ajout a bien eu lieu côté serveur

  await page.click('#btnSendKitchen');
  await expect(page.locator('#floorPlanView')).toHaveClass(/active/);
  expect(keys).toHaveLength(2);
  expect(keys[0]).toMatch(/^[0-9a-f-]{36}$/);
  expect(keys[1]).toBe(keys[0]);
  expect(await quantity(page, number)).toBe(1); // rejeu : pas de ligne en double
  await page.unroute(ITEMS_URL);
});
