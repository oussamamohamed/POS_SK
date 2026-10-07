import Foundation

extension LocalPosAPI {
    /// Une commande prise sur une table de salle est consommée sur place ; seul « Comptoir » (ventes directes) reste à emporter
    /// (`TableManagementService.DestinationForTable`).
    func defaultDestination(forTable number: String) -> OrderDestination {
        number.caseInsensitiveCompare(DiningTable.counterNumber) == .orderedSame ? .takeaway : .eatIn
    }

    /// Renseigne le total de chaque ligne et le total TTC de la commande (même calcul que le serveur : `OrderMath`).
    func decorate(_ order: ActiveOrder) -> ActiveOrder {
        var decorated = order
        let cart = decorated.lines.map(CartLine.init(serverLine:))
        for index in decorated.lines.indices { decorated.lines[index].totalPrice = OrderMath.lineTotal(cart[index]) }
        decorated.totalTtcAmount = OrderMath.totals(lines: cart, destination: decorated.destination, discount: decorated.globalDiscount).totalTtc
        return decorated
    }

    /// Commande active d'une table, avec serveur, couverts et heure d'ouverture de la table.
    func activeOrder(on table: DiningTable) throws -> ActiveOrder? {
        guard let id = table.activeOrderId, var order = try orderRepository.order(id: id) else { return nil }
        order.waiterName = table.assignedWaiterName
        order.coversCount = table.coversCount
        order.openedAtUtc = table.openedAtUtc ?? order.openedAtUtc
        return decorate(order)
    }

    public func openTable(number: String, covers: Int, operatorId: UUID?, waiterName: String?) async throws {
        try requireAuth()
        let covers = covers <= 0 ? 2 : covers
        let floor = floorRepository, orders = orderRepository
        try db.transaction {
            if try !floor.tableExists(number) { try floor.insert(DiningTable(tableNumber: number, capacity: covers)) }
            let existing = try floor.table(number)
            // Écart assumé : une table déjà occupée garde sa commande (le .NET en crée une nouvelle et orpheline l'ancienne).
            let orderId: UUID
            if let id = existing?.activeOrderId, try orders.order(id: id) != nil {
                orderId = id
            } else {
                let created = ActiveOrder(tableNumber: number, destination: defaultDestination(forTable: number))
                try orders.insert(created, operatorId: operatorId ?? localNoOperator, status: .open)
                orderId = created.orderId
            }
            let openedAt = existing?.activeOrderId == nil ? Date() : (existing?.openedAtUtc ?? Date())
            try floor.update(number, status: .occupied, covers: covers, waiterName: waiterName ?? "Serveur", waiterId: operatorId, activeOrderId: orderId, openedAt: openedAt)
        }
    }

    public func activeOrder(table: String) async throws -> ActiveOrder? {
        try requireAuth()
        guard let found = try floorRepository.table(table) else { return nil }
        return try activeOrder(on: found)
    }

    public func addItems(table number: String, items: [OrderItemInput]) async throws -> ActiveOrder {
        try requireAuth()
        guard items.allSatisfy({ $0.quantity > 0 }) else { throw APIError.server(status: 400, message: "La quantité doit être positive.") }
        let floor = floorRepository, orders = orderRepository
        try db.transaction {
            if try !floor.tableExists(number) { try floor.insert(DiningTable(tableNumber: number, capacity: 2)) }
            guard let table = try floor.table(number) else { throw SQLiteError(code: -1, message: "Table \(number) introuvable après création") }
            let orderId: UUID
            if let id = table.activeOrderId, try orders.order(id: id) != nil {
                orderId = id
            } else {
                let created = ActiveOrder(tableNumber: number, destination: defaultDestination(forTable: number))
                try orders.insert(created, operatorId: localNoOperator, status: .open)
                orderId = created.orderId
            }
            var lines = try orders.lines(orderId: orderId)
            for input in items {
                let sameLine = lines.firstIndex {
                    !$0.isDispatched && $0.productId == input.productId && $0.course == input.course && $0.unitPrice == input.unitPrice
                        && $0.isHappyHourApplied == input.isHappyHourApplied && $0.modifiersPriceExtra == input.modifiersPriceExtra
                        && $0.modifiersSummary == input.modifiers
                }
                if let index = sameLine {
                    lines[index].quantity += input.quantity
                    try orders.setQuantity(lineId: lines[index].lineId, quantity: lines[index].quantity)
                } else {
                    let line = OrderLine(
                        productId: input.productId, productName: input.productName, quantity: input.quantity, unitPrice: input.unitPrice,
                        taxRatePercent: input.taxRatePercent, preparationStationId: input.preparationStationId, modifiersSummary: input.modifiers,
                        course: input.course, modifiersPriceExtra: input.modifiersPriceExtra, taxRateTakeawayPercent: input.taxRateTakeawayPercent,
                        isHappyHourApplied: input.isHappyHourApplied, originalUnitPrice: input.originalUnitPrice,
                        appliedHappyHourScheduleId: input.appliedHappyHourScheduleId
                    )
                    try orders.insert(line, orderId: orderId)
                    lines.append(line)
                }
            }
            try floor.update(
                number, status: table.activeOrderId == orderId ? table.status : .occupied, covers: table.coversCount,
                waiterName: table.assignedWaiterName, waiterId: try floor.waiterId(of: number), activeOrderId: orderId,
                openedAt: table.openedAtUtc ?? Date()
            )
        }
        guard let table = try floorRepository.table(number), let order = try activeOrder(on: table) else {
            throw SQLiteError(code: -1, message: "Commande de la table \(number) introuvable après écriture")
        }
        return order
    }

    public func setDestination(orderId: UUID, destination: OrderDestination) async throws {
        try requireAuth()
        guard try orderRepository.setDestination(orderId: orderId, destination) else { throw APIError.notFound("Commande introuvable.") }
    }
}
