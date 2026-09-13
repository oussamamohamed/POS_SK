import { test, expect } from '@playwright/test';

test.describe('Happy Hour Multi-Select Configuration (Articles & Familles)', () => {

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

  async function openHappyHourAdmin(page: any) {
    await ensureLoggedIn(page);
    // Click on Admin Navigation Button
    await page.click('button[data-view="adminView"]');
    // Click on Happy Hour tab
    await page.click('button[data-admin-tab="tabHappyHour"]');
    await expect(page.locator('#tabHappyHour')).toHaveClass(/active/);
  }

  test('US1: Application groupée de remise en % sur plusieurs Familles / Catégories', async ({ page }) => {
    await openHappyHourAdmin(page);

    // 1. Basculer sur l'onglet Familles éligibles
    await page.click('#btnSubtabHhFamilies');
    await expect(page.locator('#hhSubtabFamilies')).toBeVisible();

    // 2. Tout sélectionner ou cocher les premières familles
    const familyCards = page.locator('#hhFamiliesGrid .hh-select-card');
    await expect(familyCards.first()).toBeVisible();

    // Cliquer sur le bouton "Tout sélectionner"
    await page.click('#btnHhSelectAllFamilies');
    const selectedCount = await page.locator('#hhSelectedFamiliesCount').innerText();
    expect(parseInt(selectedCount, 10)).toBeGreaterThan(0);

    // 3. Saisir la remise en pourcentage (ex: 25%)
    await page.fill('#inputHhFamilyDiscount', '25');

    // 4. Cliquer sur "Appliquer à la sélection"
    await page.click('#btnApplyBatchFamilies');

    // 5. Vérifier le toast de confirmation
    const toast = page.locator('.toast.success');
    await expect(toast).toBeVisible();

    // 6. Basculer sur le sous-onglet Règles Actives
    await page.click('#btnSubtabHhActiveRules');
    await expect(page.locator('#hhSubtabActiveRules')).toBeVisible();

    // Vérifier la présence de la remise -25% dans la liste des familles
    const catRules = page.locator('#hhActiveCategoryRulesList .hh-active-rule-item');
    await expect(catRules.first()).toBeVisible();
    await expect(catRules.first()).toContainText('-25%');
  });

  test('US2: Sélection tactile multiple d articles avec Prix Fixe et filtre catégorie', async ({ page }) => {
    await openHappyHourAdmin(page);

    // 1. Ouvrir l'onglet Articles spécifiques
    await page.click('#btnSubtabHhArticles');
    await expect(page.locator('#hhSubtabArticles')).toBeVisible();

    // 2. Filtrer ou rechercher un article
    await page.fill('#inputHhArticleSearch', 'Bière');
    const articleCards = page.locator('#hhArticlesGrid .hh-select-card');
    await expect(articleCards.first()).toBeVisible();

    // 3. Cocher le premier article trouvé
    await articleCards.first().click();
    await expect(page.locator('#hhSelectedArticlesCount')).toHaveText('1');

    // 4. Configurer en Prix Fixe 4.50 €
    await page.click('#btnHhModeFixed');
    await page.fill('#inputHhBatchValue', '4.50');

    // 5. Appliquer la règle aux articles sélectionnés
    await page.click('#btnApplyBatchArticles');

    // Vérifier notification succès
    const toast = page.locator('.toast.success');
    await expect(toast).toBeVisible();

    // 6. Vérifier dans Règles actives
    await page.click('#btnSubtabHhActiveRules');
    const prodRules = page.locator('#hhActiveProductRulesList .hh-active-rule-item');
    await expect(prodRules.first()).toBeVisible();
    await expect(prodRules.first()).toContainText('4.50 €');
  });

  test('US3: Suppression par lot de règles actives', async ({ page }) => {
    await openHappyHourAdmin(page);

    // 1. Aller sur l'onglet Règles Actives
    await page.click('#btnSubtabHhActiveRules');
    await expect(page.locator('#hhSubtabActiveRules')).toBeVisible();

    // 2. Cocher "Tout cocher"
    const ruleItems = page.locator('.hh-active-rule-item');
    if (await ruleItems.count() > 0) {
      await page.click('#btnHhSelectAllActiveRules');

      // 3. Cliquer sur Supprimer la sélection
      await page.click('#btnDeleteSelectedRules');

      // 4. Vérifier notification d'information de suppression
      const toast = page.locator('.toast.info');
      await expect(toast).toBeVisible();
    }
  });

  test('US4: Chevauchement de créneaux - le créneau avec la priorité la plus élevée l emporte', async ({ page, request }) => {
    // 1. Créer via API 2 créneaux qui se chevauchent sur la même plage horaire (ex: 12:00 - 23:59)
    // Créneau A: Priorité 1, Tarif Bière 5.50 €
    // Créneau B: Priorité 5, Tarif Bière 3.50 €
    const prodsRes = await request.get('/api/catalog/products');
    const products = await prodsRes.json();
    const beer = products.find((p: any) => p.name.includes('Bière'));
    expect(beer).toBeTruthy();

    const todayDayOfWeek = new Date().getDay(); // 0-6

    // Sched Low Priority
    const schedLowRes = await request.post('/api/happy-hour/schedules', {
      data: {
        name: 'Overlap Low Priority',
        daysOfWeek: [todayDayOfWeek],
        startTime: '00:01',
        endTime: '23:59',
        isActive: true,
        appliesToTakeaway: false,
        priority: 1,
        priceRules: [
          {
            targetType: 0,
            targetId: beer.id,
            targetName: beer.name,
            pricingMode: 0,
            fixedPrice: 5.50,
            discountPercent: null
          }
        ]
      }
    });
    expect(schedLowRes.ok()).toBeTruthy();
    const schedLow = await schedLowRes.json();

    // Sched High Priority
    const schedHighRes = await request.post('/api/happy-hour/schedules', {
      data: {
        name: 'Overlap High Priority VIP',
        daysOfWeek: [todayDayOfWeek],
        startTime: '00:01',
        endTime: '23:59',
        isActive: true,
        appliesToTakeaway: false,
        priority: 10,
        priceRules: [
          {
            targetType: 0,
            targetId: beer.id,
            targetName: beer.name,
            pricingMode: 0,
            fixedPrice: 3.50,
            discountPercent: null
          }
        ]
      }
    });
    expect(schedHighRes.ok()).toBeTruthy();
    const schedHigh = await schedHighRes.json();

    try {
      // 2. Vérifier le statut de caisse via l'API: le créneau VIP de priorité 10 doit être celui actif
      const statusRes = await request.get('/api/happy-hour/status?terminalId=POS_A');
      const status = await statusRes.json();
      expect(status.isActive).toBe(true);
      expect(status.activeScheduleId).toBe(schedHigh.id);
      expect(status.activeScheduleName).toBe('Overlap High Priority VIP');

      // 3. Ouvrir l'interface caisse et vérifier dans Paramétrage que la priorité est bien visible
      await openHappyHourAdmin(page);
      await page.click('#btnSubtabHhAllSchedules');
      await expect(page.locator('#hhSubtabAllSchedules')).toBeVisible();

      // Vérifier la présence du badge Priorité: 10
      const vipCard = page.locator('#adminHappyHourList > div').filter({ hasText: 'Overlap High Priority VIP' }).first();
      await expect(vipCard).toBeVisible();
      await expect(vipCard).toContainText('Priorité: 10');
    } finally {
      // Nettoyage des créneaux de test
      await request.delete(`/api/happy-hour/schedules/${schedLow.id}`);
      await request.delete(`/api/happy-hour/schedules/${schedHigh.id}`);
    }
  });

});

