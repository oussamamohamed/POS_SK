import Foundation

/// Conversion pure des enregistrements locaux en entrées de `TicketBuilder` (miroir des entités lues par `TicketDocumentBuilder` du .NET).
enum LocalTicketMapping {
    /// Champs absents → chaîne vide : le constructeur retombe alors sur ses valeurs par défaut (comme `settings?.X` vide côté .NET).
    static func settings(_ s: RestaurantSettings) -> TicketSettings {
        TicketSettings(companyName: s.companyName ?? "", addressLines: s.addressLines ?? "", siret: s.siret ?? "", vatNumber: s.vatNumber ?? "",
                       certificateNumber: s.certificateNumber)
    }

    /// Total de ligne = `OrderItem.CalculateTotalTtc()` (offert, remise de ligne, modificateurs ; le prix Happy Hour est déjà le prix unitaire).
    static func order(_ o: ActiveOrder) -> TicketOrder {
        TicketOrder(destination: o.destination, tableNumber: o.tableNumber, pickupNumber: o.pickupNumber, pickupBuzzer: o.pickupBuzzer, items: o.lines.map {
            TicketOrderItem(quantity: $0.quantity, productName: $0.productName, productId: $0.productId,
                            totalTtcCents: OrderMath.lineTotal(CartLine(serverLine: $0)).cents, selectedModifiers: $0.modifiersSummary, course: $0.course)
        })
    }

    static func receipt(_ r: FiscalReceiptRecord) -> TicketReceipt {
        TicketReceipt(receiptNumber: r.receiptNumber, createdAtUtc: r.createdAtUtc, terminalId: r.terminalId, totalTtcCents: r.ttcCents,
                      totalHtCents: r.htCents, taxBreakdownJson: r.taxBreakdownJson, signatureHash: r.signature)
    }

    /// Lignes `KitchenTickets` et `KitchenTicketItems` (dans l'ordre d'insertion).
    static func kitchen(_ ticket: SQLRow, items: [SQLRow]) -> TicketKitchen {
        TicketKitchen(
            serverName: ticket.string("ServerName") ?? "", coversCount: ticket.int("CoversCount") ?? 0,
            dispatchedAtUtc: ticket.date("DispatchedAtUtc") ?? Date(timeIntervalSince1970: 0),
            items: items.map {
                TicketKitchenItem(quantity: $0.int("Quantity") ?? 1, productName: $0.string("ProductName") ?? "", productId: $0.uuid("ProductId"),
                                  modifiersSummary: $0.string("ModifiersSummary"), kitchenComment: $0.string("KitchenComment"))
            }
        )
    }

    /// `ClosedAtUtc` du DTO .NET = fin de période de la Z.
    static func dailyClosure(_ c: DailyFiscalClosureRecord, receiptCount: Int) -> TicketDailyClosure {
        TicketDailyClosure(
            terminalId: c.terminalId, closureSequence: c.sequence, totalSalesTtcCents: c.totalSalesTtcCents, totalSalesHtCents: c.totalSalesHtCents,
            receiptCount: receiptCount, vatBreakdownCents: rateAmounts(c.taxesSummaryJson), paymentTotalsCents: methodAmounts(c.tenderTotalsJson),
            perpetualGrandTotalCents: c.perpetualGrandTotalCents, signatureHash: c.signatureHash, closedAtUtc: c.periodEndUtc,
            periodStartUtc: c.periodStartUtc, sealedByUserName: c.sealedByUserName
        )
    }

    static func periodClosure(_ c: PeriodClosureRecord) -> TicketPeriodClosure {
        TicketPeriodClosure(
            terminalId: c.terminalId, isMonthly: c.periodType == .monthly, periodKey: c.periodKey, closureSequence: c.sequence,
            periodStartUtc: c.periodStartUtc, periodEndUtc: c.periodEndUtc, totalTtcCents: c.totalTtcCents, totalHtCents: c.totalHtCents,
            taxesSummaryJson: c.taxesSummaryJson, tenderTotalsJson: c.tenderTotalsJson, perpetualGrandTotalCents: c.perpetualGrandTotalCents,
            dailyClosureCount: c.dailyClosureCount, signatureHash: c.signatureHash, sealedByUserName: c.sealedByUserName
        )
    }

    /// Rapport X (`FiscalSummaryDto`) : le début de période est toujours connu sur l'iPad.
    static func reportSummary(_ s: LocalFiscalRepository.FiscalSummary, terminalId: String) -> TicketReportSummary {
        TicketReportSummary(
            terminalId: terminalId, periodStartUtc: s.periodStartUtc, periodEndUtc: s.periodEndUtc, totalSalesTtcCents: s.totalSalesTtcCents,
            totalSalesHtCents: s.totalSalesHtCents, receiptCount: s.receiptCount, vatBreakdownCents: rateAmounts(s.vatBreakdown.map { ($0.key, $0.value.cents) }),
            paymentTotalsCents: methodAmounts(s.paymentTotals.map { ($0.key, $0.value.cents) }), perpetualGrandTotalCents: s.perpetualGrandTotalCents
        )
    }

    static func rateAmounts(_ json: String) -> [TicketRateAmount] { rateAmounts(FiscalJSON.parseIntObject(json) ?? []) }

    static func methodAmounts(_ json: String) -> [TicketMethodAmount] { methodAmounts(FiscalJSON.parseIntObject(json) ?? []) }

    /// `Dictionary<decimal, long>` : clés illisibles ignorées, clés égales en décimal fusionnées ; trié par taux.
    static func rateAmounts(_ entries: [(String, Int)]) -> [TicketRateAmount] {
        var result: [TicketRateAmount] = []
        for (key, cents) in entries {
            guard let rate = Decimal(string: key, locale: Locale(identifier: "en_US_POSIX")) else { continue }
            if let i = result.firstIndex(where: { $0.rate == rate }) { result[i].cents += cents } else { result.append(TicketRateAmount(rate: rate, cents: cents)) }
        }
        return result.sorted { $0.rate < $1.rate }
    }

    /// `Enum.TryParse<PaymentMethod>` sur les noms écrits par le résumé fiscal ; trié par valeur.
    static func methodAmounts(_ entries: [(String, Int)]) -> [TicketMethodAmount] {
        var result: [TicketMethodAmount] = []
        for (key, cents) in entries {
            guard let method = methodNames[key] ?? Int(key) else { continue }
            if let i = result.firstIndex(where: { $0.method == method }) { result[i].cents = cents } else { result.append(TicketMethodAmount(method: method, cents: cents)) }
        }
        return result.sorted { $0.method < $1.method }
    }

    private static let methodNames = ["Cash": 0, "CreditCard": 1, "MealVoucher": 2, "GiftCard": 3, "RoomCharge": 4]
}

/// Répartiteur : port de `PrintDispatcher` (.NET). Mise en file APRÈS la transaction métier ; jamais d'exception vers l'appelant
/// (erreur journalisée, `false`). Seuls les duplicata écrivent numéro, job et JET dans une même transaction.
extension LocalPosAPI {
    /// Version imprimée sur les tickets (celle du .NET de la fixture de parité).
    static let softwareVersion = "1.0.0"
    static let receiptStation = "RECEIPT"

    var ticketContext: TicketContext { TicketContext(timeZone: calendar.timeZone, softwareVersion: Self.softwareVersion) }

    func queueCounterSale(orderId: UUID, receiptNumber: String, withFiscalReceipt: Bool, hasCash: Bool) -> Bool {
        queueing {
            guard let printer = try receiptPrinter() else { return false }
            let order = try loadOrder(orderId)
            let settings = try floorRepository.settings()
            let pickup = order.pickupNumber ?? ""
            if withFiscalReceipt {
                let receipt = try loadReceipt(receiptNumber)
                let doc = TicketBuilder.fiscalReceipt(LocalTicketMapping.receipt(receipt), order: order, pickupNumber: pickup, buzzer: order.pickupBuzzer,
                                                      language: settings.receiptLanguage, settings: LocalTicketMapping.settings(settings),
                                                      duplicateNumber: nil, context: ticketContext)
                try enqueue(printer, .receipt, doc, drawer: hasCash && printer.openCashDrawerOnReceipt, documentId: receipt.id)
            } else {
                let doc = TicketBuilder.pickupCoupon(order: order, pickupNumber: pickup, buzzer: order.pickupBuzzer, language: settings.receiptLanguage, now: clock())
                try enqueue(printer, .pickupVoucher, doc, drawer: hasCash && printer.openCashDrawerOnReceipt, documentId: nil)
            }
            return true
        }
    }

    func queueTableReceipt(orderId: UUID, receiptNumber: String, hasCash: Bool) -> Bool {
        queueing {
            guard let printer = try receiptPrinter() else { return false }
            let order = try loadOrder(orderId)
            let receipt = try loadReceipt(receiptNumber)
            let settings = try floorRepository.settings()
            let doc = TicketBuilder.receipt(LocalTicketMapping.receipt(receipt), order: order, language: settings.receiptLanguage,
                                            settings: LocalTicketMapping.settings(settings), duplicateNumber: nil, context: ticketContext)
            try enqueue(printer, .receipt, doc, drawer: hasCash && printer.openCashDrawerOnReceipt, documentId: receipt.id)
            return true
        }
    }

    /// Un job par imprimante active du poste du bon, jamais de tiroir ; un échec sur une imprimante n'arrête pas les autres.
    func queueKitchenTickets(ticketIds: [UUID]) {
        guard !ticketIds.isEmpty else { return }
        _ = queueing {
            let printers = try printerRepository.all().filter(\.isActive)
            let language = try floorRepository.settings().kitchenTicketLanguage
            var queued = false
            for id in ticketIds {
                guard let row = try db.query("SELECT * FROM KitchenTickets WHERE Id = ?", [.uuid(id)]).first else { continue }
                let station = row.string("StationId") ?? ""
                let targets = printers.filter { $0.assignedStationIds.contains(station) }
                guard !targets.isEmpty else {
                    Self.printLog.warning("Aucune imprimante active pour le poste \(station, privacy: .public)")
                    continue
                }
                let items = try db.query("SELECT * FROM KitchenTicketItems WHERE TicketId = ? ORDER BY rowid", [.uuid(id)])
                guard let orderId = row.uuid("OrderId") else { continue }
                let doc = TicketBuilder.kitchenTicket(LocalTicketMapping.kitchen(row, items: items), order: try loadOrder(orderId), language: language,
                                                      context: ticketContext)
                for printer in targets {
                    do {
                        try printQueue.enqueue(printerId: printer.id, kind: .kitchenTicket, documentJson: try TicketDocumentJSON.serialize(doc),
                                               openCashDrawer: false, duplicateOfDocumentId: nil, duplicateNumber: nil, now: clock())
                        queued = true
                    } catch {
                        Self.logQueueFailure(error)
                    }
                }
            }
            if queued { notifyPrintQueue() }
            return queued
        }
    }

    /// `summary` : le résumé du rapport X (`LocalFiscalRepository.summary`) du poste `terminalId`.
    func queueXReport(summary: LocalFiscalRepository.FiscalSummary, terminalId: String) -> Bool {
        queueReport(documentId: nil) { settings in
            let data = try fiscalRepository.reportPrintData(terminalId: terminalId, periodStart: summary.periodStartUtc, periodEnd: summary.periodEndUtc)
            return TicketBuilder.xReport(LocalTicketMapping.reportSummary(summary, terminalId: terminalId), data: data, language: settings.receiptLanguage,
                                         now: clock(), settings: LocalTicketMapping.settings(settings), context: ticketContext)
        }
    }

    func queueZClosure(_ closure: DailyFiscalClosureRecord) -> Bool {
        queueReport(documentId: closure.id) { settings in try zClosureDocument(closure, settings: settings, duplicateNumber: nil) }
    }

    func queuePeriodClosure(_ closure: PeriodClosureRecord) -> Bool {
        queueReport(documentId: nil) { settings in
            let data = try fiscalRepository.reportPrintData(terminalId: closure.terminalId, periodStart: closure.periodStartUtc, periodEnd: closure.periodEndUtc)
            return TicketBuilder.periodClosure(LocalTicketMapping.periodClosure(closure), data: data, language: settings.receiptLanguage,
                                               settings: LocalTicketMapping.settings(settings), context: ticketContext)
        }
    }

    /// Numéro, job (s'il y a une imprimante de ticket) et JET `DUPLICATE_PRINTED` dans une seule transaction ; erreur → `(false, 0)`,
    /// rien n'est consommé. Le document est le ticket d'origine en duplicata (repli sans lignes si la commande a disparu).
    func queueReceiptReprint(_ receipt: FiscalReceiptRecord, operatorId: UUID?, now: Date) -> (printQueued: Bool, duplicateNumber: Int) {
        reprinting {
            let printer = try receiptPrinter()
            let settings = try floorRepository.settings()
            let order = try orderRepository.order(id: receipt.orderId).map(LocalTicketMapping.order)
            let fiscal = fiscalRepository
            return try db.transaction {
                // JET d'abord : il porte le numéro (`nextDuplicateNumber`) ; le job reprend ce numéro, et tout est annulé ensemble.
                let number = try fiscal.recordDuplicate(of: receipt, operatorId: operatorId, now: now, printQueued: printer != nil)
                if let printer {
                    let ticket = LocalTicketMapping.receipt(receipt), ticketSettings = LocalTicketMapping.settings(settings)
                    let doc = order.map {
                        TicketBuilder.receipt(ticket, order: $0, language: settings.receiptLanguage, settings: ticketSettings, duplicateNumber: number, context: ticketContext)
                    } ?? TicketBuilder.receiptFallback(ticket, language: settings.receiptLanguage, settings: ticketSettings, duplicateNumber: number, context: ticketContext)
                    try enqueue(printer, .receipt, doc, drawer: false, documentId: receipt.id, duplicateNumber: number, notify: false)
                }
                return (printer != nil, number)
            }
        }
    }

    func queueZClosureReprint(_ closure: DailyFiscalClosureRecord, operatorId: UUID?, now: Date) -> (printQueued: Bool, duplicateNumber: Int) {
        reprinting {
            let printer = try receiptPrinter()
            let settings = try floorRepository.settings()
            let fiscal = fiscalRepository
            return try db.transaction {
                let number = try fiscal.recordClosureDuplicate(of: closure, operatorId: operatorId, now: now, printQueued: printer != nil)
                if let printer {
                    try enqueue(printer, .report, try zClosureDocument(closure, settings: settings, duplicateNumber: number), drawer: false,
                                documentId: closure.id, duplicateNumber: number, notify: false)
                }
                return (printer != nil, number)
            }
        }
    }

    // MARK: Utilitaires

    /// Imprimante de ticket : première imprimante active du poste `RECEIPT` par nom ordinal (le poste autonome n'a pas d'appareil appairé).
    private func receiptPrinter() throws -> Printer? {
        let printer = try printerRepository.all()
            .filter { $0.isActive && $0.assignedStationIds.contains(Self.receiptStation) }
            .min { $0.name.utf16.lexicographicallyPrecedes($1.name.utf16) }
        if printer == nil { Self.printLog.warning("Aucune imprimante de ticket active (poste RECEIPT)") }
        return printer
    }

    private func loadOrder(_ id: UUID) throws -> TicketOrder {
        guard let order = try orderRepository.order(id: id) else { throw SQLiteError(code: -1, message: "Commande \(id) introuvable") }
        return LocalTicketMapping.order(order)
    }

    private func loadReceipt(_ number: String) throws -> FiscalReceiptRecord {
        guard let receipt = try db.query("SELECT Id FROM FiscalReceipts WHERE TerminalId = ? AND ReceiptNumber = ?",
                                         [.text(Self.standaloneTerminalId), .text(number)]).first?.uuid("Id"),
              let record = try fiscalRepository.receipt(id: receipt)
        else { throw SQLiteError(code: -1, message: "Reçu \(number) introuvable") }
        return record
    }

    private func zClosureDocument(_ closure: DailyFiscalClosureRecord, settings: RestaurantSettings, duplicateNumber: Int?) throws -> TicketDocument {
        let fiscal = fiscalRepository
        let count = try fiscal.receiptCountForClosure(terminalId: closure.terminalId, sequence: closure.sequence,
                                                      periodStartUtc: closure.periodStartUtc, periodEndUtc: closure.periodEndUtc)
        let data = try fiscal.reportPrintData(terminalId: closure.terminalId, periodStart: closure.periodStartUtc, periodEnd: closure.periodEndUtc)
        return TicketBuilder.zClosure(LocalTicketMapping.dailyClosure(closure, receiptCount: count), data: data, language: settings.receiptLanguage,
                                      settings: LocalTicketMapping.settings(settings), duplicateNumber: duplicateNumber, context: ticketContext)
    }

    /// Rapport sur l'imprimante de ticket, jamais de tiroir.
    private func queueReport(documentId: UUID?, _ build: (RestaurantSettings) throws -> TicketDocument) -> Bool {
        queueing {
            guard let printer = try receiptPrinter() else { return false }
            try enqueue(printer, .report, try build(try floorRepository.settings()), drawer: false, documentId: documentId)
            return true
        }
    }

    private func enqueue(_ printer: Printer, _ kind: PrintJobKind, _ doc: TicketDocument, drawer: Bool, documentId: UUID?,
                         duplicateNumber: Int? = nil, notify: Bool = true) throws {
        try printQueue.enqueue(printerId: printer.id, kind: kind, documentJson: try TicketDocumentJSON.serialize(doc), openCashDrawer: drawer,
                               duplicateOfDocumentId: documentId, duplicateNumber: duplicateNumber, now: clock())
        if notify { notifyPrintQueue() }
    }

    private func queueing(_ body: () throws -> Bool) -> Bool {
        do { return try body() } catch {
            Self.logQueueFailure(error)
            return false
        }
    }

    private func reprinting(_ body: () throws -> (printQueued: Bool, duplicateNumber: Int)) -> (printQueued: Bool, duplicateNumber: Int) {
        do {
            let result = try body()
            if result.printQueued { notifyPrintQueue() }
            return result
        } catch {
            Self.logQueueFailure(error)
            return (false, 0)
        }
    }

    private static func logQueueFailure(_ error: any Error) {
        printLog.error("Mise en file d'impression impossible : \(error.localizedDescription, privacy: .public)")
    }
}
