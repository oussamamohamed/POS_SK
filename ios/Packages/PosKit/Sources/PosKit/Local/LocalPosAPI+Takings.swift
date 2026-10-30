import Foundation

/// Encaissements du mode autonome : mêmes règles que `FinancialDashboardService` (.NET), calculés sur les reçus fiscaux et les commandes.
extension LocalPosAPI {
    private struct Receipt {
        let id: UUID, orderId: UUID, cents: Int, at: Date
        let vat: [Decimal: Int], ht: [Decimal: Int]?
    }

    private static func rates(_ json: String?) -> [Decimal: Int]? {
        guard let json, !json.isEmpty, let data = json.data(using: .utf8), let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        var out: [Decimal: Int] = [:]
        for (key, value) in object {
            if let rate = Decimal(string: key, locale: nil), let cents = (value as? NSNumber)?.intValue { out[rate] = cents }
        }
        return out
    }

    public func takings(from: Date, to: Date, tzOffsetMinutes: Int, compare: String?) async throws -> TakingsReport {
        try requireManager()
        let now = clock()
        let span = to.timeIntervalSince(from)
        let byHour = span <= 25 * 3600
        let mode = (compare == "previous" || compare == "lastWeek") ? compare! : (byHour ? "lastWeek" : "previous")
        let shift = mode == "previous" ? span : 7 * 86_400
        let offset = TimeInterval(tzOffsetMinutes * 60)
        // Une heure de tolérance, comme le .NET : une semaine locale qui traverse un changement d'heure mesure 7 jours ± 1 h.
        let buckets = byHour ? 24 : min(92, max(1, Int(((span - 3600) / 86_400).rounded(.up))))
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = TimeZone(secondsFromGMT: 0)!

        let rows = try db.query("SELECT Id, OrderId, TotalTtcAmount, CreatedAtUtc, VoidedReceiptId, TaxBreakdownJson, HtBreakdownJson FROM FiscalReceipts")
        let voidedIds = Set(rows.compactMap { $0.uuid("VoidedReceiptId") })
        let valid = rows.compactMap { row -> Receipt? in
            guard row.uuid("VoidedReceiptId") == nil, let id = row.uuid("Id"), !voidedIds.contains(id), let order = row.uuid("OrderId"), let at = row.date("CreatedAtUtc") else { return nil }
            return Receipt(id: id, orderId: order, cents: row.int("TotalTtcAmount") ?? 0, at: at, vat: Self.rates(row.string("TaxBreakdownJson")) ?? [:], ht: Self.rates(row.string("HtBreakdownJson")))
        }
        func inRange(_ r: Receipt, _ lo: Date, _ hi: Date) -> Bool { r.at >= lo && r.at < hi }
        func series(_ lo: Date, _ hi: Date) -> [Int] {
            var values = [Int](repeating: 0, count: buckets)
            for r in valid where inRange(r, lo, hi) {
                let i = byHour ? utc.component(.hour, from: r.at.addingTimeInterval(offset)) : min(buckets - 1, Int(r.at.timeIntervalSince(lo) / 86_400))
                values[i] += r.cents
            }
            return values
        }
        let current = series(from, to)
        let reference = series(from.addingTimeInterval(-shift), to.addingTimeInterval(-shift))
        let compareByNow = valid.filter { inRange($0, from.addingTimeInterval(-shift), to.addingTimeInterval(-shift)) && $0.at <= now.addingTimeInterval(-shift) }.reduce(0) { $0 + $1.cents }
        let points = (0..<buckets).map { i -> TakingsPoint in
            let label: String
            if byHour { label = "\(i)" } else {
                let c = utc.dateComponents([.month, .day], from: from.addingTimeInterval(offset + Double(i) * 86_400))
                label = String(format: "%02d-%02d", c.month ?? 0, c.day ?? 0)
            }
            return TakingsPoint(label: label, current: Money(cents: current[i]), compare: Money(cents: reference[i]))
        }

        let todays = valid.filter { inRange($0, from, to) }
        let total = todays.reduce(0) { $0 + $1.cents }
        let todayIds = Set(todays.map(\.id))

        // Commandes réglées de la période, hors commandes annulées par un avoir.
        let voidedOrderIds = Set(rows.compactMap { row -> UUID? in
            guard let id = row.uuid("Id"), voidedIds.contains(id) else { return nil }
            return row.uuid("OrderId")
        })
        let products = Dictionary(uniqueKeysWithValues: try db.query("SELECT Id, CategoryId FROM Products").compactMap { row in row.uuid("Id").map { ($0, row.string("CategoryId") ?? "") } })
        let categoryNames = Dictionary(uniqueKeysWithValues: try db.query("SELECT Id, Name FROM Categories").map { ($0.string("Id") ?? "", $0.string("Name") ?? "") })
        let orderRows = try db.query("SELECT Id, OperatorId, TableNumber, CreatedAtUtc, TipAmount FROM Orders WHERE Status = ?", [.integer(LocalOrderStatus.paid.rawValue)])
        var discounts = 0, comps = 0, tips = 0
        var tipsByServer: [String: Int] = [:]
        var byCategory: [String: (quantity: Int, sales: Decimal)] = [:]
        var byProduct: [UUID: (name: String, quantity: Int, sales: Decimal)] = [:]
        func serverName(_ id: UUID?) -> String { id.flatMap { try? staffRepository.member(id: $0)?.name } ?? "Serveur Inconnu" }

        for row in orderRows {
            guard let orderId = row.uuid("Id"), let created = row.date("CreatedAtUtc"), created >= from, created < to, !voidedOrderIds.contains(orderId),
                  let order = try orderRepository.order(id: orderId) else { continue }
            let lines = order.lines.map(CartLine.init(serverLine:))
            let totals = OrderMath.totals(lines: lines, destination: order.destination, discount: order.globalDiscount)
            let subtotal = totals.subtotalTtc.cents, orderTotal = totals.totalTtc.cents
            let scale: Decimal = subtotal > 0 ? Decimal(orderTotal) / Decimal(subtotal) : 0
            let isGlobalComp = order.globalDiscountType == .comp
            if isGlobalComp {
                comps += subtotal
            } else {
                discounts += subtotal - orderTotal
                for line in lines {
                    let base = line.effectiveUnitPrice.cents * line.quantity
                    if line.isComp { comps += base } else if line.discountPercent > 0 { discounts += base - OrderMath.lineTotal(line).cents }
                }
            }
            let tip = row.int("TipAmount") ?? 0
            let server = serverName(row.uuid("OperatorId"))
            tips += tip
            tipsByServer[server, default: 0] += tip
            if isGlobalComp { continue }
            for line in lines where !line.isComp && line.quantity > 0 {
                let sales = Decimal(OrderMath.lineTotal(line).cents) * scale / 100
                let category = products[line.productId].flatMap { categoryNames[$0] } ?? "—"
                byCategory[category] = ((byCategory[category]?.quantity ?? 0) + line.quantity, (byCategory[category]?.sales ?? 0) + sales)
                byProduct[line.productId] = (line.name, (byProduct[line.productId]?.quantity ?? 0) + line.quantity, (byProduct[line.productId]?.sales ?? 0) + sales)
            }
        }
        let salesTotal = byCategory.values.reduce(Decimal(0)) { $0 + $1.sales }
        func eurosCents(_ v: Decimal) -> Money { Money(cents: Money.roundAwayFromZero(v * 100)) }
        let categories = byCategory.sorted { $0.value.sales > $1.value.sales }.map {
            CategoryTaking(name: $0.key, quantity: $0.value.quantity, salesTtc: eurosCents($0.value.sales), share: salesTotal > 0 ? (NSDecimalNumber(decimal: $0.value.sales / salesTotal).doubleValue * 10_000).rounded() / 10_000 : 0)
        }
        let topProducts = byProduct.sorted { $0.value.sales > $1.value.sales }.prefix(5).map {
            FinancialDashboard.TopProduct(productId: $0.key, productName: $0.value.name, quantitySold: $0.value.quantity, totalSalesTtc: eurosCents($0.value.sales), percentageOfTotal: salesTotal > 0 ? (NSDecimalNumber(decimal: $0.value.sales / salesTotal * 100).doubleValue * 10).rounded() / 10 : 0)
        }

        var vat: [Decimal: (base: Int, vat: Int)] = [:]
        for r in todays {
            for (rate, vatCents) in r.vat {
                let base = r.ht?[rate] ?? (rate > 0 ? Money.roundAwayFromZero(Decimal(vatCents) * 100 / rate) : 0)
                vat[rate] = ((vat[rate]?.base ?? 0) + base, (vat[rate]?.vat ?? 0) + vatCents)
            }
        }

        // Couverts : ceux des bons de cuisine de la commande, au moins 1 (la base ne garde pas les couverts d'une table libérée).
        let ticketCovers = Dictionary(grouping: try db.query("SELECT OrderId, CoversCount FROM KitchenTickets"), by: { $0.uuid("OrderId") })
            .mapValues { $0.compactMap { $0.int("CoversCount") }.max() ?? 1 }
        let covers = todays.reduce(0) { $0 + max(1, ticketCovers[$1.orderId] ?? 1) }

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

        let orderInfo = Dictionary(uniqueKeysWithValues: orderRows.compactMap { row in row.uuid("Id").map { ($0, (operatorId: row.uuid("OperatorId"), table: row.string("TableNumber") ?? "")) } })
        let staff = Dictionary(grouping: todays, by: { orderInfo[$0.orderId]?.operatorId ?? localNoOperator }).map { operatorId, receipts -> FinancialDashboard.StaffPerformance in
            let sales = receipts.reduce(0) { $0 + $1.cents }
            let tables = Set(receipts.compactMap { orderInfo[$0.orderId]?.table.lowercased() }).count
            return FinancialDashboard.StaffPerformance(serverName: serverName(operatorId), tablesServedCount: tables, totalSalesTtc: Money(cents: sales), averageTableTtc: Money(cents: tables > 0 ? Money.roundAwayFromZero(Decimal(sales) / Decimal(tables)) : 0))
        }.sorted { $0.totalSalesTtc > $1.totalSalesTtc }

        let credits = rows.filter { row in row.uuid("VoidedReceiptId") != nil && (row.date("CreatedAtUtc").map { $0 >= from && $0 < to } ?? false) }
        return TakingsReport(
            fromUtc: from, toUtc: to, granularity: byHour ? "hour" : "day", compare: mode, totalTtc: Money(cents: total), compareTotalTtc: Money(cents: reference.reduce(0, +)),
            compareByNowTtc: Money(cents: compareByNow), covers: covers, tablesTurned: todays.count,
            spendPerCover: Money(cents: covers > 0 ? Money.roundAwayFromZero(Decimal(total) / Decimal(covers)) : 0), series: points,
            paymentMethods: payments, staffPerformance: staff, tipsTotalTtc: Money(cents: tips),
            tipsByServer: tipsByServer.filter { $0.value > 0 }.sorted { $0.value > $1.value }.map { ServerTip(serverName: $0.key, tipsTtc: Money(cents: $0.value)) },
            discountsTtc: Money(cents: discounts), compsTtc: Money(cents: comps), voidsCount: credits.count,
            voidsTtc: Money(cents: abs(credits.reduce(0) { $0 + ($1.int("TotalTtcAmount") ?? 0) })),
            categories: categories, topProducts: Array(topProducts),
            vat: vat.sorted { $0.key < $1.key }.map { TakingsVatLine(ratePercent: $0.key, baseHt: Money(cents: $0.value.base), vat: Money(cents: $0.value.vat)) })
    }

    public func takingsReceipts(from: Date, to: Date) async throws -> [TakingsReceipt] {
        try requireManager()
        let rows = try db.query("SELECT Id, OrderId, ReceiptNumber, TotalTtcAmount, CreatedAtUtc, VoidedReceiptId FROM FiscalReceipts")
        let voidedIds = Set(rows.compactMap { $0.uuid("VoidedReceiptId") })
        let methods = Dictionary(grouping: try db.query("SELECT FiscalReceiptId, Method FROM PaymentTenders"), by: { $0.uuid("FiscalReceiptId") })
        let orders = Dictionary(uniqueKeysWithValues: try db.query("SELECT Id, OperatorId, TableNumber FROM Orders").compactMap { row in row.uuid("Id").map { ($0, (operatorId: row.uuid("OperatorId"), table: row.string("TableNumber"))) } })
        return rows.compactMap { row -> TakingsReceipt? in
            guard let id = row.uuid("Id"), let at = row.date("CreatedAtUtc"), at >= from, at < to else { return nil }
            let order = row.uuid("OrderId").flatMap { orders[$0] }
            let labels = Array(Set((methods[id] ?? []).compactMap { $0.int("Method") }.map { (PaymentMethod(rawValue: $0) ?? .cash).label })).sorted()
            return TakingsReceipt(
                id: id, receiptNumber: row.string("ReceiptNumber") ?? "", atUtc: at, tableNumber: order?.table,
                serverName: order?.operatorId.flatMap { try? staffRepository.member(id: $0)?.name } ?? "Serveur Inconnu", methods: labels,
                totalTtc: Money(cents: row.int("TotalTtcAmount") ?? 0), isCredit: row.uuid("VoidedReceiptId") != nil, isCancelled: voidedIds.contains(id))
        }.sorted { $0.atUtc > $1.atUtc }.prefix(300).map { $0 }
    }

    /// Journal du jour courant seulement : le début du jour se calcule avec le décalage local du restaurant.
    public func journalToday(tzOffsetMinutes: Int) async throws -> [JournalEntry] {
        try requireManager()
        let offset = TimeInterval(tzOffsetMinutes * 60)
        let localNow = clock().timeIntervalSince1970 + offset
        let startUtc = Date(timeIntervalSince1970: (localNow / 86_400).rounded(.down) * 86_400 - offset)
        let rows = try db.query("SELECT Id, LocalSequence, TerminalId, OccurredAtUtc, EventType, PayloadJson, OperatorId FROM JournalEntries")
        return rows.compactMap { row -> (JournalEntry, Int)? in
            guard let id = row.uuid("Id"), let at = row.date("OccurredAtUtc"), at >= startUtc else { return nil }
            let name = row.uuid("OperatorId").flatMap { try? staffRepository.member(id: $0)?.name }
            return (JournalEntry(id: id, atUtc: at, eventType: row.string("EventType") ?? "", terminalId: row.string("TerminalId"), operatorName: name, payloadJson: row.string("PayloadJson") ?? "{}"), row.int("LocalSequence") ?? 0)
        }.sorted { ($0.0.atUtc, $0.1) > ($1.0.atUtc, $1.1) }.prefix(300).map(\.0)
    }
}
