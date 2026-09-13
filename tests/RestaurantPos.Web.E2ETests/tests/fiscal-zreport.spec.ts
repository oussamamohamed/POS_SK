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
