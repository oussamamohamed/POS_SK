import Foundation
import Observation

/// Commande en cours de saisie (table ou comptoir).
///
/// Les articles touchés sont d'abord gardés localement (`pending`), ce qui rend la saisie
/// instantanée même en coup de feu ; ils sont enregistrés sur le serveur à l'envoi en cuisine,
/// à l'encaissement, à la mise en attente ou lorsqu'on quitte la table.
@MainActor
@Observable
public final class OrderModel {
    public private(set) var context: OrderContext?
    public private(set) var order: ActiveOrder?
    public private(set) var pending: [PendingLine] = []
    public var currentCourse: CourseType = .direct
    public private(set) var destination: OrderDestination = .takeaway
    public private(set) var isBusy = false
    /// Incrémenté à chaque ajout : sert de déclencheur au retour haptique.
    public private(set) var addCounter = 0

    private let ctx: POSContext

    public init(context: POSContext) {
        self.ctx = context
    }

    // MARK: Lecture

    public var serverLines: [OrderLine] { order?.lines ?? [] }
    public var sentLines: [OrderLine] { serverLines.filter(\.isDispatched) }
    public var savedUnsentLines: [OrderLine] { serverLines.filter { !$0.isDispatched } }
    public var hasPending: Bool { !pending.isEmpty }
    public var isEmpty: Bool { serverLines.isEmpty && pending.isEmpty }
    public var itemCount: Int { serverLines.reduce(0) { $0 + $1.quantity } + pending.reduce(0) { $0 + $1.quantity } }

    /// Lignes à envoyer en cuisine (enregistrées non envoyées + saisies locales).
    public var hasUnsentItems: Bool { hasPending || !savedUnsentLines.isEmpty }

    public var pendingTotals: OrderTotals {
        TaxCalculator.totals(pending.map { (ttc: $0.lineTotal, ratePercent: $0.product.taxRatePercent) })
    }

    /// Totaux affichés : ceux calculés par le serveur + estimation des lignes locales.
    public var totals: OrderTotals {
        let server = OrderTotals(
            ht: order?.totalHtAmount ?? .zero,
            vat: order?.totalVatAmount ?? .zero,
            ttc: order?.totalTtcAmount ?? .zero
        )
        return server + pendingTotals
    }

    public var totalDue: Money { totals.ttc }

    // MARK: Contexte

    /// Ouvre une table ou le comptoir. Les saisies locales du contexte précédent sont enregistrées d'abord.
    public func open(_ newContext: OrderContext, destination newDestination: OrderDestination = .takeaway) async {
        if context != nil, context != newContext, hasPending {
            guard await save() else { return }
        }
        context = newContext
        order = nil
        pending = []
        currentCourse = .direct
        await reload(destination: newDestination)
    }

    /// Recharge la commande serveur du contexte courant.
    public func reload(destination newDestination: OrderDestination? = nil) async {
        guard let context else { return }
        isBusy = true
        defer { isBusy = false }
        do {
            switch context {
            case .table(let number):
                order = try await ctx.api.activeOrder(tableNumber: number)
            case .counter:
                let wanted = newDestination ?? destination
                var counterOrder = try await ctx.api.openCounterOrder(terminalId: ctx.terminalId, destination: wanted)
                if counterOrder.destination != wanted {
                    counterOrder = try await ctx.api.switchDestination(orderId: counterOrder.orderId, to: wanted)
                }
                order = counterOrder
                destination = counterOrder.destination
            }
        } catch {
            ctx.report(error)
        }
    }

    /// Quitte le contexte courant (après enregistrement des saisies locales).
    public func close() async {
        if hasPending {
            guard await save() else { return }
        }
        context = nil
        order = nil
        pending = []
    }

    // MARK: Saisie locale

    public func add(_ product: Product, modifiers: [ModifierOption] = [], note: String? = nil) {
        guard context != nil else {
            ctx.notify("Choisissez d'abord une table ou le comptoir.", style: .warning)
            return
        }
        guard product.isSellable else {
            ctx.notify("\(product.name) est indisponible.", style: .warning)
            return
        }
        let line = PendingLine(product: product, modifiers: modifiers, note: note, course: currentCourse)
        if let index = pending.firstIndex(where: { $0.canMerge(with: line) }) {
            pending[index].quantity += 1
        } else {
            pending.append(line)
        }
        addCounter += 1
    }

    public func increment(_ lineID: UUID) {
        guard let index = pending.firstIndex(where: { $0.id == lineID }) else { return }
        pending[index].quantity += 1
    }

    public func decrement(_ lineID: UUID) {
        guard let index = pending.firstIndex(where: { $0.id == lineID }) else { return }
        if pending[index].quantity > 1 {
            pending[index].quantity -= 1
        } else {
            pending.remove(at: index)
        }
    }

    public func remove(_ lineID: UUID) {
        pending.removeAll { $0.id == lineID }
    }

    public func clearPending() {
        pending.removeAll()
    }

    // MARK: Synchronisation serveur

    /// Enregistre les lignes locales. Renvoie `false` en cas d'échec (les lignes restent en local).
    @discardableResult
    public func save() async -> Bool {
        guard let context else { return false }
        guard hasPending else { return true }
        isBusy = true
        defer { isBusy = false }
        do {
            order = try await ctx.api.addItems(tableNumber: context.tableNumber, items: pending.map(\.input))
            pending = []
            return true
        } catch {
            ctx.report(error)
            return false
        }
    }

    /// Enregistre puis envoie en cuisine toutes les lignes non envoyées.
    public func sendToKitchen() async {
        guard let context else { return }
        guard hasUnsentItems else {
            ctx.notify("Rien de nouveau à envoyer en cuisine.", style: .info)
            return
        }
        guard await save() else { return }
        isBusy = true
        defer { isBusy = false }
        do {
            try await ctx.api.dispatch(tableNumber: context.tableNumber)
            if case .table(let number) = context {
                order = try await ctx.api.activeOrder(tableNumber: number)
            } else if var current = order {
                current.lines = current.lines.map { var line = $0; line.isDispatched = true; return line }
                order = current
            }
            ctx.notify("Commande envoyée en cuisine", style: .success)
        } catch {
            ctx.report(error)
        }
    }

    /// Prépare l'encaissement : enregistre les saisies locales et vérifie qu'il y a quelque chose à payer.
    public func prepareCheckout() async -> Bool {
        guard await save() else { return false }
        guard let order, !order.lines.isEmpty, order.totalTtcAmount.isPositive else {
            ctx.notify("La note est vide.", style: .warning)
            return false
        }
        return true
    }

    // MARK: Comptoir

    public func setDestination(_ newDestination: OrderDestination) async {
        guard context == .counter, newDestination != destination else { return }
        destination = newDestination
        guard let orderId = order?.orderId else { return }
        do {
            order = try await ctx.api.switchDestination(orderId: orderId, to: newDestination)
        } catch {
            ctx.report(error)
        }
    }

    /// Met la vente comptoir en attente et libère la caisse pour le client suivant.
    public func hold(label: String?) async -> Bool {
        guard context == .counter else { return false }
        guard await save() else { return false }
        guard let current = order, !current.lines.isEmpty else {
            ctx.notify("Impossible de mettre en attente un panier vide.", style: .warning)
            return false
        }
        do {
            let held = try await ctx.api.holdCounterOrder(orderId: current.orderId, terminalId: ctx.terminalId, label: label)
            ctx.notify("Commande « \(held.customerLabel ?? "Client") » mise en attente", style: .success)
            await reload()
            return true
        } catch {
            ctx.report(error)
            return false
        }
    }

    /// Reprend une commande mise en attente au comptoir.
    public func recall(_ held: HeldOrder) async -> Bool {
        if context != nil, hasPending {
            guard await save() else { return false }
        }
        if context == .counter, let current = order, !current.lines.isEmpty {
            ctx.notify("Mettez d'abord la vente en cours en attente ou encaissez-la.", style: .warning)
            return false
        }
        do {
            let recalled = try await ctx.api.recallHeldOrder(holdId: held.holdId)
            context = .counter
            order = recalled
            pending = []
            destination = recalled.destination
            ctx.notify("Commande « \(held.customerLabel ?? "Client") » reprise", style: .success)
            return true
        } catch {
            ctx.report(error)
            return false
        }
    }

    /// Après un encaissement complet : la table est libérée, le comptoir passe au client suivant.
    public func paymentCompleted() async {
        switch context {
        case .counter:
            order = nil
            pending = []
            await reload()
        case .table, .none:
            context = nil
            order = nil
            pending = []
        }
    }
}
