import { test, expect } from '@playwright/test';
import { pairTill, managerToken } from './helpers/pairing';

test.describe('Résumé du jour', () => {
  test('le résumé reprend les totaux du dernier Z et se consulte depuis Encaissements', async ({ page }) => {
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
    await page.request.post('/api/sync/receipt', { headers, data: { orderId: '00000000-0000-0000-0000-000000000000', tableNumber: 'RES', totalTtcCents: 4200, tenders: [{ method: 0, amountCents: 4200, tenderedCents: 4200 }] } });
    const z = await page.request.post('/api/fiscal/z-closure', { headers, data: { terminalId: device.terminalId, managerId: '00000000-0000-0000-0000-000000000001', managerName: 'Gérant' } });
    expect(z.ok()).toBeTruthy();
    const latest = await (await page.request.get(`/api/fiscal/latest-closure?terminalId=${device.terminalId}`, { headers })).json();

    await page.click('#btnNavTakings');
    await page.click('#openDailySummary');
    const card = page.locator(`.summary-terminal[data-terminal="${device.terminalId}"]`);
    await expect(card).toBeVisible();
    await expect(card.locator('.summary-ttc')).toContainText(latest.totalSalesTtc.toFixed(2));
    await expect(page.locator('#dailySummaryModal [data-act="print"]')).toBeVisible();
  });
});
