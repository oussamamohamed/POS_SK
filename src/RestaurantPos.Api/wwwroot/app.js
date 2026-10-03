// Restaurant POS Web Client Interactive Application
document.addEventListener('DOMContentLoaded', async () => {
    await window.i18nReady;
    // App State
    const state = {
        operator: { name: 'Alexandre Dupont (Manager)', role: 'FloorManager', id: null },
        categories: [],
        products: [],
        tables: [],
        printers: [],
        hotelRooms: [],
        gridLayouts: {},
        activeGridPage: 0,
        activeAdminGridCategory: null,
        activeAdminGridPage: 0,
        draggedSlot: null,
        draggedCatalogItem: null,
        activeCategory: null,
        activeTable: 'Comptoir',
        activeCovers: 1,
        activeOrderId: null,
        destination: 'Takeaway',
        terminalId: 'POS_A',
        heldOrders: [],
        supervisorPinInput: '',
        pendingVoidHoldId: null,
        cart: [],
        pinInput: '',
        splitGuests: 2,
        selectedTipPercent: 0,
        customTipAmount: 0,
        globalDiscount: null,
        happyHour: {
            isActive: false,
            isOverride: false,
            activeScheduleName: null,
            remainingMinutes: 0,
            pricingTable: {}, // productId -> { happyHourPrice, standardPrice, ruleType }
            countdownInterval: null
        },
        token: localStorage.getItem('pos_jwt_token') || null,
        device: loadStoredDevice()
    };
    if (state.device) state.terminalId = state.device.terminalId;

    function loadStoredDevice() {
        try {
            const raw = localStorage.getItem('pos_device');
            return raw ? JSON.parse(raw) : null;
        } catch {
            return null;
        }
    }

    function storeDevice(device) {
        state.device = device;
        if (device) state.terminalId = device.terminalId;
        try {
            if (device) localStorage.setItem('pos_device', JSON.stringify(device));
            else localStorage.removeItem('pos_device');
        } catch {
            // Stockage indisponible : l'appairage reste valable jusqu'au rechargement.
        }
    }

    function setHeader(config, name, value) {
        if (config.headers instanceof Headers) config.headers.set(name, value);
        else if (Array.isArray(config.headers)) config.headers.push([name, value]);
        else config.headers[name] = value;
    }

    // Auto attach JWT bearer token to API requests & handle 401
    const originalFetch = window.fetch;
    window.fetch = async (...args) => {
        let [resource, config] = args;
        const token = state.token || localStorage.getItem('pos_jwt_token');
        if (token) {
            config = config || {};
            config.headers = config.headers || {};
            if (config.headers instanceof Headers) {
                if (!config.headers.has('Authorization')) {
                    config.headers.set('Authorization', `Bearer ${token}`);
                }
            } else if (Array.isArray(config.headers)) {
                if (!config.headers.some(([k]) => k.toLowerCase() === 'authorization')) {
                    config.headers.push(['Authorization', `Bearer ${token}`]);
                }
            } else {
                if (!config.headers['Authorization'] && !config.headers['authorization']) {
                    config.headers['Authorization'] = `Bearer ${token}`;
                }
            }
        }
        // Le jeton du poste et la langue ne partent que vers l'API de ce serveur, jamais vers un autre domaine.
        const target = new URL(resource instanceof Request ? resource.url : String(resource), location.href);
        if (target.origin === location.origin && target.pathname.startsWith('/api/')) {
            config = config || {};
            config.headers = config.headers || {};
            if (state.device?.token) setHeader(config, 'X-Device-Token', state.device.token);
            setHeader(config, 'Accept-Language', window.i18n.lang);
        }
        const res = await originalFetch(resource, config);
        if (res.status === 401) {
            const body = await res.clone().json().catch(() => ({}));
            if (body.code === 'device_not_paired') {
                // Poste inconnu ou révoqué : on garde la session opérateur, on demande un code.
                storeDevice(null);
                openDevicePairingModal();
                return res;
            }
        }
        if (res.status === 401 && !resource.toString().includes('/api/auth/login')) {
            console.warn('Requête API 401: session non authentifiée ou expirée. Verrouillage du terminal.');
            state.token = null;
            localStorage.removeItem('pos_jwt_token');
            if (elements.pinLockModal) {
                elements.pinLockModal.classList.add('active');
                state.pinInput = '';
                updatePinDots();
            }
        }
        return res;
    };

    // DOM Elements
    const elements = {
        // Nav
        navBtns: document.querySelectorAll('.nav-btn'),
        viewPanels: document.querySelectorAll('.view-panel'),
        btnLockTerminal: document.getElementById('btnLockTerminal'),
        operatorPill: document.getElementById('operatorPill'),
        currentOperatorName: document.getElementById('currentOperatorName'),
        currentOperatorRole: document.getElementById('currentOperatorRole'),

        // Cart
        cartItemsList: document.getElementById('cartItemsList'),
        summaryHt: document.getElementById('summaryHt'),
        summaryVat: document.getElementById('summaryVat'),
        summaryTtc: document.getElementById('summaryTtc'),
        btnClearCart: document.getElementById('btnClearCart'),
        btnSendKitchen: document.getElementById('btnSendKitchen'),
        btnFireSuite: document.getElementById('btnFireSuite'),
        btnDiscountModal: document.getElementById('btnDiscountModal'),
        btnTransferModal: document.getElementById('btnTransferModal'),
        btnSplitBill: document.getElementById('btnSplitBill'),
        btnPayModal: document.getElementById('btnPayModal'),
        activeTableBadge: document.getElementById('activeTableBadge'),
        activeCoversBadge: document.getElementById('activeCoversBadge'),

        // Feature 018: Takeaway & Direct Sales
        destinationToggleGroup: document.getElementById('destinationToggleGroup'),
        btnDestTakeaway: document.getElementById('btnDestTakeaway'),
        btnDestEatIn: document.getElementById('btnDestEatIn'),
        btnHeldQueue: document.getElementById('btnHeldQueue'),
        heldBadgeCount: document.getElementById('heldBadgeCount'),
        btnHoldCart: document.getElementById('btnHoldCart'),
        cartFastCashBar: document.getElementById('cartFastCashBar'),
        inputPickupBuzzer: document.getElementById('inputPickupBuzzer'),
        selectMealVoucherPolicy: document.getElementById('selectMealVoucherPolicy'),
        chkPrintFiscalReceipt: document.getElementById('chkPrintFiscalReceipt'),
        changeOverlayModal: document.getElementById('changeOverlayModal'),
        changeOverlayAmount: document.getElementById('changeOverlayAmount'),
        changeOverlayDetails: document.getElementById('changeOverlayDetails'),
        changeOverlayPickupNumber: document.getElementById('changeOverlayPickupNumber'),
        changeOverlayBuzzer: document.getElementById('changeOverlayBuzzer'),
        changeOverlayBuzzerVal: document.getElementById('changeOverlayBuzzerVal'),
        changeOverlayCreditVoucherBox: document.getElementById('changeOverlayCreditVoucherBox'),
        changeOverlayCreditVoucherCode: document.getElementById('changeOverlayCreditVoucherCode'),
        changeOverlayCreditVoucherAmount: document.getElementById('changeOverlayCreditVoucherAmount'),
        btnChangeDone: document.getElementById('btnChangeDone'),
        heldOrdersModal: document.getElementById('heldOrdersModal'),
        btnCloseHeldModal: document.getElementById('btnCloseHeldModal'),
        heldOrdersList: document.getElementById('heldOrdersList'),
        supervisorPinModal: document.getElementById('supervisorPinModal'),
        supervisorPinInput: document.getElementById('supervisorPinInput'),
        btnCancelSupervisorPin: document.getElementById('btnCancelSupervisorPin'),
        btnConfirmSupervisorPin: document.getElementById('btnConfirmSupervisorPin'),
        agecPromptModal: document.getElementById('agecPromptModal'),
        btnAgecYes: document.getElementById('btnAgecYes'),
        btnAgecNo: document.getElementById('btnAgecNo'),
        agecCountdown: document.getElementById('agecCountdown'),

        // Happy Hour Elements (Feature 019)
        happyHourBanner: document.getElementById('happyHourBanner'),
        hhBannerTitle: document.getElementById('hhBannerTitle'),
        hhBannerSubtitle: document.getElementById('hhBannerSubtitle'),
        hhCountdownTime: document.getElementById('hhCountdownTime'),
        btnHhOverrideQuickAction: document.getElementById('btnHhOverrideQuickAction'),
        hhOverrideModal: document.getElementById('hhOverrideModal'),
        btnCloseHhOverrideModal: document.getElementById('btnCloseHhOverrideModal'),
        inputHhPin: document.getElementById('inputHhPin'),
        inputHhReason: document.getElementById('inputHhReason'),
        btnHhExtend30: document.getElementById('btnHhExtend30'),
        btnHhForce60: document.getElementById('btnHhForce60'),
        btnHhForceStop: document.getElementById('btnHhForceStop'),

        // Catalog
        quickKeysBar: document.getElementById('quickKeysBar'),
        categoryTabsBar: document.getElementById('categoryTabsBar'),
        productsGrid: document.getElementById('productsGrid'),
        gridPaginationBar: document.getElementById('gridPaginationBar'),
        btnPrevGridPage: document.getElementById('btnPrevGridPage'),
        btnNextGridPage: document.getElementById('btnNextGridPage'),
        gridPageDots: document.getElementById('gridPageDots'),
        gridPageLabel: document.getElementById('gridPageLabel'),

        // Floor Plan
        floorTablesGrid: document.getElementById('floorTablesGrid'),
        btnOpenAddTableModal: document.getElementById('btnOpenAddTableModal'),

        // KDS
        kdsPendingCards: document.getElementById('kdsPendingCards'),
        kdsInPrepCards: document.getElementById('kdsInPrepCards'),
        kdsReadyCards: document.getElementById('kdsReadyCards'),
        countPending: document.getElementById('countPending'),
        countInPrep: document.getElementById('countInPrep'),
        countReady: document.getElementById('countReady'),
        kdsFilterBtns: document.querySelectorAll('.kds-filter-btn'),

        // Admin
        adminTabBtns: document.querySelectorAll('.admin-tab-btn'),
        adminTabPanes: document.querySelectorAll('.admin-tab-pane'),
        formAddCategory: document.getElementById('formAddCategory'),
        formAddProduct: document.getElementById('formAddProduct'),
        formAddStaff: document.getElementById('formAddStaff'),
        formAddPrinter: document.getElementById('formAddPrinter'),
        adminCategoryList: document.getElementById('adminCategoryList'),
        adminProductList: document.getElementById('adminProductList'),
        adminStaffList: document.getElementById('adminStaffList'),
        adminPrinterList: document.getElementById('adminPrinterList'),
        selectProductCat: document.getElementById('selectProductCat'),
        btnExecuteZ: document.getElementById('btnExecuteZ'),
        btnPrintXReport: document.getElementById('btnPrintXReport'),
        btnReprintZ: document.getElementById('btnReprintZ'),
        btnPreviewX: document.getElementById('btnPreviewX'),
        btnVerifyChains: document.getElementById('btnVerifyChains'),
        fiscalVerificationPanel: document.getElementById('fiscalVerificationPanel'),
        fiscalVerificationSummary: document.getElementById('fiscalVerificationSummary'),
        fiscalChainsTableBody: document.getElementById('fiscalChainsTableBody'),
        periodClosureType: document.getElementById('periodClosureType'),
        periodClosureKey: document.getElementById('periodClosureKey'),
        btnExecutePeriodClosure: document.getElementById('btnExecutePeriodClosure'),
        periodClosureAlert: document.getElementById('periodClosureAlert'),
        periodClosuresTableBody: document.getElementById('periodClosuresTableBody'),
        archivesTableBody: document.getElementById('archivesTableBody'),
        archiveVerifyFileInput: document.getElementById('archiveVerifyFileInput'),
        btnVerifyArchive: document.getElementById('btnVerifyArchive'),
        archiveVerifyAlert: document.getElementById('archiveVerifyAlert'),
        slipTitle: document.getElementById('slipTitle'),
        slipDuplicateBanner: document.getElementById('slipDuplicateBanner'),
        reprintReceiptInput: document.getElementById('reprintReceiptInput'),
        btnReprintReceipt: document.getElementById('btnReprintReceipt'),
        reprintReceiptResult: document.getElementById('reprintReceiptResult'),
        btnReprintCurrentReceipt: document.getElementById('btnReprintCurrentReceipt'),
        reprintCurrentReceiptNotice: document.getElementById('reprintCurrentReceiptNotice'),
        slipTerminal: document.getElementById('slipTerminal'),
        slipTotalTtc: document.getElementById('slipTotalTtc'),
        slipTotalHt: document.getElementById('slipTotalHt'),
        slipVatBreakdown: document.getElementById('slipVatBreakdown'),
        slipPaymentBreakdown: document.getElementById('slipPaymentBreakdown'),
        slipPerpetual: document.getElementById('slipPerpetual'),
        slipCount: document.getElementById('slipCount'),
        slipHash: document.getElementById('slipHash'),
        slipDate: document.getElementById('slipDate'),
        slipTag: document.getElementById('slipTag'),

        // FEC Export
        btnExportFec: document.getElementById('btnExportFec'),
        fecStartDate: document.getElementById('fecStartDate'),
        fecEndDate: document.getElementById('fecEndDate'),
        fecSiren: document.getElementById('fecSiren'),
        fecStatusMessage: document.getElementById('fecStatusMessage'),

        // Financial Dashboard
        kpiSalesTtc: document.getElementById('kpiSalesTtc'),
        kpiSalesHt: document.getElementById('kpiSalesHt'),
        kpiAvgCover: document.getElementById('kpiAvgCover'),
        kpiTotalCovers: document.getElementById('kpiTotalCovers'),
        kpiAvgOrder: document.getElementById('kpiAvgOrder'),
        kpiTotalOrders: document.getElementById('kpiTotalOrders'),
        dashServicesList: document.getElementById('dashServicesList'),
        dashPaymentsList: document.getElementById('dashPaymentsList'),
        dashTopProductsList: document.getElementById('dashTopProductsList'),
        dashStaffList: document.getElementById('dashStaffList'),
        btnRefreshDashboard: document.getElementById('btnRefreshDashboard'),
        dashFilterBtns: document.querySelectorAll('.dash-filter-btn'),

        // Admin Grid Editor
        adminCatalogList: document.getElementById('adminCatalogList'),
        adminStaffList: document.getElementById('adminStaffList'),
        adminPrintersList: document.getElementById('adminPrintersList'),
        selectAdminGridCat: document.getElementById('selectAdminGridCat'),
        adminMatrixGrid: document.getElementById('adminMatrixGrid'),
        adminCatalogListForGrid: document.getElementById('adminCatalogListForGrid'),
        adminGridLayoutVersion: document.getElementById('adminGridLayoutVersion'),
        btnResetGridLayout: document.getElementById('btnResetGridLayout'),
        adminGridPageTabs: document.getElementById('adminGridPageTabs'),
        selectGridDimensionsPreset: document.getElementById('selectGridDimensionsPreset'),
        customDimensionsInputs: document.getElementById('customDimensionsInputs'),
        inputGridCols: document.getElementById('inputGridCols'),
        inputGridRows: document.getElementById('inputGridRows'),
        checkApplyAllGridDimensions: document.getElementById('checkApplyAllGridDimensions'),
        btnApplyGridDimensions: document.getElementById('btnApplyGridDimensions'),

        // Modals
        modifiersModal: document.getElementById('modifiersModal'),
        modifiersModalTitle: document.getElementById('modifiersModalTitle'),
        modifiersModalSubtitle: document.getElementById('modifiersModalSubtitle'),
        modifiersGroupsContainer: document.getElementById('modifiersGroupsContainer'),
        inputModifiersComment: document.getElementById('inputModifiersComment'),
        lblModifiersExtraTotal: document.getElementById('lblModifiersExtraTotal'),
        lblModifiersEffectivePrice: document.getElementById('lblModifiersEffectivePrice'),
        btnCloseModifiersModal: document.getElementById('btnCloseModifiersModal'),
        btnCancelModifiers: document.getElementById('btnCancelModifiers'),
        btnConfirmModifiers: document.getElementById('btnConfirmModifiers'),
        pinLockModal: document.getElementById('pinLockModal'),
        paymentModal: document.getElementById('paymentModal'),
        btnClosePayModal: document.getElementById('btnClosePayModal'),
        splitBillModal: document.getElementById('splitBillModal'),
        addTableModal: document.getElementById('addTableModal'),
        transferTableModal: document.getElementById('transferTableModal'),
        discountModal: document.getElementById('discountModal'),
        roomChargeModal: document.getElementById('roomChargeModal'),
        editProductModal: document.getElementById('editProductModal'),
        editCategoryModal: document.getElementById('editCategoryModal'),
        editStaffModal: document.getElementById('editStaffModal'),
        editPrinterModal: document.getElementById('editPrinterModal'),
        editSlotModal: document.getElementById('editSlotModal'),
        btnCloseEditSlotModal: document.getElementById('btnCloseEditSlotModal'),

        // Form elements for Modals
        formAddNewTable: document.getElementById('formAddNewTable'),
        inputNewTableNum: document.getElementById('inputNewTableNum'),
        inputNewTableCap: document.getElementById('inputNewTableCap'),
        formTransferTable: document.getElementById('formTransferTable'),
        transferSourceTable: document.getElementById('transferSourceTable'),
        selectTargetTable: document.getElementById('selectTargetTable'),
        formApplyDiscount: document.getElementById('formApplyDiscount'),
        selectDiscountTarget: document.getElementById('selectDiscountTarget'),
        selectDiscountReason: document.getElementById('selectDiscountReason'),
        inputDiscountCustomReason: document.getElementById('inputDiscountCustomReason'),
        btnResetDiscount: document.getElementById('btnResetDiscount'),
        inputDiscountVal: document.getElementById('inputDiscountVal'),
        selectDiscountUnit: document.getElementById('selectDiscountUnit'),
        groupDiscountType: document.getElementById('groupDiscountType'),
        groupDiscountValue: document.getElementById('groupDiscountValue'),

        // Edit Slot Modal elements
        formEditSlot: document.getElementById('formEditSlot'),
        editSlotRow: document.getElementById('editSlotRow'),
        editSlotCol: document.getElementById('editSlotCol'),
        selectSlotProduct: document.getElementById('selectSlotProduct'),
        inputSlotCustomLabel: document.getElementById('inputSlotCustomLabel'),
        inputSlotCustomColor: document.getElementById('inputSlotCustomColor'),
        btnUnassignSlot: document.getElementById('btnUnassignSlot'),

        // Hotel Room Charge elements
        formRoomCharge: document.getElementById('formRoomCharge'),
        selectHotelRoom: document.getElementById('selectHotelRoom'),
        roomGuestName: document.getElementById('roomGuestName'),
        roomCreditAvailable: document.getElementById('roomCreditAvailable'),
        roomChargeTotalAmount: document.getElementById('roomChargeTotalAmount'),
        signatureCanvas: document.getElementById('signatureCanvas'),
        btnClearSignature: document.getElementById('btnClearSignature'),
        btnOpenRoomChargeModal: document.getElementById('btnOpenRoomChargeModal'),

        // Payment / Tips elements
        payRemainingAmount: document.getElementById('payRemainingAmount'),
        payTotalWithTip: document.getElementById('payTotalWithTip'),
        inputCustomTip: document.getElementById('inputCustomTip'),
        tipCustomInputRow: document.getElementById('tipCustomInputRow'),
        tipPills: document.querySelectorAll('.btn-tip-pill'),

        // Split Bill
        btnSplitMinus: document.getElementById('btnSplitMinus'),
        btnSplitPlus: document.getElementById('btnSplitPlus'),
        splitGuestsCount: document.getElementById('splitGuestsCount'),
        splitPartitionsList: document.getElementById('splitPartitionsList'),
        btnConfirmSplit: document.getElementById('btnConfirmSplit'),

        // PIN
        btnPinClear: document.getElementById('btnPinClear'),
        btnPinDel: document.getElementById('btnPinDel'),
        toastContainer: document.getElementById('toastContainer')
    };

    // ==================== INITIALIZATION ====================
    async function init() {
        setupNavListeners();
        setupPinKeypad();
        const languageSelect = document.getElementById('languageSelect');
        languageSelect.value = window.i18n.lang;
        languageSelect.addEventListener('change', e => window.i18n.setLanguage(e.target.value));
        setupAdminTabs();
        setupDashboardHandlers();
        setupModals();
        setupEditModals();
        setupForms();
        setupSalesGridPaginationListeners();
        setupAdminGridEditorListeners();
        setupSignatureCanvas();
        setupSignalR();

        // Ensure active authentication session
        await ensureAuthToken();
        startPosHub();

        await loadCatalogData();
        // Le terminal démarre sur le comptoir : on charge sa commande en cours (articles, destination).
        if (state.token) await openDirectCounterOrder(state.destination);
        await loadFloorPlanData();
        await loadKdsData();
        await loadAdminData();
        await loadNetworkSyncData();

        setupTakeawayListeners();
        setupHappyHourControls();

        // Check Happy Hour status on startup
        await checkHappyHourStatus();
        await updateHeldQueueCount();
    }

    // ==================== NAVIGATION ====================
    function setupNavListeners() {
        elements.navBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                const targetViewId = btn.getAttribute('data-view');
                switchView(targetViewId);
            });
        });

        elements.btnLockTerminal.addEventListener('click', () => {
            elements.pinLockModal.classList.add('active');
            state.pinInput = '';
            updatePinDots();
        });

        if (elements.operatorPill) {
            elements.operatorPill.addEventListener('click', () => {
                elements.pinLockModal.classList.add('active');
                state.pinInput = '';
                updatePinDots();
            });
        }
    }

    function switchView(viewId) {
        elements.navBtns.forEach(btn => {
            btn.classList.toggle('active', btn.getAttribute('data-view') === viewId);
        });
        elements.viewPanels.forEach(panel => {
            panel.classList.toggle('active', panel.id === viewId);
        });

        if (viewId === 'floorPlanView') loadFloorPlanData();
        if (viewId === 'kdsView') loadKdsData();
        if (viewId === 'adminView') loadAdminData();
        if (viewId === 'fiscalView') loadFiscalViewData();
        if (viewId === 'posView') loadActiveTableOrder(state.activeTable || 'Comptoir');
    }

    // ==================== CATALOG & QUICK-KEYS ====================
    async function loadCatalogData() {
        try {
            const [catRes, prodRes] = await Promise.all([
                fetch('/api/catalog/categories'),
                fetch('/api/catalog/products')
            ]);
            state.categories = await catRes.json();
            state.products = await prodRes.json();

            if (!state.activeCategory || (state.activeCategory !== 'ALL' && !state.categories.some(c => c.id === state.activeCategory))) {
                state.activeCategory = 'ALL';
            }

            renderCatalogTabs();
            renderQuickKeys();
            renderProductsGrid();
            renderCategorySelectOptions();
        } catch (err) {
            console.error('Erreur chargement catalogue:', err);
            showToast(t('order.catalog_load_error'), 'error');
        }
    }

    function renderCatalogTabs() {
        elements.categoryTabsBar.innerHTML = '';

        // All items button
        const allBtn = document.createElement('button');
        allBtn.className = `btn-cat-tab ${state.activeCategory === 'ALL' ? 'active' : ''}`;
        allBtn.style.borderInlineStartColor = '#3b82f6';
        allBtn.style.borderInlineStartWidth = '6px';
        allBtn.innerHTML = `<span>🍽️</span> <span>${t('order.all_menu_tab')}</span>`;
        allBtn.addEventListener('click', () => {
            state.activeCategory = 'ALL';
            state.activeGridPage = 0;
            renderCatalogTabs();
            renderProductsGrid();
        });
        elements.categoryTabsBar.appendChild(allBtn);

        state.categories.forEach(cat => {
            const tabBtn = document.createElement('button');
            tabBtn.className = `btn-cat-tab ${cat.id === state.activeCategory ? 'active' : ''}`;
            tabBtn.style.borderInlineStartColor = cat.colorHex || '#4A90E2';
            tabBtn.style.borderInlineStartWidth = '6px';
            tabBtn.innerHTML = `
                <span>${getCategoryIcon(cat.iconName || cat.name.toLowerCase())}</span>
                <span>${cat.name}</span>
                <span class="btn-edit-cat-trigger" data-cat-id="${cat.id}" title="${t('common.edit')}" style="margin-inline-start:auto; font-size:0.75rem; opacity:0.6; padding:2px 4px;">✏️</span>
            `;

            tabBtn.addEventListener('click', (e) => {
                if (e.target.closest('.btn-edit-cat-trigger')) {
                    e.stopPropagation();
                    document.getElementById('editCatId').value = cat.id;
                    document.getElementById('editCatName').value = cat.name;
                    document.getElementById('editCatColor').value = cat.colorHex || '#4A90E2';
                    document.getElementById('editCatStation').value = cat.preparationStationId || '';
                    elements.editCategoryModal.classList.add('active');
                    return;
                }
                state.activeCategory = cat.id;
                state.activeGridPage = 0; // Réinitialisation automatique à la Page 1 (Option A)
                renderCatalogTabs();
                renderProductsGrid();
            });
            elements.categoryTabsBar.appendChild(tabBtn);
        });
    }

    function renderQuickKeys() {
        elements.quickKeysBar.innerHTML = '';
        const quickItems = state.products.filter(p => p.isQuickKey);
        quickItems.forEach(prod => {
            const btn = document.createElement('button');
            btn.className = 'btn-quick-key';
            btn.innerHTML = `<span>⚡</span> <span>${prod.name} (${Number(prod.price).toFixed(2)} €)</span>`;
            btn.addEventListener('click', () => handleProductClick(prod));
            elements.quickKeysBar.appendChild(btn);
        });
    }

    async function loadGridLayout(categoryId, pageIndex = 0) {
        if (!categoryId) return null;
        const cacheKey = `${categoryId}_page_${pageIndex}`;
        if (state.gridLayouts[cacheKey]) return state.gridLayouts[cacheKey];

        try {
            const res = await fetch(`/api/grid-layouts/${encodeURIComponent(categoryId)}?page=${pageIndex}`);
            if (res.ok) {
                const layout = await res.json();
                state.gridLayouts[cacheKey] = layout;
                localStorage.setItem(`grid_layout_${categoryId}_page_${pageIndex}`, JSON.stringify(layout));
                return layout;
            }
        } catch (err) {
            console.warn(`Fallback cache local pour la grille ${categoryId} (p${pageIndex}):`, err);
            const cached = localStorage.getItem(`grid_layout_${categoryId}_page_${pageIndex}`);
            if (cached) {
                try {
                    const layout = JSON.parse(cached);
                    state.gridLayouts[cacheKey] = layout;
                    return layout;
                } catch (e) {}
            }
        }
        return null;
    }

    async function renderProductsGrid() {
        elements.productsGrid.innerHTML = '';
        
        const currentCategory = state.activeCategory || 'ALL';
        const layout = await loadGridLayout(currentCategory, state.activeGridPage);

        if (!layout || !layout.slots || layout.slots.length === 0) {
            const filtered = currentCategory === 'ALL' ? state.products : state.products.filter(p => p.categoryId === currentCategory);
            const cols = 4;
            const rows = 4;
            elements.productsGrid.style.setProperty('--grid-cols', cols);
            elements.productsGrid.style.setProperty('--grid-rows', rows);

            const itemsPerPage = cols * rows;
            const totalPages = Math.max(1, Math.ceil(filtered.length / itemsPerPage));
            const currentPage = Math.min(state.activeGridPage, totalPages - 1);
            const pageProducts = filtered.slice(currentPage * itemsPerPage, (currentPage + 1) * itemsPerPage);

            pageProducts.forEach(prod => {
                const card = createProductCardElement(prod);
                elements.productsGrid.appendChild(card);
            });

            for (let i = pageProducts.length; i < itemsPerPage; i++) {
                const emptyEl = document.createElement('div');
                emptyEl.className = 'product-card-empty';
                elements.productsGrid.appendChild(emptyEl);
            }

            renderProductsGridPagination({ totalPages, pageIndex: currentPage });
            return;
        }

        const cols = layout.columnsCount || 4;
        const rows = layout.rowsCount || 4;
        elements.productsGrid.style.setProperty('--grid-cols', cols);
        elements.productsGrid.style.setProperty('--grid-rows', rows);

        const totalSlots = cols * rows;
        const sortedSlots = [...layout.slots].sort((a, b) => a.slotIndex - b.slotIndex);

        for (let i = 0; i < totalSlots; i++) {
            const r = Math.floor(i / cols);
            const c = i % cols;
            const slot = sortedSlots.find(s => s.rowIndex === r && s.columnIndex === c) || {
                rowIndex: r,
                columnIndex: c,
                slotIndex: i,
                productId: null
            };

            if (slot.productId && !slot.isDisabled) {
                const prod = state.products.find(p => p.id === slot.productId) || slot.product;
                if (prod) {
                    const card = createProductCardElement(prod, slot);
                    elements.productsGrid.appendChild(card);
                } else {
                    const emptyEl = document.createElement('div');
                    emptyEl.className = 'product-card-empty';
                    elements.productsGrid.appendChild(emptyEl);
                }
            } else {
                const emptyEl = document.createElement('div');
                emptyEl.className = 'product-card-empty';
                elements.productsGrid.appendChild(emptyEl);
            }
        }

        renderProductsGridPagination(layout);
    }

    function renderProductsGridPagination(layout) {
        if (!elements.gridPaginationBar) return;

        const totalPages = (layout && layout.totalPages) ? layout.totalPages : 1;
        const currentPage = (layout && layout.pageIndex !== undefined) ? layout.pageIndex : state.activeGridPage;

        if (totalPages <= 1) {
            elements.gridPaginationBar.style.display = 'none';
            return;
        }

        elements.gridPaginationBar.style.display = 'flex';
        elements.gridPageLabel.textContent = t('order.grid_page_label', { page: currentPage + 1, total: totalPages });
        elements.btnPrevGridPage.disabled = currentPage <= 0;
        elements.btnNextGridPage.disabled = currentPage >= totalPages - 1;

        elements.gridPageDots.innerHTML = '';
        for (let p = 0; p < totalPages; p++) {
            const dot = document.createElement('div');
            dot.className = `grid-page-dot ${p === currentPage ? 'active' : ''}`;
            dot.title = t('order.grid_page_dot_title', { page: p + 1 });
            dot.addEventListener('click', () => {
                state.activeGridPage = p;
                renderProductsGrid();
            });
            elements.gridPageDots.appendChild(dot);
        }
    }

    function setupSalesGridPaginationListeners() {
        if (elements.btnPrevGridPage) {
            elements.btnPrevGridPage.addEventListener('click', () => {
                if (state.activeGridPage > 0) {
                    state.activeGridPage--;
                    renderProductsGrid();
                }
            });
        }

        if (elements.btnNextGridPage) {
            elements.btnNextGridPage.addEventListener('click', async () => {
                const layout = await loadGridLayout(state.activeCategory, state.activeGridPage);
                const totalPages = layout ? layout.totalPages : 1;
                if (state.activeGridPage < totalPages - 1) {
                    state.activeGridPage++;
                    renderProductsGrid();
                }
            });
        }

        // Tactile Touch Swipe Support (Left/Right)
        if (elements.productsGrid) {
            let touchStartX = 0;
            let touchStartY = 0;

            elements.productsGrid.addEventListener('touchstart', (e) => {
                if (e.touches && e.touches.length > 0) {
                    touchStartX = e.touches[0].clientX;
                    touchStartY = e.touches[0].clientY;
                }
            }, { passive: true });

            elements.productsGrid.addEventListener('touchend', async (e) => {
                if (e.changedTouches && e.changedTouches.length > 0) {
                    const deltaX = e.changedTouches[0].clientX - touchStartX;
                    const deltaY = e.changedTouches[0].clientY - touchStartY;

                    // Horizontal swipe threshold: > 50px horizontal and < 40px vertical
                    if (Math.abs(deltaX) > 50 && Math.abs(deltaY) < 40) {
                        const layout = await loadGridLayout(state.activeCategory, state.activeGridPage);
                        const totalPages = layout ? layout.totalPages : 1;

                        if (deltaX < 0 && state.activeGridPage < totalPages - 1) {
                            // Swipe Left -> Next Page
                            state.activeGridPage++;
                            renderProductsGrid();
                        } else if (deltaX > 0 && state.activeGridPage > 0) {
                            // Swipe Right -> Prev Page
                            state.activeGridPage--;
                            renderProductsGrid();
                        }
                    }
                }
            }, { passive: true });
        }
    }

    function createProductCardElement(prod, slot = null) {
        const card = document.createElement('div');
        card.className = 'product-card';
        const color = (slot && slot.customColorHex) || prod.colorHex;
        if (color) {
            card.style.borderTop = `4px solid ${color}`;
        }
        const displayName = (slot && slot.customLabel) ? slot.customLabel : prod.name;
        const hasOptions = prod.modifierGroups && prod.modifierGroups.length > 0;

        // Check if Happy Hour is active for this product
        const hhItem = state.happyHour && state.happyHour.isActive ? state.happyHour.pricingTable[prod.id] : null;

        let priceHtml = `<div class="product-price">${Number(prod.price).toFixed(2)} €</div>`;
        if (hhItem && hhItem.happyHourPrice < hhItem.standardPrice) {
            priceHtml = `
                <div class="product-price-hh-container">
                    <span class="product-price-strike">${Number(hhItem.standardPrice).toFixed(2)} €</span>
                    <span class="product-price-hh">🍻 ${Number(hhItem.happyHourPrice).toFixed(2)} €</span>
                </div>
            `;
        }

        card.innerHTML = `
            <div class="product-card-top">
                <span class="product-badge-station">${prod.preparationStationId || 'HOT'}</span>
                ${hasOptions ? `<span style="font-size:0.68rem; background:rgba(14, 165, 233, 0.9); color:white; padding:2px 6px; border-radius:4px; font-weight:700; box-shadow:0 2px 4px rgba(0,0,0,0.3);">⚙️ Options</span>` : ''}
            </div>
            <div class="product-name">${displayName}</div>
            ${priceHtml}
        `;
        card.addEventListener('pointerdown', () => {
            card.style.transform = 'scale(0.95)';
        });
        card.addEventListener('pointerup', () => {
            card.style.transform = '';
        });
        card.addEventListener('click', () => handleProductClick(prod));
        return card;
    }

    // ==================== MODIFIERS & PAID EXTRAS (NF525 COMPLIANT) ====================
    let currentModifierProduct = null;
    let currentModifierCourse = 'Direct';
    let currentSelectedModifiers = [];

    function handleProductClick(prod, course = 'Direct') {
        if (prod.modifierGroups && prod.modifierGroups.length > 0) {
            openModifiersModal(prod, course);
        } else {
            addToCart(prod, course);
        }
    }

    function openModifiersModal(prod, course = 'Direct') {
        currentModifierProduct = prod;
        currentModifierCourse = course;
        currentSelectedModifiers = [];

        elements.modifiersModalTitle.textContent = prod.name;
        elements.modifiersModalSubtitle.textContent = t('order.modifier_base_price', { price: Number(prod.price).toFixed(2), vat: prod.taxRatePercent || 10 });
        elements.inputModifiersComment.value = '';
        elements.modifiersGroupsContainer.innerHTML = '';

        prod.modifierGroups.forEach(group => {
            const groupCard = document.createElement('div');
            groupCard.className = 'modifier-group-card';
            groupCard.style.cssText = 'background:rgba(255,255,255,0.03); border:1px solid rgba(255,255,255,0.08); border-radius:8px; padding:14px;';

            const header = document.createElement('div');
            header.style.cssText = 'display:flex; justify-content:space-between; align-items:center; margin-bottom:12px;';
            header.innerHTML = `
                <div style="font-weight:700; color:#f1f5f9; font-size:0.95rem;">${group.groupName}</div>
                <span style="font-size:0.75rem; font-weight:600; padding:2px 8px; border-radius:4px; ${group.isMandatory ? 'background:rgba(245, 158, 11, 0.15); color:#fbbf24; border:1px solid rgba(245, 158, 11, 0.3);' : 'background:rgba(148, 163, 184, 0.1); color:#94a3b8;'}">
                    ${group.isSingleChoice ? (group.isMandatory ? t('order.modifier_choice_required') : t('order.modifier_choice_max')) : (group.minSelections > 0 ? t('order.modifier_min_selections', { min: group.minSelections }) : t('order.modifier_optional'))}
                </span>
            `;
            groupCard.appendChild(header);

            const optionsGrid = document.createElement('div');
            optionsGrid.style.cssText = 'display:grid; grid-template-columns:repeat(auto-fill, minmax(140px, 1fr)); gap:10px;';

            group.options.forEach(opt => {
                const isPreSelected = opt.isDefault || false;
                if (isPreSelected) {
                    currentSelectedModifiers.push({
                        groupId: group.id,
                        optionId: opt.id,
                        optionName: opt.name,
                        extraPrice: Number(opt.extraPrice) || 0
                    });
                }

                const optBtn = document.createElement('button');
                optBtn.type = 'button';
                optBtn.className = `btn-modifier-option ${isPreSelected ? 'selected' : ''}`;
                optBtn.setAttribute('data-group-id', group.id);
                optBtn.setAttribute('data-option-id', opt.id);
                optBtn.style.cssText = `
                    display:flex; flex-direction:column; align-items:flex-start; justify-content:center;
                    padding:10px 12px; border-radius:8px; cursor:pointer; text-align:start; transition:all 0.15s ease;
                    border: 1px solid ${isPreSelected ? '#38bdf8' : 'rgba(255,255,255,0.1)'};
                    background: ${isPreSelected ? 'rgba(56, 189, 248, 0.15)' : 'rgba(15, 23, 42, 0.6)'};
                    color: ${isPreSelected ? '#38bdf8' : '#e2e8f0'};
                `;

                optBtn.innerHTML = `
                    <span style="font-weight:600; font-size:0.85rem; line-height:1.2;">${opt.name}</span>
                    ${Number(opt.extraPrice) > 0 ? `<span style="font-size:0.75rem; color:#10b981; font-weight:700; margin-top:4px;">+${Number(opt.extraPrice).toFixed(2)} €</span>` : `<span style="font-size:0.7rem; color:#64748b; margin-top:4px;">${t('order.modifier_included')}</span>`}
                `;

                optBtn.addEventListener('click', () => {
                    const isAlreadySelected = currentSelectedModifiers.some(m => m.optionId === opt.id);
                    if (group.isSingleChoice) {
                        currentSelectedModifiers = currentSelectedModifiers.filter(m => m.groupId !== group.id);
                        if (!isAlreadySelected || group.isMandatory) {
                            currentSelectedModifiers.push({
                                groupId: group.id,
                                optionId: opt.id,
                                optionName: opt.name,
                                extraPrice: Number(opt.extraPrice) || 0
                            });
                        }
                    } else {
                        if (isAlreadySelected) {
                            currentSelectedModifiers = currentSelectedModifiers.filter(m => m.optionId !== opt.id);
                        } else {
                            const currentCount = currentSelectedModifiers.filter(m => m.groupId === group.id).length;
                            if (group.maxSelections > 0 && currentCount >= group.maxSelections) {
                                showToast(t('order.modifier_max_warning', { max: group.maxSelections, group: group.groupName }), 'warning');
                                return;
                            }
                            currentSelectedModifiers.push({
                                groupId: group.id,
                                optionId: opt.id,
                                optionName: opt.name,
                                extraPrice: Number(opt.extraPrice) || 0
                            });
                        }
                    }

                    optionsGrid.querySelectorAll('.btn-modifier-option').forEach(b => {
                        const bOptId = b.getAttribute('data-option-id');
                        const sel = currentSelectedModifiers.some(m => m.optionId === bOptId);
                        b.style.border = `1px solid ${sel ? '#38bdf8' : 'rgba(255,255,255,0.1)'}`;
                        b.style.background = sel ? 'rgba(56, 189, 248, 0.15)' : 'rgba(15, 23, 42, 0.6)';
                        b.style.color = sel ? '#38bdf8' : '#e2e8f0';
                    });

                    updateModifiersModalTotals();
                });

                optionsGrid.appendChild(optBtn);
            });

            groupCard.appendChild(optionsGrid);
            elements.modifiersGroupsContainer.appendChild(groupCard);
        });

        updateModifiersModalTotals();
        elements.modifiersModal.classList.add('active');
    }

    function updateModifiersModalTotals() {
        if (!currentModifierProduct) return;
        const totalExtra = currentSelectedModifiers.reduce((sum, m) => sum + m.extraPrice, 0);
        const effectivePrice = Number(currentModifierProduct.price) + totalExtra;

        elements.lblModifiersExtraTotal.textContent = `+${totalExtra.toFixed(2)} €`;
        elements.lblModifiersEffectivePrice.textContent = `${effectivePrice.toFixed(2)} €`;
    }

    function addToCart(product, course = 'Direct', selectedModifiers = [], modifiersPriceExtra = 0, kitchenComment = '') {
        const modStrings = selectedModifiers.map(m => m.extraPrice > 0 ? `${m.optionName} (+${m.extraPrice.toFixed(2)} €)` : m.optionName);
        const modKey = modStrings.slice().sort().join('|');

        // Resolve Happy Hour price for product if active
        let effectiveUnitPrice = Number(product.price);
        let isHhApplied = false;
        let originalUnitPrice = null;
        let scheduleId = null;

        const isTakeaway = isTakeawayMode();
        const hhAllowed = !isTakeaway || (state.happyHour && state.happyHour.appliesToTakeaway);

        if (state.happyHour && state.happyHour.isActive && hhAllowed) {
            const hhItem = state.happyHour.pricingTable[product.id];
            if (hhItem && hhItem.happyHourPrice < hhItem.standardPrice) {
                effectiveUnitPrice = hhItem.happyHourPrice;
                isHhApplied = true;
                originalUnitPrice = hhItem.standardPrice;
                scheduleId = state.happyHour.activeScheduleId;
            }
        }

        const existing = state.cart.find(item =>
            item.product.id === product.id &&
            !item.lineId &&
            item.course === course &&
            item.isHappyHourApplied === isHhApplied &&
            (item.product.price === effectiveUnitPrice) &&
            (item.modifiersPriceExtra || 0) === modifiersPriceExtra &&
            (item.modifiers || []).slice().sort().join('|') === modKey &&
            (item.kitchenComment || '') === kitchenComment
        );

        if (existing) {
            existing.quantity += 1;
        } else {
            state.cart.push({
                product: {
                    id: product.id,
                    name: product.name,
                    price: effectiveUnitPrice,
                    taxRatePercent: product.taxRatePercent || 10.0,
                    taxRateTakeawayPercent: product.taxRateTakeawayPercent ?? null,
                    preparationStationId: product.preparationStationId || null
                },
                quantity: 1,
                course: course,
                isDispatched: false,
                isComp: false,
                discountPercent: 0,
                isHappyHourApplied: isHhApplied,
                originalUnitPrice: originalUnitPrice,
                appliedHappyHourScheduleId: scheduleId,
                modifiers: modStrings,
                modifiersPriceExtra: modifiersPriceExtra,
                kitchenComment: kitchenComment
            });
        }
        renderCart();
        showToast(t('order.item_added_toast', { name: product.name, course: courseLabel(course) }), 'success');
    }

    // ==================== AUTO-SAVE & TABLE RECALL ====================
    async function ensureAuthToken() {
        if (state.token) return state.token;
        try {
            const res = await fetch('/api/auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pin: '1234' })
            });
            if (res.ok) {
                const data = await res.json();
                state.token = data.token;
                localStorage.setItem('pos_jwt_token', data.token);
                startPosHub();
                state.operator = { name: data.operatorName, role: data.role, id: data.operatorId };
                return data.token;
            }
        } catch (e) {
            console.warn('ensureAuthToken failed:', e);
        }
        return null;
    }

    /**
     * Enregistre les articles en brouillon (sans lineId) côté serveur.
     * L'API ne permet que l'ajout de lignes : les brouillons restent donc modifiables localement
     * et ne sont envoyés qu'au moment utile (envoi cuisine, paiement, remise, attente, transfert,
     * changement de table ou fermeture de la page). Renvoie true si tout est enregistré.
     */
    async function saveActiveCartToServer(options = {}) {
        if (!state.activeTable) return true;
        const drafts = state.cart.filter(i => !i.lineId);
        if (drafts.length === 0) return true;

        const courseMap = { 'Direct': 0, 'Suite': 1, 'Dessert': 2, 'OnDemand': 3 };
        const itemsPayload = drafts.map(i => {
            const modifiers = (i.modifiers || []).slice();
            if (i.kitchenComment && i.kitchenComment.trim()) {
                modifiers.push(`💬 ${i.kitchenComment.trim()}`);
            }
            return {
                productId: i.product.id,
                productName: i.product.name,
                quantity: i.quantity,
                unitPrice: i.product.price,
                taxRatePercent: i.product.taxRatePercent || 10.0,
                taxRateTakeawayPercent: i.product.taxRateTakeawayPercent ?? null,
                preparationStationId: i.product.preparationStationId || null,
                modifiers: modifiers,
                modifiersPriceExtra: i.modifiersPriceExtra || 0,
                course: courseMap[i.course] || 0,
                isHappyHourApplied: !!i.isHappyHourApplied,
                originalUnitPrice: i.originalUnitPrice || null,
                appliedHappyHourScheduleId: i.appliedHappyHourScheduleId || null
            };
        });

        const table = state.activeTable;
        const isCounter = table === 'Comptoir';
        const wantedDestination = isCounter ? destinationToEnum(state.destination) : destinationToEnum('EatIn');

        try {
            await ensureAuthToken();
            const res = await fetch(`/api/tables/${encodeURIComponent(table)}/items`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ items: itemsPayload }),
                keepalive: !!options.keepalive
            });
            if (!res.ok) {
                if (!options.keepalive) showToast(t('order.save_ticket_error', { status: res.status }), 'error');
                return false;
            }
            if (options.keepalive) return true;

            const data = await res.json();
            // Le serveur a pris en compte tous les brouillons : on repart de sa version du ticket.
            if (state.activeTable === table) {
                state.cart = state.cart.filter(i => i.lineId || !drafts.includes(i));
                hydrateCartFromOrder(data, true);
            }
            // Le serveur crée les commandes « À emporter » par défaut ; une table est servie sur place.
            if (data && data.orderId && data.destination !== wantedDestination) {
                await fetch(`/api/orders/${data.orderId}/destination`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ destination: wantedDestination })
                });
            }
            renderCart();
            return true;
        } catch (err) {
            console.error('Erreur enregistrement du ticket:', err);
            if (!options.keepalive) showToast(t('order.save_ticket_network_error'), 'error');
            return false;
        }
    }

    // Filet de sécurité : les brouillons sont envoyés si la page est fermée ou masquée.
    window.addEventListener('pagehide', () => { saveActiveCartToServer({ keepalive: true }); });
    document.addEventListener('visibilitychange', () => {
        if (document.visibilityState === 'hidden') saveActiveCartToServer();
    });

    async function loadActiveTableOrder(tableNumber) {
        const switching = state.activeTable && state.activeTable !== tableNumber;
        // Avant de quitter une table, ses brouillons sont enregistrés côté serveur.
        if (switching) {
            await saveActiveCartToServer();
            state.cart = [];
            state.amountPaid = 0;
            state.splitPlan = null;
        }

        state.activeTable = tableNumber;
        elements.activeTableBadge.textContent = t('order.table_label', { number: tableNumber });

        try {
            await ensureAuthToken();
            const res = await fetch(`/api/tables/${encodeURIComponent(tableNumber)}/order`);

            if (res.ok) {
                const orderData = await res.json();
                state.activeCovers = orderData.coversCount || 2;
                elements.activeCoversBadge.textContent = t('order.covers_badge', { count: state.activeCovers });
                hydrateCartFromOrder(orderData, true);
                renderCart();
            } else if (res.status === 404) {
                // Aucune commande enregistrée : on conserve les éventuels brouillons de cette table.
                state.activeOrderId = null;
                state.globalDiscount = null;
                state.amountPaid = 0;
                state.cart = state.cart.filter(i => !i.lineId);
                elements.activeCoversBadge.textContent = t('order.covers_badge_free', { count: 2 });
                renderCart();
            } else if (res.status === 401) {
                // Non authentifié : on ne vide pas le panier, le terminal redemande le PIN.
                console.warn(`Rappel table ${tableNumber} refusé (401 Non Authentifié). Authentification requise.`);
            } else {
                showToast(t('order.table_recall_error', { status: res.status, table: tableNumber }), 'error');
            }
        } catch (err) {
            console.error('Erreur rappel table:', err);
            showToast(t('order.table_load_error', { table: tableNumber }), 'error');
        }
    }

    // Enum serveur OrderDestination : Takeaway = 0, EatIn = 1.
    function destinationToEnum(dest) {
        return dest === 'EatIn' ? 1 : 0;
    }

    function destinationFromEnum(value) {
        return value === 1 || value === 'EatIn' ? 'EatIn' : 'Takeaway';
    }

    function isTakeawayMode() {
        return state.destination === 'Takeaway';
    }

    /** Ligne de commande serveur → ligne de panier (enregistrée : porte un lineId). */
    function mapServerLine(line) {
        return {
            lineId: line.lineId,
            product: {
                id: line.productId,
                name: line.productName,
                price: line.unitPrice,
                taxRatePercent: line.taxRatePercent,
                taxRateTakeawayPercent: line.taxRateTakeawayPercent,
                preparationStationId: line.preparationStationId
            },
            quantity: line.quantity,
            course: typeof line.course === 'number' ? getCourseNameFromEnum(line.course) : (line.course || 'Direct'),
            isDispatched: line.isDispatched,
            isComp: line.isComp || false,
            discountPercent: line.discountPercent || 0,
            isHappyHourApplied: line.isHappyHourApplied || false,
            originalUnitPrice: line.originalUnitPrice || null,
            appliedHappyHourScheduleId: line.appliedHappyHourScheduleId || null,
            modifiers: line.modifiersSummary || [],
            modifiersPriceExtra: Number(line.modifiersPriceExtra) || 0
        };
    }

    /** Recharge le panier depuis une commande serveur en conservant les brouillons non enregistrés. */
    function hydrateCartFromOrder(orderData, keepDrafts = true) {
        const drafts = keepDrafts ? state.cart.filter(i => !i.lineId) : [];
        state.activeOrderId = orderData.orderId;
        state.globalDiscount = orderData.globalDiscountType !== null && orderData.globalDiscountType !== undefined ? {
            type: orderData.globalDiscountType,
            value: orderData.globalDiscountValue,
            reason: orderData.globalDiscountReason
        } : null;
        state.cart = (orderData.lines || []).map(mapServerLine).concat(drafts);
    }

    function getCourseNameFromEnum(val) {
        const map = ['Direct', 'Suite', 'Dessert', 'OnDemand'];
        return map[val] || 'Direct';
    }

    function cycleCourse(currentCourse) {
        const order = ['Direct', 'Suite', 'Dessert'];
        const nextIdx = (order.indexOf(currentCourse) + 1) % order.length;
        return order[nextIdx];
    }

    /** Libellé affiché pour un service : la valeur interne (envoyée au serveur) n'est pas traduite. */
    function courseLabel(course) {
        if (course === 'Suite') return t('order.course_suite');
        if (course === 'Dessert') return t('order.course_dessert');
        if (course === 'OnDemand') return t('order.course_ondemand');
        return t('order.course_direct');
    }

    function renderCart() {
        elements.cartItemsList.innerHTML = '';
        let totalHt = 0;
        let totalVat = 0;
        let totalTtc = 0;

        if (state.cart.length === 0) {
            elements.cartItemsList.innerHTML = `
                <div style="text-align:center;color:#64748b;padding:40px 10px;">
                    <div style="font-size:2.5rem;margin-bottom:8px;">🍽️</div>
                    <strong>${t('order.cart_empty_title', { table: state.activeTable })}</strong>
                    <div style="font-size:0.8rem;margin-top:4px;">${t('order.cart_empty_hint')}</div>
                </div>
            `;
        }

        state.cart.forEach((item, index) => {
            const unitPrice = Number(item.product.price || 0) + Number(item.modifiersPriceExtra || 0);
            let lineTtc = item.isComp ? 0 : (unitPrice * item.quantity);
            if (item.discountPercent > 0 && !item.isComp) {
                lineTtc = lineTtc * (1.0 - (item.discountPercent / 100.0));
            }

            const isTakeaway = isTakeawayMode();
            const effectiveVatPercent = (isTakeaway && item.product.taxRateTakeawayPercent !== undefined && item.product.taxRateTakeawayPercent !== null)
                ? Number(item.product.taxRateTakeawayPercent)
                : Number(item.product.taxRatePercent || 10.0);

            const vatRate = effectiveVatPercent / 100.0;
            const lineHt = lineTtc / (1.0 + vatRate);
            const lineVat = lineTtc - lineHt;

            totalHt += lineHt;
            totalVat += lineVat;
            totalTtc += lineTtc;

            const courseClass = (item.course || 'Direct').toLowerCase();

            const row = document.createElement('div');
            row.className = `cart-item-row ${item.isDispatched ? 'dispatched' : 'pending'}`;
            row.innerHTML = `
                <div class="cart-item-info">
                    <div style="display:flex;align-items:center;gap:6px;flex-wrap:wrap;">
                        <span class="cart-item-title">${item.product.name}</span>
                        ${item.isHappyHourApplied ? `<span class="cart-item-badge-hh" title="${t('order.hh_badge_title')}">🍻 [HH]</span>` : ''}
                        <span class="course-badge ${courseClass}" data-idx="${index}" title="${t('order.course_badge_title')}">${courseLabel(item.course || 'Direct')}</span>
                        ${item.isComp ? `<span class="comp-badge">${t('order.comp_badge')}</span>` : ''}
                        ${item.isDispatched
                            ? `<span class="badge-dispatched" title="${t('order.dispatched_badge_title')}">${t('order.dispatched_badge')}</span>`
                            : item.lineId
                                ? `<span class="badge-pending" title="${t('order.pending_saved_title')}">${t('order.pending_saved_badge')}</span>`
                                : `<span class="badge-pending" title="${t('order.pending_new_title')}">${t('order.pending_new_badge')}</span>`}
                    </div>
                    <span class="cart-item-meta">${unitPrice.toFixed(2)} € × ${item.quantity} ${item.originalUnitPrice ? `<span style="text-decoration:line-through; color:#94a3b8; margin-inline-start:4px;">(${Number(item.originalUnitPrice).toFixed(2)} €)</span>` : ''} ${item.modifiersPriceExtra ? `<span style="color:#10b981; font-weight:600;">${t('order.modifiers_extra_note', { amount: Number(item.modifiersPriceExtra).toFixed(2) })}</span>` : ''} ${t('order.cart_item_vat_suffix', { vat: effectiveVatPercent })}</span>
                    ${item.modifiers && item.modifiers.length > 0 ? `<div style="font-size:0.75rem;color:#f59e0b;margin-top:2px;">↳ ${item.modifiers.join(', ')}</div>` : ''}
                    ${item.kitchenComment ? `<div style="font-size:0.72rem;color:#94a3b8;font-style:italic;margin-top:1px;">💬 ${item.kitchenComment}</div>` : ''}
                </div>
                <div class="cart-item-controls">
                    <button class="btn-qty" data-action="minus" data-idx="${index}">-</button>
                    <span style="font-weight:700;">${item.quantity}</span>
                    <button class="btn-qty" data-action="plus" data-idx="${index}">+</button>
                    <span class="cart-item-price" style="${item.isComp ? 'text-decoration:line-through;color:#94a3b8;' : ''}">${lineTtc.toFixed(2)} €</span>
                </div>
            `;
            elements.cartItemsList.appendChild(row);
        });

        // Apply global discount if active
        if (state.globalDiscount) {
            if (state.globalDiscount.type === 0 || state.globalDiscount.type === 'Percentage') {
                const discVal = state.globalDiscount.value;
                totalTtc = Math.max(0, totalTtc * (1.0 - (discVal / 100.0)));
            } else if (state.globalDiscount.type === 1 || state.globalDiscount.type === 'FixedAmount') {
                totalTtc = Math.max(0, totalTtc - state.globalDiscount.value);
            }
        }

        elements.summaryHt.textContent = `${totalHt.toFixed(2)} €`;
        elements.summaryVat.textContent = `${totalVat.toFixed(2)} €`;
        if (document.getElementById('summaryVatLabel')) {
            document.getElementById('summaryVatLabel').textContent = isTakeawayMode()
                ? t('order.vat_label')
                : t('order.vat_label_eatin');
        }
        elements.summaryTtc.textContent = `${totalTtc.toFixed(2)} €`;

        // Attach quantity buttons
        elements.cartItemsList.querySelectorAll('.btn-qty').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const idx = parseInt(btn.getAttribute('data-idx'));
                const action = btn.getAttribute('data-action');
                const item = state.cart[idx];
                if (!item) return;
                if (action === 'plus') {
                    if (item.lineId) {
                        // Ligne déjà enregistrée : on ajoute un brouillon identique (fusionné côté serveur).
                        const draft = state.cart.find(i => !i.lineId && i.product.id === item.product.id && i.course === item.course
                            && (i.modifiers || []).join('|') === (item.modifiers || []).join('|') && !i.kitchenComment
                            && (i.modifiersPriceExtra || 0) === (item.modifiersPriceExtra || 0) && i.product.price === item.product.price);
                        if (draft) {
                            draft.quantity += 1;
                        } else {
                            state.cart.push({ ...item, product: { ...item.product }, modifiers: [...(item.modifiers || [])], lineId: null, quantity: 1, isDispatched: false, isComp: false, discountPercent: 0, kitchenComment: '' });
                        }
                    } else {
                        item.quantity += 1;
                    }
                } else if (action === 'minus') {
                    if (item.lineId) {
                        showToast(t('order.item_locked_warning'), 'warning');
                        return;
                    }
                    item.quantity -= 1;
                    if (item.quantity <= 0) {
                        state.cart.splice(idx, 1);
                    }
                }
                renderCart();
            });
        });

        // Attach course cycling on course badge click
        elements.cartItemsList.querySelectorAll('.course-badge').forEach(badge => {
            badge.addEventListener('click', (e) => {
                const idx = parseInt(badge.getAttribute('data-idx'));
                if (state.cart[idx] && !state.cart[idx].lineId) {
                    state.cart[idx].course = cycleCourse(state.cart[idx].course || 'Direct');
                    renderCart();
                    showToast(t('order.course_changed_toast', { course: courseLabel(state.cart[idx].course) }), 'info');
                }
            });
        });
    }

    elements.btnClearCart.addEventListener('click', () => {
        if (state.cart.length === 0) return;

        // Seuls les brouillons peuvent être retirés : l'API ne supprime pas de lignes enregistrées.
        const drafts = state.cart.filter(i => !i.lineId);
        const saved = state.cart.filter(i => i.lineId);

        if (drafts.length === 0) {
            showToast(t('order.clear_locked_error'), 'error');
            return;
        }
        state.cart = saved;
        renderCart();
        showToast(saved.length > 0 ? t('order.items_removed_toast', { count: drafts.length }) : t('order.cart_cleared_toast'), 'info');
    });

    // 1. Send to Kitchen
    elements.btnSendKitchen.addEventListener('click', async () => {
        if (state.cart.length === 0) {
            showToast(t('order.cart_empty_error'), 'error');
            return;
        }

        try {
            if (!(await saveActiveCartToServer())) return;

            const dispatchRes = await fetch(`/api/tables/${encodeURIComponent(state.activeTable)}/dispatch`, { method: 'POST' });
            if (!dispatchRes.ok) {
                showToast(t('order.kitchen_send_status_error', { status: dispatchRes.status }), 'error');
                return;
            }
            showToast(t('order.kitchen_sent_toast', { table: state.activeTable }), 'success');

            if (state.activeTable === 'Comptoir') {
                await loadActiveTableOrder('Comptoir');
            } else {
                state.cart = [];
                state.activeOrderId = null;
                renderCart();
                switchView('floorPlanView');
                await loadFloorPlanData();
            }
            
            await loadKdsData();
        } catch (err) {
            console.error('Erreur envoi cuisine:', err);
            showToast(t('order.kitchen_send_error'), 'error');
        }
    });

    // 2. Fire Suite (US3)
    if (elements.btnFireSuite) {
        elements.btnFireSuite.addEventListener('click', async () => {
            if (!state.activeTable) return;
            try {
                const res = await fetch(`/api/tables/${state.activeTable}/fire-suite`, { method: 'POST' });
                if (res.ok) {
                    showToast(t('order.fire_suite_toast', { table: state.activeTable }), 'success');
                    await loadKdsData();
                } else {
                    showToast(t('order.fire_suite_failed'), 'error');
                }
            } catch (err) {
                showToast(t('order.fire_suite_error'), 'error');
            }
        });
    }

    // ==================== MODALS & PAYMENTS ====================
    function setupModals() {
        // Modifiers & Extras Modal
        if (elements.btnCloseModifiersModal) {
            elements.btnCloseModifiersModal.addEventListener('click', () => {
                elements.modifiersModal.classList.remove('active');
                currentModifierProduct = null;
            });
        }
        if (elements.btnCancelModifiers) {
            elements.btnCancelModifiers.addEventListener('click', () => {
                elements.modifiersModal.classList.remove('active');
                currentModifierProduct = null;
            });
        }
        if (elements.btnConfirmModifiers) {
            elements.btnConfirmModifiers.addEventListener('click', () => {
                if (!currentModifierProduct) return;

                // Validate mandatory groups
                for (const group of (currentModifierProduct.modifierGroups || [])) {
                    if (group.isMandatory) {
                        const count = currentSelectedModifiers.filter(m => m.groupId === group.id).length;
                        if (count < (group.minSelections || 1)) {
                            showToast(t('order.modifier_min_warning', { min: group.minSelections || 1, group: group.groupName }), 'warning');
                            return;
                        }
                    }
                }

                const totalExtra = currentSelectedModifiers.reduce((sum, m) => sum + m.extraPrice, 0);
                const comment = elements.inputModifiersComment.value.trim();

                addToCart(
                    currentModifierProduct,
                    currentModifierCourse,
                    currentSelectedModifiers,
                    totalExtra,
                    comment
                );

                elements.modifiersModal.classList.remove('active');
                currentModifierProduct = null;
            });
        }

        // Transfer Modal (US1)
        elements.btnTransferModal.addEventListener('click', () => {
            elements.transferSourceTable.value = state.activeTable;
            elements.selectTargetTable.innerHTML = '';
            state.tables.forEach(t => {
                if (t.tableNumber !== state.activeTable) {
                    const opt = document.createElement('option');
                    opt.value = t.tableNumber;
                    opt.textContent = window.t('floor.table_option_label', { number: t.tableNumber, status: t.status === 0 || t.status === 'Free' ? window.t('floor.legend_free') : window.t('floor.table_option_status_merge') });
                    elements.selectTargetTable.appendChild(opt);
                }
            });
            elements.transferTableModal.classList.add('active');
        });

        document.getElementById('btnCloseTransferModal').addEventListener('click', () => {
            elements.transferTableModal.classList.remove('active');
        });
        document.getElementById('btnCancelTransfer').addEventListener('click', () => {
            elements.transferTableModal.classList.remove('active');
        });

        elements.formTransferTable.addEventListener('submit', async (e) => {
            e.preventDefault();
            const targetTable = elements.selectTargetTable.value;
            const mode = document.querySelector('input[name="transferMode"]:checked').value;
            const endpoint = mode === 'merge' ? `/api/tables/${state.activeTable}/merge` : `/api/tables/${state.activeTable}/transfer`;

            try {
                if (!(await saveActiveCartToServer())) return;
                await ensureAuthToken();
                const headers = { 'Content-Type': 'application/json' };
                if (state.token) headers['Authorization'] = `Bearer ${state.token}`;

                const res = await fetch(endpoint, {
                    method: 'POST',
                    headers: headers,
                    body: JSON.stringify({ targetTableNumber: targetTable })
                });
                const data = await res.json();
                if (res.ok && data.success) {
                    showToast(data.message || t('floor.table_transferred', { to: targetTable }), 'success');
                    elements.transferTableModal.classList.remove('active');
                    await loadFloorPlanData();
                    await loadActiveTableOrder(targetTable);
                } else {
                    showToast(data.message || t('floor.transfer_merge_error'), 'error');
                }
            } catch (err) {
                showToast(t('floor.transfer_network_error'), 'error');
            }
        });

        // Discount Modal (US2)
        elements.btnDiscountModal.addEventListener('click', async () => {
            if (state.cart.length > 0) {
                await saveActiveCartToServer();
            }
            elements.selectDiscountTarget.innerHTML = `<option value="global">${t('order.discount_target_global')}</option>`;
            state.cart.filter(item => item.lineId && !item.isComp).forEach(item => {
                const opt = document.createElement('option');
                opt.value = item.lineId;
                const lineTotal = (Number(item.product.price) + Number(item.modifiersPriceExtra || 0)) * item.quantity;
                opt.textContent = t('order.discount_target_comp', { qty: item.quantity, name: item.product.name, total: lineTotal.toFixed(2) });
                elements.selectDiscountTarget.appendChild(opt);
            });
            elements.discountModal.classList.add('active');
        });

        document.getElementById('btnCloseDiscountModal').addEventListener('click', () => {
            elements.discountModal.classList.remove('active');
        });
        document.getElementById('btnCancelDiscount').addEventListener('click', () => {
            elements.discountModal.classList.remove('active');
        });

        elements.selectDiscountTarget.addEventListener('change', () => {
            const isGlobal = elements.selectDiscountTarget.value === 'global';
            elements.groupDiscountType.style.display = isGlobal ? 'flex' : 'none';
            elements.groupDiscountValue.style.display = isGlobal ? 'flex' : 'none';
        });

        document.querySelectorAll('#groupDiscountType .btn-cap-quick').forEach(btn => {
            btn.addEventListener('click', () => {
                document.querySelectorAll('#groupDiscountType .btn-cap-quick').forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                elements.inputDiscountVal.value = btn.getAttribute('data-disc-val');
                elements.selectDiscountUnit.value = btn.getAttribute('data-disc-type') === 'Percent' ? 'Percentage' : 'FixedAmount';
            });
        });

        elements.selectDiscountReason.addEventListener('change', () => {
            elements.inputDiscountCustomReason.style.display = elements.selectDiscountReason.value === 'Autre motif' ? 'block' : 'none';
        });

        elements.btnResetDiscount.addEventListener('click', async () => {
            if (state.activeOrderId) {
                await fetch(`/api/orders/${state.activeOrderId}/discount`, { method: 'DELETE' });
                state.globalDiscount = null;
                showToast(t('order.discount_removed_toast'), 'info');
                elements.discountModal.classList.remove('active');
                await loadActiveTableOrder(state.activeTable);
            }
        });

        elements.formApplyDiscount.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (state.cart.length > 0) {
                await saveActiveCartToServer();
            }
            if (!state.activeOrderId) {
                showToast(t('order.no_active_order_error'), 'error');
                return;
            }

            const target = elements.selectDiscountTarget.value;
            let reason = elements.selectDiscountReason.value;
            if (reason === 'Autre motif') {
                reason = elements.inputDiscountCustomReason.value.trim() || 'Remise accordée'; // nf525-texte-fixe : motif écrit dans le journal d'audit, jamais traduit
            }

            try {
                if (target === 'global') {
                    const unit = elements.selectDiscountUnit.value;
                    const type = unit === 'Percentage' ? 0 : 1;
                    const val = parseFloat(elements.inputDiscountVal.value);

                    const res = await fetch(`/api/orders/${state.activeOrderId}/discount`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ type: type, value: val, reason: reason })
                    });
                    if (res.ok) {
                        showToast(t('order.discount_applied_toast', { val: val, unit: type === 0 ? '%' : '€' }), 'success');
                    }
                } else {
                    // Comp item
                    const itemId = target;
                    const res = await fetch(`/api/orders/${state.activeOrderId}/items/${itemId}/comp`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ reason: reason })
                    });
                    if (res.ok) {
                        showToast(t('order.item_comped_toast'), 'success');
                    }
                }
                elements.discountModal.classList.remove('active');
                await loadActiveTableOrder(state.activeTable);
            } catch (err) {
                showToast(t('order.discount_apply_error'), 'error');
            }
        });

        // Payment Modal & Tips (US4)
        elements.btnPayModal.addEventListener('click', async () => {
            if (getAmountDue() <= 0) {
                showToast(t('order.amount_zero_error'), 'error');
                return;
            }
            if (!(await saveActiveCartToServer())) return;
            state.splitPlan = null;
            state.splitActivePart = null;
            state.selectedTipPercent = 0;
            state.customTipAmount = 0;
            updateTipCalculation();
            syncPaymentPrintRow();
            syncTipVisibility();
            elements.paymentModal.classList.add('active');
        });

        elements.btnClosePayModal.addEventListener('click', () => {
            elements.paymentModal.classList.remove('active');
        });

        elements.tipPills.forEach(pill => {
            pill.addEventListener('click', () => {
                elements.tipPills.forEach(p => p.classList.remove('active'));
                pill.classList.add('active');
                if (pill.id === 'btnTipCustom') {
                    elements.tipCustomInputRow.style.display = 'block';
                    elements.inputCustomTip.focus();
                } else {
                    elements.tipCustomInputRow.style.display = 'none';
                    state.selectedTipPercent = parseInt(pill.getAttribute('data-tip-percent'));
                    state.customTipAmount = 0;
                    updateTipCalculation();
                }
            });
        });

        elements.inputCustomTip.addEventListener('input', () => {
            state.customTipAmount = parseFloat(elements.inputCustomTip.value) || 0;
            updateTipCalculation();
        });

        document.querySelectorAll('.btn-cash-bill').forEach(btn => {
            btn.addEventListener('click', () => {
                const amount = parseFloat(btn.getAttribute('data-cash'));
                executePayment(0, amount);
            });
        });

        document.querySelectorAll('.btn-tender:not(.btn-tender-hotel)').forEach(btn => {
            btn.addEventListener('click', () => {
                const tenderName = btn.getAttribute('data-tender');
                let method = 1; // CreditCard
                if (tenderName === 'Cash') method = 0;
                else if (tenderName === 'MealVoucher') method = 2;

                const totalWithTip = getFinalPayTotal();
                executePayment(method, totalWithTip);
            });
        });

        // Hotel Room Charge PMS (US5)
        elements.btnOpenRoomChargeModal.addEventListener('click', async () => {
            elements.paymentModal.classList.remove('active');
            await loadHotelRooms();
            elements.roomChargeTotalAmount.textContent = `${getFinalPayTotal().toFixed(2)} €`;
            elements.roomChargeModal.classList.add('active');
        });

        document.getElementById('btnCloseRoomChargeModal').addEventListener('click', () => {
            elements.roomChargeModal.classList.remove('active');
        });
        document.getElementById('btnCancelRoomCharge').addEventListener('click', () => {
            elements.roomChargeModal.classList.remove('active');
        });

        elements.selectHotelRoom.addEventListener('change', () => {
            const roomNum = elements.selectHotelRoom.value;
            const room = state.hotelRooms.find(r => r.roomNumber === roomNum);
            if (room) {
                elements.roomGuestName.textContent = room.guestName;
                const avail = (room.maxCreditLimit - room.currentBalance).toFixed(2);
                elements.roomCreditAvailable.textContent = `${avail} €`;
            }
        });

        elements.formRoomCharge.addEventListener('submit', async (e) => {
            e.preventDefault();
            const roomNum = elements.selectHotelRoom.value;
            const room = state.hotelRooms.find(r => r.roomNumber === roomNum);
            if (!room) return;

            if (!(await saveActiveCartToServer())) return;
            const baseAmount = getAmountDue();
            const tipAmount = getTipAmount();
            const sigData = elements.signatureCanvas.toDataURL('image/png');

            try {
                const res = await fetch('/api/hotel/room-charge', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({
                        orderId: state.activeOrderId || '00000000-0000-0000-0000-000000000000',
                        tableNumber: state.activeTable,
                        roomNumber: roomNum,
                        guestName: room.guestName,
                        amount: baseAmount,
                        tipAmount: tipAmount,
                        signatureDataUrl: sigData,
                        notes: `Facturation chambre ${roomNum}` // nf525-texte-fixe : note stockée telle quelle côté serveur
                    })
                });

                const data = await res.json();
                if (res.ok && data.success) {
                    showToast(data.message || t('payment.room_charge_success_toast'), 'success');
                    elements.roomChargeModal.classList.remove('active');
                    state.cart = [];
                    state.activeOrderId = null;
                    renderCart();
                    await loadFloorPlanData();
                } else {
                    showToast(data.message || t('payment.room_charge_failed_toast'), 'error');
                }
            } catch (err) {
                showToast(t('payment.room_charge_network_error'), 'error');
            }
        });

        // Split Bill Modal
        elements.btnSplitBill.addEventListener('click', async () => {
            if (getAmountDue() <= 0) {
                showToast(t('payment.split_bill_empty_error'), 'error');
                return;
            }
            if (!(await saveActiveCartToServer())) return;
            updateSplitPartitions();
            elements.splitBillModal.classList.add('active');
        });

        document.getElementById('btnCloseSplitModal').addEventListener('click', () => {
            elements.splitBillModal.classList.remove('active');
        });

        elements.btnSplitMinus.addEventListener('click', () => {
            if (state.splitGuests > 2) {
                state.splitGuests -= 1;
                updateSplitPartitions();
            }
        });

        elements.btnSplitPlus.addEventListener('click', () => {
            if (state.splitGuests < 10) {
                state.splitGuests += 1;
                updateSplitPartitions();
            }
        });

        elements.btnConfirmSplit.addEventListener('click', () => {
            elements.splitBillModal.classList.remove('active');
            // Parts figées au centime près : le reste de la division va aux premiers convives.
            state.splitPlan = { parts: splitIntoParts(getAmountDue(), state.splitGuests), index: 0 };
            showCurrentSplitPart();
        });

        // Edit Grid Slot Modal (US2 & US3)
        if (elements.btnCloseEditSlotModal) {
            elements.btnCloseEditSlotModal.addEventListener('click', () => {
                elements.editSlotModal.classList.remove('active');
            });
        }

        if (elements.formEditSlot) {
            elements.formEditSlot.addEventListener('submit', async (e) => {
                e.preventDefault();
                await saveSlotCustomizationFromModal();
            });
        }

        if (elements.btnUnassignSlot) {
            elements.btnUnassignSlot.addEventListener('click', async () => {
                const row = parseInt(elements.editSlotRow.value);
                const col = parseInt(elements.editSlotCol.value);
                if (state.activeAdminGridCategory) {
                    await unassignSlot(state.activeAdminGridCategory, row, col);
                    elements.editSlotModal.classList.remove('active');
                }
            });
        }
    }

    async function loadHotelRooms() {
        try {
            const res = await fetch('/api/hotel/rooms');
            state.hotelRooms = await res.json();
            elements.selectHotelRoom.innerHTML = '';
            state.hotelRooms.forEach(room => {
                const opt = document.createElement('option');
                opt.value = room.roomNumber;
                opt.textContent = t('payment.hotel_room_option', { room: room.roomNumber, guest: room.guestName });
                elements.selectHotelRoom.appendChild(opt);
            });
            if (state.hotelRooms.length > 0) {
                elements.selectHotelRoom.dispatchEvent(new Event('change'));
            }
        } catch (err) {
            console.error('Erreur chargement chambres:', err);
        }
    }

    /** Pourboire calculé une seule fois en centimes : montant encaissé et pourboire envoyés en dérivent (affiché = envoyé). */
    function getTipCents() {
        if (state.customTipAmount > 0) return Math.round(state.customTipAmount * 100);
        return Math.round(Math.round(getAmountDue() * 100) * state.selectedTipPercent / 100);
    }

    function getTipAmount() {
        return getTipCents() / 100;
    }

    function getFinalPayTotal() {
        return (Math.round(getAmountDue() * 100) + getTipCents()) / 100;
    }

    function updateTipCalculation() {
        if (state.splitPlan) {
            showCurrentSplitPart();
            return;
        }
        const total = getAmountDue();
        const tip = getTipAmount();
        const finalTotal = getFinalPayTotal();

        elements.payRemainingAmount.innerHTML = `<bdi dir="ltr">${total.toFixed(2)} €</bdi>`;
        elements.payTotalWithTip.textContent = t('payment.total_with_tip_value', { total: finalTotal.toFixed(2), tip: tip.toFixed(2) });
    }

    function setupSignatureCanvas() {
        const canvas = elements.signatureCanvas;
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        let isDrawing = false;

        ctx.strokeStyle = '#38bdf8';
        ctx.lineWidth = 2.5;
        ctx.lineCap = 'round';

        function getPos(e) {
            const rect = canvas.getBoundingClientRect();
            const clientX = e.touches ? e.touches[0].clientX : e.clientX;
            const clientY = e.touches ? e.touches[0].clientY : e.clientY;
            return {
                x: clientX - rect.left,
                y: clientY - rect.top
            };
        }

        function start(e) {
            isDrawing = true;
            const pos = getPos(e);
            ctx.beginPath();
            ctx.moveTo(pos.x, pos.y);
            e.preventDefault();
        }

        function move(e) {
            if (!isDrawing) return;
            const pos = getPos(e);
            ctx.lineTo(pos.x, pos.y);
            ctx.stroke();
            e.preventDefault();
        }

        function stop() {
            isDrawing = false;
        }

        canvas.addEventListener('mousedown', start);
        canvas.addEventListener('mousemove', move);
        canvas.addEventListener('mouseup', stop);
        canvas.addEventListener('touchstart', start, { passive: false });
        canvas.addEventListener('touchmove', move, { passive: false });
        canvas.addEventListener('touchend', stop);

        elements.btnClearSignature.addEventListener('click', () => {
            ctx.clearRect(0, 0, canvas.width, canvas.height);
        });
    }

    function calculateTotalTtc() {
        let total = state.cart.reduce((sum, item) => {
            if (item.isComp) return sum;
            const effectiveUnitPrice = Number(item.product.price || 0) + Number(item.modifiersPriceExtra || 0);
            let line = effectiveUnitPrice * item.quantity;
            if (item.discountPercent > 0) line *= (1.0 - item.discountPercent / 100.0);
            return sum + line;
        }, 0);

        if (state.globalDiscount) {
            if (state.globalDiscount.type === 0 || state.globalDiscount.type === 'Percentage') {
                total = Math.max(0, total * (1.0 - (state.globalDiscount.value / 100.0)));
            } else if (state.globalDiscount.type === 1 || state.globalDiscount.type === 'FixedAmount') {
                total = Math.max(0, total - state.globalDiscount.value);
            }
        }
        return total;
    }

    /** Montant restant dû : tient compte des paiements partiels déjà encaissés (split). */
    function getAmountDue() {
        const total = calculateTotalTtc();
        return Math.max(0, Math.round((total - (state.amountPaid || 0)) * 100) / 100);
    }

    function splitIntoParts(amount, guests) {
        const totalCents = Math.round(amount * 100);
        const base = Math.floor(totalCents / guests);
        const remainder = totalCents % guests;
        return Array.from({ length: guests }, (_, i) => base + (i < remainder ? 1 : 0));
    }

    function showCurrentSplitPart() {
        const plan = state.splitPlan;
        if (!plan) return;
        state.splitActivePart = plan.parts[plan.index] / 100;
        state.selectedTipPercent = 0;
        state.customTipAmount = 0;
        // Seul le montant est isolé en LTR : la phrase suit la direction de la page (RTL en arabe).
        elements.payRemainingAmount.innerHTML = t('payment.split_part_amount', { amount: `<bdi dir="ltr">${state.splitActivePart.toFixed(2)} €</bdi>`, index: plan.index + 1, total: plan.parts.length });
        elements.payTotalWithTip.textContent = `${state.splitActivePart.toFixed(2)} €`;
        syncPaymentPrintRow();
        syncTipVisibility();
        elements.paymentModal.classList.add('active');
    }

    function updateSplitPartitions() {
        elements.splitGuestsCount.textContent = t('payment.split_guests_count', { count: state.splitGuests });
        elements.splitPartitionsList.innerHTML = '';
        const totalCents = Math.round(getAmountDue() * 100);
        const base = Math.floor(totalCents / state.splitGuests);
        let remainder = totalCents % state.splitGuests;

        for (let i = 1; i <= state.splitGuests; i++) {
            const cents = base + (remainder > 0 ? 1 : 0);
            if (remainder > 0) remainder--;
            const row = document.createElement('div');
            row.className = 'partition-row';
            row.innerHTML = `<span>${t('payment.split_guest_label', { n: i })}</span> <strong>${(cents / 100).toFixed(2)} €</strong>`;
            elements.splitPartitionsList.appendChild(row);
        }
    }

    /** Case « ticket de caisse » : réservée au paiement à table (le comptoir a sa propre case). */
    function syncPaymentPrintRow() {
        const row = document.getElementById('paymentPrintReceiptRow');
        if (row) row.style.display = state.activeTable === 'Comptoir' ? 'none' : '';
    }

    /** Pas de pourboire en partage : le serveur ne l'accepte que sur le paiement qui solde. */
    function syncTipVisibility() {
        const section = elements.tipPills[0]?.closest('.tip-section');
        if (section) section.style.display = state.splitPlan ? 'none' : '';
        if (state.splitPlan) {
            elements.inputCustomTip.value = '';
            elements.tipCustomInputRow.style.display = 'none';
            elements.tipPills.forEach((p, i) => p.classList.toggle('active', i === 0));
        }
    }

    function warnIfNotQueued(resData) {
        if (resData && resData.printQueued === false) showToast(t('payment.print_not_queued'), 'warning');
    }

    async function executePayment(tenderMethod, tendered) {
        if (state.activeTable === 'Comptoir') {
            await executeCounterCheckout(tenderMethod, tendered);
            return;
        }

        if (!(await saveActiveCartToServer())) return;
        const isSplit = (typeof state.splitActivePart === 'number' && state.splitActivePart > 0);
        const total = Math.round((isSplit ? state.splitActivePart : getFinalPayTotal()) * 100) / 100;
        if (tenderMethod === 0 && tendered + 0.001 < total) {
            showToast(t('payment.insufficient_tendered', { tendered: tendered.toFixed(2), total: total.toFixed(2) }), 'warning');
            return;
        }
        // Hors espèces, le montant remis est exactement le montant encaissé (pas de rendu).
        if (tenderMethod !== 0) tendered = total;
        const change = Math.max(0, Math.round((tendered - total) * 100) / 100);

        try {
            await ensureAuthToken();
            const headers = { 'Content-Type': 'application/json' };
            if (state.token) {
                headers['Authorization'] = `Bearer ${state.token}`;
            }

            const payload = {
                orderId: state.activeOrderId || '00000000-0000-0000-0000-000000000000',
                tableNumber: state.activeTable,
                operatorId: state.operator?.id || '00000000-0000-0000-0000-000000000000',
                tenders: [
                    {
                        method: tenderMethod,
                        amount: total,
                        tendered: tendered,
                        changeGiven: change
                    }
                ],
                tipAmount: isSplit ? 0 : Math.round(getTipAmount() * 100) / 100,
                requestReceiptPrint: document.getElementById('paymentPrintReceipt')?.checked === true
            };

            const res = await fetch('/api/checkout/pay', {
                method: 'POST',
                headers: headers,
                body: JSON.stringify(payload)
            });

            if (res.ok) {
                const resData = await res.json();
                if (payload.requestReceiptPrint && !(resData.remainingBalance > 0.001)) warnIfNotQueued(resData);
                const printBox = document.getElementById('paymentPrintReceipt');
                if (printBox) printBox.checked = false;
                showToast(t('payment.paid_toast', { receipt: resData.receiptNumber || 'NF', change: change.toFixed(2) }), 'success');
                elements.paymentModal.classList.remove('active');

                if (resData.remainingBalance > 0.001) {
                    state.amountPaid = (state.amountPaid || 0) + Number(resData.totalPaid ?? total);
                    showToast(t('payment.remaining_balance_toast', { balance: resData.remainingBalance.toFixed(2) }), 'info');
                    const plan = state.splitPlan;
                    if (plan && plan.index + 1 < plan.parts.length) {
                        // Part suivante du partage.
                        plan.index += 1;
                        showCurrentSplitPart();
                    } else {
                        // Solde restant (arrondis, paiement partiel libre) : encaissement classique.
                        state.splitPlan = null;
                        state.splitActivePart = null;
                        syncTipVisibility();
                        renderCart();
                    }
                } else {
                    state.splitActivePart = null;
                    state.splitPlan = null;
                    state.amountPaid = 0;
                    state.cart = [];
                    state.activeOrderId = null;
                    state.activeTable = null;
                    renderCart();
                    await loadFloorPlanData();
                    switchView('floorPlanView');
                }
            } else {
                let errMsg = t('payment.pay_error_generic');
                try {
                    const errData = await res.json();
                    if (errData && errData.message) errMsg = errData.message;
                } catch (_) { }
                showToast(errMsg, 'error');
            }
        } catch (err) {
            console.error('Erreur paiement:', err);
            showToast(t('payment.pay_error_catch'), 'error');
        }
    }

    // ==================== TAKEAWAY & DIRECT SALES (FEATURE 018) ====================
    async function openDirectCounterOrder(destination = 'Takeaway') {
        const wasCounter = state.activeTable === 'Comptoir';
        if (state.activeTable && !wasCounter) {
            await saveActiveCartToServer();
            state.cart = [];
        }
        state.activeTable = 'Comptoir';
        state.destination = destination;

        if (elements.activeTableBadge) {
            elements.activeTableBadge.textContent = 'Comptoir';
        }

        updateDestinationToggleUI();

        try {
            await ensureAuthToken();
            const destEnum = destinationToEnum(destination);
            const res = await fetch('/api/orders/counter/direct', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    terminalId: state.terminalId || 'POS_A',
                    destination: destEnum
                })
            });

            if (res.ok) {
                const orderData = await res.json();
                state.activeCovers = 1;
                state.pickupNumber = orderData.pickupNumber || null;
                state.pickupBuzzer = orderData.pickupBuzzer || null;
                state.amountPaid = 0;
                state.splitPlan = null;
                // Une commande comptoir déjà entamée garde sa destination ; sinon on applique celle choisie.
                if ((orderData.lines || []).length > 0) {
                    state.destination = destinationFromEnum(orderData.destination);
                    updateDestinationToggleUI();
                } else if (orderData.destination !== destEnum) {
                    await fetch(`/api/orders/${orderData.orderId}/destination`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ destination: destEnum })
                    });
                }
                hydrateCartFromOrder(orderData, wasCounter);
                renderCart();
            }
        } catch (err) {
            console.error('Erreur openDirectCounterOrder:', err);
        }
    }

    function updateDestinationToggleUI() {
        const isTakeaway = isTakeawayMode();
        if (elements.btnDestTakeaway && elements.btnDestEatIn) {
            elements.btnDestTakeaway.classList.toggle('active', isTakeaway);
            elements.btnDestEatIn.classList.toggle('active', !isTakeaway);
        }
        if (elements.activeCoversBadge) {
            elements.activeCoversBadge.style.display = 'none';
        }
    }

    async function switchDestination(newDest) {
        state.destination = newDest;
        updateDestinationToggleUI();

        // Aucune commande comptoir chargée : on l'ouvre d'abord pour pouvoir y enregistrer la destination.
        if (!state.activeOrderId && state.activeTable === 'Comptoir') {
            await openDirectCounterOrder(newDest);
        }

        if (state.activeOrderId) {
            try {
                await ensureAuthToken();
                const destEnum = destinationToEnum(newDest);
                await fetch(`/api/orders/${state.activeOrderId}/destination`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ destination: destEnum })
                });
            } catch (err) {
                console.warn('Erreur switchDestination API:', err);
            }
        }

        renderCart();
        showToast(t('order.destination_switched_toast', { mode: newDest === 'Takeaway' ? t('order.destination_takeaway_label') : t('order.destination_eatin_label') }), 'info');
    }

    async function updateHeldQueueCount() {
        try {
            await ensureAuthToken();
            const res = await fetch(`/api/orders/counter/held?terminalId=${encodeURIComponent(state.terminalId || 'POS_A')}`);
            if (res.ok) {
                const list = await res.json();
                state.heldOrders = list;
                if (elements.heldBadgeCount) {
                    elements.heldBadgeCount.textContent = list.length;
                }
            }
        } catch (err) {
            console.warn('Erreur updateHeldQueueCount:', err);
        }
    }

    async function holdCurrentCart() {
        if (state.cart.length === 0) {
            showToast(t('order.hold_cart_empty_warning'), 'warning');
            return;
        }

        // Nom proposé : premier « Client #N » pas encore utilisé (même règle que l'app iPad).
        const usedLabels = new Set(state.heldOrders.map(h => h.customerLabel));
        let number = 1;
        while (usedLabels.has(t('order.hold_customer_label', { n: number }))) number++;
        const suggested = t('order.hold_customer_label', { n: number });

        const input = prompt(t('order.hold_prompt'), suggested);
        if (input === null) return; // Annuler : le panier reste en cours
        const label = input.trim() || suggested;

        await saveActiveCartToServer();

        try {
            await ensureAuthToken();
            const res = await fetch('/api/orders/counter/hold', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    orderId: state.activeOrderId,
                    terminalId: state.terminalId || 'POS_A',
                    customerLabel: label
                })
            });

            if (res.ok) {
                showToast(t('order.hold_success_toast', { label: label }), 'success');
                state.cart = [];
                state.activeOrderId = null;
                await updateHeldQueueCount();
                await openDirectCounterOrder(state.destination);
            } else {
                const errData = await res.json().catch(() => ({}));
                showToast(errData.message || t('order.hold_error_generic'), 'error');
            }
        } catch (err) {
            showToast(t('order.hold_network_error'), 'error');
        }
    }

    async function renderHeldOrdersModal() {
        await updateHeldQueueCount();
        if (!elements.heldOrdersList) return;
        elements.heldOrdersList.innerHTML = '';

        if (state.heldOrders.length === 0) {
            elements.heldOrdersList.innerHTML = `
                <div style="text-align:center; padding:30px; color:var(--text-muted);">
                    <div style="font-size:2.5rem; margin-bottom:8px;">⏸️</div>
                    <strong>${t('order.held_list_empty_title')}</strong>
                    <div style="font-size:0.85rem; margin-top:4px;">${t('order.held_list_empty_hint')}</div>
                </div>
            `;
            elements.heldOrdersModal.classList.add('active');
            return;
        }

        state.heldOrders.forEach(h => {
            const card = document.createElement('div');
            card.className = 'held-order-card';
            const heldTime = new Date(h.heldAtUtc).toLocaleTimeString(window.i18n.locale, { hour: '2-digit', minute: '2-digit' });
            const holdId = h.id || h.holdId;
            let totalAmount = 0;
            if (typeof h.totalTtc === 'object' && h.totalTtc !== null) {
                totalAmount = h.totalTtc.amountInCents !== undefined ? (h.totalTtc.amountInCents / 100) : Number(h.totalTtc.amount || 0);
            } else {
                totalAmount = Number(h.totalTtc || 0);
            }
            card.innerHTML = `
                <div>
                    <div style="font-weight:700; font-size:1rem; color:#f8fafc;">${h.customerLabel || t('order.held_card_default_name')}</div>
                    <div style="font-size:0.8rem; color:var(--text-muted); margin-top:2px;">
                        ${t('order.held_card_meta', { count: h.itemCount, amount: totalAmount.toFixed(2), time: heldTime })}
                    </div>
                </div>
                <div class="held-card-actions">
                    <button type="button" class="btn-action btn-pay btn-recall-held" data-id="${holdId}" style="padding:8px 14px; font-size:0.85rem;">
                        ${t('order.held_recall_btn')}
                    </button>
                    <button type="button" class="btn-secondary btn-void-held" data-id="${holdId}" style="padding:8px 12px; font-size:0.85rem; color:#ef4444; border-color:rgba(239,68,68,0.3);">
                        ${t('order.held_void_btn')}
                    </button>
                </div>
            `;

            card.querySelector('.btn-recall-held').addEventListener('click', async () => {
                await recallHeldOrder(holdId);
            });

            card.querySelector('.btn-void-held').addEventListener('click', () => {
                state.pendingVoidHoldId = holdId;
                state.supervisorPinInput = '';
                elements.supervisorPinInput.value = '';
                elements.heldOrdersModal.classList.remove('active');
                elements.supervisorPinModal.classList.add('active');
            });

            elements.heldOrdersList.appendChild(card);
        });

        elements.heldOrdersModal.classList.add('active');
    }

    async function recallHeldOrder(holdId) {
        try {
            await ensureAuthToken();
            const res = await fetch(`/api/orders/counter/held/${holdId}/recall`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' }
            });

            if (res.ok) {
                const orderData = await res.json();
                state.activeTable = 'Comptoir';
                state.destination = destinationFromEnum(orderData.destination);
                state.amountPaid = 0;
                state.splitPlan = null;
                updateDestinationToggleUI();
                hydrateCartFromOrder(orderData, false);

                elements.heldOrdersModal.classList.remove('active');
                renderCart();
                await updateHeldQueueCount();
                showToast(t('order.recall_success_toast'), 'success');
            } else {
                showToast(t('order.recall_failed_toast'), 'error');
            }
        } catch (err) {
            showToast(t('order.recall_error_toast'), 'error');
        }
    }

    async function verifySupervisorPinAndVoid() {
        if (!state.pendingVoidHoldId) return;
        const pin = state.supervisorPinInput;
        if (!pin) {
            showToast(t('order.supervisor_pin_required'), 'warning');
            return;
        }

        try {
            await ensureAuthToken();
            const res = await fetch(`/api/orders/counter/held/${state.pendingVoidHoldId}/void`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    supervisorPin: pin,
                    voidReason: 'Annulation au comptoir', // nf525-texte-fixe : motif écrit dans le journal d'audit, jamais traduit
                    terminalId: state.terminalId || 'POS_A'
                })
            });

            if (res.ok) {
                showToast(t('order.void_success_toast'), 'success');
                elements.supervisorPinModal.classList.remove('active');
                state.pendingVoidHoldId = null;
                state.supervisorPinInput = '';
                await updateHeldQueueCount();
            } else {
                const data = await res.json().catch(() => ({}));
                showToast(data.message || t('order.pin_invalid_toast'), 'error');
                state.supervisorPinInput = '';
                elements.supervisorPinInput.value = '';
            }
        } catch (err) {
            showToast(t('order.void_error_toast'), 'error');
        }
    }

    async function executeCounterCheckout(tenderMethod, tenderedAmount, facialValue = null, requestFiscalPrint = false) {
        if (state.cart.length === 0) {
            showToast(t('payment.cart_empty_warning'), 'warning');
            return;
        }

        await saveActiveCartToServer();

        const total = getFinalPayTotal();
        const policy = parseInt(elements.selectMealVoucherPolicy ? elements.selectMealVoucherPolicy.value : '0') || 0;
        const buzzer = elements.inputPickupBuzzer ? elements.inputPickupBuzzer.value.trim() : null;

        const payload = {
            orderId: state.activeOrderId,
            destination: destinationToEnum(state.destination),
            pickupBuzzer: buzzer || null,
            pickupScheduledAtUtc: null,
            tipAmount: getTipAmount(),
            requestFiscalReceiptPrint: requestFiscalPrint || (elements.chkPrintFiscalReceipt && elements.chkPrintFiscalReceipt.checked),
            mealVoucherPolicy: policy,
            tenders: [
                {
                    method: tenderMethod,
                    amount: Math.min(tenderedAmount, total),
                    tendered: tenderedAmount,
                    facialValue: facialValue || (tenderMethod === 2 ? tenderedAmount : null)
                }
            ]
        };

        try {
            await ensureAuthToken();
            const res = await fetch('/api/orders/counter/checkout', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            const data = await res.json();
            if (res.ok) {
                warnIfNotQueued(data);
                // Success: Close payment modal
                if (elements.paymentModal) elements.paymentModal.classList.remove('active');

                // Fill Change Overlay
                elements.changeOverlayAmount.textContent = `${Number(data.changeGiven || 0).toFixed(2)} €`;
                elements.changeOverlayDetails.textContent = t('payment.change_details', { tendered: tenderedAmount.toFixed(2), total: Number(data.totalPaid || 0).toFixed(2) });
                elements.changeOverlayPickupNumber.textContent = data.pickupNumber || '#A-01';

                if (buzzer) {
                    elements.changeOverlayBuzzer.style.display = 'block';
                    elements.changeOverlayBuzzerVal.textContent = buzzer;
                } else {
                    elements.changeOverlayBuzzer.style.display = 'none';
                }

                if (data.issuedCreditVoucher) {
                    elements.changeOverlayCreditVoucherBox.style.display = 'block';
                    elements.changeOverlayCreditVoucherCode.textContent = data.issuedCreditVoucher.voucherCode;
                    elements.changeOverlayCreditVoucherAmount.textContent = t('payment.credit_voucher_amount', { amount: Number(data.issuedCreditVoucher.amount).toFixed(2) });
                } else {
                    elements.changeOverlayCreditVoucherBox.style.display = 'none';
                }

                // Show change overlay
                elements.changeOverlayModal.style.display = 'flex';
                elements.changeOverlayModal.classList.add('active');

                // Clear active cart & prepare for next sale
                state.cart = [];
                state.activeOrderId = null;
                renderCart();

                showToast(t('payment.sale_confirmed_toast', { pickup: data.pickupNumber, change: Number(data.changeGiven || 0).toFixed(2) }), 'success');
            } else {
                showToast(data.message || t('payment.checkout_error_generic'), 'error');
            }
        } catch (err) {
            console.error('Erreur executeCounterCheckout:', err);
            showToast(t('payment.checkout_network_error'), 'error');
        }
    }

    function setupTakeawayListeners() {
        // Destination toggle
        if (elements.btnDestTakeaway) {
            elements.btnDestTakeaway.addEventListener('click', () => switchDestination('Takeaway'));
        }
        if (elements.btnDestEatIn) {
            elements.btnDestEatIn.addEventListener('click', () => switchDestination('EatIn'));
        }

        // Held queue badge and modal
        if (elements.btnHeldQueue) {
            elements.btnHeldQueue.addEventListener('click', () => renderHeldOrdersModal());
        }
        if (elements.btnCloseHeldModal) {
            elements.btnCloseHeldModal.addEventListener('click', () => elements.heldOrdersModal.classList.remove('active'));
        }
        if (elements.btnHoldCart) {
            elements.btnHoldCart.addEventListener('click', () => holdCurrentCart());
        }

        // Supervisor PIN keypad
        document.querySelectorAll('.supervisor-pin-grid .btn-key').forEach(btn => {
            btn.addEventListener('click', () => {
                const key = btn.getAttribute('data-key');
                if (key === 'clear') {
                    state.supervisorPinInput = '';
                } else if (key === 'back') {
                    state.supervisorPinInput = state.supervisorPinInput.slice(0, -1);
                } else if (state.supervisorPinInput.length < 6) {
                    state.supervisorPinInput += key;
                }
                elements.supervisorPinInput.value = state.supervisorPinInput;
            });
        });

        if (elements.btnCancelSupervisorPin) {
            elements.btnCancelSupervisorPin.addEventListener('click', () => {
                elements.supervisorPinModal.classList.remove('active');
                state.pendingVoidHoldId = null;
                state.supervisorPinInput = '';
            });
        }

        if (elements.btnConfirmSupervisorPin) {
            elements.btnConfirmSupervisorPin.addEventListener('click', () => verifySupervisorPinAndVoid());
        }

        // Change overlay done button
        if (elements.btnChangeDone) {
            elements.btnChangeDone.addEventListener('click', async () => {
                elements.changeOverlayModal.style.display = 'none';
                elements.changeOverlayModal.classList.remove('active');
                await openDirectCounterOrder('Takeaway');
            });
        }

        // Fast Cash bar on cart
        if (elements.cartFastCashBar) {
            elements.cartFastCashBar.querySelectorAll('.btn-quick-cash').forEach(btn => {
                btn.addEventListener('click', async () => {
                    const cashVal = btn.getAttribute('data-cash');
                    const finalTotal = getFinalPayTotal();
                    if (finalTotal <= 0) {
                        showToast(t('payment.cart_empty_warning'), 'warning');
                        return;
                    }
                    const amount = cashVal === 'exact' ? finalTotal : parseFloat(cashVal);
                    if (amount < finalTotal && cashVal !== 'exact') {
                        showToast(t('payment.insufficient_amount_toast', { amount: amount.toFixed(2), total: finalTotal.toFixed(2) }), 'warning');
                        return;
                    }
                    await executeCounterCheckout(0, amount);
                });
            });
        }
    }

    // ==================== FLOOR PLAN ====================
    async function loadFloorPlanData() {
        try {
            const res = await fetch('/api/tables');
            state.tables = await res.json();
            renderFloorPlan();
        } catch (err) {
            console.error('Erreur chargement plan de salle:', err);
        }
    }

    function renderFloorPlan() {
        elements.floorTablesGrid.innerHTML = '';
        state.tables.forEach(table => {
            const card = document.createElement('div');
            const statusClass = getTableStatusClass(table.status);
            card.className = `table-card ${statusClass}`;
            const total = Number(table.activeOrderTotalTtc || 0);
            const totalHtml = total > 0
                ? `<div class="table-total">💶 ${total.toFixed(2)} €</div>`
                : (table.status !== 0 ? `<div class="table-sub" style="font-weight:700;color:var(--text-muted);margin-top:4px;">${t('floor.table_total_zero')}</div>` : '');

            card.innerHTML = `
                <div class="table-num">${table.tableNumber}</div>
                <div class="table-sub">${t('floor.table_capacity', { capacity: table.capacity })}</div>
                <div class="table-sub">${t('floor.table_status_label', { status: getTableStatusLabel(table.status) })}</div>
                <div class="table-sub">${t('floor.table_waiter', { name: table.assignedWaiterName || '—' })}</div>
                ${table.coversCount > 0 ? `<div style="font-size:0.75rem;color:#10b981;font-weight:700;margin-top:4px;">${t('floor.table_covers_active', { count: table.coversCount })}</div>` : ''}
                ${totalHtml}
            `;

            card.addEventListener('click', async () => {
                await loadActiveTableOrder(table.tableNumber);
                switchView('posView');
                showToast(t('floor.table_recalled_toast', { number: table.tableNumber }), 'info');
            });
            elements.floorTablesGrid.appendChild(card);
        });
    }

    // ==================== KITCHEN KDS ====================
    async function loadKdsData() {
        try {
            const res = await fetch('/api/kds/tickets');
            const tickets = await res.json();
            renderKdsBoard(tickets);
        } catch (err) {
            console.error('Erreur KDS:', err);
        }
    }

    function renderKdsBoard(tickets) {
        elements.kdsPendingCards.innerHTML = '';
        elements.kdsInPrepCards.innerHTML = '';
        elements.kdsReadyCards.innerHTML = '';

        let pendingCount = 0;
        let inPrepCount = 0;
        let readyCount = 0;

        tickets.forEach(ticket => {
            const card = document.createElement('div');
            card.className = 'kds-ticket-card';
            card.innerHTML = `
                <div class="ticket-header">
                    <span>${ticket.tableNumber}</span>
                    <small>${ticket.stationId || 'HOT'}</small>
                </div>
                <div class="ticket-items">
                    ${ticket.items ? ticket.items.map(i => `<div>• ${i.quantity}x ${i.productName}</div>`).join('') : t('kitchen.default_dish')}
                </div>
            `;

            card.addEventListener('click', async () => {
                await fetch(`/api/kds/tickets/${ticket.id}/bump`, { method: 'POST' });
                showToast(t('kitchen.ticket_bumped_toast', { table: ticket.tableNumber }), 'success');
                loadKdsData();
            });

            if (ticket.status === 0 || ticket.status === 'Pending') {
                elements.kdsPendingCards.appendChild(card);
                pendingCount++;
            } else if (ticket.status === 1 || ticket.status === 'InPreparation') {
                elements.kdsInPrepCards.appendChild(card);
                inPrepCount++;
            } else if (ticket.status === 2 || ticket.status === 'Ready') {
                elements.kdsReadyCards.appendChild(card);
                readyCount++;
            }
        });

        elements.countPending.textContent = pendingCount;
        elements.countInPrep.textContent = inPrepCount;
        elements.countReady.textContent = readyCount;
    }

    // ==================== ADMIN FORMS & TABS ====================
    function setupAdminTabs() {
        elements.adminTabBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                elements.adminTabBtns.forEach(b => b.classList.remove('active'));
                elements.adminTabPanes.forEach(p => p.classList.remove('active'));
                btn.classList.add('active');
                const targetTab = btn.getAttribute('data-admin-tab');
                const targetPane = document.getElementById(targetTab);
                if (targetPane) targetPane.classList.add('active');

                if (targetTab === 'tabLayout') {
                    loadAdminGridEditor();
                } else if (targetTab === 'tabDashboard') {
                    loadFinancialDashboard('today');
                } else if (targetTab === 'tabDevices') {
                    loadAdminDevices();
                } else if (targetTab === 'tabEstablishment') {
                    loadPrintSettings();
                }
            });
        });
    }

    async function loadAdminData() {
        await Promise.all([
            loadAdminCatalog(),
            loadAdminStaff(),
            loadAdminPrinters(),
            loadAdminGridEditor(),
            loadAdminDevices(),
            loadNetworkSyncData(),
            loadFinancialDashboard('today'),
            loadAdminHappyHour(),
            loadPrintSettings()
        ]);
    }

    // ==================== FINANCIAL DASHBOARD & KPIS ====================
    let currentDashboardRange = 'today';

    function setupDashboardHandlers() {
        const filterBtns = document.querySelectorAll('.dash-filter-btn');
        filterBtns.forEach(btn => {
            btn.addEventListener('click', () => {
                filterBtns.forEach(b => b.classList.remove('active'));
                btn.classList.add('active');
                currentDashboardRange = btn.getAttribute('data-range') || 'today';
                loadFinancialDashboard(currentDashboardRange);
            });
        });

        const refreshBtn = document.getElementById('btnRefreshDashboard');
        if (refreshBtn) {
            refreshBtn.addEventListener('click', () => {
                loadFinancialDashboard(currentDashboardRange);
            });
        }
    }

    async function loadFinancialDashboard(range = 'today') {
        const now = new Date();
        let fromDate = new Date();
        let toDate = new Date();

        if (range === 'today') {
            fromDate.setHours(0, 0, 0, 0);
        } else if (range === 'yesterday') {
            fromDate.setDate(now.getDate() - 1);
            fromDate.setHours(0, 0, 0, 0);
            toDate.setDate(now.getDate() - 1);
            toDate.setHours(23, 59, 59, 999);
        } else if (range === 'week') {
            fromDate.setDate(now.getDate() - 7);
            fromDate.setHours(0, 0, 0, 0);
        } else if (range === 'month') {
            fromDate.setDate(now.getDate() - 30);
            fromDate.setHours(0, 0, 0, 0);
        }

        try {
            await ensureAuthToken();
            const headers = state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
            const url = `/api/dashboard/financial?from=${encodeURIComponent(fromDate.toISOString())}&to=${encodeURIComponent(toDate.toISOString())}`;
            const res = await fetch(url, { headers });
            if (!res.ok) {
                console.error('Erreur chargement dashboard financier:', res.status);
                return;
            }

            const data = await res.json();
            renderFinancialDashboard(data);
        } catch (err) {
            console.error('Erreur communication dashboard:', err);
        }
    }

    function renderFinancialDashboard(data) {
        if (!data || !data.kpis) return;

        // 1. KPIs
        const elSalesTtc = document.getElementById('kpiSalesTtc');
        const elSalesHt = document.getElementById('kpiSalesHt');
        const elAvgCover = document.getElementById('kpiAvgCover');
        const elTotalCovers = document.getElementById('kpiTotalCovers');
        const elAvgOrder = document.getElementById('kpiAvgOrder');
        const elTotalOrders = document.getElementById('kpiTotalOrders');

        if (elSalesTtc) elSalesTtc.textContent = `${data.kpis.totalSalesTtc.toFixed(2)} €`;
        if (elSalesHt) elSalesHt.textContent = t('admin.kpi_sales_ht_value', { amount: data.kpis.totalSalesHt.toFixed(2) });
        if (elAvgCover) elAvgCover.textContent = `${data.kpis.averageCoverTtc.toFixed(2)} €`;
        if (elTotalCovers) elTotalCovers.textContent = t('admin.kpi_total_covers_value', { count: data.kpis.totalCoversCount });
        if (elAvgOrder) elAvgOrder.textContent = `${data.kpis.averageOrderTtc.toFixed(2)} €`;
        if (elTotalOrders) elTotalOrders.textContent = t('admin.kpi_total_orders_value', { count: data.kpis.totalOrdersCount });

        // 2. Services
        const elServices = document.getElementById('dashServicesList');
        if (elServices) {
            if (!data.services || data.services.length === 0) {
                elServices.innerHTML = `<p class="text-muted">${t('admin.dash_no_services')}</p>`;
            } else {
                elServices.innerHTML = data.services.map(s => `
                    <div style="background:rgba(255,255,255,0.03); padding:12px; border-radius:8px; border:1px solid rgba(255,255,255,0.06);">
                        <div style="display:flex; justify-content:space-between; margin-bottom:4px;">
                            <strong>${s.serviceName}</strong>
                            <strong style="color:#10b981; direction:ltr; unicode-bidi:isolate;">${s.salesTtc.toFixed(2)} €</strong>
                        </div>
                        <div style="display:flex; justify-content:space-between; font-size:0.8rem; color:#94a3b8;">
                            <span>${t('admin.dash_service_summary', { orders: s.ordersCount, covers: s.coversCount })}</span>
                            <span>${t('admin.dash_avg_per_cover', { amount: s.averageCoverTtc.toFixed(2) })}</span>
                        </div>
                    </div>
                `).join('');
            }
        }

        // 3. Payment Methods
        const elPayments = document.getElementById('dashPaymentsList');
        if (elPayments) {
            if (!data.paymentMethods || data.paymentMethods.length === 0) {
                elPayments.innerHTML = `<p class="text-muted">${t('admin.dash_no_payments')}</p>`;
            } else {
                elPayments.innerHTML = data.paymentMethods.map(p => `
                    <div style="background:rgba(255,255,255,0.03); padding:10px 12px; border-radius:8px; border:1px solid rgba(255,255,255,0.06);">
                        <div style="display:flex; justify-content:space-between; margin-bottom:6px; font-size:0.88rem;">
                            <span>${p.methodName} <span style="color:#94a3b8; font-size:0.78rem;">(${p.transactionsCount} tx)</span></span>
                            <strong style="direction:ltr; unicode-bidi:isolate;">${p.totalAmount.toFixed(2)} € <span style="color:#38bdf8; font-size:0.8rem;">(${p.percentageOfTotal}%)</span></strong>
                        </div>
                        <div style="background:rgba(255,255,255,0.08); height:6px; border-radius:3px; overflow:hidden;">
                            <div style="background:#38bdf8; height:100%; width:${Math.min(100, Math.max(0, p.percentageOfTotal))}%;"></div>
                        </div>
                    </div>
                `).join('');
            }
        }

        // 4. Top Products
        const elTopProds = document.getElementById('dashTopProductsList');
        if (elTopProds) {
            if (!data.topProducts || data.topProducts.length === 0) {
                elTopProds.innerHTML = `<p class="text-muted">${t('admin.dash_no_sales')}</p>`;
            } else {
                elTopProds.innerHTML = data.topProducts.map((prod, idx) => `
                    <div style="background:rgba(255,255,255,0.03); padding:10px 12px; border-radius:8px; border:1px solid rgba(255,255,255,0.06);">
                        <div style="display:flex; justify-content:space-between; margin-bottom:4px; font-size:0.88rem;">
                            <span><strong style="color:#fbbf24; margin-inline-end:6px;">#${idx + 1}</strong> ${prod.productName} <span style="color:#94a3b8; font-size:0.8rem;">(x${prod.quantitySold})</span></span>
                            <strong style="color:#10b981; direction:ltr; unicode-bidi:isolate;">${prod.totalSalesTtc.toFixed(2)} € <span style="color:#94a3b8; font-size:0.75rem;">(${prod.percentageOfTotal}%)</span></strong>
                        </div>
                        <div style="background:rgba(255,255,255,0.08); height:6px; border-radius:3px; overflow:hidden;">
                            <div style="background:#10b981; height:100%; width:${Math.min(100, Math.max(0, prod.percentageOfTotal))}%;"></div>
                        </div>
                    </div>
                `).join('');
            }
        }

        // 5. Staff Productivity
        const elStaff = document.getElementById('dashStaffList');
        if (elStaff) {
            if (!data.staffPerformance || data.staffPerformance.length === 0) {
                elStaff.innerHTML = `<p class="text-muted">${t('admin.dash_no_staff_activity')}</p>`;
            } else {
                elStaff.innerHTML = data.staffPerformance.map(s => `
                    <div style="background:rgba(255,255,255,0.03); padding:10px 12px; border-radius:8px; border:1px solid rgba(255,255,255,0.06); display:flex; justify-content:space-between; align-items:center;">
                        <div>
                            <strong>👤 ${s.serverName}</strong>
                            <div style="font-size:0.78rem; color:#94a3b8; margin-top:2px;">${t('admin.dash_staff_summary', { tables: s.tablesServedCount, amount: s.averageTableTtc.toFixed(2) })}</div>
                        </div>
                        <div style="text-align:end;">
                            <strong style="color:#c084fc; font-size:1.05rem; direction:ltr; unicode-bidi:isolate;">${s.totalSalesTtc.toFixed(2)} €</strong>
                        </div>
                    </div>
                `).join('');
            }
        }
    }

    async function loadAdminCatalog() {
        if (!elements.adminCatalogList) {
            elements.adminCatalogList = document.getElementById('adminCatalogList');
        }
        if (!elements.adminCatalogList) return;
        elements.adminCatalogList.innerHTML = '';
        state.products.forEach(p => {
            const row = document.createElement('div');
            row.className = 'item-list-row';
            row.innerHTML = `
                <div>
                    <strong>${p.name}</strong> ${t('admin.catalog_row_price_vat', { price: p.price.toFixed(2), rate: p.taxRatePercent })}
                    <small style="display:block;color:#94a3b8;">${t('admin.catalog_row_station', { station: p.preparationStationId || 'HOT' })}${p.isQuickKey ? ' | ' + t('admin.catalog_row_quick_key') : ''}</small>
                </div>
                <div style="display:flex; gap:6px;">
                    <button class="btn-archive btn-edit-product" data-id="${p.id}" style="background:rgba(59,130,246,0.2); border-color:rgba(59,130,246,0.4); color:#60a5fa;">${t('admin.btn_edit_row')}</button>
                    <button class="btn-archive btn-del-product" data-id="${p.id}">${t('admin.btn_deactivate')}</button>
                </div>
            `;
            row.querySelector('.btn-edit-product').addEventListener('click', () => {
                document.getElementById('editProdId').value = p.id;
                document.getElementById('editProdName').value = p.name;
                const catSelect = document.getElementById('editProdCat');
                catSelect.innerHTML = state.categories.map(c => `<option value="${c.id}">${c.name}</option>`).join('');
                catSelect.value = p.categoryId;
                document.getElementById('editProdPrice').value = p.price;
                setSelectValue('editProdTax', String(Number(p.taxRatePercent)));
                setSelectValue('editProdStation', p.preparationStationId || '');
                document.getElementById('editProdQuickKey').checked = !!p.isQuickKey;
                elements.editProductModal.classList.add('active');
            });
            row.querySelector('.btn-del-product').addEventListener('click', async () => {
                await fetch(`/api/catalog/products/${p.id}`, { method: 'DELETE' });
                showToast(t('admin.product_deactivated_toast', { name: p.name }), 'info');
                await loadCatalogData();
                await loadAdminCatalog();
            });
            elements.adminCatalogList.appendChild(row);
        });
    }

    // Rôles opérateur (enum serveur) -> clé i18n d'affichage. La valeur envoyée au serveur ne change pas.
    const OPERATOR_ROLE_LABEL_KEYS = {
        Waiter: 'admin.role_waiter',
        Cashier: 'admin.role_cashier',
        KitchenStaff: 'admin.role_kitchen_staff',
        FloorManager: 'admin.role_floor_manager',
        Admin: 'admin.role_admin'
    };
    function operatorRoleLabel(role) {
        const key = OPERATOR_ROLE_LABEL_KEYS[role];
        return key ? t(key) : role;
    }

    // Rôles appareil (enum serveur, valeurs françaises) -> clé i18n d'affichage.
    const DEVICE_ROLE_LABEL_KEYS = {
        Caisse: 'admin.device_role_caisse',
        Serveur: 'admin.device_role_serveur',
        Cuisine: 'admin.device_role_cuisine',
        BackOffice: 'admin.device_role_backoffice'
    };
    function deviceRoleLabel(role) {
        const key = DEVICE_ROLE_LABEL_KEYS[role];
        return key ? t(key) : role;
    }

    async function loadAdminStaff() {
        if (!elements.adminStaffList) elements.adminStaffList = document.getElementById('adminStaffList');
        if (!elements.adminStaffList) return;
        elements.adminStaffList.innerHTML = '';
        try {
            const res = await fetch('/api/staff/operators');
            const staff = await res.json();
            staff.forEach(s => {
                const row = document.createElement('div');
                row.className = 'item-list-row';
                row.innerHTML = `
                    <div>
                        <strong>${s.name}</strong>
                        <small style="display:block;color:#94a3b8;">${t('admin.staff_row_role', { role: operatorRoleLabel(s.role) })}</small>
                    </div>
                    <div style="display:flex; gap:6px;">
                        <button class="btn-archive btn-edit-staff" data-id="${s.id}" style="background:rgba(59,130,246,0.2); border-color:rgba(59,130,246,0.4); color:#60a5fa;">${t('admin.btn_edit_row')}</button>
                        <button class="btn-archive btn-del-staff" data-id="${s.id}">${t('admin.btn_deactivate')}</button>
                    </div>
                `;
                row.querySelector('.btn-edit-staff').addEventListener('click', () => {
                    document.getElementById('editStaffId').value = s.id;
                    document.getElementById('editStaffName').value = s.name;
                    document.getElementById('editStaffRole').value = s.role;
                    elements.editStaffModal.classList.add('active');
                });
                row.querySelector('.btn-del-staff').addEventListener('click', async () => {
                    await fetch(`/api/staff/operators/${s.id}`, { method: 'DELETE' });
                    showToast(t('admin.staff_deactivated_toast', { name: s.name }), 'info');
                    await loadAdminStaff();
                });
                elements.adminStaffList.appendChild(row);
            });
        } catch (err) {
            console.error('Erreur chargement serveurs:', err);
        }
    }

    async function loadPrintSettings() {
        try {
            await ensureAuthToken();
            const headers = state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
            const res = await fetch('/api/settings', { headers });
            if (!res.ok) return;
            const s = await res.json();
            const rl = document.getElementById('settingsReceiptLanguage');
            if (rl && s.receiptLanguage) rl.value = s.receiptLanguage;
            const kl = document.getElementById('settingsKitchenLanguage');
            if (kl && s.kitchenTicketLanguage) kl.value = s.kitchenTicketLanguage;

            const cn = document.getElementById('settingsCompanyName');
            if (cn) cn.value = s.companyName || '';
            const al = document.getElementById('settingsAddressLines');
            if (al) al.value = s.addressLines || '';
            const si = document.getElementById('settingsSiret');
            if (si) si.value = s.siret || '';
            const vn = document.getElementById('settingsVatNumber');
            if (vn) vn.value = s.vatNumber || '';
            const cr = document.getElementById('settingsCertificateNumber');
            if (cr) cr.value = s.certificateNumber || '';
            const sm = document.getElementById('settingsFiscalYearMonth');
            if (sm) sm.value = String(s.fiscalYearStartMonth || 1);
            const sd = document.getElementById('settingsFiscalYearDay');
            if (sd) sd.value = s.fiscalYearStartDay || 1;

            updateFiscalCertificateBadge(s.certificateNumber);
        } catch (err) {
            console.error('Erreur chargement réglages:', err);
        }
    }

    function updateFiscalCertificateBadge(certNumber) {
        const el = document.getElementById('fiscalCertBadge');
        if (!el) return;
        if (certNumber && certNumber.trim()) {
            el.innerHTML = `<span style="display:inline-block; padding:4px 10px; border-radius:999px; background:rgba(16,185,129,0.2); color:#10b981; font-weight:600; font-size:0.82rem;">✅ ${t('fiscal.cert_label')}: ${escapeHtml(certNumber.trim())}</span>`;
        } else {
            el.innerHTML = `<span style="display:inline-block; padding:4px 10px; border-radius:999px; background:rgba(234,179,8,0.2); color:#eab308; font-weight:600; font-size:0.82rem;">⏳ ${t('fiscal.cert_pending')}</span>`;
        }
    }

    async function saveEstablishmentSettings(e) {
        if (e) e.preventDefault();
        try {
            await ensureAuthToken();
            const headers = { 'Content-Type': 'application/json' };
            if (state.token) headers['Authorization'] = `Bearer ${state.token}`;
            const payload = {
                companyName: document.getElementById('settingsCompanyName')?.value?.trim() || null,
                addressLines: document.getElementById('settingsAddressLines')?.value?.trim() || null,
                siret: document.getElementById('settingsSiret')?.value?.trim() || null,
                vatNumber: document.getElementById('settingsVatNumber')?.value?.trim() || null,
                certificateNumber: document.getElementById('settingsCertificateNumber')?.value?.trim() || null,
                fiscalYearStartMonth: parseInt(document.getElementById('settingsFiscalYearMonth')?.value, 10) || null,
                fiscalYearStartDay: parseInt(document.getElementById('settingsFiscalYearDay')?.value, 10) || null
            };
            const res = await fetch('/api/settings', {
                method: 'PUT',
                headers,
                body: JSON.stringify(payload)
            });
            if (res.ok) {
                showToast(t('admin.settings_saved'), 'success');
                await loadPrintSettings();
            } else {
                const errData = await res.json().catch(() => ({}));
                showToast(errData.message || t('admin.settings_error'), 'error');
            }
        } catch (err) {
            console.error('Erreur sauvegarde identité:', err);
            showToast(t('admin.settings_error'), 'error');
        }
    }

    async function savePrintSettings() {
        await ensureAuthToken();
        const headers = { 'Content-Type': 'application/json' };
        if (state.token) headers['Authorization'] = `Bearer ${state.token}`;
        const res = await fetch('/api/settings', {
            method: 'PUT', headers,
            body: JSON.stringify({
                receiptLanguage: document.getElementById('settingsReceiptLanguage').value,
                kitchenTicketLanguage: document.getElementById('settingsKitchenLanguage').value
            })
        });
        showToast(res.ok ? t('admin.settings_saved') : await readApiError(res, t('admin.settings_error')), res.ok ? 'success' : 'error');
    }

    async function togglePrinterJobs(row, pr) {
        const existing = row.nextElementSibling;
        if (existing?.classList.contains('printer-jobs')) { existing.remove(); return; }
        const res = await fetch(`/api/printers/${pr.id}/jobs`);
        if (!res.ok) return;
        const jobs = (await res.json()).filter(j => j.status === 'Failed' || j.status === 'Pending');
        const kinds = {
            pickupvoucher: t('admin.print_job_kind_pickupvoucher'),
            receipt: t('admin.print_job_kind_receipt'),
            kitchenticket: t('admin.print_job_kind_kitchenticket'),
            report: t('admin.print_job_kind_report')
        };
        const box = document.createElement('div');
        box.className = 'printer-jobs';
        box.innerHTML = jobs.map(j => `
            <div class="item-list-row" data-job-id="${j.id}">
                <div>
                    ${kinds[String(j.kind).toLowerCase()] || escapeHtml(j.kind)} · ${new Date(j.createdAtUtc).toLocaleTimeString(window.i18n.locale, { hour: '2-digit', minute: '2-digit' })}
                    ${j.lastError ? `<small style="display:block;color:#f87171;">${escapeHtml(j.lastError)}</small>` : ''}
                </div>
                <button type="button" class="btn-archive btn-print-job-action" data-action="${j.status === 'Failed' ? 'retry' : 'cancel'}">${j.status === 'Failed' ? t('admin.print_job_retry') : t('admin.print_job_cancel')}</button>
            </div>`).join('');
        box.querySelectorAll('.btn-print-job-action').forEach(btn => btn.addEventListener('click', async () => {
            const id = btn.closest('[data-job-id]').dataset.jobId;
            const r = await fetch(`/api/print-jobs/${id}/${btn.dataset.action}`, { method: 'POST' });
            showToast(r.ok ? btn.textContent : await readApiError(r, String(r.status)), r.ok ? 'success' : 'error');
            await loadAdminPrinters();
        }));
        row.after(box);
    }

    async function loadAdminPrinters() {
        if (!elements.adminPrintersList) elements.adminPrintersList = document.getElementById('adminPrintersList');
        if (!elements.adminPrintersList) return;
        elements.adminPrintersList.innerHTML = '';
        loadPrintSettings();
        try {
            const res = await fetch('/api/printers');
            state.printers = await res.json();
            const statusRes = await fetch('/api/printers/status');
            const statuses = statusRes.ok ? await statusRes.json() : [];
            state.printers.forEach(pr => {
                const st = statuses.find(s => s.printerId === pr.id);
                const status = !st || st.isOnline === null ? t('admin.printer_status_unknown')
                    : st.isOnline ? t('admin.printer_status_online')
                    : t('admin.printer_status_offline_since', { time: new Date(st.sinceUtc).toLocaleTimeString(window.i18n.locale, { hour: '2-digit', minute: '2-digit' }) });
                const pending = st?.pendingCount ? ' · ' + t('admin.printer_pending', { count: st.pendingCount }) : '';
                const hasJobs = (st?.pendingCount || 0) + (st?.failedCount || 0) > 0;
                const row = document.createElement('div');
                row.className = 'item-list-row';
                row.innerHTML = `
                    <div>
                        <strong>${escapeHtml(pr.name)}</strong> (${escapeHtml(pr.ipAddress)}:${pr.port})${pr.textMode ? ` · ${t('admin.printer_text_mode_badge')}` : ''}${pr.isActive === false ? ` <span style="color:#f87171;">${t('admin.printer_row_inactive_suffix')}</span>` : ''}
                        <span class="printer-status">${status}${pending}</span>
                        <small style="display:block;color:#94a3b8;">${t('admin.printer_row_meta', { stations: (pr.assignedStationIds || []).join(', ') || '—', drawer: pr.openCashDrawerOnReceipt ? t('common.yes') : t('common.no') })}</small>
                    </div>
                    <div style="display:flex; gap:6px;">
                        ${hasJobs ? `<button class="btn-archive btn-printer-jobs" data-id="${pr.id}">${t('admin.printer_jobs_btn')}</button>` : ''}
                        <button class="btn-archive btn-edit-printer" data-id="${pr.id}" style="background:rgba(59,130,246,0.2); border-color:rgba(59,130,246,0.4); color:#60a5fa;">${t('admin.btn_edit_row')}</button>
                        <button class="btn-archive btn-del-printer" data-id="${pr.id}">${pr.isActive === false ? t('admin.btn_reactivate') : t('admin.btn_deactivate')}</button>
                    </div>
                `;
                row.querySelector('.btn-printer-jobs')?.addEventListener('click', () => togglePrinterJobs(row, pr));
                row.querySelector('.btn-edit-printer').addEventListener('click', () => {
                    document.getElementById('editPrinterId').value = pr.id;
                    document.getElementById('editPrinterName').value = pr.name;
                    document.getElementById('editPrinterIp').value = pr.ipAddress;
                    document.getElementById('editPrinterPort').value = pr.port;
                    document.getElementById('editPrinterDrawer').checked = !!pr.openCashDrawerOnReceipt;
                    document.getElementById('editPrinterTextMode').checked = !!pr.textMode;
                    elements.editPrinterModal.classList.add('active');
                });
                row.querySelector('.btn-del-printer').addEventListener('click', async () => {
                    // L'API n'expose pas de suppression : on (dés)active l'imprimante via PUT.
                    const activate = pr.isActive === false;
                    const res = await savePrinter(pr.id, { ...printerToUpdatePayload(pr), isActive: activate });
                    if (res.ok) {
                        showToast(activate ? t('admin.printer_reactivated_toast', { name: pr.name }) : t('admin.printer_deactivated_toast', { name: pr.name }), 'info');
                        await loadAdminPrinters();
                    }
                });
                elements.adminPrintersList.appendChild(row);
            });
        } catch (err) {
            console.error('Erreur chargement imprimantes:', err);
        }
    }

    // ==================== HAPPY HOUR MULTI-SELECT (Feature 020) ====================
    const hhAdminState = {
        schedules: [],
        selectedScheduleId: null,
        activeSubtab: 'hhSubtabArticles',
        selectedArticleIds: new Set(),
        selectedFamilyIds: new Set(),
        selectedActiveRuleIds: new Set(),
        articleFilterCatId: null,
        articleSearchQuery: '',
        priceMode: 'FixedPrice' // 'FixedPrice' or 'PercentageDiscount'
    };

    async function loadAdminHappyHour() {
        const selectSched = document.getElementById('selectHhActiveSchedule');
        const listAllSched = document.getElementById('adminHappyHourList');
        if (!selectSched && !listAllSched) return;

        try {
            const res = await fetch('/api/happy-hour/schedules');
            if (res.ok) {
                hhAdminState.schedules = await res.json() || [];
                
                // Populate schedule select dropdown
                if (selectSched) {
                    if (hhAdminState.schedules.length === 0) {
                        selectSched.innerHTML = `<option value="">${t('admin.hh_no_schedule_option')}</option>`;
                        hhAdminState.selectedScheduleId = null;
                    } else {
                        selectSched.innerHTML = hhAdminState.schedules.map(s => 
                            `<option value="${s.id}" ${s.id === hhAdminState.selectedScheduleId ? 'selected' : ''}>${s.name} (${s.startTime} - ${s.endTime})</option>`
                        ).join('');
                        if (!hhAdminState.selectedScheduleId || !hhAdminState.schedules.some(s => s.id === hhAdminState.selectedScheduleId)) {
                            hhAdminState.selectedScheduleId = hhAdminState.schedules[0].id;
                        }
                    }
                }

                // Render all sub-sections
                renderHhArticleFilters();
                renderHhArticlesGrid();
                renderHhFamiliesGrid();
                renderHhActiveRules();
                renderHhAllSchedulesList();
            }
        } catch (err) {
            console.error('Erreur chargement plannings Happy Hour:', err);
        }
    }

    function renderHhArticleFilters() {
        const container = document.getElementById('hhArticleCategoryFilters');
        if (!container) return;

        let html = `<button type="button" class="hh-pill-filter ${hhAdminState.articleFilterCatId === null ? 'active' : ''}" data-cat-id="all">${t('admin.hh_all_categories')}</button>`;
        if (state.categories) {
            html += state.categories.map(c => 
                `<button type="button" class="hh-pill-filter ${hhAdminState.articleFilterCatId === c.id ? 'active' : ''}" data-cat-id="${c.id}">${c.name}</button>`
            ).join('');
        }
        container.innerHTML = html;

        container.querySelectorAll('.hh-pill-filter').forEach(btn => {
            btn.addEventListener('click', () => {
                const catId = btn.getAttribute('data-cat-id');
                hhAdminState.articleFilterCatId = catId === 'all' ? null : catId;
                renderHhArticleFilters();
                renderHhArticlesGrid();
            });
        });
    }

    function renderHhArticlesGrid() {
        const grid = document.getElementById('hhArticlesGrid');
        const countSpan = document.getElementById('hhSelectedArticlesCount');
        if (!grid) return;

        let filtered = state.products || [];
        if (hhAdminState.articleFilterCatId) {
            filtered = filtered.filter(p => p.categoryId === hhAdminState.articleFilterCatId);
        }
        if (hhAdminState.articleSearchQuery.trim()) {
            const q = hhAdminState.articleSearchQuery.toLowerCase().trim();
            filtered = filtered.filter(p => p.name.toLowerCase().includes(q));
        }

        if (countSpan) {
            countSpan.textContent = hhAdminState.selectedArticleIds.size;
        }

        if (filtered.length === 0) {
            grid.innerHTML = `<p class="text-muted" style="grid-column:1/-1; padding:20px; text-align:center;">${t('admin.hh_no_articles_found')}</p>`;
            return;
        }

        grid.innerHTML = filtered.map(p => {
            const isChecked = hhAdminState.selectedArticleIds.has(p.id);
            return `
                <div class="hh-select-card ${isChecked ? 'selected' : ''}" data-prod-id="${p.id}">
                    <input type="checkbox" ${isChecked ? 'checked' : ''} data-prod-id="${p.id}">
                    <div class="hh-select-card-info">
                        <div class="hh-select-card-title">${p.name}</div>
                        <div class="hh-select-card-sub">${t('admin.hh_std_price', { price: p.price.toFixed(2) })}</div>
                    </div>
                </div>
            `;
        }).join('');

        grid.querySelectorAll('.hh-select-card').forEach(card => {
            card.addEventListener('click', (e) => {
                const prodId = card.getAttribute('data-prod-id');
                const cb = card.querySelector('input[type="checkbox"]');
                if (e.target !== cb) {
                    cb.checked = !cb.checked;
                }
                if (cb.checked) {
                    hhAdminState.selectedArticleIds.add(prodId);
                    card.classList.add('selected');
                } else {
                    hhAdminState.selectedArticleIds.delete(prodId);
                    card.classList.remove('selected');
                }
                if (countSpan) countSpan.textContent = hhAdminState.selectedArticleIds.size;
            });
        });
    }

    function renderHhFamiliesGrid() {
        const grid = document.getElementById('hhFamiliesGrid');
        const countSpan = document.getElementById('hhSelectedFamiliesCount');
        if (!grid) return;

        if (countSpan) {
            countSpan.textContent = hhAdminState.selectedFamilyIds.size;
        }

        const cats = state.categories || [];
        if (cats.length === 0) {
            grid.innerHTML = `<p class="text-muted" style="grid-column:1/-1; padding:20px; text-align:center;">${t('admin.hh_no_families_defined')}</p>`;
            return;
        }

        grid.innerHTML = cats.map(c => {
            const isChecked = hhAdminState.selectedFamilyIds.has(c.id);
            const prodsInCat = (state.products || []).filter(p => p.categoryId === c.id);
            return `
                <div class="hh-select-card ${isChecked ? 'selected' : ''}" data-cat-id="${c.id}">
                    <input type="checkbox" ${isChecked ? 'checked' : ''} data-cat-id="${c.id}">
                    <div class="hh-select-card-info">
                        <div class="hh-select-card-title">${c.name}</div>
                        <div class="hh-select-card-sub">${t('admin.hh_products_included_count', { count: prodsInCat.length })}</div>
                    </div>
                </div>
            `;
        }).join('');

        grid.querySelectorAll('.hh-select-card').forEach(card => {
            card.addEventListener('click', (e) => {
                const catId = card.getAttribute('data-cat-id');
                const cb = card.querySelector('input[type="checkbox"]');
                if (e.target !== cb) {
                    cb.checked = !cb.checked;
                }
                if (cb.checked) {
                    hhAdminState.selectedFamilyIds.add(catId);
                    card.classList.add('selected');
                } else {
                    hhAdminState.selectedFamilyIds.delete(catId);
                    card.classList.remove('selected');
                }
                if (countSpan) countSpan.textContent = hhAdminState.selectedFamilyIds.size;
            });
        });
    }

    function renderHhActiveRules() {
        const catList = document.getElementById('hhActiveCategoryRulesList');
        const prodList = document.getElementById('hhActiveProductRulesList');
        const badge = document.getElementById('hhRulesCountBadge');
        if (!catList || !prodList) return;

        const currentSched = hhAdminState.schedules.find(s => s.id === hhAdminState.selectedScheduleId);
        const rules = currentSched ? (currentSched.priceRules || []) : [];
        if (badge) badge.textContent = rules.length;

        const catRules = rules.filter(r => r.targetType === 1 || r.targetType === 'Category');
        const prodRules = rules.filter(r => r.targetType === 0 || r.targetType === 'Product');

        if (catRules.length === 0) {
            catList.innerHTML = `<p class="text-muted" style="font-size:0.85rem;">${t('admin.hh_no_category_rules')}</p>`;
        } else {
            catList.innerHTML = catRules.map(r => `
                <div class="hh-active-rule-item">
                    <div style="display:flex; align-items:center; gap:10px;">
                        <input type="checkbox" class="cb-active-rule" data-rule-id="${r.id}" ${hhAdminState.selectedActiveRuleIds.has(r.id) ? 'checked' : ''}>
                        <div>
                            <strong>🏷️ ${r.targetName || t('admin.hh_category_fallback_name')}</strong>
                            <span style="color:#f59e0b; margin-inline-start:6px; font-weight:bold;">-${r.discountPercent}%</span>
                        </div>
                    </div>
                    <button type="button" class="btn-archive btn-del-single-rule" data-rule-id="${r.id}" style="color:#f87171; border-color:rgba(239,68,68,0.3); padding:4px 8px; font-size:0.8rem;">🗑️</button>
                </div>
            `).join('');
        }

        if (prodRules.length === 0) {
            prodList.innerHTML = `<p class="text-muted" style="font-size:0.85rem;">${t('admin.hh_no_product_rules')}</p>`;
        } else {
            prodList.innerHTML = prodRules.map(r => `
                <div class="hh-active-rule-item">
                    <div style="display:flex; align-items:center; gap:10px;">
                        <input type="checkbox" class="cb-active-rule" data-rule-id="${r.id}" ${hhAdminState.selectedActiveRuleIds.has(r.id) ? 'checked' : ''}>
                        <div>
                            <strong>🍺 ${r.targetName || t('admin.hh_product_fallback_name')}</strong>
                            <span style="color:#38bdf8; margin-inline-start:6px; font-weight:bold;">${r.fixedPrice ? r.fixedPrice.toFixed(2) + ' €' : '-' + r.discountPercent + '%'}</span>
                        </div>
                    </div>
                    <button type="button" class="btn-archive btn-del-single-rule" data-rule-id="${r.id}" style="color:#f87171; border-color:rgba(239,68,68,0.3); padding:4px 8px; font-size:0.8rem;">🗑️</button>
                </div>
            `).join('');
        }

        // Attach checkbox change handlers
        document.querySelectorAll('.cb-active-rule').forEach(cb => {
            cb.addEventListener('change', () => {
                const rId = cb.getAttribute('data-rule-id');
                if (cb.checked) hhAdminState.selectedActiveRuleIds.add(rId);
                else hhAdminState.selectedActiveRuleIds.delete(rId);
            });
        });

        // Attach single delete handlers
        document.querySelectorAll('.btn-del-single-rule').forEach(btn => {
            btn.addEventListener('click', async () => {
                const ruleId = btn.getAttribute('data-rule-id');
                await deleteRulesBatch([ruleId]);
            });
        });
    }

    function renderHhAllSchedulesList() {
        const listEl = document.getElementById('adminHappyHourList');
        if (!listEl) return;

        if (hhAdminState.schedules.length === 0) {
            listEl.innerHTML = `<p class="text-muted">${t('admin.hh_no_schedules_configured')}</p>`;
            return;
        }

        listEl.innerHTML = hhAdminState.schedules.map(s => {
            const daysMap = [t('admin.day_sun'), t('admin.day_mon'), t('admin.day_tue'), t('admin.day_wed'), t('admin.day_thu'), t('admin.day_fri'), t('admin.day_sat')];
            const dayLabels = (s.daysOfWeek || []).map(d => daysMap[d] || d).join(', ');
            const rulesCount = (s.priceRules || []).length;
            const isSelected = s.id === hhAdminState.selectedScheduleId;

            return `
                <div style="background:rgba(255,255,255,0.03); padding:12px; border-radius:8px; border:1px solid ${isSelected ? 'var(--primary)' : 'rgba(255,255,255,0.06)'}; display:flex; justify-content:space-between; align-items:center;">
                    <div>
                        <strong>🍻 ${s.name}</strong>
                        <span style="font-size:0.75rem; color:#f59e0b; margin-inline-start:6px;">${s.startTime} - ${s.endTime}</span>
                        <span style="font-size:0.72rem; background:rgba(59,130,246,0.2); color:#60a5fa; border:1px solid rgba(59,130,246,0.3); padding:2px 6px; border-radius:10px; margin-inline-start:6px;">${t('admin.hh_priority_value', { value: s.priority || 1 })}</span>
                        <div style="font-size:0.8rem; color:#94a3b8; margin-top:2px;">${t('admin.hh_days_value', { days: dayLabels })} ${s.appliesToTakeaway ? t('admin.hh_takeaway_included') : t('admin.hh_dine_in_only')}</div>
                        <div style="font-size:0.78rem; color:#38bdf8; margin-top:2px;">${t('admin.hh_rules_configured_count', { count: rulesCount })}</div>
                    </div>
                    <div style="display:flex; gap:8px;">
                        <button class="btn-primary btn-select-hh-sched" data-id="${s.id}" style="padding:6px 12px; font-size:0.8rem;">${t('admin.hh_manage_pricing')}</button>
                        <button class="btn-archive btn-del-hh-sched" data-id="${s.id}" style="color:#f87171; border-color:rgba(239,68,68,0.3); padding:6px 10px; font-size:0.8rem;">🗑️</button>
                    </div>
                </div>
            `;
        }).join('');

        listEl.querySelectorAll('.btn-select-hh-sched').forEach(btn => {
            btn.addEventListener('click', () => {
                const sId = btn.getAttribute('data-id');
                hhAdminState.selectedScheduleId = sId;
                const select = document.getElementById('selectHhActiveSchedule');
                if (select) select.value = sId;
                switchHhSubtab('hhSubtabArticles');
                renderHhActiveRules();
            });
        });

        listEl.querySelectorAll('.btn-del-hh-sched').forEach(btn => {
            btn.addEventListener('click', async () => {
                const schedId = btn.getAttribute('data-id');
                await fetch(`/api/happy-hour/schedules/${schedId}`, { method: 'DELETE' });
                showToast(t('admin.hh_schedule_deleted_toast'), 'info');
                await loadAdminHappyHour();
                await checkHappyHourStatus();
            });
        });
    }

    function switchHhSubtab(subtabId) {
        hhAdminState.activeSubtab = subtabId;
        document.querySelectorAll('.hh-subtab-btn').forEach(btn => {
            btn.classList.toggle('active', btn.getAttribute('data-hh-subtab') === subtabId);
        });
        document.querySelectorAll('.hh-subtab-pane').forEach(pane => {
            pane.style.display = pane.id === subtabId ? 'block' : 'none';
        });
    }

    async function applyBatchArticles() {
        if (!hhAdminState.selectedScheduleId) {
            showToast(t('admin.hh_select_schedule_first_warning'), 'warning');
            return;
        }
        if (hhAdminState.selectedArticleIds.size === 0) {
            showToast(t('admin.hh_select_article_warning'), 'warning');
            return;
        }

        const valInput = document.getElementById('inputHhBatchValue');
        const value = parseFloat(valInput ? valInput.value : 0);
        if (isNaN(value) || value <= 0) {
            showToast(t('admin.hh_invalid_price_error'), 'error');
            return;
        }

        const isFixed = hhAdminState.priceMode === 'FixedPrice';
        const payload = {
            targetType: 0, // Product = 0
            targetIds: Array.from(hhAdminState.selectedArticleIds),
            pricingMode: isFixed ? 0 : 1, // FixedPrice = 0, PercentageDiscount = 1
            fixedPrice: isFixed ? value : null,
            discountPercent: isFixed ? null : value
        };

        try {
            const res = await fetch(`/api/happy-hour/schedules/${hhAdminState.selectedScheduleId}/rules/batch`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (res.ok) {
                const data = await res.json();
                showToast(t('admin.hh_rules_applied_toast', { count: data.appliedCount || payload.targetIds.length }), 'success');
                hhAdminState.selectedArticleIds.clear();
                await loadAdminHappyHour();
                await checkHappyHourStatus();
            } else {
                const err = await res.json().catch(() => ({}));
                showToast(err.message || t('admin.hh_batch_apply_error'), 'error');
            }
        } catch (err) {
            console.error('Erreur batch rules articles:', err);
            showToast(t('admin.hh_save_network_error'), 'error');
        }
    }

    async function applyBatchFamilies() {
        if (!hhAdminState.selectedScheduleId) {
            showToast(t('admin.hh_select_schedule_first_warning'), 'warning');
            return;
        }
        if (hhAdminState.selectedFamilyIds.size === 0) {
            showToast(t('admin.hh_select_family_warning'), 'warning');
            return;
        }

        const discountInput = document.getElementById('inputHhFamilyDiscount');
        const discount = parseFloat(discountInput ? discountInput.value : 0);
        if (isNaN(discount) || discount <= 0 || discount > 100) {
            showToast(t('admin.hh_discount_range_error'), 'error');
            return;
        }

        const payload = {
            targetType: 1, // Category = 1
            targetIds: Array.from(hhAdminState.selectedFamilyIds),
            pricingMode: 1, // PercentageDiscount = 1
            fixedPrice: null,
            discountPercent: discount
        };

        try {
            const res = await fetch(`/api/happy-hour/schedules/${hhAdminState.selectedScheduleId}/rules/batch`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (res.ok) {
                const data = await res.json();
                showToast(t('admin.hh_families_configured_toast', { count: data.appliedRulesCount || payload.targetIds.length, discount }), 'success');
                hhAdminState.selectedFamilyIds.clear();
                await loadAdminHappyHour();
                await checkHappyHourStatus();
            } else {
                const err = await res.json().catch(() => ({}));
                showToast(err.message || t('admin.hh_batch_families_error'), 'error');
            }
        } catch (err) {
            console.error('Erreur batch rules familles:', err);
            showToast(t('admin.hh_save_network_error'), 'error');
        }
    }

    async function deleteRulesBatch(ruleIds) {
        if (!hhAdminState.selectedScheduleId) return;
        if (!ruleIds || ruleIds.length === 0) {
            showToast(t('admin.hh_no_rule_selected_warning'), 'warning');
            return;
        }

        try {
            const res = await fetch(`/api/happy-hour/schedules/${hhAdminState.selectedScheduleId}/rules/batch`, {
                method: 'DELETE',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ ruleIds })
            });

            if (res.ok) {
                showToast(t('admin.hh_rules_deleted_toast', { count: ruleIds.length }), 'info');
                ruleIds.forEach(id => hhAdminState.selectedActiveRuleIds.delete(id));
                await loadAdminHappyHour();
                await checkHappyHourStatus();
            } else {
                showToast(t('admin.hh_delete_rules_error'), 'error');
            }
        } catch (err) {
            console.error('Erreur suppression lot règles:', err);
            showToast(t('admin.hh_delete_rules_network_error'), 'error');
        }
    }

    function setupForms() {
        // Schedule Selection Dropdown
        const selectSched = document.getElementById('selectHhActiveSchedule');
        if (selectSched) {
            selectSched.addEventListener('change', () => {
                hhAdminState.selectedScheduleId = selectSched.value;
                renderHhActiveRules();
                renderHhAllSchedulesList();
            });
        }

        // Toggle New Schedule Form Collapsible
        const btnToggleForm = document.getElementById('btnToggleNewScheduleForm');
        const containerForm = document.getElementById('containerNewScheduleForm');
        const btnCancelNewSched = document.getElementById('btnCancelNewSchedule');
        if (btnToggleForm && containerForm) {
            btnToggleForm.addEventListener('click', () => {
                containerForm.style.display = containerForm.style.display === 'none' ? 'block' : 'none';
            });
            if (btnCancelNewSched) {
                btnCancelNewSched.addEventListener('click', () => {
                    containerForm.style.display = 'none';
                });
            }
        }

        // Subtabs Navigation
        document.querySelectorAll('.hh-subtab-btn').forEach(btn => {
            btn.addEventListener('click', () => {
                const subtab = btn.getAttribute('data-hh-subtab');
                switchHhSubtab(subtab);
            });
        });

        // Search Filter for Articles
        const inputSearch = document.getElementById('inputHhArticleSearch');
        if (inputSearch) {
            inputSearch.addEventListener('input', (e) => {
                hhAdminState.articleSearchQuery = e.target.value;
                renderHhArticlesGrid();
            });
        }

        // Articles Select All / Deselect All
        const btnSelectAllArticles = document.getElementById('btnHhSelectAllArticles');
        const btnDeselectAllArticles = document.getElementById('btnHhDeselectAllArticles');
        if (btnSelectAllArticles) {
            btnSelectAllArticles.addEventListener('click', () => {
                let filtered = state.products || [];
                if (hhAdminState.articleFilterCatId) {
                    filtered = filtered.filter(p => p.categoryId === hhAdminState.articleFilterCatId);
                }
                if (hhAdminState.articleSearchQuery.trim()) {
                    const q = hhAdminState.articleSearchQuery.toLowerCase().trim();
                    filtered = filtered.filter(p => p.name.toLowerCase().includes(q));
                }
                filtered.forEach(p => hhAdminState.selectedArticleIds.add(p.id));
                renderHhArticlesGrid();
            });
        }
        if (btnDeselectAllArticles) {
            btnDeselectAllArticles.addEventListener('click', () => {
                hhAdminState.selectedArticleIds.clear();
                renderHhArticlesGrid();
            });
        }

        // Families Select All / Deselect All
        const btnSelectAllFamilies = document.getElementById('btnHhSelectAllFamilies');
        const btnDeselectAllFamilies = document.getElementById('btnHhDeselectAllFamilies');
        if (btnSelectAllFamilies) {
            btnSelectAllFamilies.addEventListener('click', () => {
                (state.categories || []).forEach(c => hhAdminState.selectedFamilyIds.add(c.id));
                renderHhFamiliesGrid();
            });
        }
        if (btnDeselectAllFamilies) {
            btnDeselectAllFamilies.addEventListener('click', () => {
                hhAdminState.selectedFamilyIds.clear();
                renderHhFamiliesGrid();
            });
        }

        // Articles Price Mode Toggle (Fixed vs Discount)
        const btnFixed = document.getElementById('btnHhModeFixed');
        const btnPercent = document.getElementById('btnHhModePercent');
        const valUnit = document.getElementById('hhBatchValueUnit');
        const valInput = document.getElementById('inputHhBatchValue');
        if (btnFixed && btnPercent) {
            btnFixed.addEventListener('click', () => {
                hhAdminState.priceMode = 'FixedPrice';
                btnFixed.classList.add('active');
                btnPercent.classList.remove('active');
                if (valUnit) valUnit.textContent = '€';
                if (valInput) { valInput.value = '5.00'; valInput.step = '0.10'; }
            });
            btnPercent.addEventListener('click', () => {
                hhAdminState.priceMode = 'PercentageDiscount';
                btnPercent.classList.add('active');
                btnFixed.classList.remove('active');
                if (valUnit) valUnit.textContent = '%';
                if (valInput) { valInput.value = '25'; valInput.step = '1'; }
            });
        }

        // Batch Apply Buttons
        const btnApplyArticles = document.getElementById('btnApplyBatchArticles');
        if (btnApplyArticles) {
            btnApplyArticles.addEventListener('click', applyBatchArticles);
        }
        const btnApplyFamilies = document.getElementById('btnApplyBatchFamilies');
        if (btnApplyFamilies) {
            btnApplyFamilies.addEventListener('click', applyBatchFamilies);
        }

        // Active Rules Select All & Batch Delete
        const btnSelectAllActive = document.getElementById('btnHhSelectAllActiveRules');
        const btnDeleteSelected = document.getElementById('btnDeleteSelectedRules');
        if (btnSelectAllActive) {
            btnSelectAllActive.addEventListener('click', () => {
                const currentSched = hhAdminState.schedules.find(s => s.id === hhAdminState.selectedScheduleId);
                const rules = currentSched ? (currentSched.priceRules || []) : [];
                rules.forEach(r => hhAdminState.selectedActiveRuleIds.add(r.id));
                renderHhActiveRules();
            });
        }
        if (btnDeleteSelected) {
            btnDeleteSelected.addEventListener('click', async () => {
                if (hhAdminState.selectedActiveRuleIds.size === 0) {
                    showToast(t('admin.hh_check_rule_warning'), 'warning');
                    return;
                }
                await deleteRulesBatch(Array.from(hhAdminState.selectedActiveRuleIds));
            });
        }

        // Happy Hour Add Schedule Form
        const formHh = document.getElementById('formAddHappyHourSchedule');
        if (formHh) {
            formHh.addEventListener('submit', async (e) => {
                e.preventDefault();
                const name = document.getElementById('inputHhScheduleName').value.trim();
                const startTime = document.getElementById('inputHhStartTime').value;
                const endTime = document.getElementById('inputHhEndTime').value;
                const takeaway = document.getElementById('checkHhTakeaway').checked;
                const priorityEl = document.getElementById('inputHhPriority');
                const priority = priorityEl ? (parseInt(priorityEl.value, 10) || 1) : 1;
                const checkedDays = Array.from(document.querySelectorAll('input[name="hhDays"]:checked')).map(cb => parseInt(cb.value, 10));

                const payload = {
                    name: name,
                    daysOfWeek: checkedDays,
                    startTime: startTime,
                    endTime: endTime,
                    isActive: true,
                    appliesToTakeaway: takeaway,
                    priority: priority,
                    priceRules: []
                };

                const res = await fetch('/api/happy-hour/schedules', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify(payload)
                });

                if (res.ok) {
                    const newSched = await res.json();
                    showToast(t('admin.hh_schedule_created_toast'), 'success');
                    formHh.reset();
                    if (containerForm) containerForm.style.display = 'none';
                    hhAdminState.selectedScheduleId = newSched.id;
                    await loadAdminHappyHour();
                    await checkHappyHourStatus();
                } else {
                    showToast(t('admin.hh_schedule_create_error'), 'error');
                }
            });
        }

        // Add Category Form
        elements.formAddCategory.addEventListener('submit', async (e) => {
            e.preventDefault();
            const name = document.getElementById('inputCatName').value;
            const color = document.getElementById('inputCatColor').value;

            const res = await fetch('/api/catalog/categories', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name, colorHex: color, displayOrder: state.categories.length + 1, iconName: 'utensils' })
            });

            if (res.ok) {
                showToast(t('admin.category_created_toast', { name }), 'success');
                document.getElementById('inputCatName').value = '';
                await loadCatalogData();
            }
        });

        // Add Product Form
        elements.formAddProduct.addEventListener('submit', async (e) => {
            e.preventDefault();
            const name = document.getElementById('inputProdName').value;
            const categoryId = elements.selectProductCat.value;
            const price = parseFloat(document.getElementById('inputProdPrice').value);
            const taxEl = document.getElementById('selectProdVat');
            const tax = taxEl ? parseFloat(taxEl.value) : 10.0;
            const stationEl = document.getElementById('selectProdStation');
            const station = stationEl ? (stationEl.value || null) : null;
            const quickEl = document.getElementById('checkQuickKey');
            const isQuick = quickEl ? quickEl.checked : false;

            const res = await fetch('/api/catalog/products', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name,
                    categoryId,
                    price,
                    taxRatePercent: tax,
                    stationId: station,
                    isQuickKey: isQuick,
                    displayOrder: state.products.length + 1,
                    description: '',
                    colorHex: '#3b82f6'
                })
            });

            if (res.ok) {
                showToast(t('admin.product_created_toast', { name }), 'success');
                document.getElementById('inputProdName').value = '';
                document.getElementById('inputProdPrice').value = '';
                await loadCatalogData();
                await loadAdminCatalog();
            }
        });

        // Add Staff Form
        elements.formAddStaff.addEventListener('submit', async (e) => {
            e.preventDefault();
            const name = document.getElementById('inputStaffName').value;
            const roleEl = document.getElementById('selectStaffRole');
            const role = roleEl ? roleEl.value : 'Waiter';
            const pin = document.getElementById('inputStaffPin').value;

            const res = await fetch('/api/staff', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name, role, pin })
            });

            if (res.ok) {
                showToast(t('admin.staff_created_toast', { name }), 'success');
                document.getElementById('inputStaffName').value = '';
                document.getElementById('inputStaffPin').value = '';
                await loadAdminStaff();
            } else {
                showToast(await readApiError(res, t('admin.staff_create_error')), 'error');
            }
        });

        // Add Printer Form
        elements.formAddPrinter.addEventListener('submit', async (e) => {
            e.preventDefault();
            const name = document.getElementById('inputPrinterName').value;
            const ip = document.getElementById('inputPrinterIp').value;
            const drawerEl = document.getElementById('checkPrinterDrawer');
            const drawer = drawerEl ? drawerEl.checked : false;

            const res = await fetch('/api/printers', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name,
                    ipAddress: ip,
                    port: 9100,
                    paperWidthMm: 80,
                    openCashDrawerOnReceipt: drawer,
                    textMode: document.getElementById('newPrinterTextMode')?.checked === true,
                    assignedStationIds: ["HOT_KITCHEN", "RECEIPT"]
                })
            });

            if (res.ok) {
                showToast(t('admin.printer_created_toast', { name }), 'success');
                document.getElementById('inputPrinterName').value = '';
                document.getElementById('inputPrinterIp').value = '';
                document.getElementById('newPrinterTextMode').checked = false;
                await loadAdminPrinters();
            }
        });

        // Fiscal Reports (NF525)
        if (elements.btnPreviewX) {
            elements.btnPreviewX.addEventListener('click', async () => {
                await previewXReport();
                showToast(t('fiscal.x_report_refreshed_toast'), 'info');
            });
        }

        if (elements.btnExecuteZ) {
            elements.btnExecuteZ.addEventListener('click', async () => {
                const managerId = state.operator?.id || '01a067d9-b8b9-7b6a-8b3c-d272e6128c35';
                const managerName = state.operator?.name || 'Alexandre Dupont (Manager)';
                try {
                    await ensureAuthToken();
                    const headers = { 'Content-Type': 'application/json' };
                    if (state.token) {
                        headers['Authorization'] = `Bearer ${state.token}`;
                    }
                    const res = await fetch('/api/fiscal/z-closure', {
                        method: 'POST',
                        headers: headers,
                        body: JSON.stringify({
                            terminalId: 'POS_MAIN_TERM',
                            managerId: managerId,
                            managerName: managerName
                        })
                    });
                    if (res.ok) {
                        const closure = await res.json();
                        renderFiscalSlip(closure, true);
                        showToast(t('fiscal.z_closure_success_toast'), 'success');
                        if (closure.printQueued === false) showToast(t('payment.print_not_queued'), 'warning');
                    } else {
                        const err = await res.json().catch(() => ({ message: t('fiscal.z_closure_error') }));
                        showToast(err.message || t('fiscal.z_closure_error'), 'error');
                    }
                } catch (err) {
                    console.error('Erreur clôture Z:', err);
                    showToast(t('fiscal.z_closure_network_error'), 'error');
                }
            });
        }

        if (elements.btnPrintXReport) elements.btnPrintXReport.addEventListener('click', () => postFiscalPrint('/api/fiscal/x-report/print'));
        if (elements.btnReprintZ) elements.btnReprintZ.addEventListener('click', () => postFiscalPrint('/api/fiscal/latest-closure/print'));

        if (elements.btnReprintReceipt) {
            elements.btnReprintReceipt.addEventListener('click', async () => {
                const val = elements.reprintReceiptInput?.value?.trim();
                if (val) await reprintReceipt(val);
            });
        }

        if (elements.btnReprintCurrentReceipt) {
            elements.btnReprintCurrentReceipt.addEventListener('click', async () => {
                if (!state.lastPaidReceiptNumber) return;
                const data = await reprintReceipt(state.lastPaidReceiptNumber);
                if (data && elements.reprintCurrentReceiptNotice) {
                    elements.reprintCurrentReceiptNotice.textContent = t('fiscal.duplicate_banner', { number: data.duplicateNumber });
                    elements.reprintCurrentReceiptNotice.style.display = 'block';
                }
            });
        }

        if (elements.periodClosureType) {
            elements.periodClosureType.addEventListener('change', updateDefaultPeriodKey);
            updateDefaultPeriodKey();
        }
        if (elements.btnExecutePeriodClosure) {
            elements.btnExecutePeriodClosure.addEventListener('click', executePeriodClosure);
        }
        if (elements.btnVerifyArchive) {
            elements.btnVerifyArchive.addEventListener('click', verifyArchiveFile);
        }
        if (elements.periodClosuresTableBody) {
            elements.periodClosuresTableBody.addEventListener('click', async (e) => {
                const btn = e.target.closest('.btn-create-archive');
                if (!btn) return;
                const closureId = btn.dataset.closureId;
                if (!closureId) return;
                btn.disabled = true;
                await createArchive(closureId);
            });
        }

        if (elements.btnVerifyChains) {
            elements.btnVerifyChains.addEventListener('click', async () => {
                try {
                    await ensureAuthToken();
                    const headers = { 'Content-Type': 'application/json' };
                    if (state.token) {
                        headers['Authorization'] = `Bearer ${state.token}`;
                    }
                    elements.btnVerifyChains.disabled = true;

                    const res = await fetch('/api/fiscal/verify', {
                        method: 'POST',
                        headers: headers
                    });

                    if (res.ok) {
                        const data = await res.json();
                        renderVerificationResult(data);
                        if (data.isValid) {
                            showToast(t('fiscal.verification_success'), 'success');
                        } else {
                            showToast(t('fiscal.verification_failed'), 'error');
                        }
                    } else {
                        const err = await res.json().catch(() => ({ message: t('fiscal.verify_error') }));
                        showToast(err.message || t('fiscal.verify_error'), 'error');
                    }
                } catch (err) {
                    console.error('Erreur vérification chaînes:', err);
                    showToast(t('fiscal.verify_error'), 'error');
                } finally {
                    elements.btnVerifyChains.disabled = false;
                }
            });
        }

        // FEC Export Handler
        if (elements.btnExportFec) {
            const now = new Date();
            const firstDay = new Date(now.getFullYear(), now.getMonth(), 1);
            if (elements.fecStartDate && !elements.fecStartDate.value) {
                elements.fecStartDate.value = firstDay.toISOString().split('T')[0];
            }
            if (elements.fecEndDate && !elements.fecEndDate.value) {
                elements.fecEndDate.value = now.toISOString().split('T')[0];
            }

            elements.btnExportFec.addEventListener('click', async () => {
                const startDate = elements.fecStartDate ? elements.fecStartDate.value : '';
                const endDate = elements.fecEndDate ? elements.fecEndDate.value : '';
                const siren = elements.fecSiren ? elements.fecSiren.value.trim() : '123456789';

                if (elements.fecStatusMessage) {
                    elements.fecStatusMessage.textContent = t('fiscal.fec_generating_status');
                    elements.fecStatusMessage.style.color = '#38bdf8';
                }

                try {
                    let url = `/api/fiscal/fec?siren=${encodeURIComponent(siren)}`;
                    if (startDate) url += `&from=${encodeURIComponent(startDate + 'T00:00:00Z')}`;
                    if (endDate) url += `&to=${encodeURIComponent(endDate + 'T23:59:59Z')}`;

                    const res = await fetch(url);
                    if (res.ok) {
                        const blob = await res.blob();
                        const downloadUrl = window.URL.createObjectURL(blob);
                        const a = document.createElement('a');
                        a.style.display = 'none';
                        a.href = downloadUrl;

                        let filename = `${siren}FEC${endDate ? endDate.replace(/-/g, '') : '20261231'}.txt`;
                        const disposition = res.headers.get('content-disposition');
                        if (disposition && disposition.includes('filename=')) {
                            const match = disposition.match(/filename[^;=\n]*=((['"]).*?\2|[^;\n]*)/);
                            if (match && match[1]) {
                                filename = match[1].replace(/['"]/g, '');
                            }
                        }

                        a.download = filename;
                        document.body.appendChild(a);
                        a.click();
                        window.URL.revokeObjectURL(downloadUrl);
                        a.remove();

                        if (elements.fecStatusMessage) {
                            elements.fecStatusMessage.textContent = t('fiscal.fec_generated_status', { filename });
                            elements.fecStatusMessage.style.color = '#10b981';
                        }
                        showToast(t('fiscal.fec_downloaded_toast', { filename }), 'success');
                    } else if (res.status === 401 || res.status === 403) {
                        if (elements.fecStatusMessage) {
                            elements.fecStatusMessage.textContent = t('fiscal.fec_insufficient_privileges_status');
                            elements.fecStatusMessage.style.color = '#ef4444';
                        }
                        showToast(t('fiscal.fec_export_denied_toast'), 'error');
                    } else {
                        if (elements.fecStatusMessage) {
                            elements.fecStatusMessage.textContent = t('fiscal.fec_generation_error_status', { status: res.status });
                            elements.fecStatusMessage.style.color = '#ef4444';
                        }
                        showToast(t('fiscal.fec_generation_error_toast'), 'error');
                    }
                } catch (err) {
                    console.error('Erreur export FEC:', err);
                    if (elements.fecStatusMessage) {
                        elements.fecStatusMessage.textContent = t('fiscal.fec_communication_error_status');
                        elements.fecStatusMessage.style.color = '#ef4444';
                    }
                    showToast(t('fiscal.fec_download_error_toast'), 'error');
                }
            });
        }

        // Network Sync Handlers
        setupNetworkSyncHandlers();
        setupDevicePairingHandlers();
        setupDeviceAdminHandlers();
    }

    // ==================== FISCAL TRAIL & NF525 REPORTS ====================
    async function loadFiscalViewData() {
        try {
            await ensureAuthToken();
            loadPrintSettings();
            await loadPeriodClosures();
            const headers = state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
            const res = await fetch('/api/fiscal/latest-closure', { headers });
            if (res.ok) {
                const closure = await res.json();
                renderFiscalSlip(closure, true);
            } else {
                await previewXReport();
            }
        } catch {
            await previewXReport();
        }
    }

    async function postFiscalPrint(path) {
        await ensureAuthToken();
        const headers = state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
        const res = await fetch(`${path}?terminalId=POS_MAIN_TERM`, { method: 'POST', headers });
        if (!res.ok) {
            showToast(await readApiError(res, t('fiscal.print_error')), 'error');
            return;
        }
        const data = await res.json();
        if (data.duplicateNumber) {
            if (elements.slipDuplicateBanner) {
                elements.slipDuplicateBanner.textContent = t('fiscal.duplicate_banner', { number: data.duplicateNumber });
                elements.slipDuplicateBanner.style.display = 'block';
            }
            showToast(t('fiscal.duplicate_queued', { number: data.duplicateNumber }), 'success');
        } else {
            if (elements.slipDuplicateBanner) {
                elements.slipDuplicateBanner.style.display = 'none';
            }
            showToast(data.printQueued ? t('fiscal.print_queued') : t('payment.print_not_queued'), data.printQueued ? 'success' : 'warning');
        }
    }

    async function reprintReceipt(receiptIdentifier) {
        if (!receiptIdentifier) return null;
        await ensureAuthToken();
        const headers = state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
        try {
            const res = await fetch(`/api/checkout/receipts/${encodeURIComponent(receiptIdentifier)}/reprint`, { method: 'POST', headers });
            if (!res.ok) {
                const err = await readApiError(res, t('fiscal.print_error'));
                showToast(err, 'error');
                if (elements.reprintReceiptResult) {
                    elements.reprintReceiptResult.style.display = 'block';
                    elements.reprintReceiptResult.style.background = 'rgba(239,68,68,0.15)';
                    elements.reprintReceiptResult.style.border = '1px solid #ef4444';
                    elements.reprintReceiptResult.style.color = '#ef4444';
                    elements.reprintReceiptResult.textContent = err;
                }
                return null;
            }
            const data = await res.json();
            const msg = t('fiscal.receipt_reprinted_success', { number: data.duplicateNumber });
            showToast(msg, 'success');
            if (elements.reprintReceiptResult) {
                elements.reprintReceiptResult.style.display = 'block';
                elements.reprintReceiptResult.style.background = 'rgba(16,185,129,0.15)';
                elements.reprintReceiptResult.style.border = '1px solid #10b981';
                elements.reprintReceiptResult.style.color = '#10b981';
                elements.reprintReceiptResult.textContent = msg;
            }
            return data;
        } catch (err) {
            console.error('Erreur réimpression reçu:', err);
            showToast(t('fiscal.print_error'), 'error');
            return null;
        }
    }

    async function previewXReport() {
        if (elements.slipDuplicateBanner) {
            elements.slipDuplicateBanner.style.display = 'none';
        }
        try {
            await ensureAuthToken();
            const headers = state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
            const res = await fetch('/api/fiscal/x-report?terminalId=POS_MAIN_TERM', { headers });
            if (res.ok) {
                const data = await res.json();
                renderFiscalSlip(data, false);
            }
        } catch (err) {
            console.error('Erreur chargement Rapport X:', err);
        }
    }

    function renderFiscalSlip(data, isZClosure = true) {
        if (!data) return;

        if (elements.slipTitle) {
            elements.slipTitle.textContent = isZClosure
                ? t('fiscal.slip_title_z', { sequence: data.closureSequence || 1 })
                : t('fiscal.slip_title_x');
        }

        if (elements.slipDate) {
            const d = data.closedAtUtc || data.periodEndUtc || new Date().toISOString();
            elements.slipDate.textContent = t('fiscal.slip_date_value', { date: d.replace('T', ' ').substring(0, 19) });
        }

        if (elements.slipTerminal) {
            elements.slipTerminal.textContent = t('fiscal.slip_terminal_value', { id: data.terminalId || 'POS_MAIN_TERM' });
        }

        if (elements.slipTotalTtc) {
            elements.slipTotalTtc.textContent = `${(data.totalSalesTtc || 0).toFixed(2)} €`;
        }

        if (elements.slipTotalHt) {
            elements.slipTotalHt.textContent = `${(data.totalSalesHt || 0).toFixed(2)} €`;
        }

        if (elements.slipCount) {
            elements.slipCount.textContent = data.receiptCount !== undefined ? data.receiptCount : '0';
        }

        if (elements.slipPerpetual) {
            elements.slipPerpetual.textContent = `${(data.perpetualGrandTotal || 0).toFixed(2)} €`;
        }

        if (elements.slipHash) {
            elements.slipHash.textContent = data.signatureHash || t('fiscal.slip_hash_pending_z');
        }

        if (elements.slipTag) {
            elements.slipTag.textContent = isZClosure
                ? t('fiscal.slip_chain_sealed')
                : t('fiscal.slip_live_data_unsealed');
            elements.slipTag.style.color = isZClosure ? '#10b981' : '#38bdf8';
        }

        // Dynamic VAT rows
        if (elements.slipVatBreakdown) {
            if (data.vatBreakdown && Object.keys(data.vatBreakdown).length > 0) {
                elements.slipVatBreakdown.innerHTML = Object.entries(data.vatBreakdown).map(([rate, amount]) => `
                    <div class="slip-row" style="font-size:0.85rem; color:#94a3b8;">
                        <span>${t('fiscal.slip_vat_row_label', { rate })}</span>
                        <span>${Number(amount).toFixed(2)} €</span>
                    </div>
                `).join('');
            } else {
                elements.slipVatBreakdown.innerHTML = '';
            }
        }

        // Dynamic Payment tenders rows
        if (elements.slipPaymentBreakdown) {
            if (data.paymentTotals && Object.keys(data.paymentTotals).length > 0) {
                elements.slipPaymentBreakdown.innerHTML = Object.entries(data.paymentTotals).map(([method, amount]) => `
                    <div class="slip-row" style="font-size:0.85rem; color:#94a3b8;">
                        <span>${t('fiscal.slip_payment_row_label', { method })}</span>
                        <span>${Number(amount).toFixed(2)} €</span>
                    </div>
                `).join('');
            } else {
                elements.slipPaymentBreakdown.innerHTML = '';
            }
        }
    }

    function renderVerificationResult(data) {
        if (!elements.fiscalVerificationPanel) return;
        elements.fiscalVerificationPanel.style.display = 'block';

        if (elements.fiscalVerificationSummary) {
            if (data.isValid) {
                elements.fiscalVerificationSummary.style.background = 'rgba(16, 185, 129, 0.15)';
                elements.fiscalVerificationSummary.style.color = '#10b981';
                elements.fiscalVerificationSummary.style.border = '1px solid rgba(16, 185, 129, 0.3)';
                elements.fiscalVerificationSummary.innerHTML = '✓ ' + t('fiscal.verification_success') + ' (' + new Date(data.checkedAtUtc).toLocaleString() + ')';
            } else {
                elements.fiscalVerificationSummary.style.background = 'rgba(239, 68, 68, 0.15)';
                elements.fiscalVerificationSummary.style.color = '#ef4444';
                elements.fiscalVerificationSummary.style.border = '1px solid rgba(239, 68, 68, 0.3)';
                elements.fiscalVerificationSummary.innerHTML = '⚠️ ' + t('fiscal.verification_failed') + ' (' + new Date(data.checkedAtUtc).toLocaleString() + ')';
            }
        }

        if (elements.fiscalChainsTableBody) {
            elements.fiscalChainsTableBody.innerHTML = '';
            (data.chains || []).forEach(chain => {
                const tr = document.createElement('tr');
                tr.style.borderBottom = '1px solid rgba(255,255,255,0.05)';

                const tdChain = document.createElement('td');
                tdChain.style.padding = '8px';
                tdChain.textContent = chain.chain;
                tr.appendChild(tdChain);

                const tdTerminal = document.createElement('td');
                tdTerminal.style.padding = '8px';
                tdTerminal.textContent = chain.terminalId || t('fiscal.global');
                tr.appendChild(tdTerminal);

                const tdCount = document.createElement('td');
                tdCount.style.padding = '8px';
                tdCount.textContent = chain.checkedCount + (chain.legacyCount != null ? ' (+' + chain.legacyCount + ' ' + (t('fiscal.legacy') || 'legacy') + ')' : '');
                tr.appendChild(tdCount);

                const tdStatus = document.createElement('td');
                tdStatus.style.padding = '8px';
                tdStatus.innerHTML = chain.isValid
                    ? '<span style="color:#10b981; font-weight:600;">' + t('fiscal.status_valid') + '</span>'
                    : '<span style="color:#ef4444; font-weight:600;">' + t('fiscal.status_broken') + '</span>';
                tr.appendChild(tdStatus);

                const tdDetails = document.createElement('td');
                tdDetails.style.padding = '8px';
                if (chain.break) {
                    tdDetails.innerHTML = `<span style="color:#ef4444;">${escapeHtml(chain.break.kind)} (seq #${escapeHtml(chain.break.sequenceNumber)}${chain.break.reference ? ' - ' + escapeHtml(chain.break.reference) : ''})</span>`;
                } else {
                    tdDetails.textContent = '—';
                }
                tr.appendChild(tdDetails);

                elements.fiscalChainsTableBody.appendChild(tr);
            });
        }
    }

    function updateDefaultPeriodKey() {
        if (!elements.periodClosureKey || !elements.periodClosureType) return;
        const now = new Date();
        if (elements.periodClosureType.value === 'annual') {
            elements.periodClosureKey.value = String(now.getFullYear() - 1);
            elements.periodClosureKey.placeholder = '2025';
        } else {
            const prevMonthDate = new Date(now.getFullYear(), now.getMonth() - 1, 1);
            const y = prevMonthDate.getFullYear();
            const m = String(prevMonthDate.getMonth() + 1).padStart(2, '0');
            elements.periodClosureKey.value = `${y}-${m}`;
            elements.periodClosureKey.placeholder = `${y}-${m}`;
        }
    }

    async function loadPeriodClosures() {
        if (!elements.periodClosuresTableBody) return;
        try {
            await ensureAuthToken();
            const headers = state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
            await loadArchives();
            const res = await fetch('/api/fiscal/period-closures?terminalId=POS_MAIN_TERM', { headers });
            if (!res.ok) return;
            const closures = await res.json();
            renderPeriodClosures(closures);
        } catch (err) {
            console.error('Erreur chargement des clôtures de période:', err);
        }
    }

    async function loadArchives() {
        if (!elements.archivesTableBody) return;
        try {
            await ensureAuthToken();
            const headers = state.token ? { 'Authorization': `Bearer ${state.token}` } : {};
            const res = await fetch('/api/fiscal/archives', { headers });
            if (!res.ok) return;
            const archives = await res.json();
            state.archives = archives || [];
            renderArchives(state.archives);
        } catch (err) {
            console.error('Erreur chargement des archives:', err);
        }
    }

    function renderArchives(archives) {
        if (!elements.archivesTableBody) return;
        elements.archivesTableBody.innerHTML = '';
        if (!archives || archives.length === 0) {
            const tr = document.createElement('tr');
            tr.innerHTML = `<td colspan="7" style="padding:16px; text-align:center; color:#94a3b8;">—</td>`;
            elements.archivesTableBody.appendChild(tr);
            return;
        }

        archives.forEach(a => {
            const tr = document.createElement('tr');
            tr.style.borderBottom = '1px solid rgba(255,255,255,0.05)';
            const dateStr = a.createdAtUtc ? new Date(a.createdAtUtc).toLocaleString() : '—';
            const sigTrunc = a.signatureHash ? a.signatureHash.substring(0, 16) + '...' : '—';
            const sizeKb = (a.fileSizeBytes / 1024).toFixed(1) + ' KB';

            tr.innerHTML = `
                <td style="padding:8px; font-family:monospace;">#${a.archiveSequence}</td>
                <td style="padding:8px; font-weight:600; font-family:monospace; font-size:0.85rem;">${escapeHtml(a.fileName)}</td>
                <td style="padding:8px;"><span style="display:inline-block; padding:2px 8px; border-radius:4px; font-size:0.78rem; font-weight:600; background:rgba(59,130,246,0.15); color:#60a5fa;">${escapeHtml(a.periodKey)}</span></td>
                <td style="padding:8px; font-size:0.85rem; color:#94a3b8;">${sizeKb}</td>
                <td style="padding:8px; font-size:0.82rem; color:#94a3b8;">${dateStr}</td>
                <td style="padding:8px; font-family:monospace; font-size:0.75rem; color:#a78bfa;" title="${escapeHtml(a.signatureHash || '')}">${escapeHtml(sigTrunc)}</td>
                <td style="padding:8px;">
                    <a href="/api/fiscal/archives/${a.id}/file" download="${escapeHtml(a.fileName)}" class="btn-fiscal" style="padding:4px 10px; font-size:0.78rem; background:#3b82f6; color:#fff; text-decoration:none; display:inline-block; border-radius:4px;">${t('fiscal.action_download')}</a>
                </td>
            `;
            elements.archivesTableBody.appendChild(tr);
        });
    }

    function renderPeriodClosures(closures) {
        if (!elements.periodClosuresTableBody) return;
        elements.periodClosuresTableBody.innerHTML = '';
        if (!closures || closures.length === 0) {
            const tr = document.createElement('tr');
            tr.innerHTML = `<td colspan="8" style="padding:16px; text-align:center; color:#94a3b8;">—</td>`;
            elements.periodClosuresTableBody.appendChild(tr);
            return;
        }

        closures.forEach(c => {
            const tr = document.createElement('tr');
            tr.style.borderBottom = '1px solid rgba(255,255,255,0.05)';
            const typeLabel = c.periodType === 'monthly' ? t('fiscal.period_monthly') : t('fiscal.period_annual');
            const totalTtcFormatted = (c.totalTtcCents != null ? (c.totalTtcCents / 100).toFixed(2) : (c.totalTtc || 0).toFixed(2)) + ' €';
            const grandTotalFormatted = (c.perpetualGrandTotalCents != null ? (c.perpetualGrandTotalCents / 100).toFixed(2) : (c.perpetualGrandTotal || 0).toFixed(2)) + ' €';
            const dateStr = c.createdAtUtc ? new Date(c.createdAtUtc).toLocaleString() : '—';
            const sigTrunc = c.signatureHash ? c.signatureHash.substring(0, 16) + '...' : '—';

            const existingArchive = (state.archives || []).find(a => a.periodClosureId === c.id);
            let actionHtml = '';
            if (existingArchive) {
                actionHtml = `<a href="/api/fiscal/archives/${existingArchive.id}/file" download="${escapeHtml(existingArchive.fileName)}" class="btn-fiscal" style="padding:4px 8px; font-size:0.75rem; background:#3b82f6; color:#fff; text-decoration:none; display:inline-block; border-radius:4px;">${t('fiscal.action_download')}</a>`;
            } else {
                actionHtml = `<button class="btn-create-archive" data-closure-id="${c.id}" style="padding:4px 8px; font-size:0.75rem; background:#8b5cf6; color:#fff; border:none; border-radius:4px; cursor:pointer;">${t('fiscal.action_archive')}</button>`;
            }

            tr.innerHTML = `
                <td style="padding:8px;"><span style="display:inline-block; padding:2px 8px; border-radius:4px; font-size:0.78rem; font-weight:600; background:rgba(59,130,246,0.15); color:#60a5fa;">${escapeHtml(typeLabel)}</span></td>
                <td style="padding:8px; font-weight:600;">${escapeHtml(c.periodKey)}</td>
                <td style="padding:8px; font-family:monospace;">#${c.closureSequence}</td>
                <td style="padding:8px; font-weight:600;">${totalTtcFormatted}</td>
                <td style="padding:8px; font-family:monospace; color:#34d399;">${grandTotalFormatted}</td>
                <td style="padding:8px; font-size:0.82rem; color:#94a3b8;">${dateStr}</td>
                <td style="padding:8px; font-family:monospace; font-size:0.75rem; color:#a78bfa;" title="${escapeHtml(c.signatureHash || '')}">${escapeHtml(sigTrunc)}</td>
                <td style="padding:8px;">${actionHtml}</td>
            `;
            elements.periodClosuresTableBody.appendChild(tr);
        });
    }

    async function createArchive(closureId) {
        try {
            await ensureAuthToken();
            const headers = {
                'Content-Type': 'application/json',
                ...(state.token ? { 'Authorization': `Bearer ${state.token}` } : {})
            };
            const res = await fetch('/api/fiscal/archives', {
                method: 'POST',
                headers,
                body: JSON.stringify({ periodClosureId: closureId })
            });
            if (res.ok) {
                showToast(t('fiscal.archive_created_success'), 'success');
                await loadPeriodClosures();
            } else if (res.status === 409) {
                showToast(t('fiscal.archive_exists'), 'warning');
            } else {
                showToast(t('common.error_occurred'), 'error');
            }
        } catch (err) {
            console.error('Erreur création archive:', err);
            showToast(t('common.error_occurred'), 'error');
        }
    }

    async function verifyArchiveFile() {
        if (!elements.btnVerifyArchive || !elements.archiveVerifyFileInput) return;
        const file = elements.archiveVerifyFileInput.files[0];
        if (!file) return;

        if (elements.archiveVerifyAlert) {
            elements.archiveVerifyAlert.style.display = 'none';
            elements.archiveVerifyAlert.textContent = '';
        }
        elements.btnVerifyArchive.disabled = true;

        try {
            await ensureAuthToken();
            const formData = new FormData();
            formData.append('file', file);
            const headers = state.token ? { 'Authorization': `Bearer ${state.token}` } : {};

            const res = await fetch('/api/fiscal/archives/verify', {
                method: 'POST',
                headers,
                body: formData
            });

            if (res.ok) {
                const result = await res.json();
                if (elements.archiveVerifyAlert) {
                    elements.archiveVerifyAlert.style.display = 'block';
                    if (result.isValid) {
                        elements.archiveVerifyAlert.style.background = 'rgba(16,185,129,0.15)';
                        elements.archiveVerifyAlert.style.color = '#10b981';
                        elements.archiveVerifyAlert.style.border = '1px solid rgba(16,185,129,0.3)';
                        elements.archiveVerifyAlert.textContent = t('fiscal.archive_valid');
                    } else {
                        elements.archiveVerifyAlert.style.background = 'rgba(239,68,68,0.15)';
                        elements.archiveVerifyAlert.style.color = '#ef4444';
                        elements.archiveVerifyAlert.style.border = '1px solid rgba(239,68,68,0.3)';
                        if (result.reason === 'hash_mismatch') {
                            elements.archiveVerifyAlert.textContent = t('fiscal.archive_hash_mismatch');
                        } else if (result.reason === 'unknown_archive') {
                            elements.archiveVerifyAlert.textContent = t('fiscal.archive_unknown');
                        } else if (result.reason === 'chain_break') {
                            elements.archiveVerifyAlert.textContent = t('fiscal.archive_chain_break');
                        } else {
                            elements.archiveVerifyAlert.textContent = result.reason || t('common.error_occurred');
                        }
                    }
                }
            } else {
                if (elements.archiveVerifyAlert) {
                    elements.archiveVerifyAlert.style.display = 'block';
                    elements.archiveVerifyAlert.style.background = 'rgba(239,68,68,0.15)';
                    elements.archiveVerifyAlert.style.color = '#ef4444';
                    elements.archiveVerifyAlert.style.border = '1px solid rgba(239,68,68,0.3)';
                    elements.archiveVerifyAlert.textContent = t('common.error_occurred');
                }
            }
        } catch (err) {
            console.error('Erreur vérification archive:', err);
        } finally {
            elements.btnVerifyArchive.disabled = false;
        }
    }

    async function executePeriodClosure() {
        if (!elements.btnExecutePeriodClosure || !elements.periodClosureKey || !elements.periodClosureType) return;
        const periodType = elements.periodClosureType.value;
        const periodKey = elements.periodClosureKey.value.trim();
        if (!periodKey) return;

        if (elements.periodClosureAlert) {
            elements.periodClosureAlert.style.display = 'none';
            elements.periodClosureAlert.textContent = '';
        }
        elements.btnExecutePeriodClosure.disabled = true;

        try {
            await ensureAuthToken();
            const headers = {
                'Content-Type': 'application/json',
                ...(state.token ? { 'Authorization': `Bearer ${state.token}` } : {})
            };
            const res = await fetch('/api/fiscal/period-closures', {
                method: 'POST',
                headers,
                body: JSON.stringify({
                    terminalId: 'POS_MAIN_TERM',
                    periodType,
                    periodKey
                })
            });

            if (res.ok) {
                if (elements.periodClosureAlert) {
                    elements.periodClosureAlert.style.display = 'block';
                    elements.periodClosureAlert.style.background = 'rgba(16,185,129,0.15)';
                    elements.periodClosureAlert.style.color = '#10b981';
                    elements.periodClosureAlert.style.border = '1px solid rgba(16,185,129,0.3)';
                    elements.periodClosureAlert.textContent = t('fiscal.period_closure_success');
                }
                showToast(t('fiscal.period_closure_success'), 'success');
                await loadPeriodClosures();
            } else if (res.status === 409) {
                const err = await res.json().catch(() => ({}));
                let msg = '';
                if (err.code === 'period_not_ended') {
                    msg = t('fiscal.period_not_ended');
                } else if (err.code === 'period_already_closed') {
                    msg = t('fiscal.period_already_closed');
                } else if (err.code === 'missing_daily_closures') {
                    const daysList = Array.isArray(err.days) ? err.days.join(', ') : '';
                    msg = `${t('fiscal.missing_daily_closures')} ${daysList}`.trim();
                } else if (err.code === 'missing_monthly_closures') {
                    const monthsList = Array.isArray(err.months) ? err.months.join(', ') : '';
                    msg = `${t('fiscal.missing_monthly_closures')} ${monthsList}`.trim();
                } else {
                    msg = err.message || t('common.error_occurred');
                }

                if (elements.periodClosureAlert) {
                    elements.periodClosureAlert.style.display = 'block';
                    elements.periodClosureAlert.style.background = 'rgba(239,68,68,0.15)';
                    elements.periodClosureAlert.style.color = '#ef4444';
                    elements.periodClosureAlert.style.border = '1px solid rgba(239,68,68,0.3)';
                    elements.periodClosureAlert.textContent = msg;
                }
                showToast(msg, 'error');
            } else {
                const err = await res.json().catch(() => ({}));
                const msg = err.message || t('common.error_occurred');
                if (elements.periodClosureAlert) {
                    elements.periodClosureAlert.style.display = 'block';
                    elements.periodClosureAlert.style.background = 'rgba(239,68,68,0.15)';
                    elements.periodClosureAlert.style.color = '#ef4444';
                    elements.periodClosureAlert.style.border = '1px solid rgba(239,68,68,0.3)';
                    elements.periodClosureAlert.textContent = msg;
                }
                showToast(msg, 'error');
            }
        } catch (err) {
            console.error('Erreur exécution clôture période:', err);
            showToast(t('common.error_occurred'), 'error');
        } finally {
            elements.btnExecutePeriodClosure.disabled = false;
        }
    }

    async function loadNetworkSyncData() {
        try {
            const [netRes, syncRes] = await Promise.all([
                fetch('/api/network/info'),
                fetch('/api/sync/status')
            ]);
            if (netRes.ok) {
                const netInfo = await netRes.json();
                const badge = document.getElementById('connectionBadge');
                if (badge) {
                    badge.innerHTML = `<span class="pulse-dot"></span><span class="status-text">${netInfo.serverName} (${netInfo.primaryIp}:${netInfo.port})</span>`;
                }
            }
            if (syncRes.ok) {
                const syncData = await syncRes.json();
                const pendingEl = document.getElementById('syncPendingCount');
                const completedEl = document.getElementById('syncCompletedCount');
                const timeEl = document.getElementById('syncLastTime');
                if (pendingEl) pendingEl.textContent = t('admin.sync_pending_value', { count: syncData.pendingMessages });
                if (completedEl) completedEl.textContent = t('admin.sync_completed_value', { count: syncData.completedMessages });
                if (timeEl) timeEl.textContent = new Date(syncData.lastSyncUtc).toLocaleTimeString(window.i18n.locale);
            }
        } catch (err) {
            console.error('Erreur chargement statut réseau/synchro:', err);
            const badge = document.getElementById('connectionBadge');
            if (badge) {
                badge.className = 'status-badge offline';
                badge.innerHTML = `<span class="pulse-dot" style="background:#ef4444;"></span><span class="status-text">${t('admin.sync_offline_mode')}</span>`;
            }
        }
    }

    // ==================== APPAIRAGE DES POSTES ====================
    function openDevicePairingModal() {
        const modal = document.getElementById('devicePairingModal');
        if (!modal) return;
        document.getElementById('devicePairingCodeInput').value = '';
        document.getElementById('devicePairingError').textContent = '';
        modal.classList.add('active');
        document.getElementById('devicePairingCodeInput').focus();
    }

    function setupDevicePairingHandlers() {
        const modal = document.getElementById('devicePairingModal');
        document.getElementById('btnCancelDevicePairing')?.addEventListener('click', () => modal.classList.remove('active'));
        document.getElementById('btnConfirmDevicePairing')?.addEventListener('click', async () => {
            const code = document.getElementById('devicePairingCodeInput').value.trim().toUpperCase();
            const errorEl = document.getElementById('devicePairingError');
            if (!code) {
                errorEl.textContent = t('admin.device_pairing_enter_code_error');
                return;
            }
            const res = await fetch('/api/devices/pair', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ code })
            });
            const data = await res.json().catch(() => ({}));
            if (!res.ok) {
                errorEl.textContent = data.message || t('admin.device_pairing_invalid_code_error');
                return;
            }
            storeDevice({ token: data.token, terminalId: data.terminalId, name: data.name });
            modal.classList.remove('active');
            showToast(t('admin.device_paired_toast', { name: data.name, terminalId: data.terminalId }), 'success');
        });
    }

    function escapeHtml(value) {
        return String(value).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    }

    async function loadAdminDevices() {
        const list = document.getElementById('adminDevicesList');
        if (!list) return;
        const res = await fetch('/api/devices');
        if (!res.ok) {
            list.innerHTML = `<div style="color:var(--text-muted);">${t('admin.devices_managers_only')}</div>`;
            return;
        }
        const devices = await res.json();
        if (!state.printers || state.printers.length === 0) {
            const pr = await fetch('/api/printers');
            state.printers = pr.ok ? await pr.json() : [];
        }
        list.innerHTML = devices.length === 0
            ? `<div style="color:var(--text-muted);">${t('admin.devices_none_paired')}</div>`
            : devices.map(d => `
                <div class="item-list-row" data-device-id="${d.id}">
                    <div>
                        <strong>${escapeHtml(d.name)}</strong>
                        <span style="color:var(--text-muted);">${escapeHtml(d.terminalId)} · ${escapeHtml(deviceRoleLabel(d.role))}</span>
                        <div style="font-size:0.8rem; color:#94a3b8;">${d.isRevoked ? t('admin.device_revoked') : (d.lastSeenUtc ? t('admin.device_last_seen', { date: new Date(d.lastSeenUtc).toLocaleString(window.i18n.locale) }) : t('admin.device_never_used'))}</div>
                    </div>
                    ${d.isRevoked ? '' : `<select class="device-receipt-printer" data-device-id="${d.id}" aria-label="${t('admin.device_receipt_printer_label')}">
                        <option value="">${t('admin.device_receipt_printer_default')}</option>
                        ${(state.printers || []).filter(p => p.isActive !== false).map(p => `<option value="${p.id}" ${p.id === d.receiptPrinterId ? 'selected' : ''}>${escapeHtml(p.name)}</option>`).join('')}
                    </select>
                    <button type="button" class="btn-archive btn-revoke-device" data-device-id="${d.id}">${t('admin.device_revoke_btn')}</button>`}
                </div>`).join('');
        list.querySelectorAll('.device-receipt-printer').forEach(sel => sel.addEventListener('change', async () => {
            const r = await fetch(`/api/devices/${sel.dataset.deviceId}/receipt-printer`, {
                method: 'PUT', headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ printerId: sel.value || null })
            });
            showToast(r.ok ? t('admin.device_receipt_printer_saved') : await readApiError(r, t('admin.device_receipt_printer_error')), r.ok ? 'success' : 'error');
        }));
        list.querySelectorAll('.btn-revoke-device').forEach(btn => btn.addEventListener('click', async () => {
            if (!confirm(t('admin.device_revoke_confirm'))) return;
            const r = await fetch(`/api/devices/${btn.dataset.deviceId}/revoke`, { method: 'POST' });
            if (r.ok) {
                showToast(t('admin.device_revoked_toast'), 'success');
                await loadAdminDevices();
            } else {
                showToast(t('admin.device_revoke_error'), 'error');
            }
        }));
    }

    let pairingCountdown = null;

    function setupDeviceAdminHandlers() {
        document.getElementById('settingsReceiptLanguage')?.addEventListener('change', savePrintSettings);
        document.getElementById('settingsKitchenLanguage')?.addEventListener('change', savePrintSettings);
        document.getElementById('formEstablishmentSettings')?.addEventListener('submit', saveEstablishmentSettings);
        document.getElementById('formDevicePairingCode')?.addEventListener('submit', async (e) => {
            e.preventDefault();
            const name = document.getElementById('inputDeviceName').value.trim();
            const role = document.getElementById('selectDeviceRole').value;
            const res = await fetch('/api/devices/pairing-codes', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name, role })
            });
            const data = await res.json().catch(() => ({}));
            if (!res.ok) {
                showToast(data.message || t('admin.device_pairing_code_gen_error'), 'error');
                return;
            }
            document.getElementById('devicePairingQr').src = `data:image/png;base64,${data.qrPngBase64}`;
            document.getElementById('devicePairingCode').textContent = data.code;
            document.getElementById('devicePairingCodeResult').style.display = 'block';
            const expiry = document.getElementById('devicePairingExpiry');
            clearInterval(pairingCountdown);
            const tick = () => {
                const seconds = Math.max(0, Math.round((new Date(data.expiresAtUtc) - Date.now()) / 1000));
                expiry.textContent = seconds > 0
                    ? t('admin.device_pairing_expires_in', { time: `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}` })
                    : t('admin.device_pairing_code_expired');
                if (seconds === 0) clearInterval(pairingCountdown);
            };
            tick();
            pairingCountdown = setInterval(tick, 1000);
        });
    }

    function setupNetworkSyncHandlers() {
        const formManual = document.getElementById('formManualServerConfig');
        const btnTest = document.getElementById('btnTestServerConn');
        const btnForceSync = document.getElementById('btnForceSyncNow');

        if (btnTest) {
            btnTest.addEventListener('click', async () => {
                const url = document.getElementById('inputManualServerUrl').value.trim();
                const start = performance.now();
                try {
                    const res = await fetch(`${url}/api/health`, { method: 'GET' });
                    const duration = Math.round(performance.now() - start);
                    if (res.ok) {
                        showToast(t('admin.server_connection_success_toast', { duration }), 'success');
                    } else {
                        showToast(t('admin.server_connection_http_error_toast', { status: res.status }), 'error');
                    }
                } catch (err) {
                    showToast(t('admin.server_unreachable_toast', { url }), 'error');
                }
            });
        }

        if (formManual) {
            formManual.addEventListener('submit', (e) => {
                e.preventDefault();
                const url = document.getElementById('inputManualServerUrl').value.trim();
                localStorage.setItem('pos_master_server_url', url);
                showToast(t('admin.server_address_saved_toast', { url }), 'success');
                loadNetworkSyncData();
            });
        }

        if (btnForceSync) {
            btnForceSync.addEventListener('click', async () => {
                try {
                    const res = await fetch('/api/sync/batch', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ messages: [] })
                    });
                    if (res.ok) {
                        showToast(t('admin.sync_outbox_success_toast'), 'success');
                        await loadNetworkSyncData();
                    }
                } catch (err) {
                    showToast(t('admin.sync_error_toast'), 'error');
                }
            });
        }
    }

    function setSelectValue(id, value) {
        const select = document.getElementById(id);
        if (!select) return;
        if (![...select.options].some(o => o.value === value)) {
            const opt = document.createElement('option');
            opt.value = value;
            opt.textContent = value;
            select.appendChild(opt);
        }
        select.value = value;
    }

    async function readApiError(res, fallback) {
        try {
            const data = await res.json();
            return data.message || data.Message || data.errorMessage || `${fallback} (${res.status})`;
        } catch {
            return `${fallback} (${res.status})`;
        }
    }

    function printerToUpdatePayload(pr) {
        return {
            name: pr.name,
            ipAddress: pr.ipAddress,
            port: pr.port,
            paperWidthMm: pr.paperWidthMm || 80,
            hasCashDrawer: !!pr.openCashDrawerOnReceipt,
            targetStations: pr.assignedStationIds || [],
            isActive: pr.isActive !== false,
            textMode: !!pr.textMode
        };
    }

    function savePrinter(id, payload) {
        return fetch(`/api/printers/${id}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
    }

    /** Branche la fermeture (✕ / Annuler) et l'enregistrement des fenêtres d'édition du back-office. */
    function setupEditModals() {
        const modals = [
            ['editProductModal', 'btnCloseEditProductModal', 'btnCancelEditProduct'],
            ['editCategoryModal', 'btnCloseEditCatModal', 'btnCancelEditCat'],
            ['editStaffModal', 'btnCloseEditStaffModal', 'btnCancelEditStaff'],
            ['editPrinterModal', 'btnCloseEditPrinterModal', 'btnCancelEditPrinter']
        ];
        modals.forEach(([modalId, ...buttonIds]) => {
            buttonIds.forEach(buttonId => {
                const btn = document.getElementById(buttonId);
                if (btn) btn.addEventListener('click', () => document.getElementById(modalId).classList.remove('active'));
            });
        });

        document.getElementById('formEditProduct').addEventListener('submit', async (e) => {
            e.preventDefault();
            const id = document.getElementById('editProdId').value;
            const current = state.products.find(p => p.id === id) || {};
            const res = await fetch(`/api/catalog/products/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name: document.getElementById('editProdName').value.trim(),
                    categoryId: document.getElementById('editProdCat').value || current.categoryId,
                    price: parseFloat(document.getElementById('editProdPrice').value),
                    taxRatePercent: parseFloat(document.getElementById('editProdTax').value),
                    description: current.description || '',
                    colorHex: current.colorHex || '#3b82f6',
                    displayOrder: current.displayOrder || 0,
                    isAvailable: true,
                    isActive: true,
                    isQuickKey: document.getElementById('editProdQuickKey').checked,
                    stationId: document.getElementById('editProdStation').value
                })
            });
            if (!res.ok) {
                showToast(await readApiError(res, t('admin.product_edit_error')), 'error');
                return;
            }
            elements.editProductModal.classList.remove('active');
            showToast(t('admin.product_updated_toast'), 'success');
            state.gridLayouts = {};
            await loadCatalogData();
            await loadAdminCatalog();
        });

        document.getElementById('formEditCategory').addEventListener('submit', async (e) => {
            e.preventDefault();
            const id = document.getElementById('editCatId').value;
            const current = state.categories.find(c => c.id === id) || {};
            const res = await fetch(`/api/catalog/categories/${encodeURIComponent(id)}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name: document.getElementById('editCatName').value.trim(),
                    colorHex: document.getElementById('editCatColor').value,
                    displayOrder: current.displayOrder || 0,
                    iconName: current.iconName || null,
                    isActive: true,
                    preparationStationId: document.getElementById('editCatStation').value
                })
            });
            if (!res.ok) {
                showToast(await readApiError(res, t('admin.category_edit_error')), 'error');
                return;
            }
            elements.editCategoryModal.classList.remove('active');
            showToast(t('admin.category_updated_toast'), 'success');
            await loadCatalogData();
        });

        document.getElementById('formEditStaff').addEventListener('submit', async (e) => {
            e.preventDefault();
            const id = document.getElementById('editStaffId').value;
            const pin = document.getElementById('editStaffPin').value.trim();
            if (pin && !/^\d{4,6}$/.test(pin)) {
                showToast(t('admin.staff_pin_length_warning'), 'warning');
                return;
            }
            const res = await fetch(`/api/staff/${id}`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    name: document.getElementById('editStaffName').value.trim(),
                    role: document.getElementById('editStaffRole').value,
                    pin: pin || null,
                    isActive: true
                })
            });
            if (!res.ok) {
                showToast(await readApiError(res, t('admin.staff_edit_error')), 'error');
                return;
            }
            document.getElementById('editStaffPin').value = '';
            elements.editStaffModal.classList.remove('active');
            showToast(t('admin.staff_updated_toast'), 'success');
            await loadAdminStaff();
        });

        document.getElementById('formEditPrinter').addEventListener('submit', async (e) => {
            e.preventDefault();
            const id = document.getElementById('editPrinterId').value;
            const current = (state.printers || []).find(p => p.id === id) || {};
            const res = await savePrinter(id, {
                ...printerToUpdatePayload(current),
                name: document.getElementById('editPrinterName').value.trim(),
                ipAddress: document.getElementById('editPrinterIp').value.trim(),
                port: parseInt(document.getElementById('editPrinterPort').value, 10) || 9100,
                hasCashDrawer: document.getElementById('editPrinterDrawer').checked,
                textMode: document.getElementById('editPrinterTextMode').checked
            });
            if (!res.ok) {
                showToast(await readApiError(res, t('admin.printer_edit_error')), 'error');
                return;
            }
            elements.editPrinterModal.classList.remove('active');
            showToast(t('admin.printer_updated_toast'), 'success');
            await loadAdminPrinters();
        });
    }

    function renderCategorySelectOptions() {
        elements.selectProductCat.innerHTML = '';
        state.categories.forEach(c => {
            const opt = document.createElement('option');
            opt.value = c.id;
            opt.textContent = c.name;
            elements.selectProductCat.appendChild(opt);
        });
    }

    // ==================== PIN KEYPAD ====================
    function setupPinKeypad() {
        document.querySelectorAll('.pin-keypad .btn-num[data-val]').forEach(btn => {
            btn.addEventListener('click', () => {
                if (state.pinInput.length < 6) {
                    state.pinInput += btn.getAttribute('data-val');
                    updatePinDots();
                    if (state.pinInput.length >= 4) {
                        validatePin();
                    }
                }
            });
        });

        elements.btnPinClear.addEventListener('click', () => {
            state.pinInput = '';
            updatePinDots();
        });

        elements.btnPinDel.addEventListener('click', () => {
            if (state.pinInput.length > 0) {
                state.pinInput = state.pinInput.substring(0, state.pinInput.length - 1);
                updatePinDots();
            }
        });
    }

    function updatePinDots() {
        for (let i = 0; i < 6; i++) {
            const dot = document.getElementById(`pinDot${i}`);
            if (dot) {
                dot.classList.toggle('filled', i < state.pinInput.length);
            }
        }
    }

    async function validatePin() {
        try {
            const res = await fetch('/api/auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pin: state.pinInput })
            });
            const data = await res.json();
            if (data.success) {
                state.token = data.token;
                localStorage.setItem('pos_jwt_token', data.token);
                startPosHub();
                state.operator = { name: data.operatorName, role: data.role, id: data.operatorId };
                elements.currentOperatorName.textContent = data.operatorName;
                elements.currentOperatorRole.textContent = operatorRoleLabel(data.role);
                elements.pinLockModal.classList.remove('active');
                state.pinInput = '';
                updatePinDots();
                showToast(t('common.session_unlocked_toast', { name: data.operatorName }), 'success');

                // Reload active table with newly issued token
                if (state.activeTable === 'Comptoir') {
                    await openDirectCounterOrder(state.destination || 'Takeaway');
                } else if (state.activeTable) {
                    await loadActiveTableOrder(state.activeTable);
                }
            } else {
                showToast(t('common.pin_invalid_toast'), 'error');
                state.pinInput = '';
                updatePinDots();
            }
        } catch (err) {
            showToast(t('common.pin_validation_error_toast'), 'error');
        }
    }

    // ==================== TOUCH GRID EDITOR (US2 & US3 & US5) ====================
    function setupAdminGridEditorListeners() {
        if (elements.selectAdminGridCat) {
            elements.selectAdminGridCat.addEventListener('change', async (e) => {
                state.activeAdminGridPage = 0;
                await renderAdminGridEditor(e.target.value, 0);
            });
        }

        if (elements.btnResetGridLayout) {
            elements.btnResetGridLayout.addEventListener('click', async () => {
                if (state.activeAdminGridCategory) {
                    await resetAdminGridLayout(state.activeAdminGridCategory);
                }
            });
        }

        if (elements.selectGridDimensionsPreset) {
            elements.selectGridDimensionsPreset.addEventListener('change', (e) => {
                const val = e.target.value;
                if (val === 'custom') {
                    if (elements.customDimensionsInputs) elements.customDimensionsInputs.style.display = 'flex';
                } else {
                    if (elements.customDimensionsInputs) elements.customDimensionsInputs.style.display = 'none';
                    const parts = val.split('x').map(Number);
                    if (elements.inputGridCols) elements.inputGridCols.value = parts[0];
                    if (elements.inputGridRows) elements.inputGridRows.value = parts[1];
                }
            });
        }

        if (elements.btnApplyGridDimensions) {
            elements.btnApplyGridDimensions.addEventListener('click', async () => {
                const categoryId = state.activeAdminGridCategory;
                const cols = parseInt(elements.inputGridCols ? elements.inputGridCols.value : '4') || 4;
                const rows = parseInt(elements.inputGridRows ? elements.inputGridRows.value : '4') || 4;
                const applyAll = elements.checkApplyAllGridDimensions ? elements.checkApplyAllGridDimensions.checked : false;

                try {
                    const res = await fetch('/api/grid-layouts/dimensions', {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({
                            categoryId: categoryId,
                            columnsCount: cols,
                            rowsCount: rows,
                            applyToAllCategories: applyAll
                        })
                    });

                    if (res.ok) {
                        const updatedList = await res.json();
                        updatedList.forEach(l => {
                            const cacheKey = `${l.categoryId}_page_${l.pageIndex}`;
                            state.gridLayouts[cacheKey] = l;
                            localStorage.setItem(`grid_layout_${l.categoryId}_page_${l.pageIndex}`, JSON.stringify(l));
                        });

                        showToast(t('admin.grid_format_applied_toast', { cols, rows }), 'success');
                        await renderAdminGridEditor(categoryId, state.activeAdminGridPage);
                        if (state.activeCategory === categoryId || applyAll) {
                            await renderProductsGrid();
                        }
                    } else {
                        showToast(t('admin.grid_format_change_error'), 'error');
                    }
                } catch (err) {
                    console.error('Erreur changement format:', err);
                    showToast(t('admin.grid_format_change_network_error'), 'error');
                }
            });
        }
    }

    async function loadAdminGridEditor() {
        if (!elements.selectAdminGridCat) return;
        elements.selectAdminGridCat.innerHTML = '';

        // Add 'Entire Menu' option
        const allOpt = document.createElement('option');
        allOpt.value = 'ALL';
        allOpt.textContent = t('admin.grid_all_menu_option');
        elements.selectAdminGridCat.appendChild(allOpt);

        state.categories.forEach(cat => {
            const opt = document.createElement('option');
            opt.value = cat.id;
            opt.textContent = `${getCategoryIcon(cat.iconName || cat.name.toLowerCase())} ${cat.name}`;
            elements.selectAdminGridCat.appendChild(opt);
        });

        if (!state.activeAdminGridCategory) {
            state.activeAdminGridCategory = 'ALL';
        }
        elements.selectAdminGridCat.value = state.activeAdminGridCategory;
        await renderAdminGridEditor(state.activeAdminGridCategory, state.activeAdminGridPage);
    }

    function renderAdminGridPageTabs(categoryId, activePage, totalPages) {
        if (!elements.adminGridPageTabs) return;
        elements.adminGridPageTabs.innerHTML = '';

        for (let p = 0; p < totalPages; p++) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = `btn-admin-page-tab ${p === activePage ? 'active' : ''}`;
            btn.textContent = t('admin.grid_page_tab', { number: p + 1 });
            btn.addEventListener('click', async () => {
                state.activeAdminGridPage = p;
                await renderAdminGridEditor(categoryId, p);
            });
            elements.adminGridPageTabs.appendChild(btn);
        }

        const addBtn = document.createElement('button');
        addBtn.type = 'button';
        addBtn.className = 'btn-admin-add-page';
        addBtn.innerHTML = t('admin.grid_add_page_btn');
        addBtn.title = t('admin.grid_add_page_title');
        addBtn.addEventListener('click', async () => {
            await addNewGridPage(categoryId, totalPages);
        });
        elements.adminGridPageTabs.appendChild(addBtn);
    }

    async function addNewGridPage(categoryId, currentTotalPages) {
        const newPageIndex = currentTotalPages;
        const layout = await loadGridLayout(categoryId, 0);
        const cols = (layout && layout.columnsCount) || 4;
        const rows = (layout && layout.rowsCount) || 4;
        const total = cols * rows;

        const emptySlots = [];
        for (let i = 0; i < total; i++) {
            emptySlots.push({
                rowIndex: Math.floor(i / cols),
                columnIndex: i % cols,
                productId: null,
                customLabel: null,
                customColorHex: null
            });
        }
        await saveAdminGridLayout(categoryId, newPageIndex, cols, rows, emptySlots);
        state.activeAdminGridPage = newPageIndex;
        await renderAdminGridEditor(categoryId, newPageIndex);
        showToast(t('admin.grid_page_added_toast', { number: newPageIndex + 1 }), 'success');
    }

    async function renderAdminGridEditor(categoryId, pageIndex = 0) {
        if (!categoryId || !elements.adminMatrixGrid) return;
        state.activeAdminGridCategory = categoryId;
        state.activeAdminGridPage = pageIndex;
        const layout = await loadGridLayout(categoryId, pageIndex);
        if (!layout) return;

        const totalPages = layout.totalPages || 1;
        renderAdminGridPageTabs(categoryId, pageIndex, totalPages);

        const cols = layout.columnsCount || 4;
        const rows = layout.rowsCount || 4;

        if (elements.inputGridCols) elements.inputGridCols.value = cols;
        if (elements.inputGridRows) elements.inputGridRows.value = rows;

        const presetKey = `${cols}x${rows}`;
        if (elements.selectGridDimensionsPreset) {
            if (['3x3', '4x4', '5x4', '5x5', '6x4', '6x5'].includes(presetKey)) {
                elements.selectGridDimensionsPreset.value = presetKey;
                if (elements.customDimensionsInputs) elements.customDimensionsInputs.style.display = 'none';
            } else {
                elements.selectGridDimensionsPreset.value = 'custom';
                if (elements.customDimensionsInputs) elements.customDimensionsInputs.style.display = 'flex';
            }
        }

        if (elements.adminGridLayoutVersion) {
            elements.adminGridLayoutVersion.textContent = t('admin.grid_layout_version_value', { cols, rows, page: pageIndex + 1, total: totalPages, version: layout.version || 1 });
        }

        elements.adminMatrixGrid.innerHTML = '';
        elements.adminMatrixGrid.style.gridTemplateColumns = `repeat(${cols}, 1fr)`;
        elements.adminMatrixGrid.style.gridTemplateRows = `repeat(${rows}, 1fr)`;

        const totalSlots = cols * rows;
        const sortedSlots = [...layout.slots].sort((a, b) => a.slotIndex - b.slotIndex);

        for (let i = 0; i < totalSlots; i++) {
            const row = Math.floor(i / cols);
            const col = i % cols;
            const slot = sortedSlots.find(s => s.rowIndex === row && s.columnIndex === col) || {
                rowIndex: row,
                columnIndex: col,
                slotIndex: i,
                productId: null
            };

            const slotEl = document.createElement('div');
            slotEl.className = 'admin-grid-slot';
            slotEl.dataset.row = row;
            slotEl.dataset.col = col;

            const prod = slot.productId ? (state.products.find(p => p.id === slot.productId) || slot.product) : null;

            if (prod && !slot.isDisabled) {
                const color = slot.customColorHex || prod.colorHex;
                if (color) slotEl.style.borderTop = `4px solid ${color}`;
                slotEl.draggable = true;

                const displayName = slot.customLabel || prod.name;
                slotEl.innerHTML = `
                    <div class="admin-slot-header">
                        <span class="admin-slot-coord">[P${pageIndex + 1}:${row + 1},${col + 1}]</span>
                        <span class="product-badge-station" style="font-size:0.6rem;">${prod.preparationStationId || 'HOT'}</span>
                    </div>
                    <div class="admin-slot-title">${displayName}</div>
                    <div class="admin-slot-actions">
                        <button type="button" class="btn-slot-icon btn-edit-slot-trigger" title="${t('admin.grid_slot_customize_title')}">✏️</button>
                        <button type="button" class="btn-slot-icon delete btn-del-slot-trigger" title="${t('admin.grid_slot_release_title')}">✕</button>
                    </div>
                `;

                slotEl.querySelector('.btn-edit-slot-trigger').addEventListener('click', (e) => {
                    e.stopPropagation();
                    openEditSlotModal(row, col, slot);
                });

                slotEl.querySelector('.btn-del-slot-trigger').addEventListener('click', async (e) => {
                    e.stopPropagation();
                    await unassignSlot(categoryId, row, col);
                });

                slotEl.addEventListener('dragstart', (e) => {
                    state.draggedSlot = { categoryId, pageIndex, row, col };
                    e.dataTransfer.setData('text/plain', JSON.stringify({ type: 'SLOT', categoryId, pageIndex, row, col }));
                    slotEl.classList.add('dragging');
                });

                slotEl.addEventListener('dragend', () => {
                    state.draggedSlot = null;
                    slotEl.classList.remove('dragging');
                });
            } else {
                slotEl.classList.add('empty-slot');
                slotEl.innerHTML = `
                    <span class="admin-slot-coord" style="position:absolute; top:4px; inset-inline-start:6px;">[P${pageIndex + 1}:${row + 1},${col + 1}]</span>
                    <span style="color:var(--text-dim); font-size:0.8rem; font-weight:600;">${t('admin.grid_slot_assign_label')}</span>
                `;
                slotEl.addEventListener('click', () => {
                    openEditSlotModal(row, col, slot);
                });
            }

            // Drag and Drop dropzone behavior
            slotEl.addEventListener('dragover', (e) => {
                e.preventDefault();
                slotEl.classList.add('drag-over');
            });

            slotEl.addEventListener('dragleave', () => {
                slotEl.classList.remove('drag-over');
            });

            slotEl.addEventListener('drop', async (e) => {
                e.preventDefault();
                slotEl.classList.remove('drag-over');

                if (state.draggedSlot) {
                    const { row: sRow, col: sCol } = state.draggedSlot;
                    if (sRow === row && sCol === col) return;

                    try {
                        const swapRes = await fetch('/api/grid-layouts/swap', {
                            method: 'POST',
                            headers: { 'Content-Type': 'application/json' },
                            body: JSON.stringify({
                                layoutId: layout.id,
                                sourceRow: sRow,
                                sourceCol: sCol,
                                targetRow: row,
                                targetCol: col
                            })
                        });
                        if (swapRes.ok) {
                            const updatedLayout = await swapRes.json();
                            const cacheKey = `${categoryId}_page_${pageIndex}`;
                            state.gridLayouts[cacheKey] = updatedLayout;
                            localStorage.setItem(`grid_layout_${categoryId}_page_${pageIndex}`, JSON.stringify(updatedLayout));
                            showToast(t('admin.grid_positions_swapped_toast'), 'success');
                            await renderAdminGridEditor(categoryId, pageIndex);
                            if (state.activeCategory === categoryId && state.activeGridPage === pageIndex) {
                                await renderProductsGrid();
                            }
                        }
                    } catch (err) {
                        showToast(t('admin.grid_swap_error'), 'error');
                    }
                } else if (state.draggedCatalogItem) {
                    const prod = state.draggedCatalogItem;
                    await assignProductToSlot(categoryId, row, col, prod.id);
                }
            });

            elements.adminMatrixGrid.appendChild(slotEl);
        }

        // Render available catalog list on right
        renderAdminCatalogListForGrid(categoryId, layout);
    }

    function renderAdminCatalogListForGrid(categoryId, layout) {
        if (!elements.adminCatalogListForGrid) return;
        elements.adminCatalogListForGrid.innerHTML = '';
        const categoryProducts = categoryId === 'ALL' ? state.products : state.products.filter(p => p.categoryId === categoryId);
        const placedProductIds = new Set(layout.slots.filter(s => s.productId).map(s => s.productId));

        categoryProducts.forEach(prod => {
            const isPlaced = placedProductIds.has(prod.id);
            const itemEl = document.createElement('div');
            itemEl.className = `admin-catalog-item-draggable ${isPlaced ? 'placed' : ''}`;
            itemEl.draggable = true;
            itemEl.innerHTML = `
                <div>
                    <div style="font-weight:700;">${prod.name}</div>
                    <div style="font-size:0.75rem; color:var(--text-muted);">${Number(prod.price).toFixed(2)} € • ${prod.preparationStationId || 'HOT'}</div>
                </div>
                <span>${isPlaced ? t('admin.grid_catalog_placed_label') : '⠿'}</span>
            `;

            itemEl.addEventListener('dragstart', (e) => {
                state.draggedCatalogItem = prod;
                e.dataTransfer.setData('text/plain', JSON.stringify({ type: 'CATALOG_ITEM', productId: prod.id }));
            });

            itemEl.addEventListener('dragend', () => {
                state.draggedCatalogItem = null;
            });

            elements.adminCatalogListForGrid.appendChild(itemEl);
        });
    }

    function openEditSlotModal(row, col, slot) {
        if (!elements.editSlotModal) return;
        elements.editSlotRow.value = row;
        elements.editSlotCol.value = col;
        document.getElementById('editSlotModalTitle').textContent = t('admin.grid_edit_slot_title', { page: state.activeAdminGridPage + 1, row: row + 1, col: col + 1 });

        // Populate products select
        elements.selectSlotProduct.innerHTML = `<option value="">${t('admin.grid_slot_empty_option')}</option>`;
        const categoryProducts = state.products.filter(p => p.categoryId === state.activeAdminGridCategory);
        categoryProducts.forEach(prod => {
            const opt = document.createElement('option');
            opt.value = prod.id;
            opt.textContent = `${prod.name} (${Number(prod.price).toFixed(2)} €)`;
            if (slot && slot.productId === prod.id) {
                opt.selected = true;
            }
            elements.selectSlotProduct.appendChild(opt);
        });

        elements.inputSlotCustomLabel.value = (slot && slot.customLabel) || '';
        elements.inputSlotCustomColor.value = (slot && slot.customColorHex) || '#3b82f6';

        elements.editSlotModal.classList.add('active');
    }

    async function assignProductToSlot(categoryId, row, col, productId) {
        const pageIndex = state.activeAdminGridPage;
        const layout = await loadGridLayout(categoryId, pageIndex);
        if (!layout) return;

        const updatedSlots = [...layout.slots];
        const existingIdx = updatedSlots.findIndex(s => s.rowIndex === row && s.columnIndex === col);
        const prod = state.products.find(p => p.id === productId);

        const newSlotItem = {
            rowIndex: row,
            columnIndex: col,
            productId: productId,
            customLabel: prod ? prod.name : null,
            customColorHex: prod ? prod.colorHex : null
        };

        if (existingIdx >= 0) {
            updatedSlots[existingIdx] = { ...updatedSlots[existingIdx], ...newSlotItem };
        } else {
            updatedSlots.push({
                ...newSlotItem,
                slotIndex: (row * (layout.columnsCount || 4)) + col,
                isDisabled: false
            });
        }

        await saveAdminGridLayout(categoryId, pageIndex, layout.columnsCount || 4, layout.rowsCount || 4, updatedSlots);
        showToast(t('admin.grid_item_assigned_toast'), 'success');
    }

    async function unassignSlot(categoryId, row, col) {
        const pageIndex = state.activeAdminGridPage;
        const layout = await loadGridLayout(categoryId, pageIndex);
        if (!layout) return;

        const updatedSlots = layout.slots.map(s => {
            if (s.rowIndex === row && s.columnIndex === col) {
                return {
                    ...s,
                    productId: null,
                    customLabel: null,
                    customColorHex: null,
                    product: null
                };
            }
            return s;
        });

        await saveAdminGridLayout(categoryId, pageIndex, layout.columnsCount || 4, layout.rowsCount || 4, updatedSlots);
        showToast(t('admin.grid_slot_released_toast'), 'info');
    }

    async function saveSlotCustomizationFromModal() {
        const categoryId = state.activeAdminGridCategory;
        const pageIndex = state.activeAdminGridPage;
        const row = parseInt(elements.editSlotRow.value);
        const col = parseInt(elements.editSlotCol.value);
        const productId = elements.selectSlotProduct.value || null;
        const customLabel = elements.inputSlotCustomLabel.value.trim() || null;
        const customColor = elements.inputSlotCustomColor.value || null;

        const layout = await loadGridLayout(categoryId, pageIndex);
        if (!layout) return;

        const updatedSlots = [...layout.slots];
        const existingIdx = updatedSlots.findIndex(s => s.rowIndex === row && s.columnIndex === col);

        const slotData = {
            rowIndex: row,
            columnIndex: col,
            productId: productId,
            customLabel: customLabel,
            customColorHex: customColor
        };

        if (existingIdx >= 0) {
            updatedSlots[existingIdx] = { ...updatedSlots[existingIdx], ...slotData };
        } else {
            updatedSlots.push({
                ...slotData,
                slotIndex: (row * (layout.columnsCount || 4)) + col,
                isDisabled: false
            });
        }

        await saveAdminGridLayout(categoryId, pageIndex, layout.columnsCount || 4, layout.rowsCount || 4, updatedSlots);
        elements.editSlotModal.classList.remove('active');
        showToast(t('admin.grid_slot_updated_toast'), 'success');
    }

    async function saveAdminGridLayout(categoryId, pageIndex, columnsCount, rowsCount, slots) {
        try {
            const payload = {
                categoryId: categoryId,
                pageIndex: pageIndex,
                columnsCount: columnsCount,
                rowsCount: rowsCount,
                slots: slots.map(s => ({
                    rowIndex: s.rowIndex,
                    columnIndex: s.columnIndex,
                    productId: s.productId,
                    customLabel: s.customLabel,
                    customColorHex: s.customColorHex
                }))
            };

            const res = await fetch('/api/grid-layouts', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (res.ok) {
                const saved = await res.json();
                const cacheKey = `${categoryId}_page_${pageIndex}`;
                state.gridLayouts[cacheKey] = saved;
                localStorage.setItem(`grid_layout_${categoryId}_page_${pageIndex}`, JSON.stringify(saved));
                await renderAdminGridEditor(categoryId, pageIndex);
                if (state.activeCategory === categoryId && state.activeGridPage === pageIndex) {
                    await renderProductsGrid();
                }
            } else {
                showToast(t('admin.grid_save_error'), 'error');
            }
        } catch (err) {
            console.error('Erreur sauvegarde grille:', err);
            showToast(t('admin.grid_save_network_error'), 'error');
        }
    }

    async function resetAdminGridLayout(categoryId) {
        const categoryProducts = state.products.filter(p => p.categoryId === categoryId);
        const slots = [];
        const cols = 4;
        const rows = 4;
        const total = cols * rows;

        for (let i = 0; i < total; i++) {
            const r = Math.floor(i / cols);
            const c = i % cols;
            const prod = i < categoryProducts.length ? categoryProducts[i] : null;
            slots.push({
                rowIndex: r,
                columnIndex: c,
                productId: prod ? prod.id : null,
                customLabel: null,
                customColorHex: null
            });
        }

        await saveAdminGridLayout(categoryId, state.activeAdminGridPage, cols, rows, slots);
        showToast(t('admin.grid_reset_toast'), 'success');
    }

    // ==================== REAL-TIME SIGNALR SYNC (US5) ====================
    // L'état des imprimantes est diffusé sur /hubs/pos (le reste de ce client écoute /hubs/kitchen).
    // Hub [Authorize] : démarré seulement une fois un jeton disponible (connexion ou renouvellement), et relancé si le démarrage a échoué.
    let posConnection = null;
    let posConnectionStarted = false;
    function startPosHub() {
        if (typeof signalR === 'undefined' || posConnectionStarted) return;
        const token = state.token || localStorage.getItem('pos_jwt_token');
        if (!token) return;
        if (!posConnection) {
            posConnection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/pos', { accessTokenFactory: () => state.token || localStorage.getItem('pos_jwt_token') || '' })
                .withAutomaticReconnect()
                .build();
            posConnection.on('OnPrinterStatusChanged', (printerId, printerName, isOnline, pendingCount) => {
                showToast(isOnline
                    ? t('messages.printer_back_online', { name: printerName })
                    : t('messages.printer_offline', { name: printerName, count: pendingCount }), isOnline ? 'success' : 'warning');
                if (document.getElementById('adminPrintersList')?.offsetParent) loadAdminPrinters();
            });
        }
        posConnectionStarted = true;
        posConnection.start().catch(err => {
            posConnectionStarted = false; // sera retenté à la prochaine authentification
            console.log('SignalR hub /hubs/pos non connecté:', err);
        });
    }

    function setupSignalR() {
        if (typeof signalR === 'undefined') return;
        try {
            const connection = new signalR.HubConnectionBuilder()
                .withUrl('/hubs/kitchen')
                .withAutomaticReconnect()
                .build();

            connection.on('OnGridLayoutUpdated', (layout) => {
                if (layout && layout.categoryId) {
                    const pageIndex = layout.pageIndex !== undefined ? layout.pageIndex : 0;
                    const cacheKey = `${layout.categoryId}_page_${pageIndex}`;
                    state.gridLayouts[cacheKey] = layout;
                    localStorage.setItem(`grid_layout_${layout.categoryId}_page_${pageIndex}`, JSON.stringify(layout));

                    if (state.activeCategory === layout.categoryId && state.activeGridPage === pageIndex) {
                        renderProductsGrid();
                    }
                    if (state.activeAdminGridCategory === layout.categoryId && state.activeAdminGridPage === pageIndex) {
                        renderAdminGridEditor(layout.categoryId, pageIndex);
                    }
                }
            });

            connection.on('ReceiveKitchenUpdate', () => {
                loadKdsData();
            });

            // Feature 019: Happy Hour Real-time Broadcast
            connection.on('OnHappyHourStatusChanged', (status) => {
                applyHappyHourStatus(status);
            });

            connection.start().catch(err => {
                console.log('SignalR hub non connecté (mode standalone):', err);
            });
        } catch (e) {
            console.warn('SignalR initialisation passée:', e);
        }
    }

    // ==================== HAPPY HOUR LOGIC (FEATURE 019) ====================
    async function checkHappyHourStatus() {
        try {
            const res = await fetch(`/api/happy-hour/status?terminalId=${encodeURIComponent(state.terminalId || 'POS_MAIN')}`);
            if (res.ok) {
                const status = await res.json();
                await applyHappyHourStatus(status);
            }
        } catch (err) {
            console.warn('Erreur vérification statut Happy Hour:', err);
        }
    }

    async function applyHappyHourStatus(status) {
        state.happyHour.isActive = !!status.isActive;
        state.happyHour.isOverride = !!status.isOverride;
        state.happyHour.activeScheduleName = status.activeScheduleName || 'Happy Hour';
        state.happyHour.activeScheduleId = status.activeScheduleId || null;
        state.happyHour.appliesToTakeaway = !!status.appliesToTakeaway;

        if (status.isActive && status.currentWindow) {
            state.happyHour.remainingMinutes = status.currentWindow.remainingMinutes;
            showHappyHourBanner(status.activeScheduleName, status.currentWindow.remainingMinutes, status.isOverride);
            await fetchHappyHourPricingTable();
        } else {
            hideHappyHourBanner();
            state.happyHour.pricingTable = {};
            if (state.happyHour.countdownInterval) {
                clearInterval(state.happyHour.countdownInterval);
                state.happyHour.countdownInterval = null;
            }
        }
        renderProductsGrid();
        renderCart();
    }

    async function fetchHappyHourPricingTable() {
        try {
            const res = await fetch(`/api/happy-hour/pricing-table?terminalId=${encodeURIComponent(state.terminalId || 'POS_MAIN')}`);
            if (res.ok) {
                const data = await res.json();
                state.happyHour.pricingTable = {};
                if (data.items && Array.isArray(data.items)) {
                    data.items.forEach(item => {
                        state.happyHour.pricingTable[item.productId] = item;
                    });
                }
            }
        } catch (err) {
            console.warn('Erreur chargement grille tarifaire Happy Hour:', err);
        }
    }

    function showHappyHourBanner(title, remainingMinutes, isOverride) {
        if (!elements.happyHourBanner) return;
        elements.happyHourBanner.style.display = 'block';
        if (elements.hhBannerTitle) {
            elements.hhBannerTitle.textContent = isOverride ? t('common.hh_override_banner_title', { title: title.toUpperCase() }) : t('common.hh_active_banner_title', { title: title.toUpperCase() });
        }
        if (elements.hhBannerSubtitle) {
            elements.hhBannerSubtitle.textContent = isOverride ? t('common.hh_override_subtitle') : t('common.happy_hour_subtitle');
        }

        let secondsRemaining = Math.max(0, remainingMinutes * 60);
        updateCountdownDisplay(secondsRemaining);

        if (state.happyHour.countdownInterval) {
            clearInterval(state.happyHour.countdownInterval);
        }

        state.happyHour.countdownInterval = setInterval(() => {
            secondsRemaining--;
            if (secondsRemaining <= 0) {
                clearInterval(state.happyHour.countdownInterval);
                state.happyHour.countdownInterval = null;
                checkHappyHourStatus();
            } else {
                updateCountdownDisplay(secondsRemaining);
            }
        }, 1000);
    }

    function updateCountdownDisplay(totalSecs) {
        if (!elements.hhCountdownTime) return;
        const mins = Math.floor(totalSecs / 60);
        const secs = totalSecs % 60;
        elements.hhCountdownTime.textContent = `${String(mins).padStart(2, '0')}:${String(secs).padStart(2, '0')}`;
    }

    function hideHappyHourBanner() {
        if (elements.happyHourBanner) {
            elements.happyHourBanner.style.display = 'none';
        }
    }

    function setupHappyHourControls() {
        if (elements.btnHhOverrideQuickAction) {
            elements.btnHhOverrideQuickAction.addEventListener('click', () => {
                if (elements.hhOverrideModal) {
                    elements.inputHhPin.value = '';
                    elements.hhOverrideModal.classList.add('active');
                }
            });
        }

        if (elements.btnCloseHhOverrideModal) {
            elements.btnCloseHhOverrideModal.addEventListener('click', () => {
                if (elements.hhOverrideModal) elements.hhOverrideModal.classList.remove('active');
            });
        }

        if (elements.btnHhExtend30) {
            elements.btnHhExtend30.addEventListener('click', async () => {
                await handleHhOverride(30);
            });
        }

        if (elements.btnHhForce60) {
            elements.btnHhForce60.addEventListener('click', async () => {
                await handleHhOverride(60);
            });
        }

        if (elements.btnHhForceStop) {
            elements.btnHhForceStop.addEventListener('click', async () => {
                await handleHhStop();
            });
        }
    }

    async function handleHhOverride(durationMinutes) {
        const pin = elements.inputHhPin ? elements.inputHhPin.value.trim() : '';
        const reason = elements.inputHhReason?.value.trim() || 'Dérogation responsable'; // nf525-texte-fixe : motif envoyé au serveur et stocké dans HappyHourOverrideSession.Reason, jamais traduit

        if (!pin) {
            showToast(t('admin.hh_pin_required_warning'), 'warning');
            return;
        }

        try {
            const res = await fetch('/api/happy-hour/override/activate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    terminalId: state.terminalId || 'POS_MAIN',
                    supervisorPin: pin,
                    durationMinutes: durationMinutes,
                    reason: reason
                })
            });

            if (res.ok) {
                const data = await res.json();
                showToast(data.message || t('admin.hh_extended_toast', { minutes: durationMinutes }), 'success');
                if (elements.hhOverrideModal) elements.hhOverrideModal.classList.remove('active');
                await checkHappyHourStatus();
            } else {
                const err = await res.json();
                showToast(err.message || t('admin.hh_override_denied_error'), 'error');
            }
        } catch (e) {
            console.error('Erreur forçage Happy Hour:', e);
            showToast(t('admin.hh_override_network_error'), 'error');
        }
    }

    async function handleHhStop() {
        const pin = elements.inputHhPin ? elements.inputHhPin.value.trim() : '';
        const reason = elements.inputHhReason?.value.trim() || 'Arrêt anticipé'; // nf525-texte-fixe : motif envoyé au serveur et stocké dans HappyHourOverrideSession.Reason, jamais traduit

        if (!pin) {
            showToast(t('admin.hh_pin_required_warning'), 'warning');
            return;
        }

        try {
            const res = await fetch('/api/happy-hour/override/stop', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    terminalId: state.terminalId || 'POS_MAIN',
                    supervisorPin: pin,
                    reason: reason
                })
            });

            if (res.ok) {
                showToast(t('admin.hh_stopped_toast'), 'info');
                if (elements.hhOverrideModal) elements.hhOverrideModal.classList.remove('active');
                await checkHappyHourStatus();
            } else {
                const err = await res.json();
                showToast(err.message || t('admin.hh_stop_denied_error'), 'error');
            }
        } catch (e) {
            console.error('Erreur arrêt Happy Hour:', e);
            showToast(t('admin.hh_stop_network_error'), 'error');
        }
    }

    // ==================== UTILS ====================
    function getCategoryIcon(name) {
        const map = {
            'salad': '🥗',
            'meat': '🥩',
            'pizza': '🍕',
            'cake': '🍰',
            'glass': '🍷'
        };
        return map[name] || '🏷️';
    }

    function getTableStatusClass(status) {
        if (typeof status === 'string') return status.toLowerCase();
        const map = ['free', 'occupied', 'billprinted', 'paid'];
        return map[status] || 'free';
    }

    function getTableStatusLabel(status) {
        if (status === 0 || status === 'Free') return t('floor.legend_free');
        if (status === 1 || status === 'Occupied') return t('floor.legend_occupied');
        if (status === 2 || status === 'BillPrinted' || status === 'BillRequested') return t('floor.legend_bill');
        if (status === 3 || status === 'Paid') return t('floor.legend_paid');
        return t('floor.legend_free');
    }

    function showToast(msg, type = 'info') {
        const toast = document.createElement('div');
        toast.className = `toast ${type}`;
        toast.textContent = msg;
        elements.toastContainer.appendChild(toast);
        setTimeout(() => toast.remove(), 3200);
    }

    // Start App
    init();
});

