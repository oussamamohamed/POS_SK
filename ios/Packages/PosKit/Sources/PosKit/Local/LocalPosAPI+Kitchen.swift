import Foundation

extension LocalPosAPI {
    /// Envoie en cuisine les lignes pas encore envoyées : un bon par poste de préparation (ligne → article → famille → cuisine chaude),
    /// puis marque les lignes comme envoyées. Rien à envoyer : succès sans bon. Table ou commande absente : `404`.
    public func dispatch(table number: String) async throws {
        try requireAuth()
        let floor = floorRepository, orders = orderRepository, kitchen = kitchenRepository, catalog = catalogRepository
        try db.transaction {
            guard let table = try floor.table(number), let orderId = table.activeOrderId, let order = try orders.order(id: orderId) else {
                throw APIError.notFound(nil)
            }
            let pending = order.lines.filter { !$0.isDispatched }
            guard !pending.isEmpty else { return }
            let fallbacks = try catalog.stationFallbacks()
            // Un groupe par poste, dans l'ordre d'apparition des lignes (bons déterministes).
            var groups: [(station: String, lines: [OrderLine])] = []
            for line in pending {
                let fallback = fallbacks[line.productId]
                let station = LocalStations.resolve(item: line.preparationStationId, product: fallback?.product, category: fallback?.category)
                if let index = groups.firstIndex(where: { $0.station == station }) {
                    groups[index].lines.append(line)
                } else {
                    groups.append((station: station, lines: [line]))
                }
            }
            let displayTable: String
            if order.destination == .takeaway {
                let reference = order.pickupNumber.flatMap { $0.isEmpty ? nil : $0 } ?? order.tableNumber
                let buzzer = order.pickupBuzzer.flatMap { $0.isEmpty ? nil : " (Bip: \($0))" } ?? ""
                displayTable = "[À EMPORTER] \(reference)\(buzzer)"
            } else {
                displayTable = order.tableNumber
            }
            for group in groups {
                let ticket = KitchenTicket(
                    orderId: orderId, tableNumber: displayTable, serverName: table.assignedWaiterName ?? "Serveur", coversCount: table.coversCount,
                    stationId: group.station,
                    items: group.lines.map {
                        KitchenTicketItem(productName: $0.productName, quantity: $0.quantity,
                                          modifiersSummary: $0.modifiersSummary.isEmpty ? nil : $0.modifiersSummary.joined(separator: ", "))
                    }
                )
                try kitchen.insert(ticket, productIds: group.lines.map(\.productId))
            }
            try orders.markDispatched(orderId: orderId)
            try orders.setStatus(orderId: orderId, .sentToKitchen)
        }
    }

    /// Réclame la suite : bon unique (cuisine chaude) avec les lignes « suite » de la commande. Sans ligne « suite », aucun bon
    /// (le .NET en créerait un sans article). Table ou commande absente : sans effet, comme le serveur.
    public func fireSuite(table number: String) async throws {
        try requireAuth()
        let floor = floorRepository, orders = orderRepository, kitchen = kitchenRepository
        try db.transaction {
            guard let table = try floor.table(number), let orderId = table.activeOrderId, let order = try orders.order(id: orderId) else { return }
            let suite = order.lines.filter { $0.course == .suite }
            guard !suite.isEmpty else { return }
            let ticket = KitchenTicket(
                orderId: orderId, tableNumber: number, serverName: "", coversCount: 1, stationId: LocalStations.hotKitchen,
                items: suite.map {
                    KitchenTicketItem(productName: "[RÉCLAME SUITE] \($0.productName)", quantity: $0.quantity,
                                      modifiersSummary: $0.modifiersSummary.isEmpty ? nil : $0.modifiersSummary.joined(separator: ", "))
                }
            )
            try kitchen.insert(ticket, productIds: suite.map(\.productId))
        }
    }

    /// Les 50 derniers bons, tous statuts (comme `GET /api/kds/tickets`, accessible sans connexion).
    public func kitchenTickets() async throws -> [KitchenTicket] {
        try kitchenRepository.recentTickets(limit: 50)
    }

    public func bumpTicket(id: UUID) async throws {
        let member = try requireAuth()
        guard member.role.canBumpKitchen else { throw APIError.forbidden("Réservé à la cuisine et aux responsables.") }
        guard try kitchenRepository.bump(id: id) else { throw APIError.notFound(nil) }
    }
}
