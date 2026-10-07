import Foundation

extension LocalPosAPI {
    /// Transfert (`merge == false`) ou fusion (`merge == true`) de la commande d'une table vers une autre.
    /// Échec (`success: false`) si la source n'a pas de commande, si une table est inconnue, si source et cible sont la même table,
    /// si un transfert vise une table occupée (le .NET écraserait alors la commande : écart assumé, utiliser la fusion),
    /// ou si une fusion concerne une commande portant une remise globale (elle disparaîtrait ou s'étendrait aux lignes de l'autre commande :
    /// retirer la remise d'abord).
    public func transfer(from: String, to: String, merge: Bool) async throws -> OperationResult {
        try requireAuth()
        let failure = OperationResult(success: false, message: merge ? "Échec de la fusion de tables." : "Échec du transfert de table.")
        let floor = floorRepository, orders = orderRepository
        return try db.transaction { () throws -> OperationResult in
            guard from != to, let source = try floor.table(from), let target = try floor.table(to),
                  let sourceOrderId = source.activeOrderId, try orders.order(id: sourceOrderId) != nil
            else { return failure }

            if let targetOrderId = target.activeOrderId, try orders.order(id: targetOrderId) != nil {
                guard merge, let targetOrder = try orders.order(id: targetOrderId), let sourceOrder = try orders.order(id: sourceOrderId),
                      sourceOrder.globalDiscountType == nil, targetOrder.globalDiscountType == nil
                else { return failure }
                try orders.moveLines(from: sourceOrderId, to: targetOrderId)
                try orders.setStatus(orderId: sourceOrderId, .cancelled)
                try floor.update(
                    to, status: target.status, covers: target.coversCount + source.coversCount, waiterName: target.assignedWaiterName,
                    waiterId: try floor.waiterId(of: to), activeOrderId: targetOrderId, openedAt: target.openedAtUtc
                )
                try orders.insertTransferLog(
                    source: from, target: to, orderId: targetOrderId,
                    operatorName: target.assignedWaiterName ?? source.assignedWaiterName ?? "Serveur", isMerge: true
                )
            } else {
                try orders.setTableNumber(orderId: sourceOrderId, to)
                try floor.update(
                    to, status: .occupied, covers: source.coversCount, waiterName: source.assignedWaiterName,
                    waiterId: try floor.waiterId(of: from), activeOrderId: sourceOrderId, openedAt: source.openedAtUtc
                )
                try orders.insertTransferLog(source: from, target: to, orderId: sourceOrderId, operatorName: source.assignedWaiterName ?? "Serveur", isMerge: false)
            }
            try floor.update(from, status: .free, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: nil, openedAt: nil)
            return OperationResult(success: true, message: merge ? "Tables \(from) et \(to) fusionnées" : "Commande transférée de \(from) vers \(to)")
        }
    }
}
