import Foundation

/// Encaissements du mode autonome : mêmes règles que `FinancialDashboardService.GetTakingsAsync` (.NET), calculés sur les reçus fiscaux.
extension LocalPosAPI {
    public func takings(from: Date, to: Date, tzOffsetMinutes: Int) async throws -> TakingsReport {
        try requireManager()
        let week: TimeInterval = 7 * 86_400
        let now = clock()
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = TimeZone(secondsFromGMT: 0)!

        struct Receipt { let id: UUID, orderId: UUID, cents: Int, at: Date }
        let rows = try db.query("SELECT Id, OrderId, TotalTtcAmount, CreatedAtUtc, VoidedReceiptId FROM FiscalReceipts")
        let voided = Set(rows.compactMap { $0.uuid("VoidedReceiptId") })
        let valid = rows.compactMap { row -> Receipt? in
            guard row.uuid("VoidedReceiptId") == nil, let id = row.uuid("Id"), !voided.contains(id), let order = row.uuid("OrderId"), let at = row.date("CreatedAtUtc") else { return nil }
            return Receipt(id: id, orderId: order, cents: row.int("TotalTtcAmount") ?? 0, at: at)
        }
        func inRange(_ r: Receipt, _ lo: Date, _ hi: Date) -> Bool { r.at >= lo && r.at < hi }
        func byHour(_ lo: Date, _ hi: Date) -> [Int] {
            var hours = [Int](repeating: 0, count: 24)
            for r in valid where inRange(r, lo, hi) {
                hours[utc.component(.hour, from: r.at.addingTimeInterval(TimeInterval(tzOffsetMinutes * 60)))] += r.cents
            }
            return hours
        }

        let today = valid.filter { inRange($0, from, to) }
        let todayHours = byHour(from, to), lastWeekHours = byHour(from.addingTimeInterval(-week), to.addingTimeInterval(-week))
        let lastWeekByNow = valid.filter { inRange($0, from.addingTimeInterval(-week), to.addingTimeInterval(-week)) && $0.at <= now.addingTimeInterval(-week) }.reduce(0) { $0 + $1.cents }
        let total = today.reduce(0) { $0 + $1.cents }

        // Couverts : ceux des bons de cuisine de la commande, au moins 1 par commande (la base ne garde pas les couverts d'une table libérée).
        let ticketCovers = Dictionary(grouping: try db.query("SELECT OrderId, CoversCount FROM KitchenTickets"), by: { $0.uuid("OrderId") })
            .mapValues { $0.compactMap { $0.int("CoversCount") }.max() ?? 1 }
        let covers = today.reduce(0) { $0 + max(1, ticketCovers[$1.orderId] ?? 1) }

        let todayIds = Set(today.map(\.id))
        var byMethod: [Int: (cents: Int, count: Int)] = [:]
        for row in try db.query("SELECT FiscalReceiptId, Method, Amount FROM PaymentTenders") {
            guard let rid = row.uuid("FiscalReceiptId"), todayIds.contains(rid), let method = row.int("Method") else { continue }
            byMethod[method, default: (0, 0)].cents += row.int("Amount") ?? 0
            byMethod[method, default: (0, 0)].count += 1
        }
        let tenderTotal = byMethod.values.reduce(0) { $0 + $1.cents }
        let payments = byMethod.map { method, v in
            FinancialDashboard.PaymentShare(
                methodName: (PaymentMethod(rawValue: method) ?? .cash).label, totalAmount: Money(cents: v.cents), transactionsCount: v.count,
                percentageOfTotal: tenderTotal > 0 ? (Double(v.cents) / Double(tenderTotal) * 1000).rounded() / 10 : 0)
        }.sorted { $0.totalAmount > $1.totalAmount }

        let orderRows = try db.query("SELECT Id, OperatorId, TableNumber FROM Orders")
        let orders = Dictionary(uniqueKeysWithValues: orderRows.compactMap { row in row.uuid("Id").map { ($0, (operatorId: row.uuid("OperatorId"), table: row.string("TableNumber") ?? "")) } })
        let staff = Dictionary(grouping: today, by: { orders[$0.orderId]?.operatorId ?? localNoOperator }).map { operatorId, receipts -> FinancialDashboard.StaffPerformance in
            let name = (try? staffRepository.member(id: operatorId))?.name ?? "Serveur Inconnu"
            let sales = receipts.reduce(0) { $0 + $1.cents }
            let tables = Set(receipts.compactMap { orders[$0.orderId]?.table.lowercased() }).count
            return FinancialDashboard.StaffPerformance(serverName: name, tablesServedCount: tables, totalSalesTtc: Money(cents: sales), averageTableTtc: Money(cents: tables > 0 ? Money.roundAwayFromZero(Decimal(sales) / Decimal(tables)) : 0))
        }.sorted { $0.totalSalesTtc > $1.totalSalesTtc }

        return TakingsReport(
            fromUtc: from, toUtc: to, totalTtc: Money(cents: total), lastWeekTotalTtc: Money(cents: lastWeekHours.reduce(0, +)), lastWeekByNowTtc: Money(cents: lastWeekByNow),
            covers: covers, tablesTurned: today.count, spendPerCover: Money(cents: covers > 0 ? Money.roundAwayFromZero(Decimal(total) / Decimal(covers)) : 0),
            hours: (0..<24).map { HourlyTaking(hour: $0, today: Money(cents: todayHours[$0]), lastWeek: Money(cents: lastWeekHours[$0])) },
            paymentMethods: payments, staffPerformance: staff)
    }
}
