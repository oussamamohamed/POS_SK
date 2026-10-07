import Foundation

/// Résolution du poste de préparation : ligne → article → famille → cuisine chaude (`PreparationStations.Resolve` du .NET).
enum LocalStations {
    static let hotKitchen = "HOT_KITCHEN"

    static func normalize(_ id: String?) -> String? {
        guard let trimmed = id?.trimmingCharacters(in: .whitespacesAndNewlines), !trimmed.isEmpty else { return nil }
        return trimmed
    }

    static func resolve(item: String?, product: String?, category: String?) -> String {
        normalize(item) ?? normalize(product) ?? normalize(category) ?? hotKitchen
    }
}

struct LocalKitchenRepository {
    let db: SQLiteDatabase

    /// `productIds` : un identifiant d'article par élément de `ticket.items`, dans le même ordre.
    func insert(_ ticket: KitchenTicket, productIds: [UUID]) throws {
        guard ticket.items.count == productIds.count else {
            throw SQLiteError(code: -1, message: "Bon cuisine : \(ticket.items.count) articles pour \(productIds.count) identifiants")
        }
        try db.run(
            """
            INSERT INTO KitchenTickets (Id, OrderId, TableNumber, ServerName, CoversCount, StationId, Status, DispatchedAtUtc, PreparedAtUtc, CompletedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, NULL, NULL)
            """,
            [.uuid(ticket.id), .uuid(ticket.orderId), .text(ticket.tableNumber), .text(ticket.serverName ?? ""), .integer(ticket.coversCount ?? 1),
             .text(ticket.stationId), .integer(ticket.status.rawValue), .date(ticket.dispatchedAtUtc ?? Date())]
        )
        for (item, productId) in zip(ticket.items, productIds) {
            try db.run(
                """
                INSERT INTO KitchenTicketItems (Id, TicketId, ProductId, ProductName, Quantity, ModifiersSummary, KitchenComment, Status)
                VALUES (?, ?, ?, ?, ?, ?, ?, 0)
                """,
                [.uuid(item.id), .uuid(ticket.id), .uuid(productId), .text(item.productName), .integer(item.quantity),
                 .string(item.modifiersSummary), .string(item.kitchenComment)]
            )
        }
    }

    /// Les `limit` derniers bons, tous statuts, du plus récent au plus ancien (comme `GET /api/kds/tickets`).
    func recentTickets(limit: Int) throws -> [KitchenTicket] {
        try db.query("SELECT * FROM KitchenTickets ORDER BY DispatchedAtUtc DESC, rowid DESC LIMIT ?", [.integer(limit)]).map { row in
            let id = row.uuid("Id")!
            let items = try db.query("SELECT * FROM KitchenTicketItems WHERE TicketId = ? ORDER BY rowid", [.uuid(id)]).map {
                KitchenTicketItem(
                    id: $0.uuid("Id")!, productName: $0.string("ProductName") ?? "", quantity: $0.int("Quantity") ?? 1,
                    modifiersSummary: $0.string("ModifiersSummary"), kitchenComment: $0.string("KitchenComment")
                )
            }
            return KitchenTicket(
                id: id, orderId: row.uuid("OrderId"), tableNumber: row.string("TableNumber") ?? "?", serverName: row.string("ServerName"),
                coversCount: row.int("CoversCount"), stationId: row.string("StationId") ?? LocalStations.hotKitchen,
                status: TicketStatus(rawValue: row.int("Status") ?? 0) ?? .pending, dispatchedAtUtc: row.date("DispatchedAtUtc"), items: items
            )
        }
    }

    /// Fait avancer le bon d'un cran (en attente → en préparation → prêt → servi ; servi reste servi). `false` si le bon est inconnu.
    func bump(id: UUID) throws -> Bool {
        guard let row = try db.query("SELECT Status, PreparedAtUtc FROM KitchenTickets WHERE Id = ?", [.uuid(id)]).first else { return false }
        let current = TicketStatus(rawValue: row.int("Status") ?? 0) ?? .pending
        let next = TicketStatus(rawValue: min(current.rawValue + 1, TicketStatus.served.rawValue)) ?? .served
        let now = Date()
        let prepared = (next == .inPreparation && row.date("PreparedAtUtc") == nil) ? now : row.date("PreparedAtUtc")
        try db.run(
            "UPDATE KitchenTickets SET Status = ?, PreparedAtUtc = ?, CompletedAtUtc = COALESCE(?, CompletedAtUtc) WHERE Id = ?",
            [.integer(next.rawValue), .date(prepared), .date(next == .served ? now : nil), .uuid(id)]
        )
        return true
    }
}
