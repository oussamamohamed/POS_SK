import { test, expect } from '@playwright/test';

test.describe('Plan de Salle & Rappel de Table Tactile', () => {

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

  test('Doit naviguer vers le Plan de Salle et afficher les tables', async ({ page }) => {
    await ensureLoggedIn(page);

    // Navigate to Plan de Salle
    await page.click('#btnNavFloor');
    await expect(page.locator('#floorPlanView')).toHaveClass(/active/);

    // Check table cards
    const tableCards = page.locator('#floorTablesGrid .table-card');
    await expect(tableCards.first()).toBeVisible();
    const count = await tableCards.count();
    expect(count).toBeGreaterThan(0);
  });

  test('Doit sélectionner une table et revenir à la caisse avec le badge actif', async ({ page }) => {
    await ensureLoggedIn(page);

    await page.click('#btnNavFloor');
    await expect(page.locator('#floorPlanView')).toHaveClass(/active/);

    // Click on Table T1
    const t1Card = page.locator('#floorTablesGrid .table-card').filter({ hasText: 'T1' }).first();
    if (await t1Card.isVisible()) {
      await t1Card.click();
    } else {
      await page.locator('#floorTablesGrid .table-card').first().click();
    }

    // Should switch back to posView
    await expect(page.locator('#posView')).toHaveClass(/active/);
    await expect(page.locator('#activeTableBadge')).toBeVisible();
  });
});
