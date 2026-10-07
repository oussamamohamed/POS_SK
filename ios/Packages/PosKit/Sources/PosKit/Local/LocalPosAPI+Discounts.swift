import Foundation

extension LocalPosAPI {
    private static let discountFailed = APIError.server(status: 400, message: "Opération de remise échouée.")
    private static let compFailed = APIError.server(status: 400, message: "Opération de gratuité échouée.")

    /// Remise globale sur la commande. Refusée (même message générique que le serveur) si la valeur est négative, si un pourcentage dépasse
    /// 100, si le motif est vide ou si la commande est inconnue. Chaque remise est journalisée dans `OrderDiscountAudits`.
    public func applyDiscount(orderId: UUID, type: DiscountType, value: Decimal, reason: String, operatorId: UUID?) async throws {
        try requireAuth()
        let trimmed = reason.trimmingCharacters(in: .whitespacesAndNewlines)
        guard value >= 0, !trimmed.isEmpty, !(type == .percentage && value > 100) else { throw Self.discountFailed }
        let orders = orderRepository
        try db.transaction {
            guard let order = try orders.order(id: orderId) else { throw Self.discountFailed }
            let totals = OrderMath.totals(
                lines: order.lines.map(CartLine.init(serverLine:)), destination: order.destination,
                discount: GlobalDiscount(type: type, value: value, reason: trimmed)
            )
            _ = try orders.setDiscount(orderId: orderId, type: type, value: value, reason: trimmed)
            try orders.insertDiscountAudit(
                orderId: orderId, itemId: nil, type: type, value: value, saved: (totals.subtotalTtc - totals.totalTtc).clampedAtZero(),
                reason: trimmed, operatorId: operatorId ?? localNoOperator
            )
        }
    }

    public func removeDiscount(orderId: UUID) async throws {
        try requireAuth()
        // Écart assumé : commande inconnue → 404 (le .NET lève une exception non gérée).
        guard try orderRepository.setDiscount(orderId: orderId, type: nil, value: 0, reason: nil) else { throw APIError.notFound("Commande introuvable.") }
    }

    /// Offre une ligne (gratuité). L'économie journalisée est `prix unitaire × quantité`, hors options (comme `CompOrderItemAsync`).
    public func compItem(orderId: UUID, lineId: UUID, reason: String, operatorId: UUID?) async throws {
        try requireAuth()
        let trimmed = reason.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !trimmed.isEmpty else { throw Self.compFailed }
        let orders = orderRepository
        try db.transaction {
            guard let order = try orders.order(id: orderId), let line = order.lines.first(where: { $0.lineId == lineId }),
                  try orders.comp(lineId: lineId, orderId: orderId, reason: trimmed)
            else { throw Self.compFailed }
            try orders.insertDiscountAudit(
                orderId: orderId, itemId: lineId, type: .comp, value: 100, saved: line.unitPrice * line.quantity,
                reason: trimmed, operatorId: operatorId ?? localNoOperator
            )
        }
    }
}
