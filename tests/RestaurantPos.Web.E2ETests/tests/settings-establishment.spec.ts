import { test, expect } from '@playwright/test';

test.describe('Paramétrage identité établissement & exercice fiscal NF525', () => {

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

  test('Doit afficher le formulaire établissement, enregistrer et mettre à jour le badge de certification', async ({ page }) => {
    await ensureLoggedIn(page);

    // Naviguer vers l'écran Fiscalité d'abord pour vérifier le badge
    await page.click('#btnNavFiscal');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);
    const certBadge = page.locator('#fiscalCertBadge');
    await expect(certBadge).toBeVisible();

    // Naviguer vers l'onglet Paramétrage
    await page.click('button[data-view="adminView"]');
    await expect(page.locator('#adminView')).toHaveClass(/active/);

    // Cliquer sur l'onglet Établissement & Exercice
    const tabBtn = page.locator('button[data-admin-tab="tabEstablishment"]');
    await expect(tabBtn).toBeVisible();
    await tabBtn.click();
    await expect(page.locator('#tabEstablishment')).toHaveClass(/active/);

    // Renseigner les champs
    const companyInput = page.locator('#settingsCompanyName');
    await expect(companyInput).toBeVisible();
    await companyInput.fill('Restaurant Test NF525');

    const siretInput = page.locator('#settingsSiret');
    await siretInput.fill('88877766600012');

    const vatInput = page.locator('#settingsVatNumber');
    await vatInput.fill('FR12888777666');

    const certInput = page.locator('#settingsCertificateNumber');
    await certInput.fill('INFOCERT-2026-TEST');

    // Sauvegarder
    await page.click('#btnSaveEstablishment');

    // Revenir sur l'écran fiscal pour vérifier la mise à jour du certificat
    await page.click('#btnNavFiscal');
    await expect(certBadge).toContainText('INFOCERT-2026-TEST');
  });
});
