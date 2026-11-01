import Foundation

extension LocalPosAPI {
    /// Quelques ventes réglées aujourd'hui (cartes, espèces, titres-restaurant) pour remplir le tableau de bord de la démonstration.
    /// Démonstration seulement (`-UITestDemoSales`) : passe par l'API publique, donc chaque vente est un vrai reçu fiscal chaîné.
    /// Ouvre une session responsable (PIN 1234 de la base de démonstration).
    public func seedDemoSales() async throws {
        let manager = try await login(pin: "1234").operatorId
        let catalog = try await products()
        func input(_ name: String, _ quantity: Int) -> OrderItemInput? {
            catalog.first { $0.name == name }.map {
                OrderItemInput(
                    productId: $0.id, productName: $0.name, quantity: quantity, unitPrice: $0.price, taxRatePercent: $0.taxRatePercent,
                    preparationStationId: $0.preparationStationId, modifiers: [], course: .direct, modifiersPriceExtra: .zero,
                    taxRateTakeawayPercent: $0.taxRateTakeawayPercent, isHappyHourApplied: false, originalUnitPrice: nil, appliedHappyHourScheduleId: nil)
            }
        }
        let sales: [(table: String, covers: Int, waiter: String, items: [(String, Int)], method: PaymentMethod, tip: Int)] = [
            ("T1", 2, "Sophie", [("Burger Gourmet Rossini", 2), ("Bière Artisanale IPA 33cl", 2), ("Eau Pétillante 50cl", 3)], .creditCard, 300),
            ("T2", 4, "Sophie", [("Pizza Margherita AOP", 2), ("Pizza 4 Fromages", 2), ("Tiramisu Maison", 3)], .creditCard, 500),
            ("T3", 3, "Alexandre", [("Entrecôte 300g", 2), ("Salade César Poulet", 1), ("Verre Bordeaux AOP 12cl", 3)], .cash, 0),
            ("T4", 2, "Alexandre", [("Tartare de Saumon", 2), ("Café Gourmand", 2)], .mealVoucher, 0),
        ]
        for sale in sales {
            let items = sale.items.compactMap { input($0.0, $0.1) }
            guard !items.isEmpty else { continue }
            try await openTable(number: sale.table, covers: sale.covers, operatorId: manager, waiterName: sale.waiter)
            _ = try await addItems(table: sale.table, items: items)
            guard let order = try await activeOrder(table: sale.table), let total = order.totalTtcAmount else { continue }
            let paid = total + Money(cents: sale.tip) // le règlement couvre la note et le pourboire
            let tender = TenderInput(method: sale.method, amount: paid, tendered: paid, changeGiven: .zero)
            _ = try await pay(PaymentRequest(
                orderId: order.orderId, tableNumber: sale.table, operatorId: manager, terminalId: "T01", tenders: [tender],
                requestReceiptPrint: false, tipAmount: Money(cents: sale.tip)))
        }
    }
}
