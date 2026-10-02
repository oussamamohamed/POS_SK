import { test, expect } from '@playwright/test';

test.describe('Duplicatas et réimpressions fiscales NF525', () => {

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

  test('Doit afficher la section réimpression de ticket et exécuter un duplicata', async ({ page }) => {
    let duplicateSeq = 0;
    await page.route('**/api/checkout/receipts/*/reprint', async route => {
      duplicateSeq++;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          printQueued: true,
          duplicateNumber: duplicateSeq
        })
      });
    });

    await ensureLoggedIn(page);

    // Naviguer vers Fiscalité
    await page.click('#btnNavFiscal');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);

    // Vérifier panneau réimpression
    const panel = page.locator('#fiscalReceiptReprintPanel');
    await expect(panel).toBeVisible();

    const input = page.locator('#reprintReceiptInput');
    const btnReprint = page.locator('#btnReprintReceipt');
    const resultBox = page.locator('#reprintReceiptResult');

    await expect(input).toBeVisible();
    await expect(btnReprint).toBeVisible();

    // Saisir un reçu et réimprimer (duplicata 1)
    await input.fill('POS_MAIN_TERM-000042');
    await btnReprint.click();

    await expect(resultBox).toBeVisible();
    await expect(resultBox).toContainText('DUPLICATA n°1');

    // Réimprimer à nouveau (duplicata 2)
    await btnReprint.click();
    await expect(resultBox).toContainText('DUPLICATA n°2');
  });

  test('Doit afficher la bannière DUPLICATA lors de la réimpression de la dernière clôture', async ({ page }) => {
    let closureDuplicateSeq = 0;
    await page.route('**/api/fiscal/latest-closure/print*', async route => {
      closureDuplicateSeq++;
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          printQueued: true,
          duplicateNumber: closureDuplicateSeq
        })
      });
    });

    await ensureLoggedIn(page);

    // Naviguer vers Fiscalité
    await page.click('#btnNavFiscal');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);

    const btnReprintZ = page.locator('#btnReprintZ');
    await expect(btnReprintZ).toBeVisible();

    const banner = page.locator('#slipDuplicateBanner');
    await expect(banner).toBeHidden();

    // 1ère réimpression
    await btnReprintZ.click();
    await expect(banner).toBeVisible();
    await expect(banner).toContainText('DUPLICATA n°1');

    // 2ème réimpression
    await btnReprintZ.click();
    await expect(banner).toContainText('DUPLICATA n°2');
  });
});
