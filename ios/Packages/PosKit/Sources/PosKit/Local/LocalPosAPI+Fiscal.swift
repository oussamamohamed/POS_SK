import Foundation

extension LocalPosAPI {
    /// Annule un reçu par contre-passation (`POST /api/checkout/void/{id}`) : nouveau reçu signé en négatif, entrée JET `RECEIPT_VOIDED`,
    /// commande annulée (comme le .NET, la table n'est pas touchée). Réservé aux responsables ; le reçu doit venir de `T01`.
    /// Annuler un des reçus d'une commande annule toute la commande (comme le .NET) ; un reçu comprenant une facturation chambre ne peut pas être annulé.
    public func voidReceipt(receiptId: UUID) async throws -> OperationResult {
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
            printQueued: nil
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
            printQueued: false
        )
    }

    public func zClosure(terminalId: String, managerId: UUID, managerName: String) async throws -> FiscalReport {
        let manager = try requireManager()
        let term = terminalId.isEmpty ? Self.standaloneTerminalId : terminalId
        let now = clock()

        // 1. Vérification des commandes ouvertes en salle
        let tables = try floorRepository.tables()
        var openOrders: [String] = []
        for table in tables {
            guard let order = try activeOrder(on: table), !order.lines.isEmpty else { continue }
            let paid = try fiscalRepository.paidCents(orderId: order.orderId)
            let remaining = max(0, (order.totalTtcAmount?.cents ?? 0) - paid)
            if remaining > 0 {
                let formatted = String(format: "%d.%02d", remaining / 100, remaining % 100)
                openOrders.append("\(table.tableNumber) (\(formatted))")
            }
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
            printQueued: false
        )
    }

    public func printXReport(terminalId: String) async throws -> Bool {
        try requireManager()
        return false
    }

    public func reprintLatestClosure(terminalId: String) async throws -> ReprintResult {
        let manager = try requireManager()
        let term = terminalId.isEmpty ? Self.standaloneTerminalId : terminalId
        let fiscal = fiscalRepository
        let now = clock()
        return try db.transaction { () throws -> ReprintResult in
            guard let closure = try fiscal.latestClosure(terminalId: term) else {
                throw APIError.notFound("Aucune clôture trouvée.")
            }
            let duplicateNumber = try fiscal.recordClosureDuplicate(of: closure, operatorId: manager.id, now: now)
            return ReprintResult(printQueued: false, duplicateNumber: duplicateNumber)
        }
    }

    public func verifyChains() async throws -> FiscalVerificationResult {
        try requireManager()
        return try fiscalRepository.verifyAllChains(now: clock())
    }
}

