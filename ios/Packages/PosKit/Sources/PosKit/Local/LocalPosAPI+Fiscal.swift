import Foundation

extension LocalPosAPI {
    /// Annule un reçu par contre-passation (`POST /api/checkout/void/{id}`) : nouveau reçu signé en négatif, entrée JET `RECEIPT_VOIDED`,
    /// commande annulée (comme le .NET, la table n'est pas touchée). Réservé aux responsables ; le reçu doit venir de `T01`.
    public func voidReceipt(receiptId: UUID) async throws -> OperationResult {
        let manager = try requireManager()
        let fiscal = fiscalRepository, orders = orderRepository
        let now = clock()
        return try db.transaction { () throws -> OperationResult in
            guard let original = try fiscal.receipt(id: receiptId),
                  let voided = try fiscal.voidReceipt(originalId: receiptId, terminalId: Self.standaloneTerminalId, operatorId: manager.id, now: now)
            else { throw APIError.server(status: 400, message: "Annulation du reçu impossible.") }
            try orders.setStatus(orderId: original.orderId, .cancelled)
            return OperationResult(success: true, message: "Reçu \(original.receiptNumber) annulé (\(voided.receiptNumber)).")
        }
    }

    /// Duplicata d'un reçu (`POST /api/checkout/receipts/{id}/reprint`). Sans impression (sous-projet 4) : le duplicata est numéroté et
    /// consigné au JET, `printQueued` reste faux. Réservé aux responsables.
    public func reprintReceipt(receiptIdentifier: String) async throws -> ReprintResult {
        let manager = try requireManager()
        let fiscal = fiscalRepository
        let now = clock()
        return try db.transaction { () throws -> ReprintResult in
            guard let receipt = try fiscal.receipt(identifier: receiptIdentifier) else { throw APIError.notFound("Reçu introuvable.") }
            return ReprintResult(printQueued: false, duplicateNumber: try fiscal.recordDuplicate(of: receipt, operatorId: manager.id, now: now))
        }
    }
}
