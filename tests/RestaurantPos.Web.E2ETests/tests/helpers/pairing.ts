import { expect, type APIRequestContext, type Page } from '@playwright/test';

/** Jeton gérant (PIN 9999) pour les routes du back-office. */
export async function managerToken(request: APIRequestContext): Promise<string> {
  const res = await request.post('/api/auth/login', { data: { pin: '9999' } });
  expect(res.ok()).toBeTruthy();
  return (await res.json()).token;
}

export async function createPairingCode(request: APIRequestContext, name: string): Promise<string> {
  const token = await managerToken(request);
  const res = await request.post('/api/devices/pairing-codes', {
    data: { name, role: 'Caisse' },
    headers: { Authorization: `Bearer ${token}` },
  });
  expect(res.ok()).toBeTruthy();
  return (await res.json()).code;
}

let cachedDevice: { token: string; terminalId: string; name: string } | null = null;

/** Appaire ce navigateur de test une fois par exécution, avant le premier chargement de page. */
export async function pairTill(page: Page) {
  if (!cachedDevice) {
    const code = await createPairingCode(page.request, 'Caisse E2E');
    const res = await page.request.post('/api/devices/pair', { data: { code } });
    expect(res.ok()).toBeTruthy();
    const body = await res.json();
    cachedDevice = { token: body.token, terminalId: body.terminalId, name: body.name };
  }
  await page.addInitScript((device) => localStorage.setItem('pos_device', JSON.stringify(device)), cachedDevice);
}

/**
 * Encaisse en espèces les commandes de table laissées ouvertes par d'autres specs :
 * la clôture Z les refuse (409 open_orders) et aucune route ne permet de les annuler.
 */
export async function settleOpenTableOrders(request: APIRequestContext, headers: Record<string, string>) {
  const tables = await (await request.get('/api/tables', { headers })).json();
  for (const t of tables.filter((x: { status: number }) => x.status !== 0)) {
    const res = await request.get(`/api/tables/${encodeURIComponent(t.tableNumber)}/order`, { headers });
    if (!res.ok()) continue;
    const order = await res.json();
    if (!order.lines?.length) continue;
    // Commande en partie réglée (part annulée) : seul le reste dû est encaissable.
    const due = order.amountDueTtc ?? order.totalTtcAmount;
    if (!(due > 0)) continue;
    await request.post('/api/checkout/pay', {
      headers,
      data: {
        orderId: order.orderId,
        tableNumber: t.tableNumber,
        tenders: [{ method: 0, amount: due, tendered: due, changeGiven: 0 }],
      },
    });
  }
}

export async function loginWithPin(page: Page) {
  await page.goto('/?nocache=' + Date.now());
  await page.waitForLoadState('domcontentloaded');
  const pinModal = page.locator('#pinLockModal');
  if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
    await page.click('#btnPinClear');
    for (const d of '1234') await page.click(`.pin-keypad button[data-val="${d}"]`);
    await expect(pinModal).not.toHaveClass(/active/);
  }
}
