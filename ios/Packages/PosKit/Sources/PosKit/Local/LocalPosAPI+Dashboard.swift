import Foundation

/// Tableau de bord de la Gestion en mode autonome : mêmes règles que `FinancialDashboardService.GetFinancialDashboardAsync` (.NET).
/// Paiements, personnel et couverts viennent de `takings` (même calcul) ; le reste est refait ici sur les reçus et les commandes.
extension LocalPosAPI {
    public func dashboard(from: Date, to: Date) async throws -> FinancialDashboard {
        try requireManager()
        let (start, end) = from <= to ? (from, to) : (to, from)
        let report = try await takings(from: start, to: end, tzOffsetMinutes: 0, compare: nil)

        let rows = try db.query("SELECT Id, OrderId, TotalTtcAmount, TotalHtAmount, CreatedAtUtc, VoidedReceiptId FROM FiscalReceipts")
        let voidedIds = Set(rows.compactMap { $0.uuid("VoidedReceiptId") })
        let receipts = rows.filter {
            guard $0.uuid("VoidedReceiptId") == nil, let id = $0.uuid("Id"), !voidedIds.contains(id), let at = $0.date("CreatedAtUtc") else { return false }
            return at >= start && at < end
        }
        let ht = receipts.reduce(0) { $0 + ($1.int("TotalHtAmount") ?? 0) }
        let total = report.totalTtc.cents
        let orders = report.tablesTurned
        func avg(_ cents: Int, _ count: Int) -> Money { Money(cents: count > 0 ? Money.roundAwayFromZero(Decimal(cents) / Decimal(count)) : 0) }
        let kpis = FinancialDashboard.Kpis(
            totalSalesTtc: report.totalTtc, totalSalesHt: Money(cents: ht), averageOrderTtc: avg(total, orders), averageCoverTtc: avg(total, report.covers),
            totalOrdersCount: orders, totalCoversCount: report.covers)

        // Services : l'heure UTC de la commande (comme le .NET) ; les couverts valent 1 par commande, la base ne gardant pas ceux d'une table libérée.
        let createdAt = Dictionary(uniqueKeysWithValues: try db.query("SELECT Id, CreatedAtUtc FROM Orders").compactMap { row in
            row.uuid("Id").flatMap { id in row.date("CreatedAtUtc").map { (id, $0) } }
        })
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = TimeZone(secondsFromGMT: 0)!
        func serviceName(_ date: Date) -> String {
            switch utc.component(.hour, from: date) {
            case 11..<16: "Service Midi (11h-16h)"
            case 18...23: "Service Soir (18h-24h)"
            default: "Continu / Hors Service"
            }
        }
        var groups: [String: (ttc: Int, orders: Int)] = [:]
        for r in receipts {
            let name = serviceName(r.uuid("OrderId").flatMap { createdAt[$0] } ?? r.date("CreatedAtUtc") ?? start)
            groups[name, default: (0, 0)].ttc += r.int("TotalTtcAmount") ?? 0
            groups[name, default: (0, 0)].orders += 1
        }
        var services = groups.sorted { $0.key < $1.key }.map { name, g in
            FinancialDashboard.Service(serviceName: name, salesTtc: Money(cents: g.ttc), ordersCount: g.orders, coversCount: g.orders, averageCoverTtc: avg(g.ttc, g.orders))
        }
        if services.isEmpty {
            services = ["Service Midi (11h-16h)", "Service Soir (18h-24h)"].map {
                FinancialDashboard.Service(serviceName: $0, salesTtc: .zero, ordersCount: 0, coversCount: 0, averageCoverTtc: .zero)
            }
        }

        // 10 premiers produits, toutes lignes comprises (offerts aussi), sur les commandes de la période.
        var byProduct: [UUID: (name: String, quantity: Int, cents: Int)] = [:]
        for orderId in Set(receipts.compactMap { $0.uuid("OrderId") }) {
            guard let order = try orderRepository.order(id: orderId) else { continue }
            for line in order.lines.map(CartLine.init(serverLine:)) {
                byProduct[line.productId, default: (line.name, 0, 0)].quantity += line.quantity
                byProduct[line.productId, default: (line.name, 0, 0)].cents += OrderMath.lineTotal(line).cents
            }
        }
        let top = byProduct.sorted { $0.value.cents > $1.value.cents }.prefix(10).map { id, p in
            FinancialDashboard.TopProduct(
                productId: id, productName: p.name, quantitySold: p.quantity, totalSalesTtc: Money(cents: p.cents),
                percentageOfTotal: total > 0 ? (Double(p.cents) / Double(total) * 1000).rounded() / 10 : 0)
        }

        return FinancialDashboard(kpis: kpis, services: services, topProducts: Array(top), staffPerformance: orders > 0 ? report.staffPerformance : [], paymentMethods: report.paymentMethods)
    }
}
