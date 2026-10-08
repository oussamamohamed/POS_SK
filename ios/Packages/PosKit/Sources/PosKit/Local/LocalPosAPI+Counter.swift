import Foundation

extension LocalPosAPI {
    /// Table « Comptoir » (ventes directes), créée au besoin.
    private func counterTable() throws -> DiningTable {
        let counter = DiningTable.counterNumber
        if try !floorRepository.tableExists(counter) { try floorRepository.insert(DiningTable(tableNumber: counter, capacity: 1)) }
        guard let table = try floorRepository.table(counter) else { throw SQLiteError(code: -1, message: "Table Comptoir introuvable après création") }
        return table
    }

    /// Panier courant du comptoir : le crée s'il n'y en a pas (la destination demandée ne s'applique qu'à un nouveau panier).
    public func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder {
        try requireAuth()
        let floor = floorRepository, orders = orderRepository
        try db.transaction {
            let table = try counterTable()
            if let id = table.activeOrderId, try orders.order(id: id) != nil { return }
            let created = ActiveOrder(tableNumber: DiningTable.counterNumber, destination: destination)
            try orders.insert(created, operatorId: localNoOperator, status: .open)
            try floor.update(DiningTable.counterNumber, status: .occupied, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: created.orderId, openedAt: Date())
        }
        guard let order = try activeOrder(on: try counterTable()) else { throw SQLiteError(code: -1, message: "Panier du comptoir introuvable après création") }
        return order
    }

    /// Met un panier en attente et libère le comptoir. Refusé si le panier est vide, déjà en attente ou déjà partiellement réglé
    /// (son annulation laisserait des encaissements sur une commande annulée).
    public func holdOrder(orderId: UUID, terminalId: String, label: String) async throws {
        let member = try requireAuth()
        let floor = floorRepository, orders = orderRepository, holds = holdRepository, payments = paymentRepository
        try db.transaction {
            guard let order = try orders.order(id: orderId), !order.lines.isEmpty, try orders.status(of: orderId)?.isModifiable == true else {
                throw APIError.server(status: 400, message: "Impossible de mettre en attente un panier vide.")
            }
            guard try !holds.hasActiveHold(orderId: orderId) else { throw APIError.server(status: 409, message: "Cette commande est déjà en attente.") }
            guard try payments.paidCents(orderId: orderId) == 0 else {
                throw APIError.server(status: 409, message: "Impossible de mettre en attente une commande déjà partiellement réglée.")
            }
            let decorated = decorate(order)
            let snapshot = String(decoding: try JSONEncoder().encode(decorated), as: UTF8.self)
            try holds.insert(
                orderId: orderId, terminalId: normalizedTerminal(terminalId), label: label.isEmpty ? nil : label,
                destination: order.destination, itemCount: order.lines.reduce(0) { $0 + $1.quantity }, total: decorated.totalTtcAmount ?? .zero,
                snapshot: snapshot, staffId: member.id
            )
            if let table = try floor.table(order.tableNumber), table.activeOrderId == orderId {
                try floor.update(table.tableNumber, status: .free, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: nil, openedAt: nil)
            }
        }
    }

    public func heldOrders(terminalId: String) async throws -> [HeldOrder] {
        try requireAuth()
        return try holdRepository.active(terminalId: normalizedTerminal(terminalId))
    }

    /// Remet un panier en attente sur le comptoir. Un panier de comptoir vide est annulé et remplacé ; une vente en cours n'est jamais
    /// écrasée (`409`) : la commande en attente reste alors disponible.
    public func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder {
        try requireAuth()
        let floor = floorRepository, orders = orderRepository, holds = holdRepository
        try db.transaction {
            let notFound = APIError.notFound("Commande en attente introuvable ou déjà rappelée.")
            guard let orderId = try holds.activeOrderId(holdId: holdId), try orders.order(id: orderId) != nil,
                  try orders.status(of: orderId)?.isModifiable == true
            else { throw notFound }
            let table = try counterTable()
            if let current = table.activeOrderId, let existing = try orders.order(id: current) {
                guard existing.lines.isEmpty else { throw APIError.server(status: 409, message: "Le comptoir a déjà une vente en cours.") }
                try orders.setStatus(orderId: current, .cancelled)
            }
            _ = try holds.markRecalled(id: holdId)
            // Une commande venue de salle devient une vente au comptoir : sa clôture libérera bien le comptoir.
            try orders.setTableNumber(orderId: orderId, DiningTable.counterNumber)
            try floor.update(DiningTable.counterNumber, status: .occupied, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: orderId, openedAt: table.openedAtUtc ?? Date())
        }
        guard let order = try activeOrder(on: try counterTable()) else { throw SQLiteError(code: -1, message: "Panier rappelé introuvable après écriture") }
        return order
    }

    /// Annule une commande en attente, sur PIN d'un responsable. Les échecs de PIN comptent dans le même verrouillage que la connexion.
    public func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws {
        try requireAuth()
        guard !supervisorPin.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw APIError.server(status: 400, message: "Code PIN superviseur requis.")
        }
        try ensurePinAttemptsAllowed()
        let insufficient = APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")
        guard let supervisor = try staffRepository.activeMember(pin: supervisorPin) else {
            try recordFailedPin()
            throw insufficient
        }
        guard supervisor.role.isManager else { throw insufficient }
        try resetFailedPins()
        let orders = orderRepository, holds = holdRepository
        try db.transaction {
            guard let orderId = try holds.activeOrderId(holdId: holdId) else { throw APIError.notFound("Commande en attente introuvable.") }
            guard try orders.status(of: orderId)?.isModifiable == true else { throw APIError.localOrderClosed }
            _ = try holds.markVoided(id: holdId, staffId: supervisor.id, reason: reason)
            try orders.setStatus(orderId: orderId, .cancelled)
        }
    }
}
