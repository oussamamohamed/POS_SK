import { test, expect, Page } from '@playwright/test';

/**
 * Régressions corrigées dans le client web (wwwroot/app.js).
 * Chaque test vérifie l'état côté serveur via l'API, pas seulement l'affichage.
 */
test.describe('Corrections du client web', () => {

  async function login(page: Page) {
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of '1234') await page.click(`.pin-keypad button[data-val="${d}"]`);
      await expect(pinModal).not.toHaveClass(/active/);
    }
    await page.waitForSelector('.product-card');
  }

  async function api(page: Page, method: string, path: string, body?: unknown) {
    return page.evaluate(async ({ method, path, body }) => {
      const token = localStorage.getItem('pos_jwt_token');
      const res = await fetch(path, {
        method,
        headers: { 'Content-Type': 'application/json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
        body: body ? JSON.stringify(body) : undefined,
      });
      let json: any = null;
      try { json = await res.json(); } catch { /* corps vide */ }
      return { status: res.status, json };
    }, { method, path, body });
  }

  /** Crée une table dédiée au test et l'ouvre dans la caisse. */
  async function openFreshTable(page: Page): Promise<string> {
    const name = `R${Date.now().toString().slice(-6)}`;
    const created = await api(page, 'POST', '/api/tables', { tableNumber: name, capacity: 4 });
    expect(created.status).toBe(201);
    await page.click('#btnNavFloor');
    await page.locator('.table-card').filter({ has: page.locator('.table-num', { hasText: new RegExp(`^${name}$`) }) }).click();
    await expect(page.locator('#activeTableBadge')).toHaveText(`Table ${name}`);
    return name;
  }

  async function addProduct(page: Page, name: string, confirmOptions = true) {
    await page.click('#btnNavPos');
    await page.locator('.product-card').filter({ hasText: name }).first().click();
    const modal = page.locator('#modifiersModal');
    if (confirmOptions && await modal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnConfirmModifiers');
    }
  }

  test('Comptoir : la destination envoyée respecte l’enum serveur (Takeaway = 0, EatIn = 1)', async ({ page }) => {
    await login(page);
    await page.click('#btnDestEatIn');
    await expect.poll(async () => (await api(page, 'GET', '/api/tables/Comptoir/order')).json?.destination).toBe(1);
    await page.click('#btnDestTakeaway');
    await expect.poll(async () => (await api(page, 'GET', '/api/tables/Comptoir/order')).json?.destination).toBe(0);
  });

  test('Table : la commande est « sur place » et le bon cuisine n’est pas marqué « À EMPORTER »', async ({ page }) => {
    await login(page);
    const table = await openFreshTable(page);
    await addProduct(page, 'Bière Artisanale');
    await page.click('#btnSendKitchen');
    await expect(page.locator('#floorPlanView')).toHaveClass(/active/);

    const order = await api(page, 'GET', `/api/tables/${table}/order`);
    expect(order.json.destination).toBe(1);
    const tickets = await api(page, 'GET', '/api/kds/tickets');
    const ticket = tickets.json.find((t: any) => t.orderId === order.json.orderId);
    expect(ticket.tableNumber).toBe(table);
  });

  test('Les quantités modifiées après un délai sont bien enregistrées', async ({ page }) => {
    await login(page);
    const table = await openFreshTable(page);
    await addProduct(page, 'Bière Artisanale');
    await page.waitForTimeout(900);
    await page.locator('.btn-qty[data-action="plus"]').first().click();
    await page.waitForTimeout(900);
    await page.locator('.btn-qty[data-action="plus"]').first().click();
    await page.locator('.btn-qty[data-action="minus"]').first().click();
    await page.click('#btnSendKitchen');
    await expect(page.locator('#floorPlanView')).toHaveClass(/active/);

    const order = await api(page, 'GET', `/api/tables/${table}/order`);
    const quantity = order.json.lines.reduce((sum: number, l: any) => sum + l.quantity, 0);
    expect(quantity).toBe(2);
  });

  test('Le commentaire cuisine est transmis au bon de préparation', async ({ page }) => {
    await login(page);
    const table = await openFreshTable(page);
    await addProduct(page, 'Burger Gourmet Rossini', false);
    await expect(page.locator('#modifiersModal')).toHaveClass(/active/);
    const cooking = page.locator('.btn-modifier-option').filter({ hasText: 'Saignant' });
    await cooking.click();
    await page.fill('#inputModifiersComment', 'Sans oignons');
    await page.click('#btnConfirmModifiers');
    await page.click('#btnSendKitchen');
    await expect(page.locator('#floorPlanView')).toHaveClass(/active/);

    const order = await api(page, 'GET', `/api/tables/${table}/order`);
    const tickets = await api(page, 'GET', '/api/kds/tickets');
    const ticket = tickets.json.find((t: any) => t.orderId === order.json.orderId);
    expect(ticket.items[0].modifiersSummary).toContain('Sans oignons');
  });

  test('Partage en 3 : chaque part est encaissée et la table est soldée au centime', async ({ page }) => {
    await login(page);
    const table = await openFreshTable(page);
    await addProduct(page, 'Bière Artisanale');
    await addProduct(page, 'Pizza Margherita');
    await expect(page.locator('#summaryTtc')).toHaveText('18.50 €');

    await page.click('#btnSplitBill');
    await page.click('#btnSplitPlus');
    await expect(page.locator('#splitGuestsCount')).toContainText('3');
    await page.click('#btnConfirmSplit');

    for (const [label, amount] of [['1/3', '6.17'], ['2/3', '6.17'], ['3/3', '6.16']]) {
      await expect(page.locator('#paymentModal')).toHaveClass(/active/);
      await expect(page.locator('#payRemainingAmount')).toContainText(amount);
      await expect(page.locator('#payRemainingAmount')).toContainText(label);
      await page.click('.btn-tender[data-tender="Card"]');
    }

    await expect(page.locator('#floorPlanView')).toHaveClass(/active/);
    const order = await api(page, 'GET', `/api/tables/${table}/order`);
    expect(order.status).toBe(404);
  });

  test('Création et modification d’un employé', async ({ page }) => {
    await login(page);
    const name = `Employé ${Date.now()}`;
    const pin = String(100000 + Math.floor(Math.random() * 899999));
    await page.click('#btnNavAdmin');
    await page.click('.admin-tab-btn[data-admin-tab="tabStaff"]');
    await page.fill('#inputStaffName', name);
    await page.selectOption('#selectStaffRole', 'Cashier');
    await page.fill('#inputStaffPin', pin);
    await page.click('#formAddStaff button[type="submit"]');

    await expect.poll(async () => (await api(page, 'GET', '/api/staff/operators')).json.some((s: any) => s.name === name)).toBe(true);

    const row = page.locator('#adminStaffList .item-list-row').filter({ hasText: name });
    await row.locator('.btn-edit-staff').click();
    await expect(page.locator('#editStaffModal')).toHaveClass(/active/);
    await page.selectOption('#editStaffRole', 'KitchenStaff');
    await page.click('#formEditStaff button[type="submit"]');
    await expect(page.locator('#editStaffModal')).not.toHaveClass(/active/);
    await expect.poll(async () => (await api(page, 'GET', '/api/staff/operators')).json.find((s: any) => s.name === name)?.role).toBe('KitchenStaff');
  });

  test('Modification d’un article et d’une famille', async ({ page }) => {
    await login(page);
    const products = (await api(page, 'GET', '/api/catalog/products')).json;
    const target = products.find((p: any) => p.name.startsWith('Pizza Margherita'));

    await page.click('#btnNavAdmin');
    await page.click('.admin-tab-btn[data-admin-tab="tabCatalog"]');
    await page.locator('#adminCatalogList .item-list-row').filter({ hasText: target.name }).locator('.btn-edit-product').click();
    await expect(page.locator('#editProductModal')).toHaveClass(/active/);
    const newPrice = (Number(target.price) + 0.5).toFixed(2);
    await page.fill('#editProdPrice', newPrice);
    await page.click('#formEditProduct button[type="submit"]');
    await expect(page.locator('#editProductModal')).not.toHaveClass(/active/);
    await expect.poll(async () => (await api(page, 'GET', '/api/catalog/products')).json.find((p: any) => p.id === target.id)?.price).toBe(Number(newPrice));
    // Remise en état pour les autres tests.
    await api(page, 'PUT', `/api/catalog/products/${target.id}`, { ...target, price: target.price, stationId: target.preparationStationId, isQuickKey: target.isQuickKey });

    await page.click('#btnNavPos');
    const categories = (await api(page, 'GET', '/api/catalog/categories')).json;
    const cat = categories[0];
    await page.locator(`.btn-edit-cat-trigger[data-cat-id="${cat.id}"]`).click();
    await expect(page.locator('#editCategoryModal')).toHaveClass(/active/);
    await page.fill('#editCatName', `${cat.name} ✓`);
    await page.click('#formEditCategory button[type="submit"]');
    await expect.poll(async () => (await api(page, 'GET', '/api/catalog/categories')).json.find((c: any) => c.id === cat.id)?.name).toBe(`${cat.name} ✓`);
    await api(page, 'PUT', `/api/catalog/categories/${cat.id}`, { name: cat.name, colorHex: cat.colorHex, displayOrder: cat.displayOrder, iconName: cat.iconName, isActive: true });
  });

  test('Imprimantes : création avec tiroir, modification et désactivation', async ({ page }) => {
    await login(page);
    const name = `Imprimante ${Date.now()}`;
    await page.click('#btnNavAdmin');
    await page.click('.admin-tab-btn[data-admin-tab="tabPrinters"]');
    await page.fill('#inputPrinterName', name);
    await page.fill('#inputPrinterIp', '10.0.0.42');
    await page.check('#checkPrinterDrawer');
    await page.click('#formAddPrinter button[type="submit"]');

    const find = async () => (await api(page, 'GET', '/api/printers')).json.find((p: any) => p.name === name);
    await expect.poll(async () => (await find())?.openCashDrawerOnReceipt).toBe(true);
    expect((await find()).assignedStationIds.length).toBeGreaterThan(0);

    const row = page.locator('#adminPrintersList .item-list-row').filter({ hasText: name });
    await row.locator('.btn-edit-printer').click();
    await page.fill('#editPrinterIp', '10.0.0.43');
    await page.click('#formEditPrinter button[type="submit"]');
    await expect.poll(async () => (await find())?.ipAddress).toBe('10.0.0.43');

    await page.locator('#adminPrintersList .item-list-row').filter({ hasText: name }).locator('.btn-del-printer').click();
    await expect.poll(async () => (await find())?.isActive).toBe(false);
  });

  test('Les fenêtres d’édition se ferment avec Annuler et ✕', async ({ page }) => {
    await login(page);
    await page.click('#btnNavAdmin');
    await page.click('.admin-tab-btn[data-admin-tab="tabCatalog"]');
    await page.locator('#adminCatalogList .btn-edit-product').first().click();
    await expect(page.locator('#editProductModal')).toHaveClass(/active/);
    await page.click('#btnCancelEditProduct');
    await expect(page.locator('#editProductModal')).not.toHaveClass(/active/);

    await page.click('.admin-tab-btn[data-admin-tab="tabStaff"]');
    await page.locator('#adminStaffList .btn-edit-staff').first().click();
    await expect(page.locator('#editStaffModal')).toHaveClass(/active/);
    await page.click('#btnCloseEditStaffModal');
    await expect(page.locator('#editStaffModal')).not.toHaveClass(/active/);
  });
});
