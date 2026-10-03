import { test, expect } from '@playwright/test';

test.describe('Vérification d intégrité des chaînes fiscales NF525', () => {

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

  test('Doit afficher le bouton de vérification et exécuter le contrôle d intégrité', async ({ page }) => {
    await ensureLoggedIn(page);

    // Naviguer vers l'onglet Fiscalité
    await page.click('#btnNavFiscal');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);

    // Vérifier la présence du bouton
    const btnVerify = page.locator('#btnVerifyChains');
    await expect(btnVerify).toBeVisible();

    // Cliquer sur le bouton de vérification
    await btnVerify.click();

    // Vérifier l'apparition du panneau de vérification
    const panel = page.locator('#fiscalVerificationPanel');
    await expect(panel).toBeVisible();

    // Vérifier le résumé et le tableau
    const summary = page.locator('#fiscalVerificationSummary');
    await expect(summary).toBeVisible();

    const tbody = page.locator('#fiscalChainsTableBody');
    await expect(tbody.locator('tr')).not.toHaveCount(0);
  });
});
