import { test, expect, type Page } from '@playwright/test';
import { pairTill, managerToken } from './helpers/pairing';

const uniq = () => Math.random().toString(36).slice(2, 7);

test.describe('Ingrédients et fiches techniques', () => {
  async function openAdmin(page: Page) {
    await pairTill(page);
    await page.goto('/?nocache=' + Date.now());
    await page.waitForLoadState('domcontentloaded');
    const pinModal = page.locator('#pinLockModal');
    if (await pinModal.evaluate((el: HTMLElement) => el.classList.contains('active'))) {
      await page.click('#btnPinClear');
      for (const d of '1234') await page.click(`.pin-keypad button[data-val="${d}"]`);
      await expect(pinModal).not.toHaveClass(/active/);
    }
    await page.click('button[data-view="adminView"]');
    await page.click('button[data-admin-tab="tabStock"]');
    await expect(page.locator('#tabStock')).toHaveClass(/active/);
  }

  async function addIngredient(page: Page, name: string, price: string, allergenLabel?: string) {
    await page.fill('#stockName', name);
    await page.fill('#stockPrice', price);
    if (allergenLabel) await page.locator('#stockAllergens label', { hasText: allergenLabel }).click();
    await page.click('#formStockIngredient button[type="submit"]');
    await expect(page.locator('#stockList')).toContainText(name);
  }

  test('3 ingrédients, fiche d\'un plat, coût en direct et allergènes déduits', async ({ page }) => {
    const id = uniq();
    const [flour, tomato, cheese] = [`Farine ${id}`, `Tomate ${id}`, `Mozza ${id}`];
    await openAdmin(page);
    await addIngredient(page, flour, '12.50', 'Gluten');   // 1,25 c/g
    await addIngredient(page, tomato, '3');                // 0,30 c/g
    await addIngredient(page, cheese, '18', 'Lait');       // 1,80 c/g

    const productName = `Pizza ${id}`;
    const token = await managerToken(page.request);
    const cats = await (await page.request.get('/api/catalog/categories')).json();
    await page.request.post('/api/catalog/products', {
      headers: { Authorization: `Bearer ${token}` },
      data: { name: productName, categoryId: cats[0].id, price: 12, taxRatePercent: 10, quickKey: false },
    });
    await page.reload();
    await page.click('button[data-view="adminView"]');
    // Le bouton « Modifier » de la ligne qui porte ce nom : on remonte jusqu'au plus petit conteneur qui contient le nom.
    await page.waitForFunction((n) => document.body.innerText.includes(n), productName);
    await page.evaluate((n) => {
      for (const b of document.querySelectorAll<HTMLElement>('#tabCatalog .btn-edit-product')) {
        let el: HTMLElement | null = b;
        while (el && !el.innerText.includes(n)) el = el.parentElement;
        if (el && el.querySelectorAll('.btn-edit-product').length === 1) { b.click(); return; }
      }
      throw new Error('ligne produit introuvable : ' + n);
    }, productName);
    await expect(page.locator('#editProductModal')).toHaveClass(/active/);

    for (const [name, qty] of [[flour, '150'], [tomato, '100'], [cheese, '125']]) {
      await page.selectOption('#editRecipeIngredient', { label: name });
      await page.fill('#editRecipeQty', qty);
      await page.click('#btnRecipeAdd');
    }
    // 187,5 + 30 + 225 = 442,5 centimes -> 4,43 € (demi supérieur)
    await expect(page.locator('#editRecipeCost')).toContainText('4.43');
    await page.click('#formEditProduct button[type="submit"]');
    await expect(page.locator('#editProductModal')).not.toHaveClass(/active/);

    const products = await (await page.request.get('/api/catalog/products')).json();
    const saved = products.find((p: any) => p.name === productName);
    expect(saved.allergensDeclared).toBe(true);
    expect(saved.allergenMask).toBe(1 | 64);
    const recipe = await (await page.request.get(`/api/catalog/products/${saved.id}/recipe`, { headers: { Authorization: `Bearer ${token}` } })).json();
    expect(recipe.costCents).toBe(443);
  });

  test('une fiche de préparation qui ferme une boucle est refusée', async ({ page }) => {
    const token = await managerToken(page.request);
    const headers = { Authorization: `Bearer ${token}` };
    const make = async (name: string) => (await (await page.request.post('/api/stock/ingredients', {
      headers, data: { name, unit: 'g', unitCostMilliCents: 0, isPreparation: true, yieldMilli: 1000, allergenMask: 0 },
    })).json()).id;
    const a = await make(`PrepA ${uniq()}`);
    const b = await make(`PrepB ${uniq()}`);
    const put = (owner: string, comp: string) => page.request.put(`/api/stock/ingredients/${owner}/recipe`, {
      headers, data: { lines: [{ ingredientId: comp, quantityMilli: 1000 }] },
    });
    expect((await put(a, b)).ok()).toBeTruthy();
    const res = await put(b, a);
    expect(res.status()).toBe(400);
    expect((await res.json()).code).toBe('recipe_cycle');
  });
});
