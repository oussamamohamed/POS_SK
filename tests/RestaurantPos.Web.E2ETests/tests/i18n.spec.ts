import { test, expect } from '@playwright/test';

test.describe('Langue de l\'interface', () => {
  test('anglais par défaut quand le navigateur est en allemand', async ({ browser, baseURL }) => {
    const context = await browser.newContext({ locale: 'de-DE' });
    const page = await context.newPage();
    await page.goto(baseURL!);
    await expect(page.locator('html')).toHaveAttribute('lang', 'en');
    await expect(page.locator('#pinLockModal h3')).toHaveText('Terminal locked');
    await context.close();
  });

  test('français quand le navigateur est en français', async ({ page }) => {
    await page.goto('/');
    await expect(page.locator('#pinLockModal h3')).toHaveText('Caisse verrouillée');
  });

  test('arabe : RTL, libellé arabe, pavé PIN gauche-droite', async ({ page }) => {
    // Le sélecteur de langue vit dans le modal PIN : on verrouille pour le rendre visible
    // (le terminal s'authentifie automatiquement au démarrage en environnement de démo).
    await page.goto('/');
    await page.click('#btnLockTerminal');
    // setLanguage() recharge la page : attendre CE rechargement (waitForLoadState rend la main tout de suite).
    const reloaded = page.waitForEvent('load');
    await page.locator('#languageSelect').selectOption('ar');
    await reloaded;
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.click('#btnLockTerminal');
    await expect(page.locator('#pinLockModal h3')).toHaveText('الصندوق مقفل');
    const keypad = page.locator('.pin-keypad');
    await expect(keypad).toHaveAttribute('dir', 'ltr');
    const one = await keypad.locator('[data-val="1"]').boundingBox();
    const three = await keypad.locator('[data-val="3"]').boundingBox();
    expect(one!.x).toBeLessThan(three!.x);
  });

  test('arabe : montants gauche-droite, icônes de pagination inversées, en-tête miroir', async ({ page }) => {
    // Note : contrairement au brouillon de la brief, la démo se connecte automatiquement
    // (PIN 1234) au chargement (cf. Task 5) : le modal PIN n'apparaît pas tant qu'on ne
    // verrouille pas explicitement. On teste donc directement l'UI principale déjà connectée.
    await page.goto('/');
    await page.evaluate(() => localStorage.setItem('pos_lang', 'ar'));
    await page.reload();
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await expect(page.locator('#pinLockModal')).not.toHaveClass(/active/);

    // Montants : toujours gauche-droite malgré la page RTL.
    const amounts = page.locator('.cart-item-price, .total-amount, .table-total, #roomChargeTotalAmount, .product-price, #summaryHt, #summaryVat');
    const count = await amounts.count();
    expect(count).toBeGreaterThan(0);
    for (let i = 0; i < Math.min(count, 5); i++) {
      await expect(amounts.nth(i)).toHaveCSS('direction', 'ltr');
    }

    // Icônes de pagination du grid produits : miroir en RTL (la barre n'est affichée
    // que s'il y a plusieurs pages ; on force sa visibilité pour vérifier la règle CSS).
    await page.evaluate(() => { document.getElementById('gridPaginationBar')!.style.display = 'flex'; });
    const prevIcon = page.locator('#btnPrevGridPage .icon-directional');
    await expect(prevIcon).toHaveCSS('transform', 'matrix(-1, 0, 0, 1, 0, 0)');

    // En-tête : le bloc marque (logo) passe à droite en RTL (miroir du flex natif).
    const header = await page.locator('.app-header').boundingBox();
    const brand = await page.locator('.brand-section').boundingBox();
    expect(brand!.x + brand!.width).toBeGreaterThan(header!.x + header!.width / 2);
  });

  test('arabe : partage d\'addition, seul le montant reste gauche-droite', async ({ page }) => {
    await page.goto('/');
    await page.evaluate(() => localStorage.setItem('pos_lang', 'ar'));
    await page.reload();
    await page.click('#btnNavPos');
    await page.locator('.product-card').filter({ hasText: 'Bière Artisanale' }).first().click();
    await expect(page.locator('#summaryTtc')).not.toHaveText('0.00 €');
    await page.click('#btnSplitBill');
    await page.click('#btnConfirmSplit');

    // La phrase suit la page (RTL) ; le montant et son symbole € sont isolés en LTR.
    const remaining = page.locator('#payRemainingAmount');
    await expect(remaining).toContainText('الحصة 1/2');
    await expect(remaining).toHaveCSS('direction', 'rtl');
    await expect(remaining.locator('bdi')).toHaveCSS('direction', 'ltr');
    await expect(remaining.locator('bdi')).toHaveText(/^\d+\.\d{2} €$/);
  });

  test('le choix est mémorisé et envoyé au serveur', async ({ page }) => {
    await page.goto('/');
    await page.click('#btnLockTerminal');
    // setLanguage() recharge la page : attendre CE rechargement (waitForLoadState rend la main tout de suite).
    const reloaded = page.waitForEvent('load');
    await page.locator('#languageSelect').selectOption('en');
    await reloaded;
    const request = page.waitForRequest(r => r.url().includes('/api/'));
    await page.reload();
    expect((await request).headers()['accept-language']).toBe('en');
  });
});
