import Foundation

extension LocalPosAPI {
    /// Annule un reçu par contre-passation (`POST /api/checkout/void/{id}`) : nouveau reçu signé en négatif, entrée JET `RECEIPT_VOIDED`,
    /// commande annulée et table libérée si le reçu couvrait tout son TTC, sinon (part d'une note partagée) la commande redevient réglable
    /// (`billRequested`). Réservé aux responsables ; le reçu doit venir de `T01`. Un reçu comprenant une facturation chambre ne peut pas être annulé.
    public func voidReceipt(receiptId: UUID, restock: Bool = false) async throws -> OperationResult {
        let manager = try requireManager()
        let fiscal = fiscalRepository, orders = orderRepository, database = db
        let now = clock()
        return try db.transaction { () throws -> OperationResult in
            let roomTenders = try database.query(
                "SELECT 1 AS x FROM PaymentTenders WHERE FiscalReceiptId = ? AND Method = ?", [.uuid(receiptId), .integer(PaymentMethod.roomCharge.rawValue)]
            )
            guard roomTenders.isEmpty else {
                throw APIError.server(status: 409, message: "Annulation impossible : ce reçu comprend une facturation chambre (la note de chambre ne serait pas rétablie).")
            }
            guard try !fiscal.isInClosedPeriod(receiptId: receiptId) else {
                throw APIError.server(status: 409, message: "Ce ticket appartient à une période clôturée (Z) et ne peut plus être annulé.")
            }
            guard let original = try fiscal.receipt(id: receiptId),
                  let voided = try fiscal.voidReceipt(originalId: receiptId, terminalId: Self.standaloneTerminalId, operatorId: manager.id, now: now)
            else { throw APIError.server(status: 400, message: "Annulation du reçu impossible.") }
            guard let order = try orders.order(id: original.orderId), let orderTtc = decorate(order).totalTtcAmount?.cents
            else { throw APIError.server(status: 400, message: "Annulation du reçu impossible.") }
            // Retirer la part de pourboire portée par le reçu annulé (excédent de règlement au-delà du TTC fiscal du reçu).
            let tenderSum = try database.query("SELECT COALESCE(SUM(Amount), 0) AS s FROM PaymentTenders WHERE FiscalReceiptId = ?", [.uuid(receiptId)]).first?.int("s") ?? 0
            let receiptTip = max(0, tenderSum - original.ttcCents)
            if receiptTip > 0 {
                let currentTip = try orders.tipCents(of: original.orderId)
                try orders.setTip(orderId: original.orderId, cents: max(0, currentTip - receiptTip))
            }
            if orderTtc == original.ttcCents {
                try closeOrder(order, as: .cancelled)
                // Le stock n'est restitué que sur annulation complète (jamais sur une part de note partagée, qui rouvre la commande).
                if restock {
                    stockRepository.restoreSale(orderId: original.orderId, operatorId: manager.id)
                    _ = try? stockRepository.refreshAutoAvailability()
                }
            } else {
                try orders.setStatus(orderId: original.orderId, .billRequested)
                // Réouverte : la commande retrouve sa table si elle est libre (déjà reprise par une autre commande : elle reste sans table).
                if let table = try floorRepository.table(order.tableNumber), table.activeOrderId == nil {
                    try floorRepository.update(
                        table.tableNumber, status: .occupied, covers: table.coversCount, waiterName: table.assignedWaiterName, waiterId: nil,
                        activeOrderId: order.orderId, openedAt: table.openedAtUtc ?? now
                    )
                }
            }
            _ = try? alertRepository.evaluate(now: now) // annulations : seuil d'alerte, sans jamais bloquer l'avoir
            return OperationResult(success: true, message: "Reçu \(original.receiptNumber) annulé (\(voided.receiptNumber)).")
        }
    }

    /// Duplicata d'un reçu (`POST /api/checkout/receipts/{id}/reprint`) : numéro, JET `DUPLICATE_PRINTED` et job d'impression (s'il y a une
    /// imprimante de ticket) dans la transaction du répartiteur, hors de toute autre transaction (non réentrante). Comme le .NET, un échec
    /// d'écriture rend `(false, 0)` sans rien consommer. Réservé aux responsables.
    public func reprintReceipt(receiptIdentifier: String) async throws -> ReprintResult {
        let manager = try requireManager()
        guard let receipt = try fiscalRepository.receipt(identifier: receiptIdentifier) else { throw APIError.notFound("Reçu introuvable.") }
        let result = queueReceiptReprint(receipt, operatorId: manager.id, now: clock())
        return ReprintResult(printQueued: result.printQueued, duplicateNumber: result.duplicateNumber)
    }

    // MARK: - Rapport X & Clôture Z (NF525)

    public func xReport(terminalId: String) async throws -> FiscalReport {
        try requireManager()
        let term = terminalId.isEmpty ? Self.standaloneTerminalId : terminalId
        let sum = try fiscalRepository.summary(terminalId: term, now: clock())
        return FiscalReport(
            terminalId: term,
            closureSequence: nil,
            totalSalesTtc: Money(cents: sum.totalSalesTtcCents),
            totalSalesHt: Money(cents: sum.totalSalesHtCents),
            receiptCount: sum.receiptCount,
            vatBreakdown: sum.vatBreakdown,
            paymentTotals: sum.paymentTotals,
            perpetualGrandTotal: Money(cents: sum.perpetualGrandTotalCents),
            signatureHash: nil,
            closedAtUtc: nil,
            periodEndUtc: sum.periodEndUtc,
            printQueued: nil,
            periodStartUtc: sum.periodStartUtc,
            details: try fiscalRepository.reportPrintData(terminalId: term, periodStart: sum.periodStartUtc, periodEnd: sum.periodEndUtc)
        )
    }

    public func latestClosure(terminalId: String) async throws -> FiscalReport? {
        try requireManager()
        let term = terminalId.isEmpty ? Self.standaloneTerminalId : terminalId
        guard let closure = try fiscalRepository.latestClosure(terminalId: term) else { return nil }
        let count = try fiscalRepository.receiptCountForClosure(terminalId: term, sequence: closure.sequence, periodStartUtc: closure.periodStartUtc, periodEndUtc: closure.periodEndUtc)
        var vat: [String: Money] = [:]
        let taxesData = Data(closure.taxesSummaryJson.utf8)
        if let dict = try? JSONSerialization.jsonObject(with: taxesData) as? [String: Any] {
            for (rate, val) in dict {
                let cents: Int = (val as? Int) ?? Int((val as? Double) ?? 0)
                vat[rate] = Money(cents: cents)
            }
        }
        var tenders: [String: Money] = [:]
        let tendersData = Data(closure.tenderTotalsJson.utf8)
        if let dict = try? JSONSerialization.jsonObject(with: tendersData) as? [String: Any] {
            for (method, val) in dict {
                let cents: Int = (val as? Int) ?? Int((val as? Double) ?? 0)
                tenders[method] = Money(cents: cents)
            }
        }
        return FiscalReport(
            terminalId: closure.terminalId,
            closureSequence: closure.sequence,
            totalSalesTtc: Money(cents: closure.totalSalesTtcCents),
            totalSalesHt: Money(cents: closure.totalSalesHtCents),
            receiptCount: count,
            vatBreakdown: vat,
            paymentTotals: tenders,
            perpetualGrandTotal: Money(cents: closure.perpetualGrandTotalCents),
            signatureHash: closure.signatureHash,
            closedAtUtc: closure.createdAtUtc,
            periodEndUtc: closure.periodEndUtc,
            printQueued: false,
            cash: try cashRepository.summary(terminalId: term, from: closure.periodStartUtc, to: closure.createdAtUtc).map { CashZSummary(floatCents: $0.float, countedCents: $0.counted, varianceCents: $0.variance) },
            periodStartUtc: closure.periodStartUtc,
            managerName: closure.sealedByUserName,
            details: try fiscalRepository.reportPrintData(terminalId: closure.terminalId, periodStart: closure.periodStartUtc, periodEnd: closure.periodEndUtc)
        )
    }

    public func zClosure(terminalId: String, managerId: UUID, managerName: String) async throws -> FiscalReport {
        let manager = try requireManager()
        let term = terminalId.isEmpty ? Self.standaloneTerminalId : terminalId
        let now = clock()

        // 1. Vérification des commandes ouvertes, par commande comme `FindOpenOrdersAsync` : celles des tables, puis une commande rouverte
        // restée sans table (table reprise entre-temps). Reste dû = TTC + pourboire − règlements actifs.
        var openOrders: [String] = []
        var seen = Set<UUID>()
        func checkOpen(_ order: ActiveOrder, label: String) throws {
            guard seen.insert(order.orderId).inserted, !order.lines.isEmpty,
                  try orderRepository.status(of: order.orderId)?.isModifiable == true else { return }
            let (total, tip, paid) = try balance(of: order)
            let remaining = max(0, (total + tip - paid).cents)
            if remaining > 0 {
                let formatted = String(format: "%d.%02d", remaining / 100, remaining % 100)
                openOrders.append("\(label) (\(formatted))")
            }
        }
        for table in try floorRepository.tables() {
            if let order = try activeOrder(on: table) { try checkOpen(order, label: table.tableNumber) }
        }
        let modifiable = [LocalOrderStatus.open, .sentToKitchen, .billRequested].map { SQLValue.integer($0.rawValue) }
        for row in try db.query("SELECT Id FROM Orders WHERE Status IN (?, ?, ?) ORDER BY CreatedAtUtc", modifiable) {
            // Un panier en attente est refusé plus bas avec son propre message.
            guard let id = row.uuid("Id"), try !holdRepository.hasActiveHold(orderId: id), let order = try orderRepository.order(id: id) else { continue }
            try checkOpen(decorate(order), label: order.tableNumber)
        }
        if !openOrders.isEmpty {
            throw APIError.server(
                status: 409,
                message: "Clôture Z impossible : \(openOrders.count) commande(s) en cours : \(openOrders.joined(separator: ", "))"
            )
        }

        // 2. Vérification des commandes en attente (held orders)
        let held = try holdRepository.active(terminalId: term)
        if !held.isEmpty {
            throw APIError.server(
                status: 409,
                message: "Clôture Z impossible : \(held.count) commande(s) en attente subsistent sur la caisse \(term)."
            )
        }

        let fiscal = fiscalRepository
        let closure = try db.transaction { () throws -> DailyFiscalClosureRecord in
            try fiscal.insertClosure(terminalId: term, managerId: manager.id, managerName: manager.name, now: now)
        }
        // Après la validation : l'impression ne défait jamais la clôture.
        let printQueued = queueZClosure(closure)

        let count = try fiscal.receiptCountForClosure(terminalId: term, sequence: closure.sequence, periodStartUtc: closure.periodStartUtc, periodEndUtc: closure.periodEndUtc)
        var vat: [String: Money] = [:]
        let taxesData = Data(closure.taxesSummaryJson.utf8)
        if let dict = try? JSONSerialization.jsonObject(with: taxesData) as? [String: Any] {
            for (rate, val) in dict {
                let cents: Int = (val as? Int) ?? Int((val as? Double) ?? 0)
                vat[rate] = Money(cents: cents)
            }
        }
        var tenders: [String: Money] = [:]
        let tendersData = Data(closure.tenderTotalsJson.utf8)
        if let dict = try? JSONSerialization.jsonObject(with: tendersData) as? [String: Any] {
            for (method, val) in dict {
                let cents: Int = (val as? Int) ?? Int((val as? Double) ?? 0)
                tenders[method] = Money(cents: cents)
            }
        }

        return FiscalReport(
            terminalId: closure.terminalId,
            closureSequence: closure.sequence,
            totalSalesTtc: Money(cents: closure.totalSalesTtcCents),
            totalSalesHt: Money(cents: closure.totalSalesHtCents),
            receiptCount: count,
            vatBreakdown: vat,
            paymentTotals: tenders,
            perpetualGrandTotal: Money(cents: closure.perpetualGrandTotalCents),
            signatureHash: closure.signatureHash,
            closedAtUtc: closure.createdAtUtc,
            periodEndUtc: closure.periodEndUtc,
            printQueued: printQueued,
            periodStartUtc: closure.periodStartUtc,
            managerName: closure.sealedByUserName,
            details: try fiscal.reportPrintData(terminalId: closure.terminalId, periodStart: closure.periodStartUtc, periodEnd: closure.periodEndUtc)
        )
    }

    public func printXReport(terminalId: String) async throws -> Bool {
        try requireManager()
        let term = terminalId.isEmpty ? Self.standaloneTerminalId : terminalId
        return queueXReport(summary: try fiscalRepository.summary(terminalId: term, now: clock()), terminalId: term)
    }

    /// Duplicata de la dernière Z : comme `reprintReceipt`, hors de toute transaction.
    public func reprintLatestClosure(terminalId: String) async throws -> ReprintResult {
        let manager = try requireManager()
        let term = terminalId.isEmpty ? Self.standaloneTerminalId : terminalId
        guard let closure = try fiscalRepository.latestClosure(terminalId: term) else { throw APIError.notFound("Aucune clôture trouvée.") }
        let result = queueZClosureReprint(closure, operatorId: manager.id, now: clock())
        return ReprintResult(printQueued: result.printQueued, duplicateNumber: result.duplicateNumber)
    }

    /// Vérifie toutes les chaînes (`POST /api/fiscal/verify`). Comme le .NET, une rupture est journalisée (`CHAIN_BREAK_DETECTED`, première
    /// chaîne rompue, poste de la chaîne, sans opérateur) ; un échec de cette écriture ne change pas le verdict rendu.
    public func verifyChains() async throws -> FiscalVerificationResult {
        try requireManager()
        let fiscal = fiscalRepository
        let result = try fiscal.verifyAllChains(now: clock())
        if let broken = result.chains.first(where: { !$0.isValid }) {
            let now = clock()
            _ = try? db.transaction {
                try fiscal.appendJournal(
                    eventType: LocalJournalEvents.chainBreakDetected,
                    payloadJson: FiscalJSON.object([
                        ("Chain", FiscalJSON.string(broken.chain)),
                        ("TerminalId", broken.terminalId.map(FiscalJSON.string) ?? "null"),
                        ("SequenceNumber", broken.break.map { "\($0.sequenceNumber)" } ?? "null"),
                        ("Reference", broken.break.map { FiscalJSON.string($0.reference) } ?? "null"),
                        ("Kind", broken.break.map { FiscalJSON.string($0.kind) } ?? "null"),
                    ]),
                    terminalId: broken.terminalId, operatorId: nil, now: now
                )
            }
        }
        return result
    }
}

