import { test, expect, type Page } from '@playwright/test';
import { pairTill, managerToken } from './helpers/pairing';

test.describe('Alertes de pilotage', () => {
  test('quatre annulations pour un seuil de trois : une seule alerte, clôture avec note, aucun doublon', async ({ page }) => {
    await pairTill(page);
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of '1234') await page.click(`.pin-keypad button[data-val="${d}"]`);
      await expect(pinModal).not.toHaveClass(/active/);
    }
    const device = await page.evaluate(() => JSON.parse(localStorage.getItem('pos_device') || '{}'));
    const token = await managerToken(page.request);
    const headers = { Authorization: `Bearer ${token}`, 'X-Device-Token': device.token };
    await page.request.put('/api/alerts/rules/voids_count', { headers, data: { threshold: 3, enabled: true } });

    const numbers: string[] = [];
    for (let i = 0; i < 4; i++) {
      const sale = await page.request.post('/api/sync/receipt', { headers, data: { orderId: '00000000-0000-0000-0000-000000000000', tableNumber: 'ANN' + i, totalTtcCents: 1000, tenders: [{ method: 0, amountCents: 1000, tenderedCents: 1000 }] } });
      numbers.push((await sale.json()).receiptNumber);
    }
    const from = new Date(Date.now() - 3600_000).toISOString(), to = new Date(Date.now() + 3600_000).toISOString();
    const receipts = await (await page.request.get(`/api/dashboard/receipts?from=${from}&to=${to}`, { headers })).json();
    for (const n of numbers) {
      const id = receipts.find((r: any) => r.receiptNumber === n).id;
      const res = await page.request.post(`/api/checkout/void/${id}`, { headers, data: { terminalId: '', operatorId: '00000000-0000-0000-0000-000000000000', restock: false } });
      expect(res.ok()).toBeTruthy();
    }

    const open = async () => (await (await page.request.get('/api/alerts?state=open', { headers })).json()).filter((a: any) => a.terminalId === device.terminalId && a.ruleType === 'voids_count');
    expect(await open()).toHaveLength(1);
    // Rafraîchir / réévaluer ne duplique rien.
    await page.request.put('/api/alerts/rules/voids_count', { headers, data: { threshold: 3, enabled: true } });
    expect(await open()).toHaveLength(1);

    await page.reload();
    await page.click('button[data-view="adminView"]');
    await page.click('button[data-admin-tab="tabAlerts"]');
    const row = page.locator('#alertList .alert-row').filter({ hasText: device.terminalId });
    await expect(row).toContainText('4');
    await row.locator('[data-resolve]').click();
    await page.fill('#cashF_note', 'Erreurs de saisie vérifiées');
    await page.click('#cashDialog [data-act="ok"]');
    await expect(row).toHaveCount(0);
    expect(await open()).toHaveLength(0);
    await page.request.put('/api/alerts/rules/voids_count', { headers, data: { threshold: 3, enabled: false } });
  });
});
