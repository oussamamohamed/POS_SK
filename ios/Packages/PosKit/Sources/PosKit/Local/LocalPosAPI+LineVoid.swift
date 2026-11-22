import Foundation

/// Règles partagées avec `OrderLineVoid.MaxReasonLength` (.NET) : motif limité à 256 unités UTF-16 après suppression des espaces
/// (même compte que `string.Length` en C# et que `maxlength` du navigateur).
public enum OrderLineVoidRules {
    public static let maxReasonLength = 256
    static let reasonTooLongMessage = "Le motif ne peut pas dépasser 256 caractères."
}

extension LocalPosAPI {
    private static func lineVoidError(_ status: Int, _ message: String) -> APIError { .server(status: status, message: message) }

    /// Miroir de `OrderLineVoidService.VoidAsync` (.NET) : correction d'une ligne non envoyée (tout opérateur, sans trace) ou annulation
    /// d'une ligne envoyée (responsable, motif obligatoire : retrait, ligne `OrderLineVoids`, JET et article de bon dans UNE transaction).
    /// Le bon d'annulation n'est mis en file qu'après la validation et ne peut pas faire échouer l'opération.
    public func voidLine(orderId: UUID, lineId: UUID, quantity: Int, reason: String?, operatorId: UUID?) async throws {
        let member = try requireAuth()
        let orders = orderRepository, fiscal = fiscalRepository, floor = floorRepository
        let now = clock()
        let voidId = try db.transaction { () throws -> UUID? in
            guard let order = try orders.order(id: orderId), let line = order.lines.first(where: { $0.lineId == lineId }) else {
                throw APIError.notFound("Commande introuvable.")
            }
            guard try orders.status(of: orderId)?.isModifiable == true else {
                throw Self.lineVoidError(409, "Cette commande n'est plus modifiable.")
            }
            // Mêmes reçus « actifs » que le .NET (non annulés), pas `paidCents` : un reçu sans règlement bloque aussi.
            let receipts = try fiscal.activeReceiptNumbers(orderId: orderId)
            guard receipts.isEmpty else {
                throw Self.lineVoidError(409, "Cette commande a déjà des reçus (\(receipts.joined(separator: ", "))) : annulez-les avant de modifier ses lignes.")
            }
            guard quantity >= 1, quantity <= line.quantity else {
                throw Self.lineVoidError(400, "La quantité à retirer doit être comprise entre 1 et celle de la ligne.")
            }
            guard line.isDispatched else {
                try orders.removeQuantity(of: line, quantity)
                return nil
            }
            guard member.role.isManager else { throw APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.") }
            let trimmed = (reason ?? "").trimmingCharacters(in: .whitespacesAndNewlines)
            guard !trimmed.isEmpty else { throw Self.lineVoidError(400, "Le motif est obligatoire pour annuler une ligne envoyée en cuisine.") }
            guard trimmed.utf16.count <= OrderLineVoidRules.maxReasonLength else { throw Self.lineVoidError(400, OrderLineVoidRules.reasonTooLongMessage) }

            // Article de bon encore actif pour la ligne (absent pour une ligne envoyée avant `OrderItemId`).
            let ticketItem = try db.query(
                "SELECT Id, TicketId, Quantity, CancelledQuantity FROM KitchenTicketItems WHERE OrderItemId = ? AND Status != ? ORDER BY rowid LIMIT 1",
                [.uuid(lineId), .integer(TicketItemStatus.cancelled.rawValue)]
            ).first
            if let ticketItem, let itemId = ticketItem.uuid("Id") {
                let cancelled = min(ticketItem.int("Quantity") ?? 0, (ticketItem.int("CancelledQuantity") ?? 0) + quantity)
                let done = cancelled >= (ticketItem.int("Quantity") ?? 0)
                try db.run("UPDATE KitchenTicketItems SET CancelledQuantity = ?, Status = CASE WHEN ? THEN ? ELSE Status END WHERE Id = ?",
                           [.integer(cancelled), .bool(done), .integer(TicketItemStatus.cancelled.rawValue), .uuid(itemId)])
            }
            let ticketId = ticketItem?.uuid("TicketId")
            let comment = try db.query("SELECT KitchenComment FROM OrderItems WHERE Id = ?", [.uuid(lineId)]).first?.string("KitchenComment")
            let unitPrice = (line.unitPrice + line.modifiersPriceExtra).cents  // prix réellement facturé (R9)
            let serverName = try floor.table(order.tableNumber)?.assignedWaiterName ?? ""
            let id = UUID()
            try db.run(
                """
                INSERT INTO OrderLineVoids (Id, OrderId, OrderItemId, TicketId, ProductId, ProductName, Quantity, UnitPrice, ModifiersSummary, KitchenComment,
                    PreparationStationId, Reason, OperatorId, OperatorName, ServerName, TableNumber, VoidedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(id), .uuid(orderId), .uuid(lineId), .uuid(ticketId), .uuid(line.productId), .text(line.productName), .integer(quantity), .integer(unitPrice),
                 .string(line.modifiersSummary.isEmpty ? nil : line.modifiersSummary.joined(separator: ", ")), .string(comment), .string(line.preparationStationId),
                 .text(trimmed), .uuid(member.id), .text(member.name), .text(serverName), .text(order.tableNumber), .date(now)]
            )
            try orders.removeQuantity(of: line, quantity)
            try fiscal.appendJournal(
                eventType: LocalJournalEvents.orderLineVoided,
                payloadJson: FiscalJSON.object([
                    ("orderId", FiscalJSON.guid(orderId)), ("tableNumber", FiscalJSON.string(order.tableNumber)), ("productId", FiscalJSON.guid(line.productId)),
                    ("productName", FiscalJSON.string(line.productName)), ("quantity", "\(quantity)"), ("unitPriceCents", "\(unitPrice)"),
                    ("reason", FiscalJSON.string(trimmed)), ("operatorId", FiscalJSON.guid(member.id)), ("operatorName", FiscalJSON.string(member.name)),
                    ("ticketId", ticketId.map(FiscalJSON.guid) ?? "null"),
                ]),
                terminalId: nil, operatorId: member.id, now: now
            )
            return id
        }
        if let voidId { queueKitchenCancellation(voidId: voidId) }
    }
}

extension LocalOrderRepository {
    /// Retire `quantity` unités ; la ligne disparaît quand elles le sont toutes.
    func removeQuantity(of line: OrderLine, _ quantity: Int) throws {
        if quantity == line.quantity { try db.run("DELETE FROM OrderItems WHERE Id = ?", [.uuid(line.lineId)]) } else { try setQuantity(lineId: line.lineId, quantity: line.quantity - quantity) }
    }
}

extension LocalFiscalRepository {
    /// Numéros des reçus non annulés de la commande (hors avoirs et reçus annulés), dans l'ordre (`ActiveReceiptsAsync` du .NET).
    func activeReceiptNumbers(orderId: UUID) throws -> [String] {
        try db.query(
            """
            SELECT ReceiptNumber FROM FiscalReceipts
            WHERE OrderId = ? AND VoidedReceiptId IS NULL AND Id NOT IN (SELECT VoidedReceiptId FROM FiscalReceipts WHERE VoidedReceiptId IS NOT NULL)
            ORDER BY SequenceNumber
            """,
            [.uuid(orderId)]
        ).compactMap { $0.string("ReceiptNumber") }
    }
}
