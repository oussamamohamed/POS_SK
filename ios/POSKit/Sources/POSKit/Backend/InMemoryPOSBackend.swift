import Foundation

/// Serveur de caisse simulé en mémoire.
///
/// Reproduit le comportement observé de l'API ASP.NET Core (fusion des lignes, envoi cuisine,
/// reste à payer, libération des tables, numéros de retrait, rapports X/Z, droits par rôle).
/// Il sert au mode démonstration, aux aperçus SwiftUI et aux tests UI automatisés :
/// l'app se teste ainsi sans serveur, de façon déterministe.
public actor InMemoryPOSBackend: POSAPI {
    public struct StaffMember: Sendable {
        public var pin: String
        public var op: Operator
    }

    private struct StoredOrder {
        var order: ActiveOrder
        var paid: Money = .zero
        var isClosed = false
    }

    private struct Receipt {
        var number: String
        var method: PaymentMethod
        var amount: Money
        var vatByRate: [Decimal: Money]
        var ht: Money
    }

    private struct StoredHold {
        var held: HeldOrder
        var order: ActiveOrder
    }

    private var staff: [StaffMember]
    private var categoryList: [MenuCategory]
    private var productList: [Product]
    private var tableList: [DiningTable]
    private var orders: [UUID: StoredOrder] = [:]
    private var tickets: [KitchenTicket] = []
    private var holds: [StoredHold] = []
    private var receipts: [Receipt] = []
    private var closures: [FiscalReport] = []
    private var perpetualTotal: Money = .zero
    private var receiptSequence = 0
    private var pickupSequence = 0
    private var currentOperator: Operator?
    /// Latence simulée (0 pour les tests).
    private let latency: Duration

    public init(seedDemoActivity: Bool = true, latency: Duration = .zero) {
        self.latency = latency
        staff = Self.defaultStaff
        categoryList = Self.defaultCategories
        productList = Self.defaultProducts
        var tables = Self.defaultTables
        if seedDemoActivity {
            let seed = Self.demoActivity(tables: &tables)
            orders = [seed.order.orderId: StoredOrder(order: seed.order)]
            tickets = seed.tickets
        }
        tableList = tables
    }

    // MARK: Données de démonstration (identiques au jeu de test du serveur)

    public static let defaultStaff: [StaffMember] = [
        StaffMember(pin: "1234", op: Operator(id: uuid(1), name: "Alexandre Dupont (Manager)", role: .floorManager)),
        StaffMember(pin: "2468", op: Operator(id: uuid(2), name: "Sophie Martin (Serveuse)", role: .waiter)),
        StaffMember(pin: "5678", op: Operator(id: uuid(3), name: "Thomas Bernard (Chef)", role: .kitchenStaff)),
        StaffMember(pin: "9999", op: Operator(id: uuid(4), name: "Admin Système", role: .admin)),
    ]

    public static let defaultCategories: [MenuCategory] = [
        MenuCategory(id: "CAT_ENTREES", name: "Entrées Fraîches", iconName: "salad", colorHex: "#2ECC71", displayOrder: 1),
        MenuCategory(id: "CAT_PLATS", name: "Plats & Grillades", iconName: "meat", colorHex: "#E74C3C", displayOrder: 2),
        MenuCategory(id: "CAT_PIZZAS", name: "Pizzas Artisanales", iconName: "pizza", colorHex: "#E67E22", displayOrder: 3),
        MenuCategory(id: "CAT_DESSERTS", name: "Desserts Maison", iconName: "cake", colorHex: "#9B59B6", displayOrder: 4),
        MenuCategory(id: "CAT_BOISSONS", name: "Boissons & Vins", iconName: "glass", colorHex: "#3498DB", displayOrder: 5),
    ]

    public static let defaultProducts: [Product] = {
        let cuisson = ModifierGroup(
            id: uuid(101), groupName: "Cuisson de la viande", minSelections: 1, maxSelections: 1,
            isMandatory: true, isSingleChoice: true,
            options: [
                ModifierOption(id: uuid(111), name: "Bleu"),
                ModifierOption(id: uuid(112), name: "Saignant"),
                ModifierOption(id: uuid(113), name: "À point", isDefault: true),
                ModifierOption(id: uuid(114), name: "Bien cuit"),
            ]
        )
        let supplements = ModifierGroup(
            id: uuid(102), groupName: "Suppléments gourmands", minSelections: 0, maxSelections: 3,
            isMandatory: false, isSingleChoice: false,
            options: [
                ModifierOption(id: uuid(121), name: "Bacon", extraPrice: Money(cents: 150)),
                ModifierOption(id: uuid(122), name: "Cheddar affiné", extraPrice: Money(cents: 100)),
                ModifierOption(id: uuid(123), name: "Œuf au plat", extraPrice: Money(cents: 120)),
            ]
        )
        let sauce = ModifierGroup(
            id: uuid(103), groupName: "Sauce", minSelections: 0, maxSelections: 1,
            isMandatory: false, isSingleChoice: true,
            options: [
                ModifierOption(id: uuid(131), name: "Poivre"),
                ModifierOption(id: uuid(132), name: "Béarnaise"),
                ModifierOption(id: uuid(133), name: "Roquefort", extraPrice: Money(cents: 100)),
            ]
        )
        return [
            Product(id: uuid(201), name: "Salade César Poulet", categoryId: "CAT_ENTREES", price: Money(cents: 950), colorHex: "#27AE60", displayOrder: 1, isQuickKey: true, preparationStationId: "COLD"),
            Product(id: uuid(202), name: "Tartare de Saumon Frais", categoryId: "CAT_ENTREES", price: Money(cents: 1200), colorHex: "#16A085", displayOrder: 2, preparationStationId: "COLD"),
            Product(id: uuid(203), name: "Burger Gourmet Rossini", categoryId: "CAT_PLATS", description: "Bœuf charolais, foie gras poêlé", price: Money(cents: 1950), colorHex: "#C0392B", displayOrder: 1, isQuickKey: true, preparationStationId: "HOT_KITCHEN", modifierGroups: [cuisson, supplements]),
            Product(id: uuid(204), name: "Entrecôte Grillée 300g", categoryId: "CAT_PLATS", price: Money(cents: 2400), colorHex: "#E74C3C", displayOrder: 2, preparationStationId: "HOT_KITCHEN", modifierGroups: [cuisson, sauce]),
            Product(id: uuid(205), name: "Pizza Margherita AOP", categoryId: "CAT_PIZZAS", price: Money(cents: 1250), colorHex: "#E67E22", displayOrder: 1, isQuickKey: true, preparationStationId: "HOT_KITCHEN"),
            Product(id: uuid(206), name: "Pizza Reine Royale", categoryId: "CAT_PIZZAS", price: Money(cents: 1450), colorHex: "#D35400", displayOrder: 2, preparationStationId: "HOT_KITCHEN"),
            Product(id: uuid(207), name: "Tiramisu Spéculos Maison", categoryId: "CAT_DESSERTS", price: Money(cents: 750), colorHex: "#8E44AD", displayOrder: 1, preparationStationId: "DESSERT"),
            Product(id: uuid(208), name: "Fondant Chocolat Valrhona", categoryId: "CAT_DESSERTS", price: Money(cents: 800), colorHex: "#9B59B6", displayOrder: 2, isQuickKey: true, preparationStationId: "DESSERT"),
            Product(id: uuid(209), name: "Bière Artisanale IPA 33cl", categoryId: "CAT_BOISSONS", price: Money(cents: 600), taxRatePercent: 20, colorHex: "#F1C40F", displayOrder: 1, isQuickKey: true, preparationStationId: "BAR"),
            Product(id: uuid(210), name: "Verre Bordeaux AOP 12cl", categoryId: "CAT_BOISSONS", price: Money(cents: 550), taxRatePercent: 20, colorHex: "#7B241C", displayOrder: 2, preparationStationId: "BAR"),
            Product(id: uuid(211), name: "Expresso Pur Arabica", categoryId: "CAT_BOISSONS", price: Money(cents: 250), colorHex: "#6E2C00", displayOrder: 3, isQuickKey: true, preparationStationId: "BAR"),
        ]
    }()

    public static let defaultTables: [DiningTable] = zip(1...8, [2, 4, 6, 2, 4, 4, 8, 2]).map { number, capacity in
        DiningTable(tableNumber: "T\(number)", capacity: capacity, positionX: Double((number - 1) % 4), positionY: Double((number - 1) / 4))
    }

    static func uuid(_ value: Int) -> UUID {
        UUID(uuidString: String(format: "00000000-0000-4000-8000-%012ld", value))!
    }

    /// Table T3 déjà occupée avec des plats envoyés en cuisine, pour une démo réaliste.
    private static func demoActivity(tables: inout [DiningTable]) -> (order: ActiveOrder, tickets: [KitchenTicket]) {
        let waiter = Self.defaultStaff[1].op
        let orderId = Self.uuid(900)
        let opened = Date().addingTimeInterval(-22 * 60)
        let pizza = Self.defaultProducts[4]
        let wine = Self.defaultProducts[9]
        var order = ActiveOrder(orderId: orderId, tableNumber: "T3", waiterName: waiter.name, coversCount: 4, openedAtUtc: opened, destination: .eatIn)
        order.lines = [
            Self.line(from: OrderItemInput(productId: pizza.id, productName: pizza.name, quantity: 2, unitPrice: pizza.price, taxRatePercent: pizza.taxRatePercent, preparationStationId: pizza.preparationStationId, modifiers: [], course: .direct, modifiersPriceExtra: .zero), dispatched: true),
            Self.line(from: OrderItemInput(productId: wine.id, productName: wine.name, quantity: 2, unitPrice: wine.price, taxRatePercent: wine.taxRatePercent, preparationStationId: wine.preparationStationId, modifiers: [], course: .direct, modifiersPriceExtra: .zero), dispatched: true),
        ]
        Self.recomputeTotals(&order)
        if let index = tables.firstIndex(where: { $0.tableNumber == "T3" }) {
            tables[index].status = .occupied
            tables[index].coversCount = 4
            tables[index].assignedWaiterName = waiter.name
            tables[index].activeOrderId = orderId
            tables[index].openedAtUtc = opened
            tables[index].activeOrderTotalTtc = order.totalTtcAmount
        }
        let seededTickets = [
            KitchenTicket(id: Self.uuid(950), orderId: orderId, tableNumber: "T3", serverName: waiter.name, coversCount: 4, stationId: "HOT_KITCHEN", status: .inPreparation, dispatchedAtUtc: opened.addingTimeInterval(120), items: [KitchenTicketItem(productName: pizza.name, quantity: 2)]),
            KitchenTicket(id: Self.uuid(951), orderId: orderId, tableNumber: "T3", serverName: waiter.name, coversCount: 4, stationId: "BAR", status: .ready, dispatchedAtUtc: opened.addingTimeInterval(60), items: [KitchenTicketItem(productName: wine.name, quantity: 2)]),
        ]
        return (order, seededTickets)
    }

    // MARK: Utilitaires

    private func pause() async {
        if latency > .zero {
            try? await Task.sleep(for: latency)
        }
    }

    private func requireOperator() throws -> Operator {
        guard let currentOperator else { throw APIError.unauthorized }
        return currentOperator
    }

    private func requireManager() throws -> Operator {
        let op = try requireOperator()
        guard op.role.isManager else { throw APIError.forbidden(nil) }
        return op
    }

    private func tableIndex(_ number: String) -> Int? {
        tableList.firstIndex { $0.tableNumber.caseInsensitiveCompare(number) == .orderedSame }
    }

    private func updateTable(_ number: String, _ change: (inout DiningTable) -> Void) {
        if let index = tableIndex(number) {
            change(&tableList[index])
        }
    }

    private static func line(from input: OrderItemInput, dispatched: Bool = false) -> OrderLine {
        OrderLine(
            productId: input.productId,
            productName: input.productName,
            quantity: input.quantity,
            unitPrice: input.unitPrice,
            totalPrice: (input.unitPrice + input.modifiersPriceExtra) * input.quantity,
            taxRatePercent: input.taxRatePercent,
            preparationStationId: input.preparationStationId,
            isDispatched: dispatched,
            modifiersSummary: input.modifiers,
            course: input.course,
            modifiersPriceExtra: input.modifiersPriceExtra
        )
    }

    private static func recomputeTotals(_ order: inout ActiveOrder) {
        for index in order.lines.indices {
            let line = order.lines[index]
            order.lines[index].totalPrice = line.isComp ? .zero : (line.unitPrice + line.modifiersPriceExtra) * line.quantity
        }
        let totals = TaxCalculator.totals(order.lines.map { (ttc: $0.totalPrice, ratePercent: $0.taxRatePercent) })
        order.totalHtAmount = totals.ht
        order.totalVatAmount = totals.vat
        order.totalTtcAmount = totals.ttc
    }

    private static func fakeSignature() -> String {
        (UUID().uuidString + UUID().uuidString).replacingOccurrences(of: "-", with: "")
    }

    private func nextReceiptNumber(_ terminalId: String) -> String {
        receiptSequence += 1
        return String(format: "%@-%06ld", terminalId, receiptSequence)
    }

    private func activeOrderId(forTable number: String) -> UUID? {
        guard let index = tableIndex(number) else { return nil }
        return tableList[index].activeOrderId
    }

    private func freeTable(_ number: String) {
        updateTable(number) {
            $0.status = .free
            $0.activeOrderId = nil
            $0.coversCount = 0
            $0.assignedWaiterName = nil
            $0.openedAtUtc = nil
            $0.activeOrderTotalTtc = .zero
        }
    }

    /// Enregistre un règlement et renvoie (payé, rendu, reste).
    private func settle(orderId: UUID, terminalId: String, tenders: [(method: PaymentMethod, amount: Money, tendered: Money)]) throws -> (paid: Money, change: Money, remaining: Money, receipt: String, signature: String) {
        guard var stored = orders[orderId], !stored.isClosed else {
            throw APIError.server(status: 400, message: "Commande introuvable pour ce règlement.")
        }
        let total = stored.order.totalTtcAmount
        var paid = Money.zero
        var change = Money.zero
        let receipt = nextReceiptNumber(terminalId)
        for tender in tenders {
            let due = Money.max(.zero, total - stored.paid - paid)
            let applied = Money.min(tender.amount, due)
            paid += applied
            if tender.method == .cash {
                change += Money.max(.zero, tender.tendered - applied)
            }
            let share = total.isPositive ? applied.decimal / total.decimal : 0
            var vat: [Decimal: Money] = [:]
            var ht = Money.zero
            let totals = TaxCalculator.totals(stored.order.lines.map { (ttc: $0.totalPrice, ratePercent: $0.taxRatePercent) })
            for (rate, amount) in totals.vatByRate {
                vat[rate] = Money(euros: amount.decimal * share)
            }
            ht = Money(euros: totals.ht.decimal * share)
            receipts.append(Receipt(number: receipt, method: tender.method, amount: applied, vatByRate: vat, ht: ht))
        }
        stored.paid += paid
        perpetualTotal += paid
        let remaining = Money.max(.zero, total - stored.paid)
        if !remaining.isPositive {
            stored.isClosed = true
            freeTable(stored.order.tableNumber)
        }
        orders[orderId] = stored
        return (paid, change, remaining, receipt, Self.fakeSignature())
    }

    // MARK: POSAPI — authentification & carte

    public func health() async throws -> Bool {
        await pause()
        return true
    }

    public func login(pin: String) async throws -> OperatorSession {
        await pause()
        guard let member = staff.first(where: { $0.pin == pin }) else {
            throw APIError.forbidden("Code PIN ou identifiants incorrects")
        }
        currentOperator = member.op
        return OperatorSession(operator: member.op, token: "demo-\(member.op.id.uuidString)")
    }

    public func logout() async {
        currentOperator = nil
    }

    public func categories() async throws -> [MenuCategory] {
        await pause()
        return categoryList
    }

    public func products() async throws -> [Product] {
        await pause()
        return productList
    }

    // MARK: POSAPI — salle

    public func tables() async throws -> [DiningTable] {
        await pause()
        return tableList
    }

    public func openTable(_ tableNumber: String, covers: Int, operator op: Operator) async throws -> DiningTable {
        await pause()
        _ = try requireOperator()
        guard let index = tableIndex(tableNumber) else {
            throw APIError.notFound("Table \(tableNumber) introuvable.")
        }
        if tableList[index].activeOrderId == nil {
            let order = ActiveOrder(orderId: UUID(), tableNumber: tableList[index].tableNumber, waiterName: op.name, coversCount: covers, openedAtUtc: Date(), destination: .eatIn)
            orders[order.orderId] = StoredOrder(order: order)
            tableList[index].activeOrderId = order.orderId
            tableList[index].openedAtUtc = order.openedAtUtc
        }
        tableList[index].status = .occupied
        tableList[index].coversCount = covers
        tableList[index].assignedWaiterName = op.name
        return tableList[index]
    }

    public func activeOrder(tableNumber: String) async throws -> ActiveOrder? {
        await pause()
        _ = try requireOperator()
        guard let id = activeOrderId(forTable: tableNumber) else { return nil }
        return orders[id]?.order
    }

    public func addItems(tableNumber: String, items: [OrderItemInput]) async throws -> ActiveOrder {
        await pause()
        let op = try requireOperator()
        let isCounter = tableNumber.caseInsensitiveCompare(OrderContext.counterTableNumber) == .orderedSame
        if tableIndex(tableNumber) == nil {
            tableList.append(DiningTable(tableNumber: tableNumber, capacity: isCounter ? 1 : 2))
        }
        var orderId = activeOrderId(forTable: tableNumber)
        if orderId == nil {
            let order = ActiveOrder(orderId: UUID(), tableNumber: tableNumber, waiterName: op.name, openedAtUtc: Date(), destination: isCounter ? .takeaway : .eatIn)
            orders[order.orderId] = StoredOrder(order: order)
            orderId = order.orderId
            updateTable(tableNumber) {
                $0.activeOrderId = order.orderId
                $0.status = .occupied
                $0.openedAtUtc = $0.openedAtUtc ?? Date()
            }
        }
        guard let orderId, var stored = orders[orderId] else {
            throw APIError.server(status: 500, message: "Commande introuvable.")
        }
        for input in items {
            if let index = stored.order.lines.firstIndex(where: {
                !$0.isDispatched && $0.productId == input.productId && $0.course == input.course
                    && $0.unitPrice == input.unitPrice && $0.modifiersPriceExtra == input.modifiersPriceExtra
                    && $0.modifiersSummary == input.modifiers
            }) {
                stored.order.lines[index].quantity += input.quantity
            } else {
                stored.order.lines.append(Self.line(from: input))
            }
        }
        Self.recomputeTotals(&stored.order)
        orders[orderId] = stored
        let total = stored.order.totalTtcAmount
        updateTable(tableNumber) { $0.activeOrderTotalTtc = total }
        return stored.order
    }

    public func dispatch(tableNumber: String) async throws {
        await pause()
        _ = try requireOperator()
        guard let orderId = activeOrderId(forTable: tableNumber), var stored = orders[orderId] else {
            throw APIError.notFound(nil)
        }
        let unsent = stored.order.lines.filter { !$0.isDispatched }
        let byStation = Dictionary(grouping: unsent) { $0.preparationStationId ?? "HOT_KITCHEN" }
        let label = stored.order.destination == .takeaway ? "[À EMPORTER] \(stored.order.tableNumber)" : stored.order.tableNumber
        for (station, lines) in byStation.sorted(by: { $0.key < $1.key }) {
            tickets.append(KitchenTicket(
                orderId: orderId,
                tableNumber: label,
                serverName: stored.order.waiterName ?? "",
                coversCount: max(1, stored.order.coversCount),
                stationId: station,
                status: .pending,
                dispatchedAtUtc: Date(),
                items: lines.map { KitchenTicketItem(productName: $0.productName, quantity: $0.quantity, modifiersSummary: $0.modifiersSummary.joined(separator: ", ")) }
            ))
        }
        for index in stored.order.lines.indices {
            stored.order.lines[index].isDispatched = true
        }
        orders[orderId] = stored
    }

    public func pay(_ request: PaymentSettlementRequest) async throws -> PaymentResult {
        await pause()
        _ = try requireOperator()
        var orderId = request.orderId
        if orders[orderId] == nil, let table = request.tableNumber, let active = activeOrderId(forTable: table) {
            orderId = active
        }
        let result = try settle(
            orderId: orderId,
            terminalId: request.terminalId,
            tenders: request.tenders.map { (method: $0.method, amount: $0.amount, tendered: $0.tendered) }
        )
        return PaymentResult(receiptNumber: result.receipt, totalPaid: result.paid, changeGiven: result.change, remainingBalance: result.remaining, fiscalSignature: result.signature)
    }

    // MARK: POSAPI — comptoir

    public func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder {
        await pause()
        let op = try requireOperator()
        let counter = OrderContext.counterTableNumber
        if tableIndex(counter) == nil {
            tableList.append(DiningTable(tableNumber: counter, capacity: 1))
        }
        if let id = activeOrderId(forTable: counter), let stored = orders[id] {
            return stored.order
        }
        let order = ActiveOrder(orderId: UUID(), tableNumber: counter, waiterName: op.name, openedAtUtc: Date(), destination: destination)
        orders[order.orderId] = StoredOrder(order: order)
        updateTable(counter) {
            $0.activeOrderId = order.orderId
            $0.status = .occupied
            $0.openedAtUtc = Date()
        }
        return order
    }

    public func switchDestination(orderId: UUID, to destination: OrderDestination) async throws -> ActiveOrder {
        await pause()
        _ = try requireOperator()
        guard var stored = orders[orderId] else { throw APIError.notFound("Commande introuvable.") }
        stored.order.destination = destination
        orders[orderId] = stored
        return stored.order
    }

    public func holdCounterOrder(orderId: UUID, terminalId: String, label: String?) async throws -> HeldOrder {
        await pause()
        _ = try requireOperator()
        guard let stored = orders[orderId], !stored.order.lines.isEmpty else {
            throw APIError.server(status: 400, message: "Impossible de mettre en attente un panier vide.")
        }
        let held = HeldOrder(
            holdId: UUID(),
            terminalId: terminalId,
            orderId: orderId,
            customerLabel: label,
            destination: stored.order.destination,
            itemCount: stored.order.lines.reduce(0) { $0 + $1.quantity },
            totalTtc: stored.order.totalTtcAmount,
            heldAtUtc: Date()
        )
        holds.append(StoredHold(held: held, order: stored.order))
        freeTable(stored.order.tableNumber)
        return held
    }

    public func heldOrders(terminalId: String) async throws -> [HeldOrder] {
        await pause()
        _ = try requireOperator()
        return holds.filter { $0.held.terminalId == terminalId }.map(\.held)
    }

    public func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder {
        await pause()
        _ = try requireOperator()
        guard let index = holds.firstIndex(where: { $0.held.holdId == holdId }) else {
            throw APIError.notFound("Commande en attente introuvable ou déjà rappelée.")
        }
        let hold = holds.remove(at: index)
        updateTable(OrderContext.counterTableNumber) {
            $0.activeOrderId = hold.order.orderId
            $0.status = .occupied
            $0.openedAtUtc = $0.openedAtUtc ?? Date()
        }
        return orders[hold.order.orderId]?.order ?? hold.order
    }

    public func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws {
        await pause()
        _ = try requireOperator()
        guard let supervisor = staff.first(where: { $0.pin == supervisorPin }), supervisor.op.role.isManager else {
            throw APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")
        }
        guard let index = holds.firstIndex(where: { $0.held.holdId == holdId }) else {
            throw APIError.notFound("Commande en attente introuvable.")
        }
        let hold = holds.remove(at: index)
        orders[hold.order.orderId]?.isClosed = true
    }

    public func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult {
        await pause()
        _ = try requireOperator()
        guard let stored = orders[request.orderId], !stored.order.lines.isEmpty else {
            throw APIError.server(status: 400, message: "Commande introuvable ou panier vide.")
        }
        pickupSequence += 1
        let pickup = String(format: "#A-%02ld", pickupSequence)
        var updated = stored
        updated.order.destination = request.destination
        updated.order.pickupNumber = pickup
        updated.order.pickupBuzzer = request.pickupBuzzer
        orders[request.orderId] = updated

        var voucher: CreditVoucher?
        if let mealVoucher = request.tenders.first(where: { $0.method == .mealVoucher }) {
            let surplus = mealVoucher.tendered - mealVoucher.amount
            if surplus.isPositive {
                switch request.mealVoucherPolicy {
                case .strictRejection:
                    throw APIError.server(status: 400, message: "Le titre-restaurant dépasse le montant dû.")
                case .customerCreditVoucher:
                    voucher = CreditVoucher(voucherCode: "CR-DEMO\(pickupSequence)", amount: surplus, expiresAtUtc: Date().addingTimeInterval(90 * 86_400))
                case .capAtBalance:
                    break
                }
            }
        }

        let result = try settle(
            orderId: request.orderId,
            terminalId: request.terminalId,
            tenders: request.tenders.map { (method: $0.method, amount: $0.amount, tendered: $0.tendered) }
        )
        return CounterCheckoutResult(
            orderId: request.orderId,
            pickupNumber: pickup,
            totalPaid: result.paid,
            changeGiven: result.change,
            remainingBalance: result.remaining,
            receiptNumber: result.receipt,
            fiscalSignature: result.signature,
            issuedCreditVoucher: voucher,
            openCashDrawer: request.tenders.contains { $0.method == .cash }
        )
    }

    // MARK: POSAPI — cuisine

    public func kitchenTickets() async throws -> [KitchenTicket] {
        await pause()
        return Array(tickets.sorted { ($0.dispatchedAtUtc ?? .distantPast) > ($1.dispatchedAtUtc ?? .distantPast) }.prefix(50))
    }

    public func bumpTicket(id: UUID) async throws -> KitchenTicket {
        await pause()
        _ = try requireOperator()
        guard let index = tickets.firstIndex(where: { $0.id == id }) else {
            throw APIError.notFound(nil)
        }
        tickets[index].status = tickets[index].status.next
        return tickets[index]
    }

    // MARK: POSAPI — fiscal

    public func xReport(terminalId: String) async throws -> FiscalReport {
        await pause()
        _ = try requireManager()
        return currentPeriodReport(terminalId: terminalId)
    }

    private func currentPeriodReport(terminalId: String) -> FiscalReport {
        let ttc = receipts.reduce(Money.zero) { $0 + $1.amount }
        let ht = receipts.reduce(Money.zero) { $0 + $1.ht }
        var vat: [String: Money] = [:]
        var payments: [String: Money] = [:]
        for receipt in receipts {
            for (rate, amount) in receipt.vatByRate {
                vat[Self.rateKey(rate), default: .zero] += amount
            }
            payments[receipt.method.apiName, default: .zero] += receipt.amount
        }
        return FiscalReport(
            terminalId: terminalId,
            totalSalesTtc: ttc,
            totalSalesHt: ht,
            receiptCount: Set(receipts.map(\.number)).count,
            vatBreakdown: vat,
            paymentTotals: payments,
            perpetualGrandTotal: perpetualTotal,
            periodStartUtc: closures.last?.closedAtUtc,
            periodEndUtc: Date()
        )
    }

    /// Clé de taux au format du serveur (`10.0`, `20.0`, `5.5`).
    private static func rateKey(_ rate: Decimal) -> String {
        let text = NSDecimalNumber(decimal: rate).stringValue
        return text.contains(".") ? text : text + ".0"
    }

    public func latestClosure(terminalId: String) async throws -> FiscalReport? {
        await pause()
        _ = try requireManager()
        return closures.last
    }

    public func zClosure(terminalId: String, manager: Operator) async throws -> FiscalReport {
        await pause()
        _ = try requireManager()
        var closure = currentPeriodReport(terminalId: terminalId)
        closure.closureSequence = closures.count + 1
        closure.signatureHash = Self.fakeSignature()
        closure.closedAtUtc = Date()
        closure.periodEndUtc = nil
        closures.append(closure)
        receipts.removeAll()
        return closure
    }
}
