import { test, expect } from '@playwright/test';

test.describe('À traiter (cloche et pastilles)', () => {

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

  test('une attente dépassée remonte dans la cloche et la pastille Réservations, et le clic ouvre la vue', async ({ page, request }) => {
    const login = await request.post('/api/auth/login', { data: { pin: '1234' } });
    const auth = { Authorization: `Bearer ${(await login.json()).token}` };
    const name = 'E2E Retard ' + Date.now();
    const created = await request.post('/api/reservations', {
      headers: auth,
      data: { guestName: name, covers: 2, startsAtUtc: new Date(Date.now() - 30 * 60000).toISOString(), durationMinutes: 90, isWaitlist: true, quoteMinutes: 5 }
    });
    expect(created.ok()).toBeTruthy();
    const id = (await created.json()).id;

    try {
      await ensureLoggedIn(page);
      await expect(page.locator('#needsN')).not.toHaveText('0', { timeout: 15000 });
      await expect(page.locator('#btnNavBookings')).toHaveAttribute('data-badge', /\d+/);

      await page.click('#needsBtn');
      const item = page.locator('#needsPop [data-need]', { hasText: name });
      await expect(item).toBeVisible();
      await item.click();
      await expect(page.locator('#bookingsView')).toHaveClass(/active/);
    } finally {
      await request.post(`/api/reservations/${id}/status`, { headers: auth, data: { status: 4 } });
    }
  });
});
