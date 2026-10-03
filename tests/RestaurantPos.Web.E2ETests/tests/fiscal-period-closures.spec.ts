import { test, expect } from '@playwright/test';

test.describe('Clôtures de période (Mois & Année NF525)', () => {

  async function ensureLoggedIn(page: any) {
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');

    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      await page.click('.pin-keypad button[data-val="1"]');
      await page.click('.pin-keypad button[data-val="2"]');
      await page.click('.pin-keypad button[data-val="3"]');
      await page.click('.pin-keypad button[data-val="4"]');
      await expect(pinModal).not.toHaveClass(/active/);
    }
  }

  test('Doit afficher le panneau des clôtures de période et gérer les erreurs 409 et succès', async ({ page }) => {
    await ensureLoggedIn(page);

    // Naviguer vers l'onglet Fiscalité
    await page.click('#btnNavFiscal');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);

    // Vérifier les composants du panneau
    const panel = page.locator('#fiscalPeriodClosuresPanel');
    await expect(panel).toBeVisible();

    const periodType = page.locator('#periodClosureType');
    const periodKey = page.locator('#periodClosureKey');
    const btnExecute = page.locator('#btnExecutePeriodClosure');
    const alert = page.locator('#periodClosureAlert');
    const tbody = page.locator('#periodClosuresTableBody');

    await expect(periodType).toBeVisible();
    await expect(periodKey).toBeVisible();
    await expect(btnExecute).toBeVisible();

    // 1. Simuler une erreur 409 : period_not_ended
    await page.route('**/api/fiscal/period-closures', async route => {
      if (route.request().method() === 'POST') {
        await route.fulfill({
          status: 409,
          contentType: 'application/json',
          body: JSON.stringify({
            code: 'period_not_ended',
            message: 'Period not ended yet.'
          })
        });
      } else {
        await route.continue();
      }
    });

    await periodKey.fill('2099-12');
    await btnExecute.click();
    await expect(alert).toBeVisible();
    await expect(alert).toContainText("n'est pas encore terminée");

    // 2. Simuler une erreur 409 : missing_daily_closures avec liste de jours
    await page.unroute('**/api/fiscal/period-closures');
    await page.route('**/api/fiscal/period-closures', async route => {
      if (route.request().method() === 'POST') {
        await route.fulfill({
          status: 409,
          contentType: 'application/json',
          body: JSON.stringify({
            code: 'missing_daily_closures',
            message: 'Missing daily closures.',
            days: ['2026-09-02', '2026-09-03']
          })
        });
      } else {
        await route.continue();
      }
    });

    await periodKey.fill('2026-09');
    await btnExecute.click();
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('2026-09-02');
    await expect(alert).toContainText('2026-09-03');

    // 3. Simuler un succès 200 avec rechargement de la liste
    await page.unroute('**/api/fiscal/period-closures');
    await page.route('**/api/fiscal/period-closures*', async route => {
      if (route.request().method() === 'POST') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: 'f9e0a0a0-0000-0000-0000-000000000001',
            terminalId: 'POS_MAIN_TERM',
            periodType: 'monthly',
            periodKey: '2026-08',
            closureSequence: 1,
            totalTtcCents: 15450,
            totalHtCents: 14045,
            perpetualGrandTotalCents: 15450,
            dailyClosureCount: 31,
            signatureHash: 'abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789',
            createdAtUtc: new Date().toISOString(),
            printQueued: true
          })
        });
      } else if (route.request().method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([
            {
              id: 'f9e0a0a0-0000-0000-0000-000000000001',
              terminalId: 'POS_MAIN_TERM',
              periodType: 'monthly',
              periodKey: '2026-08',
              closureSequence: 1,
              totalTtcCents: 15450,
              totalHtCents: 14045,
              perpetualGrandTotalCents: 15450,
              dailyClosureCount: 31,
              signatureHash: 'abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789',
              createdAtUtc: new Date().toISOString()
            }
          ])
        });
      } else {
        await route.continue();
      }
    });

    await periodKey.fill('2026-08');
    await btnExecute.click();
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('succès');
    await expect(tbody.locator('tr')).toHaveCount(1);
    await expect(tbody).toContainText('2026-08');
    await expect(tbody).toContainText('#1');
    await expect(tbody).toContainText('154.50 €');
  });
});
