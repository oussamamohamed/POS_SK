import Foundation

/// Backend en mémoire qui reproduit le comportement de `RestaurantPos.Api` (données d'amorçage
/// comprises). Utilisé par les tests unitaires, les tests UI (`-UITestMode`) et les aperçus SwiftUI :
/// les tests sont ainsi déterministes et ne dépendent ni du réseau ni d'une base de données.
public actor InMemoryPosAPI: PosAPI {

    // MARK: État

    struct StaffRecord { var member: StaffMember; var pin: String }
    struct OrderRecord {
        var order: ActiveOrder
        var tipCents = 0
        var paidCents = 0
    }

    private var staffRecords: [StaffRecord]
    private var categoriesStore: [MenuCategory]
    private var productsStore: [Product]
    private var tablesStore: [DiningTable]
    private var orders: [UUID: OrderRecord] = [:]
    private var tickets: [KitchenTicket] = []
    private var held: [HeldOrder] = []
    private var rooms: [HotelRoom]
    private var printersStore: [Printer]
    private var layouts: [String: TouchGridLayout] = [:]
    private var schedules: [HappyHourSchedule]
    private var overrideUntil: Date?
    private var receipts: [(terminal: String, ttc: Int, ht: Int, vat: [String: Int], method: PaymentMethod)] = []
    private var closures: [FiscalReport] = []
    private var pickupCounter = 0
    private var receiptCounter = 0
    private var token: String?
    private var failedLogins = 0

    /// Journal des appels (utile pour vérifier dans les tests qu'un endpoint a bien été sollicité).
    public private(set) var calls: [String] = []
    /// Latence artificielle pour simuler le réseau dans les aperçus.
    public var latency: Duration = .zero
    /// Si défini, chaque appel échoue avec cette erreur (tests de résilience).
    public var failure: APIError?

    public init(seed: Bool = true) {
        let seeds = Seed.make()
        staffRecords = seeds.staff
        categoriesStore = seeds.categories
        productsStore = seeds.products
        tablesStore = seeds.tables
        rooms = seeds.rooms
        printersStore = seeds.printers
        schedules = seeds.schedules
        if !seed {
            productsStore = []
            categoriesStore = []
        }
    }

    public func setFailure(_ error: APIError?) { failure = error }
    public func setLatency(_ value: Duration) { latency = value }
    public func forceHappyHour(minutes: Int) { overrideUntil = Date().addingTimeInterval(TimeInterval(minutes * 60)) }

    private func step(_ name: String) async throws {
        calls.append(name)
        if latency > .zero { try? await Task.sleep(for: latency) }
        if let failure { throw failure }
    }

    public func setToken(_ token: String?) { self.token = token }

    // MARK: Auth

    public func login(pin: String) async throws -> LoginResponse {
        try await step("login")
        guard let record = staffRecords.first(where: { $0.pin == pin && $0.member.isActive }) else {
            failedLogins += 1
            return LoginResponse(success: false, operatorId: nil, operatorName: nil, role: nil, token: nil, errorMessage: "Code PIN ou identifiants incorrects")
        }
        failedLogins = 0
        token = "mock-\(record.member.id)"
        return LoginResponse(success: true, operatorId: record.member.id, operatorName: record.member.name, role: record.member.role, token: token)
    }

    private func requireAuth() throws { if token == nil { throw APIError.unauthorized } }

    private func currentRole() -> UserRole? {
        guard let token, let id = UUID(uuidString: token.replacingOccurrences(of: "mock-", with: "")) else { return nil }
        return staffRecords.first { $0.member.id == id }?.member.role
    }

    private func requireManager() throws {
        try requireAuth()
        guard currentRole()?.isManager == true else { throw APIError.forbidden("Action réservée à un responsable.") }
    }

    private func supervisor(pin: String) -> StaffRecord? {
        staffRecords.first { $0.pin == pin && $0.member.role.isManager && $0.member.isActive }
    }

    // MARK: Catalogue

    public func categories() async throws -> [MenuCategory] { try await step("categories"); return categoriesStore.filter { $0.isActive != false } }
    public func products() async throws -> [Product] { try await step("products"); return productsStore.filter { $0.isActive != false } }

    public func createCategory(name: String, colorHex: String, displayOrder: Int) async throws {
        try await step("createCategory"); try requireManager()
        categoriesStore.append(MenuCategory(id: "CAT_\(UUID().uuidString.prefix(8))", name: name, iconName: "utensils", colorHex: colorHex, displayOrder: displayOrder))
    }

    public func updateCategory(id: String, name: String, colorHex: String, displayOrder: Int) async throws {
        try await step("updateCategory"); try requireManager()
        guard let i = categoriesStore.firstIndex(where: { $0.id == id }) else { throw APIError.notFound(nil) }
        categoriesStore[i].name = name
        categoriesStore[i].colorHex = colorHex
    }

    public func createProduct(_ d: ProductDraft) async throws {
        try await step("createProduct"); try requireManager()
        productsStore.append(Product(name: d.name, categoryId: d.categoryId, description: d.description, price: d.price, taxRatePercent: d.taxRatePercent, colorHex: d.colorHex, displayOrder: d.displayOrder, isQuickKey: d.isQuickKey, preparationStationId: d.stationId))
    }

    public func updateProduct(id: UUID, _ d: ProductDraft) async throws {
        try await step("updateProduct"); try requireManager()
        guard let i = productsStore.firstIndex(where: { $0.id == id }) else { throw APIError.notFound(nil) }
        productsStore[i].name = d.name
        productsStore[i].categoryId = d.categoryId
        productsStore[i].price = d.price
        productsStore[i].taxRatePercent = d.taxRatePercent
        productsStore[i].isQuickKey = d.isQuickKey
        productsStore[i].preparationStationId = d.stationId
        productsStore[i].colorHex = d.colorHex
    }

    public func archiveProduct(id: UUID) async throws {
        try await step("archiveProduct"); try requireManager()
        guard let i = productsStore.firstIndex(where: { $0.id == id }) else { throw APIError.notFound(nil) }
        productsStore[i].isActive = false
    }

    // MARK: Grille

    private func layoutKey(_ category: String, _ page: Int) -> String { "\(category)#\(page)" }

    private func pagesCount(_ category: String) -> Int {
        max(1, (layouts.values.filter { $0.categoryId == category }.map(\.pageIndex).max() ?? 0) + 1)
    }

    private func withTotals(_ layout: TouchGridLayout) -> TouchGridLayout {
        var l = layout
        l.totalPages = pagesCount(layout.categoryId)
        l.slots = l.slots.map { slot in
            var s = slot
            s.gridLayoutId = l.id
            s.slotIndex = slot.rowIndex * l.columnsCount + slot.columnIndex
            if let pid = slot.productId, let p = productsStore.first(where: { $0.id == pid }) {
                s.product = ProductSummary(id: p.id, name: p.name, price: p.price, preparationStationId: p.preparationStationId, colorHex: p.colorHex)
            } else {
                s.product = nil
            }
            return s
        }
        return l
    }

    public func gridLayout(categoryId: String, page: Int) async throws -> TouchGridLayout {
        try await step("gridLayout")
        if let layout = layouts[layoutKey(categoryId, page)] { return withTotals(layout) }
        guard page == 0 else { throw APIError.notFound("Aucune grille") }
        // Comme le serveur : génération automatique d'une grille 4×4 à la première lecture.
        let products = categoryId == "ALL" ? productsStore.filter { $0.isActive != false } : productsStore.filter { $0.categoryId == categoryId && $0.isActive != false }
        let slots = (0..<16).map { i in
            GridSlot(productId: products[safe: i]?.id, rowIndex: i / 4, columnIndex: i % 4)
        }
        let layout = TouchGridLayout(categoryId: categoryId, name: categoryId, slots: slots)
        layouts[layoutKey(categoryId, 0)] = layout
        return withTotals(layout)
    }

    public func saveGridLayout(_ request: UpdateGridLayoutRequest) async throws -> TouchGridLayout {
        try await step("saveGridLayout")
        let key = layoutKey(request.categoryId, request.pageIndex)
        var layout = layouts[key] ?? TouchGridLayout(categoryId: request.categoryId, pageIndex: request.pageIndex)
        layout.columnsCount = request.columnsCount
        layout.rowsCount = request.rowsCount
        layout.version = (layout.version ?? 0) + 1
        layout.slots = request.slots.map { GridSlot(productId: $0.productId, rowIndex: $0.rowIndex, columnIndex: $0.columnIndex, customLabel: $0.customLabel, customColorHex: $0.customColorHex) }
        layouts[key] = layout
        return withTotals(layout)
    }

    public func swapGridSlots(layoutId: UUID, from: GridPosition, to: GridPosition) async throws -> TouchGridLayout {
        try await step("swapGridSlots")
        guard let key = layouts.first(where: { $0.value.id == layoutId })?.key, var layout = layouts[key] else { throw APIError.notFound("Grille introuvable") }
        let a = layout.slots.firstIndex { $0.rowIndex == from.row && $0.columnIndex == from.column }
        let b = layout.slots.firstIndex { $0.rowIndex == to.row && $0.columnIndex == to.column }
        switch (a, b) {
        case let (a?, b?):
            (layout.slots[a].rowIndex, layout.slots[b].rowIndex) = (layout.slots[b].rowIndex, layout.slots[a].rowIndex)
            (layout.slots[a].columnIndex, layout.slots[b].columnIndex) = (layout.slots[b].columnIndex, layout.slots[a].columnIndex)
        case let (a?, nil):
            layout.slots[a].rowIndex = to.row
            layout.slots[a].columnIndex = to.column
        default:
            break
        }
        layout.version = (layout.version ?? 0) + 1
        layouts[key] = layout
        return withTotals(layout)
    }

    public func updateGridDimensions(categoryId: String, columns: Int, rows: Int, applyToAll: Bool) async throws -> [TouchGridLayout] {
        try await step("updateGridDimensions")
        var updated: [TouchGridLayout] = []
        for (key, var layout) in layouts where applyToAll || layout.categoryId == categoryId {
            layout.columnsCount = columns
            layout.rowsCount = rows
            layout.slots.removeAll { $0.rowIndex >= rows || $0.columnIndex >= columns }
            layouts[key] = layout
            updated.append(withTotals(layout))
        }
        return updated
    }

    // MARK: Salle & commandes

    private func tableIndex(_ number: String) -> Int? { tablesStore.firstIndex { $0.tableNumber == number } }

    private func refreshTableTotals() {
        for i in tablesStore.indices {
            if let id = tablesStore[i].activeOrderId, let record = orders[id] {
                tablesStore[i].activeOrderTotalTtc = totals(of: record.order).totalTtc
            } else {
                tablesStore[i].activeOrderTotalTtc = .zero
            }
        }
    }

    private func totals(of order: ActiveOrder) -> OrderTotals {
        OrderMath.totals(lines: order.lines.map(CartLine.init(serverLine:)), destination: order.destination, discount: order.globalDiscount)
    }

    public func tables() async throws -> [DiningTable] {
        try await step("tables")
        refreshTableTotals()
        return tablesStore
    }

    public func createTable(number: String, capacity: Int) async throws {
        try await step("createTable"); try requireAuth()
        guard tableIndex(number) == nil else { throw APIError.server(status: 400, message: "Table existante") }
        tablesStore.append(DiningTable(tableNumber: number, capacity: capacity))
    }

    public func openTable(number: String, covers: Int, operatorId: UUID?, waiterName: String?) async throws {
        try await step("openTable"); try requireAuth()
        guard let i = tableIndex(number) else { throw APIError.notFound(nil) }
        tablesStore[i].coversCount = covers
        tablesStore[i].assignedWaiterName = waiterName
        tablesStore[i].status = .occupied
        tablesStore[i].openedAtUtc = tablesStore[i].openedAtUtc ?? Date()
        if let id = tablesStore[i].activeOrderId { orders[id]?.order.coversCount = covers }
    }

    public func activeOrder(table: String) async throws -> ActiveOrder? {
        try await step("activeOrder"); try requireAuth()
        guard let i = tableIndex(table), let id = tablesStore[i].activeOrderId else { return nil }
        return decorated(orders[id]?.order)
    }

    private func decorated(_ order: ActiveOrder?) -> ActiveOrder? {
        guard var order else { return nil }
        order.totalTtcAmount = totals(of: order).totalTtc
        return order
    }

    private func ensureOrder(table: String) -> UUID {
        if tableIndex(table) == nil {
            tablesStore.append(DiningTable(tableNumber: table, capacity: table == DiningTable.counterNumber ? 1 : 2))
        }
        let i = tableIndex(table)!
        if let id = tablesStore[i].activeOrderId, orders[id] != nil { return id }
        let order = ActiveOrder(tableNumber: table, waiterName: tablesStore[i].assignedWaiterName, coversCount: tablesStore[i].coversCount, destination: .takeaway)
        orders[order.orderId] = OrderRecord(order: order)
        tablesStore[i].activeOrderId = order.orderId
        tablesStore[i].status = .occupied
        tablesStore[i].openedAtUtc = tablesStore[i].openedAtUtc ?? Date()
        return order.orderId
    }

    public func addItems(table: String, items: [OrderItemInput]) async throws -> ActiveOrder {
        try await step("addItems"); try requireAuth()
        let id = ensureOrder(table: table)
        var order = orders[id]!.order
        for input in items {
            if let j = order.lines.firstIndex(where: { !$0.isDispatched && $0.productId == input.productId && $0.course == input.course && $0.unitPrice == input.unitPrice && $0.isHappyHourApplied == input.isHappyHourApplied && $0.modifiersPriceExtra == input.modifiersPriceExtra && $0.modifiersSummary == input.modifiers }) {
                order.lines[j].quantity += input.quantity
            } else {
                order.lines.append(OrderLine(productId: input.productId, productName: input.productName, quantity: input.quantity, unitPrice: input.unitPrice, taxRatePercent: input.taxRatePercent, preparationStationId: input.preparationStationId, modifiersSummary: input.modifiers, course: input.course, modifiersPriceExtra: input.modifiersPriceExtra, taxRateTakeawayPercent: input.taxRateTakeawayPercent, isHappyHourApplied: input.isHappyHourApplied, originalUnitPrice: input.originalUnitPrice, appliedHappyHourScheduleId: input.appliedHappyHourScheduleId))
            }
        }
        orders[id]!.order = order
        return decorated(order)!
    }

    public func dispatch(table: String) async throws {
        try await step("dispatch"); try requireAuth()
        guard let i = tableIndex(table), let id = tablesStore[i].activeOrderId, var record = orders[id] else { throw APIError.notFound(nil) }
        let pending = record.order.lines.filter { !$0.isDispatched }
        let prefix = record.order.destination == .takeaway ? "[À EMPORTER] " : ""
        for (station, lines) in Dictionary(grouping: pending, by: { $0.preparationStationId ?? "HOT_KITCHEN" }) {
            tickets.append(KitchenTicket(orderId: id, tableNumber: prefix + table, serverName: record.order.waiterName, coversCount: record.order.coversCount, stationId: station, items: lines.map {
                KitchenTicketItem(productName: $0.productName, quantity: $0.quantity, modifiersSummary: $0.modifiersSummary.isEmpty ? nil : $0.modifiersSummary.joined(separator: ", "))
            }))
        }
        for j in record.order.lines.indices { record.order.lines[j].isDispatched = true }
        orders[id] = record
    }

    public func fireSuite(table: String) async throws {
        try await step("fireSuite"); try requireAuth()
        guard let i = tableIndex(table), let id = tablesStore[i].activeOrderId, let record = orders[id] else { return }
        let suite = record.order.lines.filter { $0.course == .suite }
        tickets.append(KitchenTicket(orderId: id, tableNumber: table, stationId: "HOT_KITCHEN", items: suite.map { KitchenTicketItem(productName: "[RÉCLAME SUITE] \($0.productName)", quantity: $0.quantity) }))
    }

    public func transfer(from: String, to: String, merge: Bool) async throws -> OperationResult {
        try await step(merge ? "merge" : "transfer"); try requireAuth()
        guard let s = tableIndex(from), let sourceId = tablesStore[s].activeOrderId, let t = tableIndex(to) else {
            return OperationResult(success: false, message: "Échec du transfert de table.")
        }
        if let targetId = tablesStore[t].activeOrderId, var target = orders[targetId] {
            target.order.lines += orders[sourceId]!.order.lines
            orders[targetId] = target
            orders[sourceId] = nil
        } else {
            orders[sourceId]!.order.tableNumber = to
            tablesStore[t].activeOrderId = sourceId
            tablesStore[t].status = .occupied
            tablesStore[t].coversCount = tablesStore[s].coversCount
            tablesStore[t].openedAtUtc = tablesStore[s].openedAtUtc
        }
        tablesStore[s].activeOrderId = nil
        tablesStore[s].status = .free
        tablesStore[s].coversCount = 0
        tablesStore[s].openedAtUtc = nil
        return OperationResult(success: true, message: merge ? "Tables \(from) et \(to) fusionnées" : "Commande transférée de \(from) vers \(to)")
    }

    public func setDestination(orderId: UUID, destination: OrderDestination) async throws {
        try await step("setDestination")
        orders[orderId]?.order.destination = destination
    }

    public func applyDiscount(orderId: UUID, type: DiscountType, value: Decimal, reason: String, operatorId: UUID?) async throws {
        try await step("applyDiscount"); try requireAuth()
        guard orders[orderId] != nil else { throw APIError.server(status: 400, message: "Opération de remise échouée.") }
        orders[orderId]!.order.globalDiscountType = type
        orders[orderId]!.order.globalDiscountValue = value
        orders[orderId]!.order.globalDiscountReason = reason
    }

    public func removeDiscount(orderId: UUID) async throws {
        try await step("removeDiscount")
        orders[orderId]?.order.globalDiscountType = nil
        orders[orderId]?.order.globalDiscountValue = 0
        orders[orderId]?.order.globalDiscountReason = nil
    }

    public func compItem(orderId: UUID, lineId: UUID, reason: String, operatorId: UUID?) async throws {
        try await step("compItem"); try requireAuth()
        guard let j = orders[orderId]?.order.lines.firstIndex(where: { $0.lineId == lineId }) else { throw APIError.server(status: 400, message: "Opération de gratuité échouée.") }
        orders[orderId]!.order.lines[j].isComp = true
    }

    // MARK: Encaissement

    private func record(receiptFor order: ActiveOrder, terminal: String, paidCents: Int, dueCents: Int, method: PaymentMethod) -> String {
        receiptCounter += 1
        let t = totals(of: order)
        let ratio = dueCents > 0 && paidCents < dueCents ? Decimal(paidCents) / Decimal(dueCents) : 1
        var vat: [String: Int] = [:]
        for line in t.vatLines { vat["\(line.ratePercent)", default: 0] += Money.roundAwayFromZero(Decimal(line.vat.cents) * ratio) }
        let ttc = ratio == 1 ? t.totalTtc.cents : paidCents
        let ht = Money.roundAwayFromZero(Decimal(t.totalHt.cents) * ratio)
        receipts.append((terminal, ttc, ht, vat, method))
        return "\(terminal)-\(String(format: "%06d", receiptCounter))"
    }

    private func settle(orderId: UUID, terminal: String, tenders: [(PaymentMethod, Int, Int)]) throws -> (receipt: String, paid: Int, change: Int, remaining: Int) {
        guard var record = orders[orderId] else { throw APIError.server(status: 400, message: "Commande introuvable pour ce règlement.") }
        let due = totals(of: record.order).totalTtc.cents + record.tipCents
        let paid = tenders.reduce(0) { $0 + $1.1 }
        let tendered = tenders.reduce(0) { $0 + $1.2 }
        let remainingBefore = max(0, due - record.paidCents)
        let change = max(0, tendered - remainingBefore)
        let remaining = max(0, due - (record.paidCents + paid))
        let receipt = self.record(receiptFor: record.order, terminal: terminal, paidCents: paid, dueCents: due, method: tenders.first?.0 ?? .cash)
        record.paidCents += paid
        orders[orderId] = record
        if remaining == 0, let i = tablesStore.firstIndex(where: { $0.activeOrderId == orderId }) {
            tablesStore[i].activeOrderId = nil
            tablesStore[i].status = .free
            tablesStore[i].coversCount = 0
            tablesStore[i].openedAtUtc = nil
            tablesStore[i].assignedWaiterName = nil
        }
        return (receipt, paid, change, remaining)
    }

    public func pay(_ request: PaymentRequest) async throws -> PaymentResult {
        try await step("pay"); try requireAuth()
        var id = request.orderId
        if id == nil || orders[id!] == nil, let i = tableIndex(request.tableNumber) { id = tablesStore[i].activeOrderId }
        guard let orderId = id else { throw APIError.server(status: 400, message: "Commande introuvable pour ce règlement.") }
        let result = try settle(orderId: orderId, terminal: request.terminalId, tenders: request.tenders.map { ($0.method, $0.amount.cents, $0.tendered.cents) })
        return PaymentResult(receiptNumber: result.receipt, totalPaid: Money(cents: result.paid), changeGiven: Money(cents: result.change), remainingBalance: Money(cents: result.remaining), fiscalSignature: String(repeating: "A", count: 64))
    }

    public func hotelRooms() async throws -> [HotelRoom] { try await step("hotelRooms"); return rooms }

    public func chargeRoom(_ request: RoomChargeRequest) async throws -> OperationResult {
        try await step("chargeRoom"); try requireAuth()
        guard let r = rooms.firstIndex(where: { $0.roomNumber == request.roomNumber }) else { return OperationResult(success: false, message: "Facturation chambre échouée.") }
        let total = request.amount + request.tipAmount
        guard total <= rooms[r].availableCredit else { return OperationResult(success: false, message: "Plafond de crédit chambre dépassé.") }
        rooms[r].currentBalance += total
        if let i = tableIndex(request.tableNumber) {
            if let id = tablesStore[i].activeOrderId { orders[id] = nil }
            tablesStore[i].activeOrderId = nil
            tablesStore[i].status = .free
            tablesStore[i].coversCount = 0
        }
        return OperationResult(success: true, message: "Facturation de \(total.formatted) enregistrée sur la chambre \(request.roomNumber) (\(request.guestName))")
    }

    // MARK: Comptoir

    public func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder {
        try await step("openCounterOrder"); try requireAuth()
        let existed = tableIndex(DiningTable.counterNumber).flatMap { tablesStore[$0].activeOrderId } != nil
        let id = ensureOrder(table: DiningTable.counterNumber)
        if !existed { orders[id]!.order.destination = destination }
        return decorated(orders[id]!.order)!
    }

    public func holdOrder(orderId: UUID, terminalId: String, label: String) async throws {
        try await step("holdOrder"); try requireAuth()
        guard let record = orders[orderId], !record.order.lines.isEmpty else { throw APIError.server(status: 400, message: "Impossible de mettre en attente un panier vide.") }
        held.append(HeldOrder(terminalId: terminalId, orderId: orderId, customerLabel: label, destination: record.order.destination, itemCount: record.order.lines.reduce(0) { $0 + $1.quantity }, totalTtc: totals(of: record.order).totalTtc))
        if let i = tablesStore.firstIndex(where: { $0.activeOrderId == orderId }) {
            tablesStore[i].activeOrderId = nil
            tablesStore[i].status = .free
        }
    }

    public func heldOrders(terminalId: String) async throws -> [HeldOrder] {
        try await step("heldOrders"); try requireAuth()
        return held.filter { $0.terminalId == terminalId }
    }

    public func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder {
        try await step("recallHeldOrder"); try requireAuth()
        guard let h = held.firstIndex(where: { $0.holdId == holdId }) else { throw APIError.notFound("Commande en attente introuvable ou déjà rappelée.") }
        let orderId = held[h].orderId
        held.remove(at: h)
        if tableIndex(DiningTable.counterNumber) == nil { tablesStore.append(DiningTable(tableNumber: DiningTable.counterNumber, capacity: 1)) }
        let i = tableIndex(DiningTable.counterNumber)!
        tablesStore[i].activeOrderId = orderId
        tablesStore[i].status = .occupied
        return decorated(orders[orderId]!.order)!
    }

    public func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws {
        try await step("voidHeldOrder"); try requireAuth()
        guard supervisor(pin: supervisorPin) != nil else { throw APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.") }
        guard let h = held.firstIndex(where: { $0.holdId == holdId }) else { throw APIError.notFound(nil) }
        orders[held[h].orderId] = nil
        held.remove(at: h)
    }

    public func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult {
        try await step("counterCheckout"); try requireAuth()
        guard var record = orders[request.orderId], !record.order.lines.isEmpty else { throw APIError.server(status: 400, message: "Commande introuvable ou panier vide.") }
        pickupCounter += 1
        let pickup = String(format: "#A-%02d", pickupCounter)
        record.order.destination = request.destination
        record.tipCents = request.tipAmount.cents
        orders[request.orderId] = record
        var voucher: CreditVoucher?
        if let t = request.tenders.first(where: { $0.method == .mealVoucher }) {
            let due = totals(of: record.order).totalTtc + request.tipAmount
            let facial = t.facialValue ?? t.tendered
            if facial > due {
                switch request.mealVoucherPolicy {
                case .strictRejection: throw APIError.server(status: 400, message: "Titre-restaurant supérieur au montant dû : refusé.")
                case .customerCreditVoucher: voucher = CreditVoucher(voucherCode: "CR-TEST0001", amount: facial - due, expiresAtUtc: Date().addingTimeInterval(90 * 86400))
                case .capAtBalance: break
                }
            }
        }
        let result = try settle(orderId: request.orderId, terminal: request.terminalId, tenders: request.tenders.map { ($0.method, $0.amount.cents, $0.method == .mealVoucher ? $0.amount.cents : $0.tendered.cents) })
        return CounterCheckoutResult(orderId: request.orderId, pickupNumber: pickup, totalPaid: Money(cents: result.paid), changeGiven: Money(cents: result.change), remainingBalance: Money(cents: result.remaining), receiptNumber: result.receipt, fiscalSignature: String(repeating: "B", count: 64), issuedCreditVoucher: voucher, openCashDrawer: request.tenders.contains { $0.method == .cash })
    }

    // MARK: Cuisine

    public func kitchenTickets() async throws -> [KitchenTicket] {
        try await step("kitchenTickets")
        return tickets.filter { $0.status != .served }.sorted { ($0.dispatchedAtUtc ?? .distantPast) > ($1.dispatchedAtUtc ?? .distantPast) }
    }

    public func bumpTicket(id: UUID) async throws {
        try await step("bumpTicket"); try requireAuth()
        guard currentRole()?.canBumpKitchen == true else { throw APIError.forbidden("Réservé à la cuisine et aux responsables.") }
        guard let i = tickets.firstIndex(where: { $0.id == id }) else { throw APIError.notFound(nil) }
        tickets[i].status = TicketStatus(rawValue: min(tickets[i].status.rawValue + 1, TicketStatus.served.rawValue)) ?? .served
    }

    // MARK: Fiscal

    private func summary(terminal: String) -> FiscalReport {
        let lastSequenceReceipts = receipts.filter { $0.terminal == terminal }
        var vat: [String: Money] = [:]
        var pay: [String: Money] = [:]
        for r in lastSequenceReceipts {
            for (rate, cents) in r.vat { vat[rate, default: .zero] += Money(cents: cents) }
            pay["\(r.method)".prefix(1).uppercased() + "\(r.method)".dropFirst(), default: .zero] += Money(cents: r.ttc)
        }
        let ttc = lastSequenceReceipts.reduce(0) { $0 + $1.ttc }
        let ht = lastSequenceReceipts.reduce(0) { $0 + $1.ht }
        let perpetual = (closures.last?.perpetualGrandTotal ?? .zero) + Money(cents: ttc)
        return FiscalReport(terminalId: terminal, totalSalesTtc: Money(cents: ttc), totalSalesHt: Money(cents: ht), receiptCount: lastSequenceReceipts.count, vatBreakdown: vat, paymentTotals: pay, perpetualGrandTotal: perpetual, periodEndUtc: Date())
    }

    public func xReport(terminalId: String) async throws -> FiscalReport {
        try await step("xReport"); try requireManager()
        return summary(terminal: terminalId)
    }

    public func latestClosure(terminalId: String) async throws -> FiscalReport? {
        try await step("latestClosure"); try requireManager()
        return closures.last { $0.terminalId == terminalId }
    }

    public func zClosure(terminalId: String, managerId: UUID, managerName: String) async throws -> FiscalReport {
        try await step("zClosure"); try requireManager()
        var report = summary(terminal: terminalId)
        report.closureSequence = closures.filter { $0.terminalId == terminalId }.count + 1
        report.signatureHash = String(format: "%064X", report.totalSalesTtc.cents &* 2654435761 &+ (report.closureSequence ?? 0))
        report.closedAtUtc = Date()
        closures.append(report)
        receipts.removeAll { $0.terminal == terminalId }
        return report
    }

    public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data) {
        try await step("exportFec"); try requireManager()
        let header = "JournalCode|JournalLib|EcritureNum|EcritureDate|CompteNum|CompteLib|Debit|Credit\n"
        return ("\(siren)FEC\(Int(to.timeIntervalSince1970)).txt", Data(header.utf8))
    }

    public func dashboard(from: Date, to: Date) async throws -> FinancialDashboard {
        try await step("dashboard"); try requireManager()
        let ttc = receipts.reduce(0) { $0 + $1.ttc }
        let ht = receipts.reduce(0) { $0 + $1.ht }
        let count = max(1, receipts.count)
        return FinancialDashboard(kpis: .init(totalSalesTtc: Money(cents: ttc), totalSalesHt: Money(cents: ht), averageOrderTtc: Money(cents: ttc / count), averageCoverTtc: Money(cents: ttc / count), totalOrdersCount: receipts.count, totalCoversCount: receipts.count))
    }

    // MARK: Personnel & imprimantes

    public func staff() async throws -> [StaffMember] { try await step("staff"); return staffRecords.map(\.member) }

    public func createStaff(name: String, role: UserRole, pin: String) async throws {
        try await step("createStaff"); try requireManager()
        guard !staffRecords.contains(where: { $0.pin == pin }) else { throw APIError.server(status: 400, message: "Ce code PIN est déjà attribué.") }
        staffRecords.append(StaffRecord(member: StaffMember(name: name, role: role), pin: pin))
    }

    public func updateStaff(id: UUID, name: String, role: UserRole, pin: String?, isActive: Bool) async throws {
        try await step("updateStaff"); try requireManager()
        guard let i = staffRecords.firstIndex(where: { $0.member.id == id }) else { throw APIError.notFound(nil) }
        staffRecords[i].member.name = name
        staffRecords[i].member.role = role
        staffRecords[i].member.isActive = isActive
        if let pin { staffRecords[i].pin = pin }
    }

    public func deactivateStaff(id: UUID) async throws {
        try await step("deactivateStaff"); try requireManager()
        guard let i = staffRecords.firstIndex(where: { $0.member.id == id }) else { throw APIError.notFound(nil) }
        staffRecords[i].member.isActive = false
    }

    public func printers() async throws -> [Printer] { try await step("printers"); return printersStore }

    public func savePrinter(_ printer: Printer, isNew: Bool) async throws {
        try await step("savePrinter"); try requireAuth()
        if isNew {
            printersStore.append(printer)
        } else if let i = printersStore.firstIndex(where: { $0.id == printer.id }) {
            printersStore[i] = printer
        }
    }

    public func testPrinter(id: UUID) async throws -> TestPrintResult {
        try await step("testPrinter")
        guard let p = printersStore.first(where: { $0.id == id }) else { throw APIError.notFound(nil) }
        return TestPrintResult(success: true, message: "Ticket de test envoyé à \(p.name) (\(p.ipAddress):\(p.port))")
    }

    // MARK: Happy Hour

    private var activeOverride: Bool { (overrideUntil ?? .distantPast) > Date() }

    public func happyHourStatus(terminalId: String) async throws -> HappyHourStatus {
        try await step("happyHourStatus")
        guard activeOverride, let schedule = schedules.first else { return .inactive }
        let minutes = Int(ceil(overrideUntil!.timeIntervalSinceNow / 60))
        return HappyHourStatus(isActive: true, isOverride: true, activeScheduleName: schedule.name, activeScheduleId: schedule.id, appliesToTakeaway: schedule.appliesToTakeaway, currentWindow: HappyHourWindow(startTime: nil, endTime: nil, remainingMinutes: minutes))
    }

    public func happyHourPricing(terminalId: String) async throws -> HappyHourPricingTable {
        try await step("happyHourPricing")
        guard activeOverride, let schedule = schedules.first else { return HappyHourPricingTable(isActive: false, items: []) }
        var items: [HappyHourPrice] = []
        for product in productsStore {
            if let rule = schedule.priceRules.first(where: { $0.targetType == .product && $0.targetId.caseInsensitiveCompare(product.id.uuidString) == .orderedSame }) {
                let price = rule.fixedPrice ?? product.price.multiplied(by: 1 - (rule.discountPercent ?? 0) / 100)
                items.append(HappyHourPrice(productId: product.id, productName: product.name, standardPrice: product.price, happyHourPrice: price, ruleType: "FixedPrice"))
            } else if let rule = schedule.priceRules.first(where: { $0.targetType == .category && $0.targetId == product.categoryId }) {
                items.append(HappyHourPrice(productId: product.id, productName: product.name, standardPrice: product.price, happyHourPrice: product.price.multiplied(by: 1 - (rule.discountPercent ?? 0) / 100), ruleType: "CategoryDiscount"))
            }
        }
        return HappyHourPricingTable(isActive: true, items: items)
    }

    public func activateHappyHourOverride(terminalId: String, pin: String, minutes: Int, reason: String) async throws -> OperationResult {
        try await step("activateHappyHourOverride")
        guard let sup = supervisor(pin: pin) else { throw APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.") }
        overrideUntil = max(overrideUntil ?? Date(), Date()).addingTimeInterval(TimeInterval(minutes * 60))
        return OperationResult(success: true, message: "Happy Hour activé/prolongé de \(minutes) minutes par \(sup.member.name).")
    }

    public func stopHappyHourOverride(terminalId: String, pin: String, reason: String) async throws -> OperationResult {
        try await step("stopHappyHourOverride")
        guard supervisor(pin: pin) != nil else { throw APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.") }
        overrideUntil = nil
        return OperationResult(success: true, message: "Dérogation Happy Hour désactivée.")
    }

    public func happyHourSchedules() async throws -> [HappyHourSchedule] { try await step("happyHourSchedules"); return schedules }

    public func createHappyHourSchedule(_ schedule: HappyHourSchedule) async throws -> UUID? {
        try await step("createHappyHourSchedule")
        var s = schedule
        s.id = UUID()
        schedules.append(s)
        return s.id
    }

    public func deleteHappyHourSchedule(id: UUID) async throws {
        try await step("deleteHappyHourSchedule")
        schedules.removeAll { $0.id == id }
    }

    public func applyHappyHourRules(scheduleId: UUID, _ request: BatchPriceRulesRequest) async throws -> Int {
        try await step("applyHappyHourRules")
        guard let i = schedules.firstIndex(where: { $0.id == scheduleId }) else { throw APIError.notFound(nil) }
        for target in request.targetIds {
            schedules[i].priceRules.removeAll { $0.targetType == request.targetType && $0.targetId.caseInsensitiveCompare(target) == .orderedSame }
            let name = request.targetType == .category
                ? categoriesStore.first { $0.id == target }?.name ?? target
                : productsStore.first { $0.id.uuidString.caseInsensitiveCompare(target) == .orderedSame }?.name ?? target
            schedules[i].priceRules.append(HappyHourRule(targetType: request.targetType, targetId: target, targetName: name, pricingMode: request.pricingMode, fixedPrice: request.fixedPrice, discountPercent: request.discountPercent))
        }
        return request.targetIds.count
    }

    public func deleteHappyHourRules(scheduleId: UUID, ruleIds: [UUID]) async throws {
        try await step("deleteHappyHourRules")
        guard let i = schedules.firstIndex(where: { $0.id == scheduleId }) else { throw APIError.notFound(nil) }
        schedules[i].priceRules.removeAll { rule in rule.id.map(ruleIds.contains) ?? false }
    }

    // MARK: Réseau

    public func health() async throws -> TimeInterval { try await step("health"); return 0.004 }

    public func networkInfo() async throws -> NetworkInfo {
        try await step("networkInfo")
        return NetworkInfo(hostName: "simulateur.local", primaryIp: "127.0.0.1", ipAddresses: ["127.0.0.1"], port: 5080, discoveryPort: 45454, serverName: "Caisse Principale (Démo)", status: "Online", version: "1.0.0")
    }

    public func syncStatus() async throws -> SyncStatus {
        try await step("syncStatus")
        return SyncStatus(totalMessages: 0, completedMessages: 0, pendingMessages: 0, status: "Synchronized", lastSyncUtc: Date())
    }

    public func forceSync() async throws { try await step("forceSync") }
}

// MARK: - Données d'amorçage (miroir de `Program.SeedDatabase`)

enum Seed {
    struct Bundle {
        var staff: [InMemoryPosAPI.StaffRecord]
        var categories: [MenuCategory]
        var products: [Product]
        var tables: [DiningTable]
        var rooms: [HotelRoom]
        var printers: [Printer]
        var schedules: [HappyHourSchedule]
    }

    static func make() -> Bundle {
        let staff = [
            InMemoryPosAPI.StaffRecord(member: StaffMember(name: "Alexandre Dupont (Manager)", role: .floorManager), pin: "1234"),
            InMemoryPosAPI.StaffRecord(member: StaffMember(name: "Sophie Martin (Serveuse)", role: .waiter), pin: "2468"),
            InMemoryPosAPI.StaffRecord(member: StaffMember(name: "Thomas Bernard (Chef)", role: .kitchenStaff), pin: "5678"),
            InMemoryPosAPI.StaffRecord(member: StaffMember(name: "Admin Système", role: .admin), pin: "9999"),
        ]
        let categories = [
            MenuCategory(id: "CAT_STARTERS", name: "Entrées Fraîches", iconName: "salad", colorHex: "#2ECC71", displayOrder: 1),
            MenuCategory(id: "CAT_MAINS", name: "Plats & Grillades", iconName: "meat", colorHex: "#E74C3C", displayOrder: 2),
            MenuCategory(id: "CAT_PIZZAS", name: "Pizzas Artisanales", iconName: "pizza", colorHex: "#E67E22", displayOrder: 3),
            MenuCategory(id: "CAT_DESSERTS", name: "Desserts Maison", iconName: "cake", colorHex: "#9B59B6", displayOrder: 4),
            MenuCategory(id: "CAT_DRINKS", name: "Boissons & Vins", iconName: "glass", colorHex: "#3498DB", displayOrder: 5),
        ]
        let cooking = ModifierGroup(groupName: "Cuisson de la Viande", minSelections: 1, maxSelections: 1, isMandatory: true, isSingleChoice: true, options: [
            ModifierOption(name: "Bleu"), ModifierOption(name: "Saignant"), ModifierOption(name: "À Point", isDefault: true), ModifierOption(name: "Bien Cuit"),
        ])
        let extras = ModifierGroup(groupName: "Suppléments Gourmands", minSelections: 0, maxSelections: 2, options: [
            ModifierOption(name: "Double Cheddar Fondu", extraPrice: Money(cents: 250)),
            ModifierOption(name: "Bacon Croustillant", extraPrice: Money(cents: 200)),
            ModifierOption(name: "Œuf au Plat", extraPrice: Money(cents: 150)),
        ])
        let ipa = Product(name: "Bière Artisanale IPA 33cl", categoryId: "CAT_DRINKS", price: Money(cents: 600), taxRatePercent: 20, colorHex: "#2980B9", displayOrder: 1, isQuickKey: true, preparationStationId: "BAR")
        let products = [
            ipa,
            Product(name: "Burger Gourmet Rossini", categoryId: "CAT_MAINS", price: Money(cents: 1950), taxRatePercent: 10, colorHex: "#C0392B", displayOrder: 1, isQuickKey: true, preparationStationId: "HOT_KITCHEN", modifierGroups: [cooking, extras]),
            Product(name: "Pizza Margherita AOP", categoryId: "CAT_PIZZAS", price: Money(cents: 1250), taxRatePercent: 10, taxRateTakeawayPercent: 5.5, colorHex: "#D35400", displayOrder: 1, isQuickKey: true, preparationStationId: "HOT_KITCHEN"),
            Product(name: "Salade César Poulet", categoryId: "CAT_STARTERS", price: Money(cents: 1100), taxRatePercent: 10, taxRateTakeawayPercent: 5.5, colorHex: "#27AE60", displayOrder: 1, preparationStationId: "COLD"),
            Product(name: "Tartare de Saumon", categoryId: "CAT_STARTERS", price: Money(cents: 1350), taxRatePercent: 10, colorHex: "#16A085", displayOrder: 2, preparationStationId: "COLD"),
            Product(name: "Entrecôte 300g", categoryId: "CAT_MAINS", price: Money(cents: 2600), taxRatePercent: 10, colorHex: "#922B21", displayOrder: 2, preparationStationId: "GRILL", modifierGroups: [cooking]),
            Product(name: "Pizza 4 Fromages", categoryId: "CAT_PIZZAS", price: Money(cents: 1450), taxRatePercent: 10, taxRateTakeawayPercent: 5.5, colorHex: "#E59866", displayOrder: 2, preparationStationId: "HOT_KITCHEN"),
            Product(name: "Tiramisu Maison", categoryId: "CAT_DESSERTS", price: Money(cents: 750), taxRatePercent: 10, taxRateTakeawayPercent: 5.5, colorHex: "#8E44AD", displayOrder: 1, preparationStationId: "DESSERT"),
            Product(name: "Café Gourmand", categoryId: "CAT_DESSERTS", price: Money(cents: 850), taxRatePercent: 10, colorHex: "#6C3483", displayOrder: 2, isQuickKey: true, preparationStationId: "BAR"),
            Product(name: "Verre Bordeaux AOP 12cl", categoryId: "CAT_DRINKS", price: Money(cents: 550), taxRatePercent: 20, colorHex: "#7B241C", displayOrder: 2, preparationStationId: "BAR"),
            Product(name: "Eau Pétillante 50cl", categoryId: "CAT_DRINKS", price: Money(cents: 450), taxRatePercent: 10, taxRateTakeawayPercent: 5.5, colorHex: "#5DADE2", displayOrder: 3, preparationStationId: "BAR"),
        ]
        let tables = [("T1", 2), ("T2", 4), ("T3", 6), ("T4", 2), ("T5", 8), ("T6", 4), ("T7", 2), ("T8", 4)].map { DiningTable(tableNumber: $0.0, capacity: $0.1) }
        let rooms = [
            HotelRoom(roomNumber: "101", guestName: "Jean Dujardin", maxCreditLimit: Money(cents: 30000)),
            HotelRoom(roomNumber: "204", guestName: "Alexandre Dupont", maxCreditLimit: Money(cents: 60000)),
            HotelRoom(roomNumber: "305", guestName: "Sophie Marceau", maxCreditLimit: Money(cents: 45000)),
        ]
        let printers = [
            Printer(name: "Imprimante Caisse Comptoir", ipAddress: "192.168.1.200", openCashDrawerOnReceipt: true, assignedStationIds: ["RECEIPT"]),
            Printer(name: "Imprimante Cuisine Chaude", ipAddress: "192.168.1.201", assignedStationIds: ["HOT_KITCHEN", "GRILL"]),
            Printer(name: "Imprimante Bar & Boissons", ipAddress: "192.168.1.202", assignedStationIds: ["BAR"]),
        ]
        let schedules = [
            HappyHourSchedule(id: UUID(), name: "Afterwork Standard", daysOfWeek: [1, 2, 3, 4, 5], startTime: "17:00", endTime: "20:00", priceRules: [
                HappyHourRule(targetType: .product, targetId: ipa.id.uuidString.lowercased(), targetName: ipa.name, pricingMode: .fixedPrice, fixedPrice: Money(cents: 500)),
                HappyHourRule(targetType: .category, targetId: "CAT_DRINKS", targetName: "Boissons & Vins", pricingMode: .percentageDiscount, discountPercent: 20),
            ]),
        ]
        return Bundle(staff: staff, categories: categories, products: products, tables: tables, rooms: rooms, printers: printers, schedules: schedules)
    }
}
