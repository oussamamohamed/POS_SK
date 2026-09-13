import { test, expect } from '@playwright/test';

test.describe('Facturation sur Chambre d Hôtel (PMS & Signature Tactile)', () => {

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

  test('Doit imputer la note sur une chambre d hôtel avec signature tactile', async ({ page }) => {
    await ensureLoggedIn(page);

    await page.click('#btnNavPos');

    // Ensure item in cart
    const totalText = await page.locator('#summaryTtc').textContent();
    const currentTotal = parseFloat((totalText || '0').replace('€', '').trim());

    if (currentTotal <= 0) {
      const beerCard = page.locator('.product-card').filter({ hasText: 'Bière Artisanale' }).first();
      if (await beerCard.isVisible()) {
        await beerCard.click();
      } else {
        await page.locator('.product-card').first().click();
      }
    }

    await expect(page.locator('#summaryTtc')).not.toHaveText('0.00 €');

    // Open payment modal
    await page.click('#btnPayModal');
    const payModal = page.locator('#paymentModal');
    await expect(payModal).toHaveClass(/active/);

    // Open Hotel Room Charge modal
    await page.click('#btnOpenRoomChargeModal');
    await expect(payModal).not.toHaveClass(/active/);

    const roomModal = page.locator('#roomChargeModal');
    await expect(roomModal).toHaveClass(/active/);

    // Verify room selector has rooms
    const roomSelect = page.locator('#selectHotelRoom');
    await expect(roomSelect).toBeVisible();
    const roomOptions = page.locator('#selectHotelRoom option');
    await expect(roomOptions.first()).toBeAttached();

    // Select first room and verify guest details
    await roomSelect.selectOption({ index: 0 });
    await expect(page.locator('#roomGuestName')).not.toHaveText('-');

    // Sign on canvas
    const canvas = page.locator('#signatureCanvas');
    const box = await canvas.boundingBox();
    if (box) {
      await page.mouse.move(box.x + 20, box.y + 20);
      await page.mouse.down();
      await page.mouse.move(box.x + 100, box.y + 50);
      await page.mouse.move(box.x + 150, box.y + 30);
      await page.mouse.up();
    }

    // Test clear signature button
    await page.click('#btnClearSignature');

    // Draw signature again
    if (box) {
      await page.mouse.move(box.x + 30, box.y + 30);
      await page.mouse.down();
      await page.mouse.move(box.x + 80, box.y + 60);
      await page.mouse.move(box.x + 140, box.y + 40);
      await page.mouse.up();
    }

    // Submit room charge
    await page.click('#formRoomCharge button[type="submit"]');

    // Modal should close
    await expect(roomModal).not.toHaveClass(/active/);

    // Cart should reset to 0.00 €
    await expect(page.locator('#summaryTtc')).toHaveText('0.00 €');
  });
});
