import Foundation

extension LocalPosAPI {
    /// Tendances du jour local comparées au même jour de la semaine précédente à la même heure (miroir de `InsightsService`).
    /// Aucune carte sous 3 jours de données ni sans vente aujourd'hui ; seuil réglable (`TrendThresholdPercent`).
    public func insights(tzOffsetMinutes: Int) async throws -> Insights {
        try requireManager()
        let now = clock()
        let offset = TimeInterval(tzOffsetMinutes * 60)
        let threshold = try alertRepository.settings().trendThresholdPercent

        let rows = try db.query("SELECT Id, OrderId, TotalTtcAmount, CreatedAtUtc, VoidedReceiptId FROM FiscalReceipts")
        let voided = Set(rows.compactMap { $0.uuid("VoidedReceiptId") })
        struct Sale { let orderId: UUID, cents: Int, at: Date }
        let sales = rows.compactMap { r -> Sale? in
            guard r.uuid("VoidedReceiptId") == nil, let id = r.uuid("Id"), !voided.contains(id), let order = r.uuid("OrderId"), let at = r.date("CreatedAtUtc") else { return nil }
            return Sale(orderId: order, cents: r.int("TotalTtcAmount") ?? 0, at: at)
        }
        let day = { (d: Date) in Int(((d.timeIntervalSince1970 + offset) / 86_400).rounded(.down)) }
        guard Set(sales.map { day($0.at) }).count >= InsightsMath.minDays else { return Insights(insufficientData: true) }

        let dayStart = Date(timeIntervalSince1970: Double(day(now)) * 86_400 - offset)
        let elapsed = now.timeIntervalSince(dayStart)
        let week: TimeInterval = 7 * 86_400
        let today = try await takings(from: dayStart, to: dayStart.addingTimeInterval(86_400), tzOffsetMinutes: tzOffsetMinutes, compare: "lastWeek")
        let reference = try await takings(from: dayStart.addingTimeInterval(-week), to: dayStart.addingTimeInterval(-week + elapsed), tzOffsetMinutes: tzOffsetMinutes, compare: "previous")

        // À emporter : seulement pour les commandes des deux fenêtres comparées (cette semaine et la précédente), pas pour tout l'historique.
        let windowStart = dayStart.addingTimeInterval(-week)
        let takeaway = Set(try Set(sales.filter { $0.at >= windowStart }.map(\.orderId)).filter { id in
            try db.query("SELECT 1 FROM Orders WHERE Id = ? AND Destination = ?", [.uuid(id), .integer(OrderDestination.takeaway.rawValue)]).first != nil
        })
        func window(_ from: Date, _ to: Date) -> (cents: Int, count: Int, takeaway: Int) {
            let inside = sales.filter { $0.at >= from && $0.at < to }
            return (inside.reduce(0) { $0 + $1.cents }, inside.count, inside.filter { takeaway.contains($0.orderId) }.count)
        }
        let cur = window(dayStart, dayStart.addingTimeInterval(elapsed + 0.000_001))
        let ref = window(dayStart.addingTimeInterval(-week), dayStart.addingTimeInterval(-week + elapsed + 0.000_001))
        guard cur.count > 0 else { return Insights() }

        let candidates: [(String, Int, Int)] = [
            ("cumulativeTtc", cur.cents, ref.cents),
            ("avgTicket", cur.cents / cur.count, ref.count == 0 ? 0 : ref.cents / ref.count),
            ("perCover", today.spendPerCover.cents, reference.spendPerCover.cents),
            ("takeawayShare", cur.takeaway * 10_000 / cur.count, ref.count == 0 ? 0 : ref.takeaway * 10_000 / ref.count),
        ]
        let cards = candidates.filter { $0.2 > 0 && $0.1 >= 0 }
            .map { InsightCard(metric: $0.0, currentCents: $0.1, referenceCents: $0.2, deltaPercent: InsightsMath.deltaPercent(current: $0.1, reference: $0.2)) }
            .filter { abs(NSDecimalNumber(decimal: $0.deltaPercent).doubleValue) >= Double(threshold) }
        return Insights(cards: cards)
    }

    /// Résumé du dernier Z du poste : totaux du Z, pourboires par serveur (commandes réglées dans la période), comptage de caisse, alertes ouvertes.
    public func dailySummary(terminalId: String, tzOffsetMinutes: Int) async throws -> DailySummary? {
        try requireManager()
        let term = terminalId.isEmpty ? Self.standaloneTerminalId : terminalId
        guard let z = try await latestClosure(terminalId: term), let closedAt = z.closedAtUtc, let closureSequence = z.closureSequence,
              let closure = try fiscalRepository.latestClosure(terminalId: term) else { return nil }
        let from = closure.periodStartUtc
        let report = try await takings(from: from, to: closedAt.addingTimeInterval(0.001), tzOffsetMinutes: tzOffsetMinutes, compare: nil)

        let receipts = try db.query("SELECT OrderId, VoidedReceiptId, CreatedAtUtc FROM FiscalReceipts WHERE TerminalId = ?", [.text(term)])
        let orderIds = Set(receipts.compactMap { r -> UUID? in
            guard r.uuid("VoidedReceiptId") == nil, let at = r.date("CreatedAtUtc"), at >= from, at <= closedAt else { return nil }
            return r.uuid("OrderId")
        })
        let names = Dictionary(uniqueKeysWithValues: try db.query("SELECT Id, Name FROM Users").compactMap { r in r.uuid("Id").map { ($0, r.string("Name") ?? "") } })
        var tips: [UUID: Int] = [:]
        for id in orderIds {
            guard let row = try db.query("SELECT OperatorId, TipAmount FROM Orders WHERE Id = ?", [.uuid(id)]).first, let op = row.uuid("OperatorId") else { continue }
            tips[op, default: 0] += row.int("TipAmount") ?? 0
        }
        let tipList = tips.filter { $0.value > 0 }.map { TipSummary(serverName: names[$0.key] ?? "—", tipsCents: $0.value) }.sorted { $0.serverName.localizedCaseInsensitiveCompare($1.serverName) == .orderedAscending }
        let cash = try cashRepository.summary(terminalId: term, from: from, to: closure.createdAtUtc).map { DailySummaryCash(floatCents: $0.float, countedCents: $0.counted, varianceCents: $0.variance) }
        let open = try alertRepository.list(state: "open", from: nil, to: nil).filter { $0.terminalId == term }
        return DailySummary(
            terminalId: term, closureSequence: closureSequence, closedAtUtc: closedAt, totalSalesTtcCents: z.totalSalesTtc.cents, totalSalesHtCents: z.totalSalesHt.cents, receiptCount: z.receiptCount,
            covers: report.covers, averageTicketCents: z.receiptCount > 0 ? z.totalSalesTtc.cents / z.receiptCount : 0,
            paymentTotalsCents: z.paymentTotals.mapValues(\.cents), tipsByServer: tipList, cash: cash, openAlerts: open
        )
    }
}
