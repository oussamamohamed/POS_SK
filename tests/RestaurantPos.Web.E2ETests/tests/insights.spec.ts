import { test, expect } from '@playwright/test';
import { pairTill, managerToken } from './helpers/pairing';

test.describe('Tendances du jour', () => {
  test('la page Encaissements affiche les cartes ou le message de données insuffisantes, cohérent avec l\'API', async ({ page }) => {
    await pairTill(page);
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of '1234') await page.click(`.pin-keypad button[data-val="${d}"]`);
      await expect(pinModal).not.toHaveClass(/active/);
    }
    const token = await managerToken(page.request);
    const api = await (await page.request.get(`/api/dashboard/insights?tzOffsetMinutes=${-new Date().getTimezoneOffset()}`, { headers: { Authorization: `Bearer ${token}` } })).json();

    await page.click('#btnNavTakings');
    await expect(page.locator('#insightsBox')).toBeVisible();
    if (api.insufficientData) await expect(page.locator('#insightsNote')).toContainText('3 jours');
    else await expect(page.locator('#insightsBox .insight-card, #insightsNote').first()).toBeVisible();

    // Réglage du seuil de tendance dans Gestion → Alertes.
    await page.click('button[data-view="adminView"]');
    await page.click('button[data-admin-tab="tabAlerts"]');
    await page.fill('#alertTrend', '20');
    await page.locator('#alertTrend').dispatchEvent('change');
    await expect.poll(async () => (await (await page.request.get('/api/alerts/settings', { headers: { Authorization: `Bearer ${token}` } })).json()).trendThresholdPercent).toBe(20);
    await page.fill('#alertTrend', '15');
    await page.locator('#alertTrend').dispatchEvent('change');
  });
});
