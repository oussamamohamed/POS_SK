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
    await page.locator('#languageSelect').selectOption('ar');
    await page.waitForLoadState('load');
    await expect(page.locator('html')).toHaveAttribute('dir', 'rtl');
    await page.click('#btnLockTerminal');
    await expect(page.locator('#pinLockModal h3')).toHaveText('الصندوق مقفل');
    const keypad = page.locator('.pin-keypad');
    await expect(keypad).toHaveAttribute('dir', 'ltr');
    const one = await keypad.locator('[data-val="1"]').boundingBox();
    const three = await keypad.locator('[data-val="3"]').boundingBox();
    expect(one!.x).toBeLessThan(three!.x);
  });

  test('le choix est mémorisé et envoyé au serveur', async ({ page }) => {
    await page.goto('/');
    await page.click('#btnLockTerminal');
    await page.locator('#languageSelect').selectOption('en');
    await page.waitForLoadState('load');
    const request = page.waitForRequest(r => r.url().includes('/api/'));
    await page.reload();
    expect((await request).headers()['accept-language']).toBe('en');
  });
});
