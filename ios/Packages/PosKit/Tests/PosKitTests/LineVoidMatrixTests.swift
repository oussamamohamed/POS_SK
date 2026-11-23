import Foundation
import Testing
@testable import PosKit

// Matrice de cas limites de l'annulation / correction de ligne, miroir de `OrderLineVoidMatrixTests` (.NET) : UNE table de cas jouée sur
// `LocalPosAPI` (base SQLite, JET, bons) et sur `InMemoryPosAPI` (double des tests UI). Chaque cas vérifie l'issue (type, statut, message
// de l'`APIError`) ET l'état : lignes et total, articles de bon ; côté autonome aussi `OrderLineVoids` (R2 serveur, R9 prix facturé),
// JET `ORDER_LINE_VOIDED` et bons d'annulation en file (un par annulation, aucun pour une correction ou un refus). Refus = rien ne change.

private let burgerName = "Burger Gourmet Rossini"
private let burgerCents = 1950

extension LocalPosAPI {
    func testOrder(_ id: UUID) throws -> ActiveOrder? { try orderRepository.order(id: id) }
    func testReceiptIds(_ orderId: UUID) throws -> [UUID] {
        try db.query("SELECT Id FROM FiscalReceipts WHERE OrderId = ? AND VoidedReceiptId IS NULL ORDER BY SequenceNumber", [.uuid(orderId)]).compactMap { $0.uuid("Id") }
    }
}

enum VoidBackend: String, CaseIterable, Sendable { case local, memory }

enum VoidRole: String, Sendable {
    case manager = "1234", waiter = "2468", kitchen = "5678", admin = "9999"
}

/// Reçus posés avant l'annulation : aucun, acompte, soldé, acompte puis avoir (paire annulée), soldé puis avoir (commande annulée).
enum VoidReceipts: String, Sendable { case none, partial, full, partialVoided, fullVoided }

enum VoidFeature: String, Sendable {
    case plain, modifiers, comp, discount, legacy, takeaway, counter, twoTickets
}

/// Issue attendue : celle que le .NET renvoie aujourd'hui (`OrderLineVoidOutcome`).
enum VoidOutcome: Sendable, Equatable { case voided, corrected, forbidden, reasonRequired, reasonTooLong, invalidQuantity, hasReceipts, notModifiable, notFound }

struct VoidCase: Sendable, CustomTestStringConvertible {
    var name: String
    var role: VoidRole = .manager
    var dispatched = true
    var quantity = 1
    var reason: String? = "Erreur de saisie"
    var receipts: VoidReceipts = .none
    var feature: VoidFeature = .plain
    var status: LocalOrderStatus?
    var expected: VoidOutcome
    var backends: [VoidBackend] = VoidBackend.allCases

    var testDescription: String { name }
    static let lineQuantity = 3
}

private let longReason = String(repeating: "a", count: 256)
private let emoji256 = String(repeating: "🍔", count: 128)   // 2 unités UTF-16 chacun

let voidCases: [VoidCase] = [
    // Rôle × état de ligne.
    .init(name: "manager, ligne envoyée", expected: .voided),
    .init(name: "admin, ligne envoyée", role: .admin, expected: .voided),
    .init(name: "serveur, ligne envoyée", role: .waiter, expected: .forbidden),
    .init(name: "cuisine, ligne envoyée", role: .kitchen, expected: .forbidden),
    .init(name: "serveur, ligne non envoyée", role: .waiter, dispatched: false, reason: nil, expected: .corrected),
    .init(name: "cuisine, ligne non envoyée", role: .kitchen, dispatched: false, reason: nil, expected: .corrected),
    .init(name: "manager, ligne non envoyée", dispatched: false, reason: nil, expected: .corrected),
    // Quantité (ligne de 3).
    .init(name: "quantité 0", quantity: 0, expected: .invalidQuantity),
    .init(name: "quantité -1", quantity: -1, expected: .invalidQuantity),
    .init(name: "quantité Int.min", quantity: .min, expected: .invalidQuantity),
    .init(name: "quantité 2 (partielle)", quantity: 2, expected: .voided),
    .init(name: "quantité 3 (toute la ligne)", quantity: 3, expected: .voided),
    .init(name: "quantité 4 (au-delà)", quantity: 4, expected: .invalidQuantity),
    .init(name: "quantité Int.max", quantity: .max, expected: .invalidQuantity),
    .init(name: "correction quantité 4", role: .waiter, dispatched: false, quantity: 4, reason: nil, expected: .invalidQuantity),
    .init(name: "correction de toute la ligne", role: .waiter, dispatched: false, quantity: 3, reason: nil, expected: .corrected),
    // Motif (ligne envoyée).
    .init(name: "motif absent", reason: nil, expected: .reasonRequired),
    .init(name: "motif vide", reason: "", expected: .reasonRequired),
    .init(name: "motif blanc", reason: " \t\n ", expected: .reasonRequired),
    .init(name: "motif 256 unités", reason: longReason, expected: .voided),
    .init(name: "motif 257 unités", reason: longReason + "b", expected: .reasonTooLong),
    .init(name: "motif 256 + espaces autour (rogné)", reason: "  " + longReason + "\n", expected: .voided),
    .init(name: "motif 128 emoji (256 unités)", reason: emoji256, expected: .voided),
    .init(name: "motif 128 emoji + 1 (257 unités)", reason: emoji256 + "x", expected: .reasonTooLong),
    .init(name: "motif arabe", reason: "طلب بالخطأ", expected: .voided),
    .init(name: "motif HTML gardé tel quel", reason: "<script>alert(1)</script> & \"x\"", expected: .voided),
    .init(name: "motif ignoré pour une correction, même trop long", role: .waiter, dispatched: false, reason: longReason + "b", expected: .corrected),
    // Contrôles avant le rôle et le motif (même ordre que le .NET).
    .init(name: "serveur, quantité invalide avant le rôle", role: .waiter, quantity: 9, expected: .invalidQuantity),
    .init(name: "serveur, motif absent : rôle d'abord", role: .waiter, reason: nil, expected: .forbidden),
    // État de commande (statut forcé : autonome seulement, le double en mémoire ne suit pas les statuts).
    .init(name: "commande ouverte", status: .open, expected: .voided, backends: [.local]),
    .init(name: "addition demandée", status: .billRequested, expected: .voided, backends: [.local]),
    .init(name: "commande réglée (statut)", status: .paid, expected: .notModifiable, backends: [.local]),
    .init(name: "commande annulée (statut)", status: .cancelled, expected: .notModifiable, backends: [.local]),
    .init(name: "correction sur commande annulée", role: .waiter, dispatched: false, reason: nil, status: .cancelled, expected: .notModifiable, backends: [.local]),
    // Reçus.
    .init(name: "acompte : reçu actif", receipts: .partial, expected: .hasReceipts),
    .init(name: "acompte, correction aussi bloquée", role: .waiter, dispatched: false, reason: nil, receipts: .partial, expected: .hasReceipts),
    .init(name: "soldée", receipts: .full, expected: .notModifiable),
    .init(name: "acompte puis avoir (paire annulée)", receipts: .partialVoided, expected: .voided, backends: [.local]),
    .init(name: "soldée puis avoir (commande annulée)", receipts: .fullVoided, expected: .notModifiable, backends: [.local]),
    // Particularités de ligne.
    .init(name: "options + supplément (R9)", feature: .modifiers, expected: .voided),
    .init(name: "offert", feature: .comp, expected: .voided),
    .init(name: "remise de commande 10 %", quantity: 2, feature: .discount, expected: .voided),
    .init(name: "ligne ancienne sans OrderItemId", feature: .legacy, expected: .voided, backends: [.local]),
    .init(name: "à emporter à table", feature: .takeaway, expected: .voided),
    .init(name: "comptoir", feature: .counter, expected: .voided),
    .init(name: "même plat sur deux bons : le second", feature: .twoTickets, expected: .voided),
]

/// API prête : commande d'une ligne de 3 burgers (ligne visée), session du rôle du cas.
private struct Seeded {
    let api: any PosAPI
    let local: LocalPosAPI?
    let memory: InMemoryPosAPI?
    let fake: FakePrinterTransport?
    let orderId: UUID
    let lineId: UUID
    let table: String
}

private func seed(_ c: VoidCase, on backend: VoidBackend) async throws -> Seeded {
    let fake = FakePrinterTransport()
    var local: LocalPosAPI?, memory: InMemoryPosAPI?
    let api: any PosAPI
    switch backend {
    case .local:
        let l = try await makePrintAPI(clock: LocalTestClock(), transport: fake)
        for seeded in try await l.printers() { try await l.setTestPrinterActive(seeded.id, false) }
        try await l.addTestPrinter(Printer(name: "Chaud", ipAddress: "10.0.0.1", assignedStationIds: ["HOT_KITCHEN"], textMode: true))
        local = l; api = l
    case .memory:
        let m = InMemoryPosAPI()
        _ = try await m.login(pin: VoidRole.manager.rawValue)
        memory = m; api = m
    }
    let product = try #require(try await api.products().first { $0.name == burgerName })
    var input = OrderItemInput(
        productId: product.id, productName: product.name, quantity: VoidCase.lineQuantity, unitPrice: product.price, taxRatePercent: product.taxRatePercent,
        preparationStationId: product.preparationStationId, modifiers: [], course: .direct, modifiersPriceExtra: .zero,
        taxRateTakeawayPercent: product.taxRateTakeawayPercent, isHappyHourApplied: false, originalUnitPrice: nil, appliedHappyHourScheduleId: nil
    )
    if c.feature == .modifiers { input.modifiers = ["Bacon", "Saignant"]; input.modifiersPriceExtra = Money(cents: 250) }

    let table = c.feature == .counter ? DiningTable.counterNumber : "T1"
    if c.feature == .counter { _ = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway) }
    else { try await api.openTable(number: table, covers: 2, operatorId: nil, waiterName: "Sophie") }
    if c.feature == .twoTickets {
        var first = input; first.quantity = 1
        _ = try await api.addItems(table: table, items: [first])
        try await api.dispatch(table: table)
    }
    var order = try await api.addItems(table: table, items: [input])
    let line = try #require(order.lines.last)
    if c.feature == .takeaway { try await api.setDestination(orderId: order.orderId, destination: .takeaway) }
    if c.dispatched { try await api.dispatch(table: table) }
    if c.feature == .legacy { try await local?.testExec("UPDATE KitchenTicketItems SET OrderItemId = NULL") }
    if c.feature == .comp { try await api.compItem(orderId: order.orderId, lineId: line.lineId, reason: "Offert", operatorId: nil) }
    if c.feature == .discount { try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 10, reason: "Fidélité", operatorId: nil) }
    order = try #require(try await api.activeOrder(table: table))

    if c.receipts != .none {
        let total = OrderMath.totals(lines: order.lines.map(CartLine.init(serverLine:)), destination: order.destination, discount: order.globalDiscount).totalTtc.cents
        let amount = (c.receipts == .partial || c.receipts == .partialVoided) ? 500 : total
        _ = try await api.pay(localPayment(order, table: table, tenders: [localTender(.cash, amount)]))
        if c.receipts == .partialVoided || c.receipts == .fullVoided, let local {
            for id in try await local.testReceiptIds(order.orderId) { _ = try await local.voidReceipt(receiptId: id) }
        }
    }
    if let status = c.status { try await local?.testExec("UPDATE Orders SET Status = \(status.rawValue) WHERE Id = '\(order.orderId.uuidString)'") }
    try await local?.deleteTestJobs()
    if c.role != .manager { _ = try await api.login(pin: c.role.rawValue) }
    return Seeded(api: api, local: local, memory: memory, fake: fake, orderId: order.orderId, lineId: line.lineId, table: table)
}

/// Lignes, total TTC et articles de bon de la commande, quel que soit l'état de la table.
private struct VoidSnapshot: Equatable {
    var lines: [UUID: Int]
    var totalCents: Int
    var ticketItems: [UUID: (quantity: Int, cancelled: Int, status: TicketItemStatus)]

    static func == (a: VoidSnapshot, b: VoidSnapshot) -> Bool {
        a.lines == b.lines && a.totalCents == b.totalCents
            && a.ticketItems.mapValues { "\($0.quantity)/\($0.cancelled)/\($0.status.rawValue)" } == b.ticketItems.mapValues { "\($0.quantity)/\($0.cancelled)/\($0.status.rawValue)" }
    }
}

private func snapshot(_ s: Seeded) async throws -> VoidSnapshot {
    let order: ActiveOrder?
    if let local = s.local { order = try await local.testOrder(s.orderId) } else { order = await s.memory?.orders[s.orderId]?.order }
    let o = try #require(order)
    let total = OrderMath.totals(lines: o.lines.map(CartLine.init(serverLine:)), destination: o.destination, discount: o.globalDiscount).totalTtc.cents
    let items = try await s.api.kitchenTickets().filter { $0.orderId == s.orderId }.flatMap(\.items)
    return VoidSnapshot(lines: Dictionary(uniqueKeysWithValues: o.lines.map { ($0.lineId, $0.quantity) }), totalCents: total,
                        ticketItems: Dictionary(uniqueKeysWithValues: items.map { ($0.id, ($0.quantity, $0.cancelledQuantity, $0.status)) }))
}

/// Le message et le statut exacts du .NET (textes FR). Le double en mémoire ne connaît pas les numéros de reçu : préfixe et suffixe seulement.
private func matches(_ error: Error?, _ expected: VoidOutcome) -> Bool {
    guard let error = error as? APIError else { return false }
    switch (expected, error) {
    case (.forbidden, .forbidden(let m)): return m == "Autorisation insuffisante : code PIN superviseur ou gérant requis."
    case (.notFound, .notFound(let m)): return m == "Commande introuvable."
    case (_, .server(let status, let m?)):
        switch expected {
        case .reasonRequired: return status == 400 && m == "Le motif est obligatoire pour annuler une ligne envoyée en cuisine."
        case .reasonTooLong: return status == 400 && m == "Le motif ne peut pas dépasser 256 caractères."
        case .invalidQuantity: return status == 400 && m == "La quantité à retirer doit être comprise entre 1 et celle de la ligne."
        case .notModifiable: return status == 409 && m == "Cette commande n'est plus modifiable."
        case .hasReceipts: return status == 409 && m.hasPrefix("Cette commande a déjà des reçus") && m.hasSuffix(" : annulez-les avant de modifier ses lignes.")
        default: return false
        }
    default: return false
    }
}

@Suite("Matrice d'annulation de ligne (autonome et mémoire)")
struct LineVoidMatrixTests {
    @Test(arguments: voidCases)
    func localBackend(_ c: VoidCase) async throws { if c.backends.contains(.local) { try await run(c, .local) } }

    @Test(arguments: voidCases)
    func memoryBackend(_ c: VoidCase) async throws { if c.backends.contains(.memory) { try await run(c, .memory) } }

    private func run(_ c: VoidCase, _ backend: VoidBackend) async throws {
        let s = try await seed(c, on: backend)
        let before = try await snapshot(s)
        let lineBefore = try #require(before.lines[s.lineId])
        let operatorName = try await s.api.login(pin: c.role.rawValue).operatorName   // même session, nom attendu sur la trace
        var thrown: Error?
        do { try await s.api.voidLine(orderId: s.orderId, lineId: s.lineId, quantity: c.quantity, reason: c.reason, operatorId: nil) } catch { thrown = error }
        let after = try await snapshot(s)

        let accepted = c.expected == .voided || c.expected == .corrected
        if accepted {
            #expect(thrown == nil, "\(c.name) : \(String(describing: thrown))")
            // La ligne perd la quantité retirée (disparaît à zéro), les autres lignes ne bougent pas.
            var lines = before.lines
            lines[s.lineId] = lineBefore - c.quantity == 0 ? nil : lineBefore - c.quantity
            #expect(after.lines == lines)
            #expect(after.totalCents == expectedTotal(c, remaining: lineBefore - c.quantity, others: before.totalCents - expectedTotal(c, remaining: lineBefore, others: 0)))
        } else {
            #expect(matches(thrown, c.expected), "\(c.name) : \(String(describing: thrown))")
            #expect(after == before, "refus : rien ne change")
        }

        // Articles de bon : seul celui de la ligne visée est marqué, et seulement sur une annulation (jamais sur une ligne ancienne sans lien).
        let marks = c.expected == .voided && c.feature != .legacy
        #expect(Set(after.ticketItems.keys) == Set(before.ticketItems.keys))
        let changed = after.ticketItems.filter { id, item in before.ticketItems[id].map { $0.cancelled != item.cancelled || $0.status != item.status } ?? true }
        #expect(changed.count == (marks ? 1 : 0), "\(c.name) : un seul article de bon marqué")
        if marks, let (id, item) = changed.first, let old = before.ticketItems[id] {
            // L'article de la ligne visée (3 unités ; l'autre bon du même plat n'en a qu'une).
            #expect(old.quantity == lineBefore && old.cancelled == 0 && item.cancelled == c.quantity)
            #expect((item.status == .cancelled) == (c.quantity == old.quantity))
        }

        guard let local = s.local else { return }
        let rows = try await local.testVoidRows()
        let jet = try await local.journalRows("ORDER_LINE_VOIDED")
        let jobs = try await local.allTestJobs()
        guard c.expected == .voided else {
            #expect(rows.isEmpty && jet.isEmpty && jobs.isEmpty, "\(c.name) : aucune trace")
            return
        }
        let row = try #require(rows.first)
        let trimmed = (c.reason ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
        let unit = c.feature == .modifiers ? burgerCents + 250 : burgerCents   // R9 : prix facturé (liste pour un offert)
        #expect(rows.count == 1 && row.int("Quantity") == c.quantity && row.int("UnitPrice") == unit && row.string("Reason") == trimmed)
        #expect(row.string("ServerName") == (c.feature == .counter ? "" : "Sophie"))  // R2 : serveur de la table, vide au comptoir
        #expect(row.string("TableNumber") == s.table && row.string("OperatorName") == operatorName && operatorName?.isEmpty == false)
        #expect(row.string("ModifiersSummary") == (c.feature == .modifiers ? "Bacon, Saignant" : nil))
        #expect((row.uuid("TicketId") == nil) == (c.feature == .legacy))
        #expect(jet.count == 1)
        let payload = try #require(try JSONSerialization.jsonObject(with: Data(jet[0].payload.utf8)) as? [String: Any])
        #expect(payload["reason"] as? String == trimmed && payload["quantity"] as? Int == c.quantity && payload["unitPriceCents"] as? Int == unit)
        #expect(payload["tableNumber"] as? String == s.table && payload["orderId"] as? String != nil)
        #expect(jobs.count == 1 && jobs[0].kind == .kitchenTicket && !jobs[0].openCashDrawer, "un bon d'annulation par annulation")
    }

    /// Total TTC attendu pour `remaining` unités de la ligne visée, plus `others` (autres lignes, déjà calculées).
    private func expectedTotal(_ c: VoidCase, remaining: Int, others: Int) -> Int {
        switch c.feature {
        case .comp: return others
        case .modifiers: return others + remaining * (burgerCents + 250)
        case .discount: return remaining * burgerCents * 9 / 10   // seule ligne : 10 % exact au centime ici
        default: return others + remaining * burgerCents
        }
    }

    // MARK: Enchaînements et cas propres à l'autonome

    private func localRig() async throws -> (api: LocalPosAPI, fake: FakePrinterTransport, order: UUID, line: UUID) {
        let s = try await seed(VoidCase(name: "base", expected: .voided), on: .local)
        return (try #require(s.local), try #require(s.fake), s.orderId, s.lineId)
    }

    /// Pas de clé d'idempotence (comme le .NET) : le même appel rejoué annule à nouveau tant qu'il reste de la quantité.
    @Test func sameVoidSentTwiceVoidsTwiceThenIsRefused() async throws {
        let (api, _, order, line) = try await localRig()
        try await api.voidLine(orderId: order, lineId: line, quantity: 1, reason: "Doublon", operatorId: nil)
        try await api.voidLine(orderId: order, lineId: line, quantity: 1, reason: "Doublon", operatorId: nil)
        #expect(try await api.testOrder(order)?.lines.first?.quantity == 1)
        #expect(try await api.testVoidRows().count == 2)
        #expect(try await api.journalRows("ORDER_LINE_VOIDED").count == 2)
        #expect(try await api.allTestJobs().count == 2)
        #expect(try await api.testTicketItems().first?.int("CancelledQuantity") == 2)
        await #expect(throws: APIError.server(status: 400, message: "La quantité à retirer doit être comprise entre 1 et celle de la ligne.")) {
            try await api.voidLine(orderId: order, lineId: line, quantity: 2, reason: "Doublon", operatorId: nil)
        }
        try await api.voidLine(orderId: order, lineId: line, quantity: 1, reason: "Doublon", operatorId: nil)
        await #expect(throws: APIError.notFound("Commande introuvable.")) {
            try await api.voidLine(orderId: order, lineId: line, quantity: 1, reason: "Doublon", operatorId: nil)
        }
        // Dernière ligne annulée : commande vide, total 0, toujours sur la table.
        #expect(try await api.testOrder(order)?.lines.isEmpty == true)
        #expect(try await api.activeOrder(table: "T1")?.orderId == order)
        #expect(try await api.testTicketItems().first?.int("Status") == TicketItemStatus.cancelled.rawValue)
    }

    @Test func voidThenCorrectionOfANewUnsentLine() async throws {
        let (api, _, order, line) = try await localRig()
        try await api.voidLine(orderId: order, lineId: line, quantity: 1, reason: "Erreur", operatorId: nil)
        let burger = try await localProduct(api, burgerName)
        let added = try await api.addItems(table: "T1", items: [localInput(burger, quantity: 2)])
        let unsent = try #require(added.lines.first { !$0.isDispatched })
        _ = try await api.login(pin: VoidRole.waiter.rawValue)
        try await api.voidLine(orderId: order, lineId: unsent.lineId, quantity: 2, reason: nil, operatorId: nil)
        let lines = try #require(try await api.testOrder(order)?.lines)
        #expect(lines.count == 1 && lines[0].lineId == line && lines[0].quantity == 2)
        #expect(try await api.testVoidRows().count == 1)
        #expect(try await api.allTestJobs().count == 1)
    }

    @Test func correctionThenDispatchThenVoid() async throws {
        let s = try await seed(VoidCase(name: "non envoyée", role: .waiter, dispatched: false, expected: .corrected), on: .local)
        let api = try #require(s.local)
        try await api.voidLine(orderId: s.orderId, lineId: s.lineId, quantity: 1, reason: nil, operatorId: nil)
        try await api.dispatch(table: "T1")
        try await api.deleteTestJobs()   // bon cuisine de l'envoi
        // Le serveur ne corrige plus une ligne envoyée.
        await #expect(throws: APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")) {
            try await api.voidLine(orderId: s.orderId, lineId: s.lineId, quantity: 1, reason: nil, operatorId: nil)
        }
        _ = try await api.login(pin: VoidRole.manager.rawValue)
        try await api.voidLine(orderId: s.orderId, lineId: s.lineId, quantity: 1, reason: "Erreur", operatorId: nil)
        let item = try #require(try await api.testTicketItems().first)
        #expect(item.int("Quantity") == 2 && item.int("CancelledQuantity") == 1 && item.int("Status") == TicketItemStatus.pending.rawValue)
        #expect(try await api.testVoidRows().count == 1)
        #expect(try await api.allTestJobs().count == 1)
    }

    @Test func noPrinterForTheStationStillVoids() async throws {
        let (api, _, order, line) = try await localRig()
        for printer in try await api.printers() { try await api.setTestPrinterActive(printer.id, false) }
        try await api.voidLine(orderId: order, lineId: line, quantity: 1, reason: "Erreur", operatorId: nil)
        #expect(try await api.testVoidRows().count == 1)
        #expect(try await api.journalRows("ORDER_LINE_VOIDED").count == 1)
        #expect(try await api.allTestJobs().isEmpty)
    }

    /// Poste de la ligne d'abord, sinon celui de l'article (`LocalStations.resolve`, comme `PreparationStations.Resolve`).
    @Test(arguments: [("BAR" as String?, "Bar"), (nil, "Chaud")])
    func stationOverrideRoutesTheSlip(station: String?, printerName: String) async throws {
        let api = try await makePrintAPI(clock: LocalTestClock(), transport: FakePrinterTransport())
        for seeded in try await api.printers() { try await api.setTestPrinterActive(seeded.id, false) }
        try await api.addTestPrinter(Printer(name: "Chaud", ipAddress: "10.0.0.1", assignedStationIds: ["HOT_KITCHEN"], textMode: true))
        try await api.addTestPrinter(Printer(name: "Bar", ipAddress: "10.0.0.2", assignedStationIds: ["BAR"], textMode: true))
        var input = localInput(try await localProduct(api, burgerName))
        input.preparationStationId = station
        try await seatTable(api, "T1", items: [input])
        try await api.dispatch(table: "T1")
        let order = try #require(try await api.activeOrder(table: "T1"))
        try await api.deleteTestJobs()
        try await api.voidLine(orderId: order.orderId, lineId: try #require(order.lines.first).lineId, quantity: 1, reason: "Erreur", operatorId: nil)
        let jobs = try await api.allTestJobs()
        let target = try #require(try await api.printers().first { $0.name == printerName })
        #expect(jobs.count == 1 && jobs[0].printerId == target.id)
    }

    /// Le JET échoue (trigger RAISE(ABORT)) : la transaction entière est annulée, aucun bon n'est mis en file.
    @Test func journalFailureRollsBackEverything() async throws {
        let (api, _, order, line) = try await localRig()
        let before = try await api.testTicketItems().map { "\($0.int("CancelledQuantity") ?? -1)/\($0.int("Status") ?? -1)" }
        try await api.testExec("CREATE TRIGGER fail_jet BEFORE INSERT ON JournalEntries WHEN NEW.EventType = 'ORDER_LINE_VOIDED' BEGIN SELECT RAISE(ABORT, 'jet en panne'); END;")
        await #expect(throws: (any Error).self) { try await api.voidLine(orderId: order, lineId: line, quantity: 2, reason: "Erreur", operatorId: nil) }
        #expect(try await api.testOrder(order)?.lines.first?.quantity == VoidCase.lineQuantity)
        #expect(try await api.testVoidRows().isEmpty)
        #expect(try await api.allTestJobs().isEmpty)
        #expect(try await api.testTicketItems().map { "\($0.int("CancelledQuantity") ?? -1)/\($0.int("Status") ?? -1)" } == before)
    }

    /// Rôle relu en base à chaque appel : un responsable rétrogradé après sa connexion ne peut plus annuler.
    @Test(arguments: VoidBackend.allCases)
    func managerDemotedAfterLoginIsForbidden(_ backend: VoidBackend) async throws {
        let s = try await seed(VoidCase(name: "base", expected: .voided), on: backend)
        let login = try await s.api.login(pin: VoidRole.manager.rawValue)
        let manager = try #require(login.operatorId)
        _ = try await s.api.login(pin: VoidRole.admin.rawValue)
        try await s.api.updateStaff(id: manager, name: try #require(login.operatorName), role: .waiter, pin: nil, isActive: true)
        await s.api.setToken(login.token)
        let before = try await snapshot(s)
        await #expect(throws: APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")) {
            try await s.api.voidLine(orderId: s.orderId, lineId: s.lineId, quantity: 1, reason: "Erreur", operatorId: nil)
        }
        #expect(try await snapshot(s) == before)
    }

    /// Compte désactivé après la connexion : aucune annulation. Le .NET et le double en mémoire refusent (403) ;
    /// l'autonome refuse toute session d'un compte inactif (`requireAuth`, règle commune à toutes ses routes) : 401.
    @Test(arguments: VoidBackend.allCases)
    func managerDeactivatedAfterLogin(_ backend: VoidBackend) async throws {
        let s = try await seed(VoidCase(name: "base", expected: .voided), on: backend)
        let login = try await s.api.login(pin: VoidRole.manager.rawValue)
        _ = try await s.api.login(pin: VoidRole.admin.rawValue)
        try await s.api.updateStaff(id: try #require(login.operatorId), name: try #require(login.operatorName), role: .floorManager, pin: nil, isActive: false)
        await s.api.setToken(login.token)
        let before = try await snapshot(s)
        switch backend {
        case .local:
            await #expect(throws: APIError.unauthorized) { try await s.api.voidLine(orderId: s.orderId, lineId: s.lineId, quantity: 1, reason: "Erreur", operatorId: nil) }
            #expect(try await snapshot(s) == before)
        case .memory:
            await #expect(throws: APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")) { try await s.api.voidLine(orderId: s.orderId, lineId: s.lineId, quantity: 1, reason: "Erreur", operatorId: nil) }
            #expect(try await snapshot(s) == before)
        }
    }

    @Test(arguments: VoidBackend.allCases, [true, false])
    func noSessionIs401AndChangesNothing(_ backend: VoidBackend, dispatched: Bool) async throws {
        let s = try await seed(VoidCase(name: "base", dispatched: dispatched, expected: .voided), on: backend)
        let before = try await snapshot(s)
        await s.api.setToken(nil)
        await #expect(throws: APIError.unauthorized) { try await s.api.voidLine(orderId: s.orderId, lineId: s.lineId, quantity: 1, reason: "Erreur", operatorId: nil) }
        _ = try await s.api.login(pin: VoidRole.manager.rawValue)
        #expect(try await snapshot(s) == before)
    }

    @Test(arguments: VoidBackend.allCases)
    func unknownOrderOrLineOfAnotherOrderIs404(_ backend: VoidBackend) async throws {
        let s = try await seed(VoidCase(name: "base", expected: .voided), on: backend)
        await #expect(throws: APIError.notFound("Commande introuvable.")) {
            try await s.api.voidLine(orderId: UUID(), lineId: s.lineId, quantity: 1, reason: "Erreur", operatorId: nil)
        }
        await #expect(throws: APIError.notFound("Commande introuvable.")) {
            try await s.api.voidLine(orderId: s.orderId, lineId: UUID(), quantity: 1, reason: "Erreur", operatorId: nil)
        }
    }
}
