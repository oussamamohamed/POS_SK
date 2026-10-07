import Foundation

struct LocalSettlement {
    let receiptNumber: String
    let totalPaid: Money
    let changeGiven: Money
    let remaining: Money
}

extension LocalPosAPI {
    static let paymentFailed = APIError.server(status: 400, message: "Échec de l'encaissement.")

    /// Identifiant de terminal utilisable dans un numéro de reçu (le poste autonome s'appellera `T01` au plan 1d).
    func normalizedTerminal(_ id: String) -> String {
        let trimmed = id.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty ? "LOCAL" : trimmed
    }

    /// Total TTC de la commande, pourboire déjà enregistré et montant déjà réglé.
    func balance(of order: ActiveOrder) throws -> (total: Money, tip: Money, paid: Money) {
        let total = decorate(order).totalTtcAmount ?? .zero
        let tip = Money(cents: try orderRepository.tipCents(of: order.orderId))
        let paid = Money(cents: try paymentRepository.paidCents(orderId: order.orderId))
        return (total, tip, paid)
    }

    /// Clôture la commande (réglée ou annulée) et libère sa table si elle y était encore.
    func closeOrder(_ order: ActiveOrder, as status: LocalOrderStatus) throws {
        try orderRepository.setStatus(orderId: order.orderId, status)
        if let table = try floorRepository.table(order.tableNumber), table.activeOrderId == order.orderId {
            try floorRepository.update(table.tableNumber, status: .free, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: nil, openedAt: nil)
        }
    }

    /// Enregistre des règlements sur une commande. Refuse (sans rien écrire) une liste vide, un montant ≤ 0, un total supérieur au solde
    /// dû ou une commande déjà réglée ou annulée. À appeler dans une transaction.
    func settle(order: ActiveOrder, tenders: [(method: PaymentMethod, amount: Money, tendered: Money)], terminalId: String) throws -> LocalSettlement {
        guard !tenders.isEmpty, tenders.allSatisfy({ $0.amount > .zero && $0.tendered >= .zero }),
              try orderRepository.status(of: order.orderId)?.isModifiable == true
        else { throw Self.paymentFailed }
        let (total, tip, paid) = try balance(of: order)
        let remainingBefore = (total + tip - paid).clampedAtZero()
        let totalPaid = tenders.reduce(Money.zero) { $0 + $1.amount }
        guard totalPaid <= remainingBefore else { throw Self.paymentFailed }

        let totalTendered = tenders.reduce(Money.zero) { $0 + $1.tendered }
        let change = (totalTendered - remainingBefore).clampedAtZero()
        let terminal = normalizedTerminal(terminalId)
        let receipt = try paymentRepository.nextReceiptNumber(terminalId: terminal)
        for tender in tenders {
            let tenderChange = tender.method == .cash ? (tender.tendered - tender.amount).clampedAtZero() : .zero
            try paymentRepository.insertTender(
                orderId: order.orderId, terminalId: terminal, receiptNumber: receipt, method: tender.method,
                amount: tender.amount.cents, tendered: tender.tendered.cents, change: tenderChange.cents
            )
        }
        let remaining = remainingBefore - totalPaid
        if remaining == .zero { try closeOrder(order, as: .paid) }
        return LocalSettlement(receiptNumber: receipt, totalPaid: totalPaid, changeGiven: change, remaining: remaining)
    }

    /// Règlement d'une table, en une ou plusieurs fois (note partagée). Le pourboire n'est accepté qu'au paiement qui solde la note.
    public func pay(_ request: PaymentRequest) async throws -> PaymentResult {
        try requireAuth()
        guard request.tipAmount >= .zero else { throw APIError.server(status: 400, message: "Montant de pourboire invalide.") }
        let floor = floorRepository, orders = orderRepository
        return try db.transaction { () throws -> PaymentResult in
            let notFound = APIError.server(status: 400, message: "Commande introuvable pour ce règlement.")
            let orderId: UUID
            if let id = request.orderId, try orders.order(id: id) != nil {
                orderId = id
            } else if let active = try floor.table(request.tableNumber)?.activeOrderId, try orders.order(id: active) != nil {
                orderId = active
            } else {
                throw notFound
            }
            guard let order = try orders.order(id: orderId) else { throw notFound }
            let tenders = request.tenders.map { (method: $0.method, amount: $0.amount, tendered: $0.tendered) }
            if request.tipAmount > .zero {
                let (total, tip, paid) = try balance(of: order)
                let remaining = total + tip - paid
                let offered = tenders.reduce(Money.zero) { $0 + $1.amount }
                guard offered >= remaining + request.tipAmount else {
                    throw APIError.server(status: 400, message: "Le pourboire ne peut être ajouté qu'au paiement qui solde la note.")
                }
                try orders.setTip(orderId: orderId, cents: tip.cents + request.tipAmount.cents)
            }
            let settlement = try settle(order: order, tenders: tenders, terminalId: request.terminalId)
            return PaymentResult(
                receiptNumber: settlement.receiptNumber, totalPaid: settlement.totalPaid, changeGiven: settlement.changeGiven,
                remainingBalance: settlement.remaining, fiscalSignature: nil, printQueued: false
            )
        }
    }
}
