import { test, expect } from '@playwright/test';

test.describe('Authentification Tactile & Verrouillage Caisse (PIN)', () => {

  test.beforeEach(async ({ page }) => {
    // Navigate with nocache parameter
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
  });

  test('Doit afficher l écran de verrouillage PIN et rejeter un code invalide', async ({ page }) => {
    const pinModal = page.locator('#pinLockModal');

    // If modal is not active initially, lock it
    if (!(await pinModal.evaluate(el => el.classList.contains('active')))) {
      await page.click('#btnLockTerminal');
      await expect(pinModal).toHaveClass(/active/);
    }

    // Enter wrong PIN: 0000
    for (let i = 0; i < 4; i++) {
      await page.click('.pin-keypad button[data-val="0"]');
    }

    // Verify error toast or that modal stays active
    await expect(pinModal).toHaveClass(/active/);
  });

  test('Doit déverrouiller la caisse avec le code PIN valide 1234', async ({ page }) => {
    const pinModal = page.locator('#pinLockModal');

    if (await pinModal.evaluate(el => el.classList.contains('active'))) {
      // Clear any partial input
      await page.click('#btnPinClear');

      // Type PIN 1234
      await page.click('.pin-keypad button[data-val="1"]');
      await page.click('.pin-keypad button[data-val="2"]');
      await page.click('.pin-keypad button[data-val="3"]');
      await page.click('.pin-keypad button[data-val="4"]');
    }

    // Modal should close and operator badge should be visible
    await expect(pinModal).not.toHaveClass(/active/);
    await expect(page.locator('#currentOperatorName')).toContainText('Alexandre');
  });

  test('Doit pouvoir verrouiller et déverrouiller à nouveau', async ({ page }) => {
    const pinModal = page.locator('#pinLockModal');

    // Unlock if locked
    if (await pinModal.evaluate(el => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      await page.click('.pin-keypad button[data-val="1"]');
      await page.click('.pin-keypad button[data-val="2"]');
      await page.click('.pin-keypad button[data-val="3"]');
      await page.click('.pin-keypad button[data-val="4"]');
      await expect(pinModal).not.toHaveClass(/active/);
    }

    // Click lock button
    await page.click('#btnLockTerminal');
    await expect(pinModal).toHaveClass(/active/);

    // Re-unlock
    await page.click('.pin-keypad button[data-val="1"]');
    await page.click('.pin-keypad button[data-val="2"]');
    await page.click('.pin-keypad button[data-val="3"]');
    await page.click('.pin-keypad button[data-val="4"]');
    await expect(pinModal).not.toHaveClass(/active/);
  });

  test('no implicit login at startup', async ({ page }) => {
    const logins: string[] = [];
    page.on('request', r => { if (r.method() === 'POST' && r.url().includes('/api/auth/login')) logins.push(r.url()); });
    await page.evaluate(() => localStorage.removeItem('pos_jwt_token'));
    await page.reload();
    await expect(page.locator('#pinLockModal')).toHaveClass(/active/);
    await page.waitForTimeout(1000);
    expect(logins).toEqual([]);
  });

  test('lock clears the session', async ({ page }) => {
    const pinModal = page.locator('#pinLockModal');
    await expect(pinModal).toHaveClass(/active/);
    for (const d of ['1', '2', '3', '4']) await page.click(`.pin-keypad button[data-val="${d}"]`);
    await expect(pinModal).not.toHaveClass(/active/);
    await page.click('#btnLockTerminal');
    await expect(pinModal).toHaveClass(/active/);
    expect(await page.evaluate(() => localStorage.getItem('pos_jwt_token'))).toBeNull();
    // Le cookie HttpOnly est effacé par le serveur : la requête de déconnexion est asynchrone.
    await expect.poll(() => page.evaluate(async () => (await fetch('/api/auth/me')).status)).toBe(401);
  });

  test('jwt never leaves the origin', async ({ page }) => {
    await page.evaluate(() => localStorage.setItem('pos_jwt_token', 'fake.jwt.token'));
    let auth: string | undefined = 'unset';
    await page.route('http://example.invalid/**', route => {
      auth = route.request().headers()['authorization'];
      return route.fulfill({ status: 200, body: '{}', headers: { 'access-control-allow-origin': '*', 'access-control-allow-headers': '*' } });
    });
    await page.evaluate(() => fetch('http://example.invalid/api/health').catch(() => null));
    expect(auth).toBeUndefined();
  });

  test('PIN at startup opens the counter order once and loads protected data', async ({ page }) => {
    const counterOpens: string[] = [];
    page.on('request', r => { if (r.url().includes('/api/orders/counter/direct')) counterOpens.push(r.postData() ?? ''); });
    // Catalogue retardé : init() est encore en vol quand le PIN est validé.
    await page.route('**/api/catalog/products', async route => {
      await new Promise(r => setTimeout(r, 1500));
      await route.continue();
    });
    await page.evaluate(() => localStorage.removeItem('pos_jwt_token'));
    await page.reload();
    for (const d of ['1', '2', '3', '4']) await page.click(`.pin-keypad button[data-val="${d}"]`);
    await expect(page.locator('#pinLockModal')).not.toHaveClass(/active/);
    // plan de salle (GET /api/tables protégé) rempli sans rechargement
    await expect(page.locator('#floorTablesGrid > *').first()).toBeAttached();
    await page.waitForTimeout(2500);
    expect(counterOpens).toHaveLength(1);
    expect(JSON.parse(counterOpens[0]).destination).toBe(0); // À emporter (Takeaway = 0), destination conservée
    await expect(page.locator('#btnDestTakeaway')).toHaveClass(/active/);
  });

  test('a late 401 from a request sent without token keeps the new token', async ({ page }) => {
    await page.route('**/api/orders/counter/held', async route => {
      await new Promise(r => setTimeout(r, 1500));
      await route.fulfill({ status: 401, body: '' });
    });
    await page.evaluate(() => localStorage.removeItem('pos_jwt_token'));
    await page.reload();
    // PIN validé pendant que la requête sans jeton est en vol, puis 401 tardif
    for (const d of ['1', '2', '3', '4']) await page.click(`.pin-keypad button[data-val="${d}"]`);
    await expect(page.locator('#pinLockModal')).not.toHaveClass(/active/);
    await page.waitForTimeout(2500);
    expect(await page.evaluate(() => localStorage.getItem('pos_jwt_token'))).not.toBeNull();
    await expect(page.locator('#pinLockModal')).not.toHaveClass(/active/);
  });
});
