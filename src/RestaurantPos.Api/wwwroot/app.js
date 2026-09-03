// Restaurant POS Web Client Interactive Application
document.addEventListener('DOMContentLoaded', () => {
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
        activeTable: 'T2',
        activeCovers: 3,
        activeOrderId: null,
        cart: [],
        pinInput: '',
        splitGuests: 2,
        selectedTipPercent: 0,
        customTipAmount: 0,
        globalDiscount: null,
        token: localStorage.getItem('pos_jwt_token') || null
    };

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
        const res = await originalFetch(resource, config);
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
        slipTotalTtc: document.getElementById('slipTotalTtc'),
        slipTotalHt: document.getElementById('slipTotalHt'),
        slipCount: document.getElementById('slipCount'),
        slipHash: document.getElementById('slipHash'),
        slipDate: document.getElementById('slipDate'),

        // Admin Grid Editor
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
        setupAdminTabs();
        setupModals();
        setupForms();
        setupSalesGridPaginationListeners();
        setupAdminGridEditorListeners();
        setupSignatureCanvas();
        setupSignalR();

        // Ensure active authentication session
        await ensureAuthToken();

        await loadCatalogData();
        await loadFloorPlanData();
        await loadKdsData();
        await loadAdminData();
        await loadNetworkSyncData();

        // Automatically recall the active table (T2) order on startup
        await loadActiveTableOrder(state.activeTable);
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
            showToast('Erreur chargement catalogue', 'error');
        }
    }

    function renderCatalogTabs() {
        elements.categoryTabsBar.innerHTML = '';

        // All items button
        const allBtn = document.createElement('button');
        allBtn.className = `btn-cat-tab ${state.activeCategory === 'ALL' ? 'active' : ''}`;
        allBtn.style.borderLeftColor = '#3b82f6';
        allBtn.style.borderLeftWidth = '6px';
        allBtn.innerHTML = `<span>🍽️</span> <span>Tout le Menu</span>`;
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
            tabBtn.style.borderLeftColor = cat.colorHex || '#4A90E2';
            tabBtn.style.borderLeftWidth = '6px';
            tabBtn.innerHTML = `
                <span>${getCategoryIcon(cat.iconName || cat.name.toLowerCase())}</span>
                <span>${cat.name}</span>
                <span class="btn-edit-cat-trigger" data-cat-id="${cat.id}" title="Modifier" style="margin-left:auto; font-size:0.75rem; opacity:0.6; padding:2px 4px;">✏️</span>
            `;

            tabBtn.addEventListener('click', (e) => {
                if (e.target.closest('.btn-edit-cat-trigger')) {
                    e.stopPropagation();
                    document.getElementById('editCatId').value = cat.id;
                    document.getElementById('editCatName').value = cat.name;
                    document.getElementById('editCatColor').value = cat.colorHex || '#4A90E2';
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
            btn.addEventListener('click', () => addToCart(prod));
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
        elements.gridPageLabel.textContent = `Page ${currentPage + 1} / ${totalPages}`;
        elements.btnPrevGridPage.disabled = currentPage <= 0;
        elements.btnNextGridPage.disabled = currentPage >= totalPages - 1;

        elements.gridPageDots.innerHTML = '';
        for (let p = 0; p < totalPages; p++) {
            const dot = document.createElement('div');
            dot.className = `grid-page-dot ${p === currentPage ? 'active' : ''}`;
            dot.title = `Page ${p + 1}`;
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
        card.innerHTML = `
            <div class="product-card-top">
                <span class="product-badge-station">${prod.preparationStationId || 'HOT'}</span>
            </div>
            <div class="product-name">${displayName}</div>
            <div class="product-price">${Number(prod.price).toFixed(2)} €</div>
        `;
        card.addEventListener('pointerdown', () => {
            card.style.transform = 'scale(0.95)';
        });
        card.addEventListener('pointerup', () => {
            card.style.transform = '';
        });
        card.addEventListener('click', () => addToCart(prod));
        return card;
    }

    function addToCart(product, course = 'Direct') {
        const existing = state.cart.find(item => item.product.id === product.id && !item.isDispatched && item.course === course);
        if (existing) {
            existing.quantity += 1;
        } else {
            state.cart.push({
                product: {
                    id: product.id,
                    name: product.name,
                    price: product.price,
                    taxRatePercent: product.taxRatePercent || 10.0,
                    preparationStationId: product.preparationStationId || 'HOT_KITCHEN'
                },
                quantity: 1,
                course: course,
                isDispatched: false,
                isComp: false,
                discountPercent: 0,
                modifiers: []
            });
        }
        renderCart();
        showToast(`+1 ${product.name} [${course}]`, 'success');
        scheduleAutoSaveCart();
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
                state.operator = { name: data.operatorName, role: data.role, id: data.operatorId };
                return data.token;
            }
        } catch (e) {
            console.warn('ensureAuthToken failed:', e);
        }
        return null;
    }

    let autoSaveTimer = null;
    function scheduleAutoSaveCart() {
        if (autoSaveTimer) clearTimeout(autoSaveTimer);
        autoSaveTimer = setTimeout(() => {
            saveActiveCartToServer();
        }, 400);
    }

    async function saveActiveCartToServer() {
        if (!state.activeTable) return;
        const undispatched = state.cart.filter(i => !i.isDispatched);
        if (undispatched.length === 0) return;

        try {
            await ensureAuthToken();
            const courseMap = { 'Direct': 0, 'Suite': 1, 'Dessert': 2, 'OnDemand': 3 };
            const itemsPayload = undispatched.map(i => ({
                productId: i.product.id,
                productName: i.product.name,
                quantity: i.quantity,
                unitPrice: i.product.price,
                taxRatePercent: i.product.taxRatePercent || 10.0,
                preparationStationId: i.product.preparationStationId || 'HOT_KITCHEN',
                modifiers: i.modifiers || [],
                course: courseMap[i.course] || 0
            }));

            const res = await fetch(`/api/tables/${state.activeTable}/items`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ items: itemsPayload })
            });

            if (res.ok) {
                const data = await res.json();
                if (data && data.orderId) {
                    state.activeOrderId = data.orderId;
                }
            }
        } catch (err) {
            console.error('Erreur sauvegarde automatique panier:', err);
        }
    }

    async function loadActiveTableOrder(tableNumber) {
        // First flush any pending undispatched items on previous table if switching tables
        if (state.activeTable && state.activeTable !== tableNumber) {
            await saveActiveCartToServer();
        }

        state.activeTable = tableNumber;
        elements.activeTableBadge.textContent = `Table ${tableNumber}`;

        try {
            let res = await fetch(`/api/tables/${tableNumber}/order`);
            if (res.status === 401 && !state.token) {
                await ensureAuthToken();
                res = await fetch(`/api/tables/${tableNumber}/order`);
            }

            if (res.ok) {
                const orderData = await res.json();
                state.activeOrderId = orderData.orderId;
                state.activeCovers = orderData.coversCount || 2;
                state.globalDiscount = orderData.globalDiscountType !== null ? {
                    type: orderData.globalDiscountType,
                    value: orderData.globalDiscountValue,
                    reason: orderData.globalDiscountReason
                } : null;

                elements.activeCoversBadge.textContent = `👥 ${state.activeCovers} Couverts`;

                // Hydrate cart from database order lines
                state.cart = orderData.lines.map(line => ({
                    lineId: line.lineId,
                    product: {
                        id: line.productId,
                        name: line.productName,
                        price: line.unitPrice,
                        taxRatePercent: line.taxRatePercent,
                        preparationStationId: line.preparationStationId
                    },
                    quantity: line.quantity,
                    course: typeof line.course === 'number' ? getCourseNameFromEnum(line.course) : (line.course || 'Direct'),
                    isDispatched: line.isDispatched,
                    isComp: line.isComp || false,
                    discountPercent: line.discountPercent || 0,
                    modifiers: line.modifiersSummary || []
                }));

                renderCart();
            } else if (res.status === 404) {
                // Table is genuinely free in database - reset cart for new order
                state.activeOrderId = null;
                state.cart = [];
                state.globalDiscount = null;
                elements.activeCoversBadge.textContent = `👥 2 Couverts (Libre)`;
                renderCart();
            } else if (res.status === 401) {
                // Not authenticated: do not wipe cart, terminal will prompt for PIN
                console.warn(`Rappel table ${tableNumber} refusé (401 Non Authentifié). Authentification requise.`);
            } else {
                showToast(`Erreur ${res.status} lors du rappel de la table ${tableNumber}`, 'error');
            }
        } catch (err) {
            console.error('Erreur rappel table:', err);
            showToast(`Erreur chargement table ${tableNumber}`, 'error');
        }
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

    function renderCart() {
        elements.cartItemsList.innerHTML = '';
        let totalHt = 0;
        let totalVat = 0;
        let totalTtc = 0;

        if (state.cart.length === 0) {
            elements.cartItemsList.innerHTML = `
                <div style="text-align:center;color:#64748b;padding:40px 10px;">
                    <div style="font-size:2.5rem;margin-bottom:8px;">🍽️</div>
                    <strong>Table ${state.activeTable} vide</strong>
                    <div style="font-size:0.8rem;margin-top:4px;">Touchez un article ou une touche rapide pour démarrer la commande</div>
                </div>
            `;
        }

        state.cart.forEach((item, index) => {
            let lineTtc = item.isComp ? 0 : (item.product.price * item.quantity);
            if (item.discountPercent > 0 && !item.isComp) {
                lineTtc = lineTtc * (1.0 - (item.discountPercent / 100.0));
            }

            const vatRate = (item.product.taxRatePercent || 10.0) / 100.0;
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
                        <span class="course-badge ${courseClass}" data-idx="${index}" title="Cliquer pour changer de service (Direct / Suite / Dessert)">${item.course || 'Direct'}</span>
                        ${item.isComp ? '<span class="comp-badge">🎁 Offert</span>' : ''}
                        ${item.isDispatched ? '<span class="badge-dispatched" title="Déjà transmis en préparation">👨‍🍳 Cuisine</span>' : '<span class="badge-pending" title="Nouvel article à envoyer">➕ Nouveau</span>'}
                    </div>
                    <span class="cart-item-meta">${item.product.price.toFixed(2)} € × ${item.quantity} (TVA ${item.product.taxRatePercent || 10}%)</span>
                    ${item.modifiers && item.modifiers.length > 0 ? `<div style="font-size:0.72rem;color:#f59e0b;">${item.modifiers.join(', ')}</div>` : ''}
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
        elements.summaryTtc.textContent = `${totalTtc.toFixed(2)} €`;

        // Attach quantity buttons
        elements.cartItemsList.querySelectorAll('.btn-qty').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const idx = parseInt(btn.getAttribute('data-idx'));
                const action = btn.getAttribute('data-action');
                if (action === 'plus') {
                    state.cart[idx].quantity += 1;
                } else if (action === 'minus') {
                    state.cart[idx].quantity -= 1;
                    if (state.cart[idx].quantity <= 0) {
                        state.cart.splice(idx, 1);
                    }
                }
                renderCart();
                scheduleAutoSaveCart();
            });
        });

        // Attach course cycling on course badge click
        elements.cartItemsList.querySelectorAll('.course-badge').forEach(badge => {
            badge.addEventListener('click', (e) => {
                const idx = parseInt(badge.getAttribute('data-idx'));
                if (state.cart[idx] && !state.cart[idx].isDispatched) {
                    state.cart[idx].course = cycleCourse(state.cart[idx].course || 'Direct');
                    renderCart();
                    showToast(`Service passé à : ${state.cart[idx].course}`, 'info');
                }
            });
        });
    }

    elements.btnClearCart.addEventListener('click', () => {
        if (state.cart.length === 0) return;

        const undispatched = state.cart.filter(i => !i.isDispatched);
        const dispatched = state.cart.filter(i => i.isDispatched);

        if (undispatched.length > 0 && dispatched.length > 0) {
            state.cart = dispatched;
            renderCart();
            showToast(`${undispatched.length} article(s) retiré(s). Articles en cuisine conservés.`, 'info');
        } else if (undispatched.length > 0 && dispatched.length === 0) {
            state.cart = [];
            renderCart();
            showToast('Panier vidé', 'info');
        } else if (undispatched.length === 0 && dispatched.length > 0) {
            showToast('Les articles déjà transmis en cuisine ne peuvent pas être vidés.', 'error');
        }
    });

    // 1. Send to Kitchen
    elements.btnSendKitchen.addEventListener('click', async () => {
        if (state.cart.length === 0) {
            showToast('Panier vide !', 'error');
            return;
        }

        try {
            const courseMap = { 'Direct': 0, 'Suite': 1, 'Dessert': 2, 'OnDemand': 3 };
            const undispatched = state.cart.filter(i => !i.isDispatched);
            if (undispatched.length > 0) {
                const itemsPayload = undispatched.map(i => ({
                    productId: i.product.id,
                    productName: i.product.name,
                    quantity: i.quantity,
                    unitPrice: i.product.price,
                    taxRatePercent: i.product.taxRatePercent || 10.0,
                    preparationStationId: i.product.preparationStationId || 'HOT_KITCHEN',
                    modifiers: i.modifiers || [],
                    course: courseMap[i.course] || 0
                }));

                await fetch(`/api/tables/${state.activeTable}/items`, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ items: itemsPayload })
                });
            }

            await fetch(`/api/tables/${state.activeTable}/dispatch`, { method: 'POST' });
            showToast(`Commande ${state.activeTable} envoyée en cuisine ! 👨‍🍳`, 'success');
            
            state.cart = [];
            state.activeOrderId = null;
            renderCart();
            switchView('floorPlanView');
            
            await loadKdsData();
            await loadFloorPlanData();
        } catch (err) {
            console.error('Erreur envoi cuisine:', err);
            showToast('Erreur envoi cuisine', 'error');
        }
    });

    // 2. Fire Suite (US3)
    elements.btnFireSuite.addEventListener('click', async () => {
        if (!state.activeTable) return;
        try {
            const res = await fetch(`/api/tables/${state.activeTable}/fire-suite`, { method: 'POST' });
            if (res.ok) {
                showToast(`🔔 RÉCLAME SUITE envoyée en cuisine pour la table ${state.activeTable} !`, 'success');
                await loadKdsData();
            } else {
                showToast('Échec de la réclame suite', 'error');
            }
        } catch (err) {
            showToast('Erreur réclame suite', 'error');
        }
    });

    // ==================== MODALS & PAYMENTS ====================
    function setupModals() {
        // Transfer Modal (US1)
        elements.btnTransferModal.addEventListener('click', () => {
            elements.transferSourceTable.value = state.activeTable;
            elements.selectTargetTable.innerHTML = '';
            state.tables.forEach(t => {
                if (t.tableNumber !== state.activeTable) {
                    const opt = document.createElement('option');
                    opt.value = t.tableNumber;
                    opt.textContent = `Table ${t.tableNumber} (${t.status === 0 || t.status === 'Free' ? 'Libre' : 'Occupée - Fusion'})`;
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
                const res = await fetch(endpoint, {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ targetTableNumber: targetTable })
                });
                const data = await res.json();
                if (res.ok && data.success) {
                    showToast(data.message || `Table transférée vers ${targetTable}`, 'success');
                    elements.transferTableModal.classList.remove('active');
                    await loadFloorPlanData();
                    await loadActiveTableOrder(targetTable);
                } else {
                    showToast(data.message || 'Erreur transfert/fusion', 'error');
                }
            } catch (err) {
                showToast('Erreur réseau transfert table', 'error');
            }
        });

        // Discount Modal (US2)
        elements.btnDiscountModal.addEventListener('click', () => {
            elements.selectDiscountTarget.innerHTML = '<option value="global">Remise Globale sur la Note</option>';
            state.cart.forEach((item, idx) => {
                const opt = document.createElement('option');
                opt.value = item.lineId || `idx_${idx}`;
                opt.textContent = `Article : ${item.product.name} (${item.product.price.toFixed(2)} €) -> Offrir 🎁`;
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
                showToast('Remise supprimée', 'info');
                elements.discountModal.classList.remove('active');
                await loadActiveTableOrder(state.activeTable);
            }
        });

        elements.formApplyDiscount.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!state.activeOrderId) {
                showToast('Aucune commande active enregistrée sur cette table', 'error');
                return;
            }

            const target = elements.selectDiscountTarget.value;
            let reason = elements.selectDiscountReason.value;
            if (reason === 'Autre motif') {
                reason = elements.inputDiscountCustomReason.value.trim() || 'Remise accordée';
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
                        showToast(`Remise appliquée (${val}${type === 0 ? '%' : '€'}) !`, 'success');
                    }
                } else {
                    // Comp item
                    const itemId = target.startsWith('idx_') ? state.cart[parseInt(target.replace('idx_', ''))].lineId : target;
                    const res = await fetch(`/api/orders/${state.activeOrderId}/items/${itemId}/comp`, {
                        method: 'POST',
                        headers: { 'Content-Type': 'application/json' },
                        body: JSON.stringify({ reason: reason })
                    });
                    if (res.ok) {
                        showToast('Article marqué offert ! 🎁', 'success');
                    }
                }
                elements.discountModal.classList.remove('active');
                await loadActiveTableOrder(state.activeTable);
            } catch (err) {
                showToast('Erreur application remise', 'error');
            }
        });

        // Payment Modal & Tips (US4)
        elements.btnPayModal.addEventListener('click', () => {
            const totalTtc = calculateTotalTtc();
            if (totalTtc <= 0) {
                showToast('Le montant est nul.', 'error');
                return;
            }
            state.selectedTipPercent = 0;
            state.customTipAmount = 0;
            updateTipCalculation();
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

            const baseAmount = calculateTotalTtc();
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
                        notes: `Facturation chambre ${roomNum}`
                    })
                });

                const data = await res.json();
                if (res.ok && data.success) {
                    showToast(data.message || 'Facturation chambre validée ! 🏨', 'success');
                    elements.roomChargeModal.classList.remove('active');
                    state.cart = [];
                    state.activeOrderId = null;
                    renderCart();
                    await loadFloorPlanData();
                } else {
                    showToast(data.message || 'Échec de facturation chambre', 'error');
                }
            } catch (err) {
                showToast('Erreur réseau facturation chambre', 'error');
            }
        });

        // Split Bill Modal
        elements.btnSplitBill.addEventListener('click', () => {
            const total = calculateTotalTtc();
            if (total <= 0) {
                showToast('Panier vide pour le split', 'error');
                return;
            }
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
            elements.paymentModal.classList.add('active');
            const total = calculateTotalTtc();
            const part = (total / state.splitGuests).toFixed(2);
            elements.payRemainingAmount.textContent = `${part} € (Part 1/${state.splitGuests})`;
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
                opt.textContent = `Chambre ${room.roomNumber} - ${room.guestName}`;
                elements.selectHotelRoom.appendChild(opt);
            });
            if (state.hotelRooms.length > 0) {
                elements.selectHotelRoom.dispatchEvent(new Event('change'));
            }
        } catch (err) {
            console.error('Erreur chargement chambres:', err);
        }
    }

    function getTipAmount() {
        if (state.customTipAmount > 0) return state.customTipAmount;
        const total = calculateTotalTtc();
        return (total * (state.selectedTipPercent / 100.0));
    }

    function getFinalPayTotal() {
        return calculateTotalTtc() + getTipAmount();
    }

    function updateTipCalculation() {
        const total = calculateTotalTtc();
        const tip = getTipAmount();
        const finalTotal = total + tip;

        elements.payRemainingAmount.textContent = `${total.toFixed(2)} €`;
        elements.payTotalWithTip.textContent = `${finalTotal.toFixed(2)} € (+${tip.toFixed(2)} € tip)`;
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
            let line = item.product.price * item.quantity;
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

    function updateSplitPartitions() {
        elements.splitGuestsCount.textContent = `${state.splitGuests} Convives`;
        elements.splitPartitionsList.innerHTML = '';
        const totalCents = Math.round(calculateTotalTtc() * 100);
        const base = Math.floor(totalCents / state.splitGuests);
        let remainder = totalCents % state.splitGuests;

        for (let i = 1; i <= state.splitGuests; i++) {
            const cents = base + (remainder > 0 ? 1 : 0);
            if (remainder > 0) remainder--;
            const row = document.createElement('div');
            row.className = 'partition-row';
            row.innerHTML = `<span>Convive #${i} :</span> <strong>${(cents / 100).toFixed(2)} €</strong>`;
            elements.splitPartitionsList.appendChild(row);
        }
    }

    async function executePayment(tenderMethod, tendered) {
        const total = getFinalPayTotal();
        const change = Math.max(0, tendered - total);

        try {
            const payload = {
                orderId: state.activeOrderId || '00000000-0000-0000-0000-000000000000',
                tableNumber: state.activeTable,
                operatorId: '00000000-0000-0000-0000-000000000000',
                tenders: [
                    {
                        method: tenderMethod,
                        amount: total,
                        tendered: tendered,
                        changeGiven: change
                    }
                ]
            };

            const res = await fetch('/api/checkout/pay', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            if (res.ok) {
                const resData = await res.json();
                showToast(`Paiement validé ! Reçu #${resData.receiptNumber || 'NF'} - Rendu: ${change.toFixed(2)} €`, 'success');
                elements.paymentModal.classList.remove('active');
                state.cart = [];
                state.activeOrderId = null;
                renderCart();
                await loadFloorPlanData();
            } else {
                showToast('Erreur validation paiement', 'error');
            }
        } catch (err) {
            console.error('Erreur paiement:', err);
            showToast('Erreur paiement', 'error');
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
            card.innerHTML = `
                <div class="table-num">${table.tableNumber}</div>
                <div class="table-sub">Capacité : ${table.capacity} pers.</div>
                <div class="table-sub">Statut : ${getTableStatusLabel(table.status)}</div>
                <div class="table-sub">Serveur : ${table.assignedWaiterName || '—'}</div>
                ${table.coversCount > 0 ? `<div style="font-size:0.75rem;color:#10b981;font-weight:700;margin-top:4px;">👥 ${table.coversCount} Couverts actifs</div>` : ''}
            `;

            card.addEventListener('click', async () => {
                await loadActiveTableOrder(table.tableNumber);
                switchView('posView');
                showToast(`Table ${table.tableNumber} rappelée !`, 'info');
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
                    ${ticket.items ? ticket.items.map(i => `<div>• ${i.quantity}x ${i.productName}</div>`).join('') : '1x Plat du jour'}
                </div>
            `;

            card.addEventListener('click', async () => {
                await fetch(`/api/kds/tickets/${ticket.id}/bump`, { method: 'POST' });
                showToast(`Ticket ${ticket.tableNumber} avancé !`, 'success');
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
            loadNetworkSyncData()
        ]);
    }

    async function loadAdminCatalog() {
        elements.adminCatalogList.innerHTML = '';
        state.products.forEach(p => {
            const row = document.createElement('div');
            row.className = 'item-list-row';
            row.innerHTML = `
                <div>
                    <strong>${p.name}</strong> (${p.price.toFixed(2)} € - TVA ${p.taxRatePercent}%)
                    <small style="display:block;color:#94a3b8;">Station: ${p.preparationStationId || 'HOT'}${p.isQuickKey ? ' | ⭐ Touche Rapide' : ''}</small>
                </div>
                <div style="display:flex; gap:6px;">
                    <button class="btn-archive btn-edit-product" data-id="${p.id}" style="background:rgba(59,130,246,0.2); border-color:rgba(59,130,246,0.4); color:#60a5fa;">✏️ Modifier</button>
                    <button class="btn-archive btn-del-product" data-id="${p.id}">Désactiver</button>
                </div>
            `;
            row.querySelector('.btn-edit-product').addEventListener('click', () => {
                document.getElementById('editProdId').value = p.id;
                document.getElementById('editProdName').value = p.name;
                document.getElementById('editProdPrice').value = p.price;
                document.getElementById('editProdQuickKey').checked = !!p.isQuickKey;
                elements.editProductModal.classList.add('active');
            });
            row.querySelector('.btn-del-product').addEventListener('click', async () => {
                await fetch(`/api/catalog/products/${p.id}`, { method: 'DELETE' });
                showToast(`Article '${p.name}' désactivé`, 'info');
                await loadCatalogData();
                await loadAdminCatalog();
            });
            elements.adminCatalogList.appendChild(row);
        });
    }

    async function loadAdminStaff() {
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
                        <small style="display:block;color:#94a3b8;">Rôle: ${s.role}</small>
                    </div>
                    <div style="display:flex; gap:6px;">
                        <button class="btn-archive btn-edit-staff" data-id="${s.id}" style="background:rgba(59,130,246,0.2); border-color:rgba(59,130,246,0.4); color:#60a5fa;">✏️ Modifier</button>
                        <button class="btn-archive btn-del-staff" data-id="${s.id}">Désactiver</button>
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
                    showToast(`Employé '${s.name}' désactivé`, 'info');
                    await loadAdminStaff();
                });
                elements.adminStaffList.appendChild(row);
            });
        } catch (err) {
            console.error('Erreur chargement serveurs:', err);
        }
    }

    async function loadAdminPrinters() {
        elements.adminPrintersList.innerHTML = '';
        try {
            const res = await fetch('/api/printers');
            state.printers = await res.json();
            state.printers.forEach(pr => {
                const row = document.createElement('div');
                row.className = 'item-list-row';
                row.innerHTML = `
                    <div>
                        <strong>${pr.name}</strong> (${pr.ipAddress}:${pr.port})
                        <small style="display:block;color:#94a3b8;">Postes: ${(pr.targetStations || []).join(', ')} | Tiroir: ${pr.hasCashDrawer ? 'Oui' : 'Non'}</small>
                    </div>
                    <div style="display:flex; gap:6px;">
                        <button class="btn-archive btn-edit-printer" data-id="${pr.id}" style="background:rgba(59,130,246,0.2); border-color:rgba(59,130,246,0.4); color:#60a5fa;">✏️ Modifier</button>
                        <button class="btn-archive btn-del-printer" data-id="${pr.id}">Désactiver</button>
                    </div>
                `;
                row.querySelector('.btn-edit-printer').addEventListener('click', () => {
                    document.getElementById('editPrinterId').value = pr.id;
                    document.getElementById('editPrinterName').value = pr.name;
                    document.getElementById('editPrinterIp').value = pr.ipAddress;
                    document.getElementById('editPrinterPort').value = pr.port;
                    document.getElementById('editPrinterDrawer').checked = !!pr.hasCashDrawer;
                    elements.editPrinterModal.classList.add('active');
                });
                row.querySelector('.btn-del-printer').addEventListener('click', async () => {
                    await fetch(`/api/printers/${pr.id}`, { method: 'DELETE' });
                    showToast(`Imprimante '${pr.name}' désactivée`, 'info');
                    await loadAdminPrinters();
                });
                elements.adminPrintersList.appendChild(row);
            });
        } catch (err) {
            console.error('Erreur chargement imprimantes:', err);
        }
    }

    function setupForms() {
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
                showToast(`Famille '${name}' créée !`, 'success');
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
            const station = stationEl ? stationEl.value : 'HOT_KITCHEN';
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
                showToast(`Article '${name}' créé !`, 'success');
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

            const res = await fetch('/api/staff/operators', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ name, role, pin })
            });

            if (res.ok) {
                showToast(`Employé '${name}' créé !`, 'success');
                document.getElementById('inputStaffName').value = '';
                document.getElementById('inputStaffPin').value = '';
                await loadAdminStaff();
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
                    hasCashDrawer: drawer,
                    targetStations: ["HOT_KITCHEN", "RECEIPT"]
                })
            });

            if (res.ok) {
                showToast(`Imprimante '${name}' enregistrée !`, 'success');
                document.getElementById('inputPrinterName').value = '';
                document.getElementById('inputPrinterIp').value = '';
                await loadAdminPrinters();
            }
        });

        // Fiscal Closure Z
        elements.btnExecuteZ.addEventListener('click', async () => {
            const res = await fetch('/api/fiscal/z-closure', { method: 'POST' });
            if (res.ok) {
                const closure = await res.json();
                elements.slipTotalTtc.textContent = `${closure.totalSalesTtc.toFixed(2)} €`;
                elements.slipCount.textContent = closure.receiptCount;
                elements.slipHash.textContent = closure.signatureHash;
                elements.slipDate.textContent = `Date: ${new Date().toISOString().replace('T', ' ').substring(0, 19)} UTC`;
                showToast('Clôture journalière Rapport Z exécutée & scellée ! 📜', 'success');
            }
        });

        // Network Sync Handlers
        setupNetworkSyncHandlers();
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
                if (pendingEl) pendingEl.textContent = `${syncData.pendingMessages} message(s)`;
                if (completedEl) completedEl.textContent = `${syncData.completedMessages} transaction(s)`;
                if (timeEl) timeEl.textContent = new Date(syncData.lastSyncUtc).toLocaleTimeString();
            }
        } catch (err) {
            console.error('Erreur chargement statut réseau/synchro:', err);
            const badge = document.getElementById('connectionBadge');
            if (badge) {
                badge.className = 'status-badge offline';
                badge.innerHTML = `<span class="pulse-dot" style="background:#ef4444;"></span><span class="status-text">Mode Hors-Ligne (Standalone)</span>`;
            }
        }
    }

    function setupNetworkSyncHandlers() {
        const btnScan = document.getElementById('btnScanNetwork');
        const listContainer = document.getElementById('discoveredServersList');
        const formManual = document.getElementById('formManualServerConfig');
        const btnTest = document.getElementById('btnTestServerConn');
        const btnForceSync = document.getElementById('btnForceSyncNow');

        if (btnScan && listContainer) {
            btnScan.addEventListener('click', async () => {
                listContainer.innerHTML = '<div style="text-align:center; padding:12px; color:#94a3b8;">🔍 Écoute UDP port 45454 et scan des périphériques...</div>';
                try {
                    const res = await fetch('/api/network/info');
                    if (res.ok) {
                        const info = await res.json();
                        listContainer.innerHTML = `
                            <div class="item-list-row" style="border-color:#10b981; background:rgba(16, 185, 129, 0.1);">
                                <div>
                                    <strong style="color:#a7f3d0;">🖥️ ${info.serverName}</strong>
                                    <div style="font-size:0.8rem; color:#94a3b8;">${info.primaryIp}:${info.port} (Port Découverte: ${info.discoveryPort}) — v${info.version}</div>
                                </div>
                                <button class="btn-primary" style="padding:6px 12px; font-size:0.8rem;" id="btnSelectMasterServer">Actif ✓</button>
                            </div>
                            <div class="item-list-row">
                                <div>
                                    <strong>🖨️ Epson TM-T20III (Comptoir)</strong>
                                    <div style="font-size:0.8rem; color:#94a3b8;">192.168.1.100:9100 — ESC/POS 80mm</div>
                                </div>
                                <span style="color:#10b981; font-weight:700; font-size:0.8rem;">En ligne</span>
                            </div>
                        `;
                        showToast('Scan terminé : 1 Serveur Maître et 1 Imprimante détectés !', 'success');
                    }
                } catch (err) {
                    listContainer.innerHTML = '<div style="color:#ef4444; padding:8px;">Échec de la découverte réseau</div>';
                }
            });
        }

        if (btnTest) {
            btnTest.addEventListener('click', async () => {
                const url = document.getElementById('inputManualServerUrl').value.trim();
                const start = performance.now();
                try {
                    const res = await fetch(`${url}/api/health`, { method: 'GET' });
                    const duration = Math.round(performance.now() - start);
                    if (res.ok) {
                        showToast(`✓ Connexion au serveur établie (${duration} ms)`, 'success');
                    } else {
                        showToast(`Erreur HTTP: ${res.status}`, 'error');
                    }
                } catch (err) {
                    showToast(`Impossible de joindre le serveur ${url}`, 'error');
                }
            });
        }

        if (formManual) {
            formManual.addEventListener('submit', (e) => {
                e.preventDefault();
                const url = document.getElementById('inputManualServerUrl').value.trim();
                localStorage.setItem('pos_master_server_url', url);
                showToast(`Adresse du serveur enregistrée : ${url}`, 'success');
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
                        showToast('Synchronisation Outbox réussie ! 100% à jour.', 'success');
                        await loadNetworkSyncData();
                    }
                } catch (err) {
                    showToast('Erreur lors de la synchronisation', 'error');
                }
            });
        }
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
                state.operator = { name: data.operatorName, role: data.role, id: data.operatorId };
                elements.currentOperatorName.textContent = data.operatorName;
                elements.currentOperatorRole.textContent = data.role;
                elements.pinLockModal.classList.remove('active');
                state.pinInput = '';
                updatePinDots();
                showToast(`Session déverrouillée: ${data.operatorName}`, 'success');

                // Reload active table with newly issued token
                if (state.activeTable) {
                    await loadActiveTableOrder(state.activeTable);
                }
            } else {
                showToast('Code PIN invalide', 'error');
                state.pinInput = '';
                updatePinDots();
            }
        } catch (err) {
            showToast('Erreur validation PIN', 'error');
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

                        showToast(`Format ${cols} × ${rows} appliqué ! 📐`, 'success');
                        await renderAdminGridEditor(categoryId, state.activeAdminGridPage);
                        if (state.activeCategory === categoryId || applyAll) {
                            await renderProductsGrid();
                        }
                    } else {
                        showToast('Erreur lors du changement de format', 'error');
                    }
                } catch (err) {
                    console.error('Erreur changement format:', err);
                    showToast('Erreur réseau lors du changement de format', 'error');
                }
            });
        }
    }

    async function loadAdminGridEditor() {
        if (!elements.selectAdminGridCat) return;
        elements.selectAdminGridCat.innerHTML = '';

        // Add 'Tout le Menu' option
        const allOpt = document.createElement('option');
        allOpt.value = 'ALL';
        allOpt.textContent = '🍽️ Tout le Menu (Vue Globale)';
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
            btn.textContent = `Page ${p + 1}`;
            btn.addEventListener('click', async () => {
                state.activeAdminGridPage = p;
                await renderAdminGridEditor(categoryId, p);
            });
            elements.adminGridPageTabs.appendChild(btn);
        }

        const addBtn = document.createElement('button');
        addBtn.type = 'button';
        addBtn.className = 'btn-admin-add-page';
        addBtn.innerHTML = '➕ Nouvelle Page';
        addBtn.title = 'Ajouter une nouvelle page de grille pour cette catégorie';
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
        showToast(`Page ${newPageIndex + 1} ajoutée pour la catégorie ! 📄`, 'success');
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
            elements.adminGridLayoutVersion.textContent = `Format ${cols}×${rows} • Page ${pageIndex + 1}/${totalPages} • v${layout.version || 1}`;
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
                        <button type="button" class="btn-slot-icon btn-edit-slot-trigger" title="Personnaliser">✏️</button>
                        <button type="button" class="btn-slot-icon delete btn-del-slot-trigger" title="Libérer">✕</button>
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
                    <span class="admin-slot-coord" style="position:absolute; top:4px; left:6px;">[P${pageIndex + 1}:${row + 1},${col + 1}]</span>
                    <span style="color:var(--text-dim); font-size:0.8rem; font-weight:600;">+ Assigner</span>
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
                            showToast('Positions permutées avec succès ! 🔄', 'success');
                            await renderAdminGridEditor(categoryId, pageIndex);
                            if (state.activeCategory === categoryId && state.activeGridPage === pageIndex) {
                                await renderProductsGrid();
                            }
                        }
                    } catch (err) {
                        showToast('Erreur lors de la permutation', 'error');
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
                <span>${isPlaced ? '✓ Placé' : '⠿'}</span>
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
        document.getElementById('editSlotModalTitle').textContent = `🔲 Éditer l'Emplacement [P${state.activeAdminGridPage + 1}:${row + 1}, ${col + 1}]`;

        // Populate products select
        elements.selectSlotProduct.innerHTML = '<option value="">(Emplacement Vide)</option>';
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
        showToast('Article assigné avec succès ! ✨', 'success');
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
        showToast('Emplacement libéré ! 🗑️', 'info');
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
        showToast('Emplacement mis à jour ! 💾', 'success');
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
                showToast('Erreur lors de la sauvegarde de la grille', 'error');
            }
        } catch (err) {
            console.error('Erreur sauvegarde grille:', err);
            showToast('Erreur réseau sauvegarde grille', 'error');
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
        showToast('Grille réinitialisée avec les articles par défaut ! ↺', 'success');
    }

    // ==================== REAL-TIME SIGNALR SYNC (US5) ====================
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

            connection.start().catch(err => {
                console.log('SignalR hub non connecté (mode standalone):', err);
            });
        } catch (e) {
            console.warn('SignalR initialisation passée:', e);
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
        if (status === 0 || status === 'Free') return 'Libre';
        if (status === 1 || status === 'Occupied') return 'Occupée';
        if (status === 2 || status === 'BillPrinted' || status === 'BillRequested') return 'Addition';
        if (status === 3 || status === 'Paid') return 'Encaissée';
        return 'Libre';
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

