import { test, expect } from '@playwright/test';

test.describe('Encaissements', () => {

  async function ensureLoggedIn(page: any) {
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of ['1', '2', '3', '4']) await page.click(`.pin-keypad button[data-val="${d}"]`);
      await expect(pinModal).not.toHaveClass(/active/);
    }
  }

  test('affiche les tuiles, le graphique horaire, la bascule en tableau et le lien vers la fiscalité', async ({ page }) => {
    await ensureLoggedIn(page);
    await page.click('#btnNavTakings');
    await expect(page.locator('#takingsView')).toHaveClass(/active/);
    await expect(page.locator('#takingsBody .floor-stat')).toHaveCount(8);
    await expect(page.locator('#takingsBody svg[data-pts]')).toBeVisible();

    await page.click('[data-takings-table]');
    await expect(page.locator('#takingsBody table.menu-table').first()).toBeVisible();
    await page.click('[data-takings-table]');
    await expect(page.locator('#takingsBody svg[data-pts]')).toBeVisible();

    await page.click('[data-go-z]');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);
  });

  test('périodes, comparaison, cumul, détail, tickets et journal du jour', async ({ page }) => {
    await ensureLoggedIn(page);
    await page.click('#btnNavTakings');
    await expect(page.locator('#takingsBody .floor-stat')).toHaveCount(8);

    // Cartes : courbe, paiements, rapport Z, familles, plats, TVA, serveurs, tickets, journal.
    await expect(page.locator('#takingsBody .bk-card')).toHaveCount(9);
    await expect(page.locator('#takingsBody .tk-journal, #takingsBody .needs-empty').last()).toBeVisible();

    // Cumul : la courbe de la période apparaît, les barres disparaissent.
    await page.click('[data-takings-cumulative]');
    await expect(page.locator('#takingsBody path.tk-cur')).toHaveCount(1);
    await expect(page.locator('#takingsBody rect.tk-bar')).toHaveCount(0);
    await page.click('[data-takings-cumulative]');

    // 7 jours : courbe quotidienne (7 points), comparée à la période précédente.
    await page.click('#takingsPeriod [data-period="7"]');
    await expect(page.locator('#takingsPeriod [data-period="7"]')).toHaveClass(/active/);
    await expect(page.locator('#takingsBody .takings-h h3').first()).toHaveText(/jour|day|اليوم/i);
    await page.click('[data-takings-table]');
    await expect(page.locator('#takingsBody table.menu-table tbody tr').first()).toBeVisible();

    // Hier et une date précise.
    await page.click('#takingsPeriod [data-period="yesterday"]');
    await expect(page.locator('#takingsPeriod [data-period="yesterday"]')).toHaveClass(/active/);
    await page.fill('#takingsDate', '2026-10-01');
    await page.dispatchEvent('#takingsDate', 'change');
    await expect(page.locator('#takingsPeriod .active')).toHaveCount(0);

    // Comparaison forcée.
    await page.selectOption('#takingsCompare', 'previous');
    await expect(page.locator('#takingsCompare')).toHaveValue('previous');
  });
});
