import { test, expect } from '@playwright/test';

test.describe('Fiscalité NF525, Clôtures & Export FEC', () => {

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

  test('le rapport X porte sur le terminal de l\'appareil appairé, sinon sur POS_MAIN_TERM', async ({ page }) => {
    const asked: string[] = [];
    await page.route('**/api/fiscal/x-report?**', route => {
      asked.push(new URL(route.request().url()).searchParams.get('terminalId') ?? '');
      return route.continue();
    });

    // Sans appareil appairé : l'ancien poste principal.
    await ensureLoggedIn(page);
    await page.click('#btnNavFiscal');
    await page.click('#btnPreviewX');
    await expect.poll(() => asked.at(-1)).toBe('POS_MAIN_TERM');

    // Appareil appairé T77 : les reçus s'écrivent sous T77, c'est donc son rapport X qu'il faut lire.
    await page.evaluate(() => localStorage.setItem('pos_device', JSON.stringify({ token: 'x', terminalId: 'T77', name: 'Caisse test' })));
    await ensureLoggedIn(page);
    await page.click('#btnNavFiscal');
    await page.click('#btnPreviewX');
    await expect.poll(() => asked.at(-1)).toBe('T77');
    await page.evaluate(() => localStorage.removeItem('pos_device'));
  });

  test('Doit naviguer vers l onglet Fiscalité NF525 et afficher les sections légales', async ({ page }) => {
    await ensureLoggedIn(page);

    // Click Fiscal tab
    await page.click('#btnNavFiscal');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);

    // Verify sections exist
    await expect(page.locator('#btnPreviewX')).toBeVisible();
    await expect(page.locator('#btnExecuteZ')).toBeVisible();
    await expect(page.locator('#btnExportFec')).toBeVisible();
    await expect(page.locator('#fiscalReportDisplay')).toBeVisible();
  });

  test('Doit afficher l aperçu du Rapport X en direct', async ({ page }) => {
    await ensureLoggedIn(page);

    await page.click('#btnNavFiscal');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);

    // Click Aperçu Rapport X
    await page.click('#btnPreviewX');

    // Verify slip title or tag
    await expect(page.locator('#slipTitle')).toContainText('RAPPORT');
    await expect(page.locator('#slipTerminal')).toContainText('POS_MAIN_TERM');
  });

  test('Doit afficher le formulaire de téléchargement FEC', async ({ page }) => {
    await ensureLoggedIn(page);

    await page.click('#btnNavFiscal');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);

    await expect(page.locator('#fecStartDate')).toBeVisible();
    await expect(page.locator('#fecEndDate')).toBeVisible();
    await expect(page.locator('#fecSiren')).toHaveValue('123456789');
    await expect(page.locator('#btnExportFec')).toBeVisible();
  });
});
