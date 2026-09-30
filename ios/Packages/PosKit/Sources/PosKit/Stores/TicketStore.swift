import Foundation
import Observation

/// Ticket en cours (table ou comptoir) : lignes, remises, envoi cuisine et encaissement.
@MainActor @Observable
public final class TicketStore {
    public private(set) var tableNumber: String = DiningTable.counterNumber
    public private(set) var orderId: UUID?
    public private(set) var covers: Int = 0
    public private(set) var destination: OrderDestination = .takeaway
    public private(set) var lines: [CartLine] = []
    public private(set) var discount: GlobalDiscount?
    /// Reste dû communiqué par le serveur après un paiement partiel (split).
    public private(set) var remainingBalance: Money?
    /// Le ticket du dernier encaissement a-t-il été mis en file d'impression ? nil = non demandé / inconnu.
    public private(set) var lastPrintQueued: Bool?
    public private(set) var isBusy = false
    public private(set) var heldOrders: [HeldOrder] = []

    private let api: PosAPI
    private let notifier: Notifier
    private let happyHour: HappyHourStore
    private let session: SessionStore
    private let terminalId: @MainActor () -> String
    private let onDeviceUnpaired: @MainActor () -> Void

    public init(api: PosAPI, notifier: Notifier, happyHour: HappyHourStore, session: SessionStore, terminalId: @escaping @MainActor () -> String, onDeviceUnpaired: @escaping @MainActor () -> Void = {}) {
        self.api = api
        self.notifier = notifier
        self.happyHour = happyHour
        self.session = session
        self.terminalId = terminalId
        self.onDeviceUnpaired = onDeviceUnpaired
    }

    // MARK: - Lecture

    public var isCounter: Bool { tableNumber == DiningTable.counterNumber }
    public var isEmpty: Bool { lines.isEmpty }
    public var draftLines: [CartLine] { lines.filter(\.isDraft) }
    public var hasDrafts: Bool { lines.contains(where: \.isDraft) }
    public var hasUndispatched: Bool { lines.contains { !$0.isDispatched } }
    public var itemCount: Int { lines.reduce(0) { $0 + $1.quantity } }

    public var totals: OrderTotals { OrderMath.totals(lines: lines, destination: destination, discount: discount) }

    /// Montant restant à encaisser (tient compte des paiements partiels déjà passés).
    public var amountDue: Money {
        if let remainingBalance { return min(remainingBalance, totals.totalTtc) }
        return totals.totalTtc
    }

    public var title: String { isCounter ? L10n.string("order.counter_title") : String(format: L10n.string("order.table_title"), tableNumber) }

    // MARK: - Chargement

    /// Rappelle la commande d'une table (les brouillons de la table précédente sont d'abord enregistrés).
    public func load(table: String) async {
        if table != tableNumber { await commitDraftsSilently() }
        tableNumber = table
        remainingBalance = nil
        do {
            if let order = try await api.activeOrder(table: table) {
                hydrate(order)
            } else {
                reset(keepingTable: true)
                covers = 0
                destination = table == DiningTable.counterNumber ? .takeaway : .eatIn
            }
        } catch APIError.unauthorized {
            session.handleUnauthorized()
        } catch {
            notifier.error(String(format: L10n.string("order.load_error"), title))
        }
    }

    /// Ouvre (ou reprend) la vente directe au comptoir.
    public func openCounter(destination: OrderDestination = .takeaway) async {
        if tableNumber != DiningTable.counterNumber { await commitDraftsSilently() }
        tableNumber = DiningTable.counterNumber
        remainingBalance = nil
        do {
            let order = try await api.openCounterOrder(terminalId: terminalId(), destination: destination)
            hydrate(order)
            if order.lines.isEmpty { self.destination = destination }
        } catch APIError.unauthorized {
            session.handleUnauthorized()
        } catch {
            reset(keepingTable: true)
            self.destination = destination
            notifier.error(error)
        }
        await refreshHeldOrders()
    }

    func hydrate(_ order: ActiveOrder) {
        let drafts = order.tableNumber == tableNumber ? draftLines : []
        orderId = order.orderId
        covers = order.coversCount
        destination = order.destination
        discount = order.globalDiscount
        lines = order.lines.map(CartLine.init(serverLine:)) + drafts
    }

    private func reset(keepingTable: Bool) {
        orderId = nil
        lines = []
        discount = nil
        remainingBalance = nil
        if !keepingTable { tableNumber = DiningTable.counterNumber }
    }

    // MARK: - Édition locale (brouillons)

    public func add(_ product: Product, selection: ModifierSelection? = nil, course: CourseType = .direct) {
        var unitPrice = product.price
        var original: Money?
        var scheduleId: UUID?
        let hh = happyHour.discountedPrice(for: product, destination: destination)
        if let hh {
            unitPrice = hh.happyHourPrice
            original = hh.standardPrice
            scheduleId = happyHour.status.activeScheduleId
        }
        let comment = selection?.kitchenComment.trimmingCharacters(in: .whitespacesAndNewlines)
        let line = CartLine(
            productId: product.id, name: product.name, unitPrice: unitPrice,
            taxRatePercent: product.taxRatePercent, taxRateTakeawayPercent: product.taxRateTakeawayPercent,
            station: product.station, modifiers: selection?.modifierLabels ?? [],
            modifiersExtra: selection?.extraTotal ?? .zero,
            kitchenComment: (comment?.isEmpty ?? true) ? nil : comment,
            course: course, quantity: 1, isHappyHourApplied: hh != nil,
            originalUnitPrice: original, happyHourScheduleId: scheduleId
        )
        if let index = lines.firstIndex(where: { $0.canMerge(with: line) }) {
            lines[index].quantity += 1
        } else {
            lines.append(line)
        }
    }

    public func increment(_ lineId: UUID) {
        guard let index = lines.firstIndex(where: { $0.id == lineId }) else { return }
        if lines[index].isDraft {
            lines[index].quantity += 1
            return
        }
        // Ligne déjà enregistrée : on ajoute un brouillon identique (fusionné côté serveur).
        var copy = lines[index]
        copy.id = UUID()
        copy.serverLineId = nil
        copy.quantity = 1
        copy.isDispatched = false
        copy.isComp = false
        copy.discountPercent = 0
        if let draftIndex = lines.firstIndex(where: { $0.canMerge(with: copy) }) {
            lines[draftIndex].quantity += 1
        } else {
            lines.append(copy)
        }
    }

    public func decrement(_ lineId: UUID) {
        guard let index = lines.firstIndex(where: { $0.id == lineId }) else { return }
        guard lines[index].isDraft else {
            notifier.warning(L10n.string("order.line_already_sent"))
            return
        }
        lines[index].quantity -= 1
        if lines[index].quantity <= 0 { lines.remove(at: index) }
    }

    public func remove(_ lineId: UUID) {
        guard let index = lines.firstIndex(where: { $0.id == lineId }) else { return }
        guard lines[index].isDraft else {
            notifier.warning(L10n.string("order.line_already_sent"))
            return
        }
        lines.remove(at: index)
    }

    public func cycleCourse(_ lineId: UUID) {
        guard let index = lines.firstIndex(where: { $0.id == lineId }), lines[index].isDraft else { return }
        lines[index].course = lines[index].course.next
    }

    /// Vide les articles non envoyés (les articles déjà en cuisine sont conservés).
    public func clearDrafts() {
        let removed = draftLines.count
        guard removed > 0 else {
            if !lines.isEmpty { notifier.warning(L10n.string("order.drafts_locked")) }
            return
        }
        lines.removeAll(where: \.isDraft)
        notifier.info(lines.isEmpty ? L10n.string("order.cleared") : String(format: L10n.string("order.drafts_removed"), removed))
    }

    // MARK: - Synchronisation serveur

    /// Enregistre les brouillons côté serveur. Renvoie l'identifiant de commande.
    @discardableResult
    public func commitDrafts() async throws -> UUID? {
        let drafts = draftLines
        guard !drafts.isEmpty else { return orderId }
        // Le serveur crée les commandes « à emporter » par défaut : une table est servie sur place,
        // le comptoir garde le mode choisi par le caissier.
        let wanted: OrderDestination = isCounter ? destination : .eatIn
        let order = try await api.addItems(table: tableNumber, items: drafts.map(\.asInput))
        lines.removeAll(where: \.isDraft)
        hydrate(order)
        if order.destination != wanted {
            try? await api.setDestination(orderId: order.orderId, destination: wanted)
            destination = wanted
        }
        return orderId
    }

    private func commitDraftsSilently() async {
        guard hasDrafts else { return }
        do { try await commitDrafts() } catch { notifier.error(error) }
    }

    private func run(_ operation: () async throws -> Void) async -> Bool {
        guard !isBusy else { return false }
        isBusy = true
        defer { isBusy = false }
        do {
            try await operation()
            return true
        } catch APIError.deviceNotPaired {
            // Le brouillon reste sur l'iPad : il sera envoyé après le nouvel appairage.
            onDeviceUnpaired()
            notifier.error(APIError.deviceNotPaired)
            return false
        } catch APIError.unauthorized {
            session.handleUnauthorized()
            notifier.error(APIError.unauthorized)
            return false
        } catch {
            notifier.error(error)
            return false
        }
    }

    // MARK: - Cuisine

    /// Envoie les articles en cuisine. Renvoie `true` si l'envoi a réussi.
    @discardableResult
    public func sendToKitchen() async -> Bool {
        guard !lines.isEmpty else { notifier.warning(L10n.string("order.empty_ticket")); return false }
        guard hasUndispatched else { notifier.info(L10n.string("order.already_in_kitchen")); return false }
        let ok = await run {
            try await commitDrafts()
            try await api.dispatch(table: tableNumber)
        }
        guard ok else { return false }
        notifier.success(String(format: L10n.string("order.sent_to_kitchen"), title))
        if let order = try? await api.activeOrder(table: tableNumber) { hydrate(order) }
        return true
    }

    public func fireSuite() async {
        let ok = await run { try await api.fireSuite(table: tableNumber) }
        if ok { notifier.success(String(format: L10n.string("order.suite_fired"), title)) }
    }

    // MARK: - Destination (comptoir)

    public func switchDestination(_ newValue: OrderDestination) async {
        guard newValue != destination else { return }
        destination = newValue
        if let orderId {
            try? await api.setDestination(orderId: orderId, destination: newValue)
        }
        notifier.info(newValue == .takeaway ? L10n.string("order.mode_takeaway") : L10n.string("order.mode_eat_in"))
    }

    // MARK: - Remises

    public func applyGlobalDiscount(type: DiscountType, value: Decimal, reason: String) async -> Bool {
        guard value > 0 else { notifier.warning(L10n.string("order.discount_invalid_amount")); return false }
        let ok = await run {
            guard let id = try await commitDrafts() else { throw APIError.notFound(L10n.string("order.no_active_order")) }
            try await api.applyDiscount(orderId: id, type: type, value: value, reason: reason, operatorId: session.currentOperator?.id)
            if let order = try await api.activeOrder(table: tableNumber) { hydrate(order) }
        }
        if ok { notifier.success(String(format: L10n.string("order.discount_applied"), GlobalDiscount(type: type, value: value).label)) }
        return ok
    }

    public func removeDiscount() async {
        guard let orderId else { return }
        let ok = await run {
            try await api.removeDiscount(orderId: orderId)
            if let order = try await api.activeOrder(table: tableNumber) { hydrate(order) }
        }
        if ok { notifier.info(L10n.string("order.discount_removed")) }
    }

    /// Enregistre les brouillons avant d'ouvrir l'écran de remise, pour que chaque ligne
    /// proposée à « Offrir » ait un identifiant serveur.
    public func prepareForDiscount() async -> Bool {
        await run { try await commitDrafts() }
    }

    /// Lignes pouvant être offertes (enregistrées et pas encore offertes).
    public var compEligibleLines: [CartLine] { lines.filter { !$0.isDraft && !$0.isComp } }

    public func comp(lineId: UUID, reason: String) async -> Bool {
        guard let line = lines.first(where: { $0.id == lineId }), let serverLineId = line.serverLineId else {
            notifier.warning(L10n.string("order.comp_requires_save"))
            return false
        }
        let ok = await run {
            guard let orderId else { throw APIError.notFound(L10n.string("order.no_active_order")) }
            try await api.compItem(orderId: orderId, lineId: serverLineId, reason: reason, operatorId: session.currentOperator?.id)
            if let order = try await api.activeOrder(table: tableNumber) { hydrate(order) }
        }
        if ok { notifier.success(String(format: L10n.string("order.item_comped"), line.name)) }
        return ok
    }

    // MARK: - Transfert / fusion

    public func transfer(to target: String, merge: Bool) async -> Bool {
        var message: String?
        let ok = await run {
            try await commitDrafts()
            let result = try await api.transfer(from: tableNumber, to: target, merge: merge)
            if result.success == false { throw APIError.server(status: 400, message: result.message) }
            message = result.message
        }
        guard ok else { return false }
        notifier.success(message ?? String(format: L10n.string("order.transferred"), target))
        await load(table: target)
        return true
    }

    // MARK: - Encaissement table

    /// Résultat d'un encaissement, pour l'affichage du rendu monnaie / reste dû.
    public struct CheckoutOutcome: Equatable, Sendable {
        public var receiptNumber: String?
        public var change: Money
        public var remaining: Money
        public var pickupNumber: String?
        public var buzzer: String?
        public var creditVoucher: CreditVoucher?
        public var isComplete: Bool { remaining.cents <= 0 }

        public init(receiptNumber: String?, change: Money, remaining: Money, pickupNumber: String? = nil, buzzer: String? = nil, creditVoucher: CreditVoucher? = nil) {
            self.receiptNumber = receiptNumber
            self.change = change
            self.remaining = remaining
            self.pickupNumber = pickupNumber
            self.buzzer = buzzer
            self.creditVoucher = creditVoucher
        }
    }

    /// Encaisse `amount` (part de split ou totalité + pourboire) avec le moyen `method`.
    public func pay(method: PaymentMethod, amount: Money, tendered: Money, printReceipt: Bool = false) async -> CheckoutOutcome? {
        guard amount.cents > 0 else { notifier.warning(L10n.string("payment.zero_amount")); return nil }
        guard tendered >= amount || method != .cash else {
            notifier.warning(String(format: L10n.string("payment.insufficient_tendered"), tendered.formatted, amount.formatted))
            return nil
        }
        var outcome: CheckoutOutcome?
        let ok = await run {
            try await commitDrafts()
            let change = OrderMath.change(tendered: tendered, due: amount)
            let result = try await api.pay(PaymentRequest(
                orderId: orderId, tableNumber: tableNumber, operatorId: session.currentOperator?.id,
                terminalId: terminalId(),
                tenders: [TenderInput(method: method, amount: amount, tendered: max(tendered, amount), changeGiven: change)],
                requestReceiptPrint: printReceipt
            ))
            lastPrintQueued = result.printQueued
            outcome = CheckoutOutcome(receiptNumber: result.receiptNumber, change: change, remaining: result.remainingBalance)
        }
        guard ok, let outcome else { return nil }
        if outcome.isComplete {
            notifier.success(String(format: L10n.string("payment.paid_receipt"), outcome.receiptNumber ?? "NF"))
            reset(keepingTable: true)
        } else {
            remainingBalance = outcome.remaining
            notifier.info(String(format: L10n.string("payment.remaining_due"), outcome.remaining.formatted))
        }
        return outcome
    }

    // MARK: - Encaissement comptoir

    public func counterCheckout(method: PaymentMethod, amount: Money, tendered: Money, tip: Money, buzzer: String?, printReceipt: Bool, policy: MealVoucherPolicy) async -> CheckoutOutcome? {
        guard !lines.isEmpty else { notifier.warning(L10n.string("order.empty_ticket")); return nil }
        if method == .cash && tendered < amount {
            notifier.warning(String(format: L10n.string("payment.insufficient_amount"), tendered.formatted, amount.formatted))
            return nil
        }
        var outcome: CheckoutOutcome?
        let ok = await run {
            guard let id = try await commitDrafts() else { throw APIError.notFound(L10n.string("order.counter_order_not_found")) }
            let trimmedBuzzer = buzzer?.trimmingCharacters(in: .whitespaces)
            let result = try await api.counterCheckout(CounterCheckoutRequest(
                orderId: id, terminalId: terminalId(), destination: destination,
                pickupBuzzer: (trimmedBuzzer?.isEmpty ?? true) ? nil : trimmedBuzzer,
                tipAmount: tip, requestFiscalReceiptPrint: printReceipt, mealVoucherPolicy: policy,
                tenders: [CounterTender(method: method, amount: min(tendered, amount), tendered: tendered, facialValue: method == .mealVoucher ? tendered : nil)]
            ))
            lastPrintQueued = result.printQueued
            outcome = CheckoutOutcome(
                receiptNumber: result.receiptNumber, change: result.changeGiven, remaining: result.remainingBalance,
                pickupNumber: result.pickupNumber, buzzer: (trimmedBuzzer?.isEmpty ?? true) ? nil : trimmedBuzzer,
                creditVoucher: result.issuedCreditVoucher
            )
        }
        guard ok, let outcome else { return nil }
        if outcome.isComplete {
            notifier.success(String(format: L10n.string("payment.sale_validated"), outcome.pickupNumber ?? ""))
            reset(keepingTable: true)
        } else {
            remainingBalance = outcome.remaining
        }
        return outcome
    }

    // MARK: - Facturation chambre (PMS hôtel)

    public func chargeRoom(_ room: HotelRoom, tip: Money, signaturePNG: Data?) async -> Bool {
        var message: String?
        let ok = await run {
            try await commitDrafts()
            let signature = signaturePNG.map { "data:image/png;base64,\($0.base64EncodedString())" }
            let result = try await api.chargeRoom(RoomChargeRequest(
                orderId: orderId, tableNumber: tableNumber, roomNumber: room.roomNumber, guestName: room.guestName,
                amount: amountDue, tipAmount: tip, signatureDataUrl: signature, notes: "Facturation chambre \(room.roomNumber)"
            ))
            if result.success == false { throw APIError.server(status: 400, message: result.message) }
            message = result.message
        }
        guard ok else { return false }
        notifier.success(message ?? L10n.string("payment.room_charge_validated"))
        reset(keepingTable: true)
        return true
    }

    // MARK: - Commandes en attente (comptoir)

    public func refreshHeldOrders() async {
        heldOrders = (try? await api.heldOrders(terminalId: terminalId())) ?? heldOrders
    }

    /// Nom proposé à la mise en attente : premier « Client #N » pas encore utilisé.
    public var suggestedHoldLabel: String {
        let used = Set(heldOrders.compactMap(\.customerLabel))
        var number = 1
        while used.contains("Client #\(number)") { number += 1 }
        return "Client #\(number)"
    }

    public func hold(label: String) async -> Bool {
        guard !lines.isEmpty else { notifier.warning(L10n.string("payment.empty_ticket_hold")); return false }
        // Nom par défaut envoyé au serveur et journalisé : texte figé, non localisé (cf. règles projet).
        let name = label.trimmingCharacters(in: .whitespaces).isEmpty ? "Client comptoir" : label
        let ok = await run {
            guard let id = try await commitDrafts() else { throw APIError.notFound(L10n.string("order.order_not_found")) }
            try await api.holdOrder(orderId: id, terminalId: terminalId(), label: name)
        }
        guard ok else { return false }
        notifier.success(String(format: L10n.string("payment.order_held"), name))
        reset(keepingTable: true)
        await openCounter(destination: destination)
        return true
    }

    public func recall(_ held: HeldOrder) async -> Bool {
        if hasDrafts, isCounter {
            notifier.warning(L10n.string("payment.hold_current_first"))
            return false
        }
        let ok = await run {
            let order = try await api.recallHeldOrder(holdId: held.holdId)
            tableNumber = DiningTable.counterNumber
            lines = []
            hydrate(order)
        }
        guard ok else { return false }
        notifier.success(L10n.string("payment.order_recalled"))
        await refreshHeldOrders()
        return true
    }

    public func voidHeld(_ held: HeldOrder, supervisorPin: String) async -> Bool {
        guard !supervisorPin.isEmpty else { notifier.warning(L10n.string("payment.supervisor_pin_required")); return false }
        // Motif envoyé au serveur et journalisé : texte figé, non localisé (cf. règles projet).
        let ok = await run {
            try await api.voidHeldOrder(holdId: held.holdId, supervisorPin: supervisorPin, reason: "Annulation au comptoir", terminalId: terminalId())
        }
        guard ok else { return false }
        notifier.success(L10n.string("payment.held_order_voided"))
        await refreshHeldOrders()
        return true
    }
}
