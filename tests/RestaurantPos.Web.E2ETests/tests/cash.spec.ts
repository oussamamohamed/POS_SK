import { test, expect, type Page } from '@playwright/test';
import { pairTill, managerToken } from './helpers/pairing';

test.describe('Tiroir-caisse', () => {
  async function openCash(page: Page) {
    await pairTill(page);
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of '1234') await page.click(`.pin-keypad button[data-val="${d}"]`);
      await expect(pinModal).not.toHaveClass(/active/);
    }
    await page.click('#btnNavCash');
  }

  async function keys(page: Page, text: string) {
    for (const k of text) await page.locator(`#cashDialog .stock-keypad [data-k="${k}"]`).click();
  }

  test('fond 100 €, encaissement 50 € avec rendu, sortie 12 €, comptage par coupures et écart conservé', async ({ page }) => {
    await openCash(page);
    await page.click('#cashOpen');
    await keys(page, '100');
    await page.click('#cashDialog [data-act="ok"]');
    await expect(page.locator('#cashExpected')).toContainText('100.00');

    // Encaissement espèces de 50 € avec un billet de 60 € (10 € rendus) sur le poste de ce navigateur.
    const device = await page.evaluate(() => JSON.parse(localStorage.getItem('pos_device') || '{}'));
    const token = await managerToken(page.request);
    const sale = await page.request.post('/api/sync/receipt', {
      headers: { Authorization: `Bearer ${token}`, 'X-Device-Token': device.token },
      data: { orderId: '00000000-0000-0000-0000-000000000000', tableNumber: 'CAISSE', totalTtcCents: 5000, tenders: [{ method: 0, amountCents: 5000, tenderedCents: 6000 }] },
    });
    expect(sale.ok()).toBeTruthy();

    await page.click('#cashOut');
    await keys(page, '12');
    await page.fill('#cashF_reason', 'Achat de glace');
    await page.click('#cashDialog [data-act="ok"]');
    await page.click('#cashRefresh');
    await expect(page.locator('#cashExpected')).toContainText('138.00'); // 100 + 50 − 12

    await page.click('#cashCount');
    for (const d of [10000, 2000, 1000, 500, 100]) await page.locator(`.cash-denom[data-d="${d}"] [data-step="1"]`).click();
    await expect(page.locator('#cashCountedTotal')).toContainText('136.00');
    await page.click('#cashDialog [data-act="ok"]');

    await expect(page.locator('#cashBody')).toContainText('Aucune session ouverte');
    await expect(page.locator('#cashBody table')).toContainText('-2.00');
  });
});
