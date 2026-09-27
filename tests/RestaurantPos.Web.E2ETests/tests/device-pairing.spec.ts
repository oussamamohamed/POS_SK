import { test, expect } from '@playwright/test';
import { createPairingCode, loginWithPin, managerToken } from './helpers/pairing';

test.describe('Appairage des postes', () => {
  test('un poste non appairé ouvre la modale et se couple avec un code', async ({ page, request }) => {
    await page.addInitScript(() => localStorage.removeItem('pos_device'));
    await loginWithPin(page);

    const status = await page.evaluate(async () => {
      const res = await fetch('/api/checkout/pay', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ orderId: '00000000-0000-0000-0000-000000000000', tableNumber: 'Comptoir', tenders: [] }),
      });
      return res.status;
    });
    expect(status).toBe(401);
    await expect(page.locator('#devicePairingModal')).toHaveClass(/active/);
    await expect(page.locator('#pinLockModal')).not.toHaveClass(/active/);

    await page.fill('#devicePairingCodeInput', 'ZZZZZZZZ');
    await page.click('#btnConfirmDevicePairing');
    await expect(page.locator('#devicePairingError')).toContainText('invalide');

    const code = await createPairingCode(request, 'Caisse E2E modale');
    await page.fill('#devicePairingCodeInput', ` ${code.toLowerCase()} `);
    await page.click('#btnConfirmDevicePairing');
    await expect(page.locator('#devicePairingModal')).not.toHaveClass(/active/);
    const stored = await page.evaluate(() => JSON.parse(localStorage.getItem('pos_device') || 'null'));
    expect(stored.terminalId).toMatch(/^T\d+$/);
  });

  test('le back-office génère un code avec QR et révoque un appareil', async ({ page }) => {
    await loginWithPin(page);
    await page.click('#btnNavAdmin');
    await page.click('.admin-tab-btn[data-admin-tab="tabDevices"]');

    await page.fill('#inputDeviceName', 'iPad bar');
    await page.selectOption('#selectDeviceRole', 'Serveur');
    await page.click('#formDevicePairingCode button[type="submit"]');
    await expect(page.locator('#devicePairingCode')).toHaveText(/^[A-Z2-9]{8}$/);
    await expect(page.locator('#devicePairingQr')).toHaveAttribute('src', /^data:image\/png;base64,/);
    await expect(page.locator('#devicePairingExpiry')).toContainText('Expire dans');

    const code = (await page.locator('#devicePairingCode').textContent())!;
    const paired = await (await page.request.post('/api/devices/pair', { data: { code } })).json();
    await page.click('.admin-tab-btn[data-admin-tab="tabDevices"]');
    const row = page.locator(`.item-list-row[data-device-id="${paired.deviceId}"]`);
    await expect(row).toContainText('iPad bar');

    page.once('dialog', (dialog) => dialog.accept());
    await row.locator('.btn-revoke-device').click();
    await expect(row).toContainText('Révoqué');
  });

  test('device names are rendered as text', async ({ page, request }) => {
    const token = await managerToken(request);
    const hostile = '<img src=x onerror="window.__xss=1">';
    const created = await request.post('/api/devices/pairing-codes', {
      data: { name: hostile, role: 'Caisse' },
      headers: { Authorization: `Bearer ${token}` },
    });
    const { code } = await created.json();
    await request.post('/api/devices/pair', { data: { code } });

    await loginWithPin(page);
    await page.click('#btnNavAdmin');
    await page.click('.admin-tab-btn[data-admin-tab="tabDevices"]');
    await expect(page.locator('#adminDevicesList')).toContainText(hostile);
    expect(await page.evaluate(() => (window as any).__xss)).toBeUndefined();
  });
});
