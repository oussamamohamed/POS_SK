import Foundation

/// Alertes de pilotage (miroir de `AlertService` du .NET). Une alerte est unique par (type, opérateur, période) : `DedupeKey`, index unique.
struct LocalAlertRepository {
    let db: SQLiteDatabase
    let fiscal: LocalFiscalRepository

    func rules() throws -> [AlertRule] {
        try db.query("SELECT Type, Threshold, Enabled FROM AlertRules").compactMap { r in
            r.string("Type").map { AlertRule(type: $0, threshold: r.int("Threshold") ?? 0, enabled: r.bool("Enabled")) }
        }.sorted { (AlertRuleType.all.firstIndex(of: $0.type) ?? 99) < (AlertRuleType.all.firstIndex(of: $1.type) ?? 99) }
    }

    func setRule(type: String, threshold: Int, enabled: Bool, operatorId: UUID?, now: Date) throws -> AlertRule? {
        guard AlertRuleType.all.contains(type) else { return nil }
        guard threshold >= 0 else { throw CashError.ruleInvalid }
        try db.run("UPDATE AlertRules SET Threshold = ?, Enabled = ?, UpdatedAtUtc = ?, UpdatedBy = ? WHERE Type = ?", [.integer(threshold), .bool(enabled), .date(now), .uuid(operatorId), .text(type)])
        return AlertRule(type: type, threshold: threshold, enabled: enabled)
    }

    func settings() throws -> AlertSettings {
        let r = try db.query("SELECT TrendThresholdPercent, CashVarianceNoteRequired FROM RestaurantSettings WHERE Id = 1").first
        return AlertSettings(trendThresholdPercent: r?.int("TrendThresholdPercent") ?? 15, cashVarianceNoteRequired: r?.bool("CashVarianceNoteRequired") ?? true)
    }

    func save(_ s: AlertSettings) throws {
        guard (1...100).contains(s.trendThresholdPercent) else { throw CashError.ruleInvalid }
        try db.run("UPDATE RestaurantSettings SET TrendThresholdPercent = ?, CashVarianceNoteRequired = ? WHERE Id = 1", [.integer(s.trendThresholdPercent), .bool(s.cashVarianceNoteRequired)])
    }

    func list(state: String?, from: Date?, to: Date?) throws -> [PilotAlert] {
        let names = Dictionary(uniqueKeysWithValues: try db.query("SELECT Id, Name FROM Users").compactMap { r in r.uuid("Id").map { ($0, r.string("Name") ?? "") } })
        return try db.query("SELECT * FROM Alerts").compactMap { r -> PilotAlert? in
            guard let id = r.uuid("Id"), let created = r.date("CreatedAtUtc") else { return nil }
            let st = r.string("State") ?? "open"
            guard (state == nil || st == state) && (from == nil || created >= from!) && (to == nil || created < to!) else { return nil }
            let op = r.uuid("OperatorId")
            return PilotAlert(id: id, ruleType: r.string("RuleType") ?? "", operatorId: op, operatorName: op.flatMap { names[$0] }, terminalId: r.string("TerminalId") ?? "", periodKey: r.string("PeriodKey") ?? "",
                              detailJson: r.string("DetailJson") ?? "{}", state: st, createdAtUtc: created, resolvedAtUtc: r.date("ResolvedAtUtc"), resolutionNote: r.string("ResolutionNote"))
        }.sorted { $0.createdAtUtc > $1.createdAtUtc }
    }

    func resolve(id: UUID, note: String, operatorId: UUID, now: Date) throws -> PilotAlert? {
        guard let alert = try list(state: nil, from: nil, to: nil).first(where: { $0.id == id }) else { return nil }
        guard alert.isOpen else { return alert }
        let text = note.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { throw CashError.noteRequired }
        try db.run("UPDATE Alerts SET State = 'resolved', ResolvedAtUtc = ?, ResolvedBy = ?, ResolutionNote = ? WHERE Id = ?", [.date(now), .uuid(operatorId), .text(text), .uuid(id)])
        try fiscal.appendJournal(eventType: LocalJournalEvents.alertResolved, payloadJson: FiscalJSON.object([("alertId", FiscalJSON.guid(id))]), terminalId: alert.terminalId, operatorId: operatorId, now: now)
        return try list(state: nil, from: nil, to: nil).first { $0.id == id }
    }

    /// Période en cours d'un poste : numéro du dernier Z et son début (fin de ce Z, ou le début des temps).
    private func period(_ terminal: String) throws -> (key: String, start: Date) {
        let last = try db.query("SELECT ClosureSequence, PeriodEndUtc FROM DailyFiscalClosures WHERE TerminalId = ? ORDER BY ClosureSequence DESC LIMIT 1", [.text(terminal)]).first
        return (AlertRules.periodKey(terminalId: terminal, closureSequence: last?.int("ClosureSequence") ?? 0), last?.date("PeriodEndUtc") ?? .distantPast)
    }

    /// Évalue les seuils actifs ; renvoie le nombre d'alertes créées. Idempotent.
    @discardableResult
    func evaluate(now: Date) throws -> Int {
        let enabled = Dictionary(uniqueKeysWithValues: try rules().filter(\.enabled).map { ($0.type, $0) })
        guard enabled.keys.contains(where: { $0 != AlertRuleType.cashVariance }) else { return 0 }

        struct Receipt { let id: UUID, terminal: String, orderId: UUID, voided: Bool, totalCents: Int, at: Date }
        let receipts = try db.query("SELECT Id, TerminalId, OrderId, VoidedReceiptId, TotalTtcAmount, CreatedAtUtc FROM FiscalReceipts").compactMap { r -> Receipt? in
            guard let id = r.uuid("Id"), let order = r.uuid("OrderId"), let at = r.date("CreatedAtUtc") else { return nil }
            return Receipt(id: id, terminal: r.string("TerminalId") ?? "", orderId: order, voided: r.uuid("VoidedReceiptId") != nil, totalCents: r.int("TotalTtcAmount") ?? 0, at: at)
        }
        // Commandes (et leurs lignes) : seulement celles de la période en cours de chaque poste, jamais tout l'historique.
        var periods: [String: (key: String, start: Date)] = [:]
        for terminal in Set(receipts.map(\.terminal)) { periods[terminal] = try period(terminal) }
        let neededOrders = Set(receipts.filter { r in periods[r.terminal].map { r.at >= $0.start } ?? false }.map(\.orderId))

        struct OrderRow { let operatorId: UUID, discountType: DiscountType?, discountValue: Decimal, lines: [(unitPrice: Int, modifiersExtra: Int, quantity: Int, isComp: Bool, discountPercent: Decimal)] }
        var orders: [UUID: OrderRow] = [:]
        for id in neededOrders {
            guard let row = try db.query("SELECT OperatorId, GlobalDiscountType, GlobalDiscountValue FROM Orders WHERE Id = ?", [.uuid(id)]).first, let op = row.uuid("OperatorId") else { continue }
            let lines = try db.query("SELECT UnitPrice, ModifiersPriceExtra, Quantity, IsComp, DiscountPercent FROM OrderItems WHERE OrderId = ?", [.uuid(id)])
                .map { (unitPrice: $0.int("UnitPrice") ?? 0, modifiersExtra: $0.int("ModifiersPriceExtra") ?? 0, quantity: $0.int("Quantity") ?? 0, isComp: $0.bool("IsComp"), discountPercent: $0.decimal("DiscountPercent") ?? 0) }
            orders[id] = OrderRow(operatorId: op, discountType: row.int("GlobalDiscountType").flatMap(DiscountType.init(rawValue:)), discountValue: row.decimal("GlobalDiscountValue") ?? 0, lines: lines)
        }

        var created = 0
        for terminal in Set(receipts.map(\.terminal)).sorted() {
            guard let (periodKey, start) = periods[terminal] else { continue }
            let inPeriod = receipts.filter { $0.terminal == terminal && $0.at >= start }

            if let rule = enabled[AlertRuleType.voidsCount] {
                let groups = Dictionary(grouping: inPeriod.filter { $0.voided && orders[$0.orderId] != nil }, by: { orders[$0.orderId]!.operatorId })
                for (op, rs) in groups.sorted(by: { $0.key.uuidString < $1.key.uuidString }) where rs.count > rule.threshold {
                    created += try raise(AlertRuleType.voidsCount, op, terminal, periodKey, now: now,
                                         detail: ["count": rs.count, "totalCents": rs.reduce(0) { $0 + abs($1.totalCents) }, "receiptIds": rs.map { $0.id.uuidString.lowercased() }])
                }
            }
            var seen = Set<UUID>()
            let sold = inPeriod.filter { !$0.voided && orders[$0.orderId] != nil && seen.insert($0.orderId).inserted }.map { r -> (id: UUID, order: OrderRow) in (r.orderId, orders[r.orderId]!) }
            if let rule = enabled[AlertRuleType.discountPercent] {
                let flagged = sold.map { (id: $0.id, op: $0.order.operatorId, percent: AlertRules.discountPercent(type: $0.order.discountType, value: $0.order.discountValue, subtotalCents: $0.order.lines.reduce(0) { $0 + ($1.unitPrice + $1.modifiersExtra) * $1.quantity }, maxLinePercent: $0.order.lines.filter { !$0.isComp }.map(\.discountPercent).max() ?? 0)) }
                    .filter { $0.percent > rule.threshold }
                for (op, items) in Dictionary(grouping: flagged, by: \.op).sorted(by: { $0.key.uuidString < $1.key.uuidString }) {
                    created += try raise(AlertRuleType.discountPercent, op, terminal, periodKey, now: now,
                                         detail: ["count": items.count, "maxPercent": items.map(\.percent).max() ?? 0, "orderIds": items.map { $0.id.uuidString.lowercased() }])
                }
            }
            if let rule = enabled[AlertRuleType.compsTotal] {
                let comps = sold.map { (id: $0.id, op: $0.order.operatorId, cents: AlertRules.compCents(lines: $0.order.lines.map { (unitPrice: $0.unitPrice, modifiersExtra: $0.modifiersExtra, quantity: $0.quantity, isComp: $0.isComp) }, discountType: $0.order.discountType)) }.filter { $0.cents > 0 }
                for (op, items) in Dictionary(grouping: comps, by: \.op).sorted(by: { $0.key.uuidString < $1.key.uuidString }) where items.reduce(0, { $0 + $1.cents }) > rule.threshold {
                    created += try raise(AlertRuleType.compsTotal, op, terminal, periodKey, now: now, detail: ["totalCents": items.reduce(0) { $0 + $1.cents }, "orderIds": items.map { $0.id.uuidString.lowercased() }])
                }
            }
        }
        return created
    }

    /// Alerte d'écart de caisse pour une session comptée ; `true` si une alerte a été créée.
    func evaluateCashCount(session: CashSession, now: Date) throws -> Bool {
        guard let rule = try rules().first(where: { $0.type == AlertRuleType.cashVariance }), rule.enabled, let count = session.count, abs(count.varianceCents) > rule.threshold else { return false }
        let (periodKey, _) = try period(session.terminalId)
        return try raise(AlertRuleType.cashVariance, session.openedByOperatorId, session.terminalId, periodKey, now: now,
                         detail: ["sessionId": session.id.uuidString.lowercased(), "varianceCents": count.varianceCents, "expectedCents": count.expectedCents, "countedCents": count.countedCents]) > 0
    }

    private func raise(_ type: String, _ op: UUID?, _ terminal: String, _ periodKey: String, now: Date, detail: [String: Any]) throws -> Int {
        let dedupe = AlertRules.dedupeKey(ruleType: type, operatorId: op, periodKey: periodKey)
        let json = String(decoding: try JSONSerialization.data(withJSONObject: detail, options: [.sortedKeys]), as: UTF8.self)
        if let existing = try db.query("SELECT State, DetailJson FROM Alerts WHERE DedupeKey = ?", [.text(dedupe)]).first {
            // Une alerte ouverte reflète l'état courant ; une alerte résolue reste figée. Ce n'est pas une création.
            if existing.string("State") == "open", existing.string("DetailJson") != json { try db.run("UPDATE Alerts SET DetailJson = ? WHERE DedupeKey = ?", [.text(json), .text(dedupe)]) }
            return 0
        }
        let id = UUID()
        try db.run("INSERT INTO Alerts (Id, RuleType, OperatorId, TerminalId, PeriodKey, DedupeKey, DetailJson, State, CreatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, 'open', ?)",
                   [.uuid(id), .text(type), .uuid(op), .text(terminal), .text(periodKey), .text(dedupe), .text(json), .date(now)])
        try? fiscal.appendJournal(eventType: LocalJournalEvents.alertCreated, payloadJson: FiscalJSON.object([("alertId", FiscalJSON.guid(id)), ("ruleType", FiscalJSON.string(type)), ("periodKey", FiscalJSON.string(periodKey))]), terminalId: terminal, operatorId: op, now: now)
        return 1
    }
}
