import { test, expect } from '@playwright/test';

test.describe('Archives fiscales (NF525 US5)', () => {

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

  test('Doit afficher le panneau des archives et permettre création et vérification', async ({ page }) => {
    // Intercepter la liste des archives et clôtures
    await page.route('**/api/fiscal/period-closures*', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify([
          {
            id: '018f0000-0000-7000-8000-000000000001',
            terminalId: 'POS_MAIN_TERM',
            periodType: 'monthly',
            periodKey: '2026-08',
            closureSequence: 1,
            totalTtcCents: 15000,
            perpetualGrandTotalCents: 15000,
            createdAtUtc: '2026-09-01T00:00:00Z',
            signatureHash: 'abcdef0123456789abcdef0123456789'
          }
        ])
      });
    });

    await page.route('**/api/fiscal/archives', async route => {
      if (route.request().method() === 'GET') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify([
            {
              id: '018f0000-0000-7000-8000-000000000002',
              periodClosureId: '018f0000-0000-7000-8000-000000000099',
              periodType: 'monthly',
              periodKey: '2026-07',
              fileName: 'archive-POS_MAIN_TERM-M-2026-07.zip',
              fileSha256: 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855',
              fileSizeBytes: 2048,
              archiveSequence: 1,
              signatureHash: '0123456789abcdef0123456789abcdef',
              createdAtUtc: '2026-08-01T00:00:00Z'
            }
          ])
        });
      } else if (route.request().method() === 'POST') {
        await route.fulfill({
          status: 200,
          contentType: 'application/json',
          body: JSON.stringify({
            id: '018f0000-0000-7000-8000-000000000003',
            periodClosureId: '018f0000-0000-7000-8000-000000000001',
            fileName: 'archive-POS_MAIN_TERM-M-2026-08.zip',
            fileSha256: 'a1b2c3d4e5f60123456789abcdefa1b2c3d4e5f60123456789abcdefa1b2c3d4',
            fileSizeBytes: 4096,
            signatureHash: 'fedcba9876543210fedcba9876543210'
          })
        });
      } else {
        await route.continue();
      }
    });

    await ensureLoggedIn(page);

    // Naviguer vers l'écran fiscal
    await page.click('#btnNavFiscal');
    await expect(page.locator('#fiscalView')).toHaveClass(/active/);

    // Vérifier panneau des archives
    const archivesPanel = page.locator('#fiscalArchivesPanel');
    await expect(archivesPanel).toBeVisible();

    // Vérifier la liste des archives affichée
    const archivesTbody = page.locator('#archivesTableBody');
    await expect(archivesTbody).toContainText('archive-POS_MAIN_TERM-M-2026-07.zip');
    await expect(archivesTbody).toContainText('#1');

    // Vérifier le bouton Archiver sur la clôture non-archivée
    const createBtn = page.locator('.btn-create-archive[data-closure-id="018f0000-0000-7000-8000-000000000001"]');
    await expect(createBtn).toBeVisible();
    await createBtn.click();

    // 2. Tester la vérification d'archive
    const fileInput = page.locator('#archiveVerifyFileInput');
    const btnVerify = page.locator('#btnVerifyArchive');
    const alert = page.locator('#archiveVerifyAlert');

    // Cas A : Archive valide
    await page.route('**/api/fiscal/archives/verify', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          isValid: true,
          archiveId: '018f0000-0000-7000-8000-000000000002',
          reason: null
        })
      });
    });

    await fileInput.setInputFiles({
      name: 'archive-test.zip',
      mimeType: 'application/zip',
      buffer: Buffer.from('fake zip buffer')
    });
    await btnVerify.click();
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('valide et intègre');

    // Cas B : hash mismatch
    await page.route('**/api/fiscal/archives/verify', async route => {
      await route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          isValid: false,
          archiveId: '018f0000-0000-7000-8000-000000000002',
          reason: 'hash_mismatch'
        })
      });
    });

    await btnVerify.click();
    await expect(alert).toBeVisible();
    await expect(alert).toContainText('invalide');
  });
});
