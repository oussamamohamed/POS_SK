import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : état de la commande après annulation d'un reçu")
struct LocalVoidReceiptTests {
    private func seat(_ api: LocalPosAPI) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini") // 19,50 €
        try await seatTable(api, "T1", items: [localInput(burger)])
        return try #require(try await api.activeOrder(table: "T1"))
    }

    private func receiptIds(_ db: SQLiteDatabase) throws -> [UUID] {
        try db.query("SELECT Id FROM FiscalReceipts WHERE VoidedReceiptId IS NULL ORDER BY SequenceNumber").compactMap { $0.uuid("Id") }
    }

    private func status(_ db: SQLiteDatabase, _ order: ActiveOrder) throws -> Int? {
        try db.query("SELECT Status FROM Orders WHERE Id = ?", [.uuid(order.orderId)]).first?.int("Status")
    }

    @Test func voidPartialReceiptReopensOrderAsBillRequested() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 800)]))
        _ = try await api.voidReceipt(receiptId: try #require(try receiptIds(db).first))
        #expect(try status(db, order) == LocalOrderStatus.billRequested.rawValue)
        #expect(try await api.activeOrder(table: "T1")?.orderId == order.orderId) // la table reste occupée
    }

    @Test func voidFullReceiptCancelsOrder() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        _ = try await api.voidReceipt(receiptId: try #require(try receiptIds(db).first))
        #expect(try status(db, order) == LocalOrderStatus.cancelled.rawValue)
    }

    @Test func fullVoidFreesTable() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        // Le règlement intégral libère déjà la table : on la réoccupe comme avant l'annulation pour isoler le comportement de l'annulation.
        try db.run("UPDATE DiningTables SET ActiveOrderId = ?, Status = 1 WHERE TableNumber = 'T1'", [.uuid(order.orderId)])
        _ = try await api.voidReceipt(receiptId: try #require(try receiptIds(db).first))
        #expect(try db.query("SELECT ActiveOrderId FROM DiningTables WHERE TableNumber = 'T1'").first?.uuid("ActiveOrderId") == nil)
    }

    @Test func voidFirstOfTwoReceiptsKeepsOrderPayable() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1000)]))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 950)]))
        _ = try await api.voidReceipt(receiptId: try #require(try receiptIds(db).first))
        #expect(try status(db, order) == LocalOrderStatus.billRequested.rawValue)
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1000)]))
        #expect(result.remainingBalance == .zero)
        #expect(try status(db, order) == LocalOrderStatus.paid.rawValue)
    }

    @Test func zClosureIgnoresCancelledOrder() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 800)]))
        _ = try await api.voidReceipt(receiptId: try #require(try receiptIds(db).first))
        // Commande rouverte : la clôture Z reste refusée.
        await #expect(throws: APIError.self) { try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef") }
        // Commande annulée encore posée sur la table : ignorée.
        try db.run("UPDATE Orders SET Status = ? WHERE Id = ?", [.integer(LocalOrderStatus.cancelled.rawValue), .uuid(order.orderId)])
        _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
    }

    @Test func voidFirstOfTwoReceiptsRelinksTableAndBlocksZ() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1000)]))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 950)]))
        #expect(try await api.activeOrder(table: "T1") == nil) // règlement soldé : table libérée
        _ = try await api.voidReceipt(receiptId: try #require(try receiptIds(db).first))
        #expect(try await api.activeOrder(table: "T1")?.orderId == order.orderId)
        let table = try #require(try await api.tables().first { $0.tableNumber == "T1" })
        #expect(table.status == .occupied)
        await #expect(throws: APIError.self) { try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef") }
    }

    @Test func voidLeavesTableTakenByAnotherOrderUntouched() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1000)]))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 950)]))
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)]) // autre commande sur la même table
        let other = try #require(try await api.activeOrder(table: "T1"))
        let result = try await api.voidReceipt(receiptId: try #require(try receiptIds(db).first))
        #expect(result.success == true)
        #expect(try status(db, order) == LocalOrderStatus.billRequested.rawValue)
        #expect(try await api.activeOrder(table: "T1")?.orderId == other.orderId)
    }

    /// Reste dû d'une commande rouverte = TTC + pourboire − règlements actifs (comme `FindOpenOrdersAsync`) : le pourboire de la part restante
    /// ne doit pas masquer la part annulée.
    @Test func zClosureCountsTheTipOfTheActiveShareOfAReopenedOrder() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api) // 19,50 €
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 500)]))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 2050)], tip: 600)) // 14,50 € + 6,00 € de pourboire
        _ = try await api.voidReceipt(receiptId: try #require(try receiptIds(db).first)) // part de 5,00 € annulée : reste 5,00 €
        await #expect(throws: APIError.self) { try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef") }
        let repaid = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 500)]))
        #expect(repaid.remainingBalance == .zero)
        _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
    }

    /// Commande rouverte restée sans table (table reprise entre-temps) : la Z la voit comme le .NET (par commande, pas par table), et son
    /// règlement par identifiant ne libère pas la table de l'autre client.
    @Test func zClosureSeesAReopenedOrderWhoseTableWasTaken() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1000)]))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 950)]))
        try await seatTable(api, "T1") // autre client installé, rien de saisi : sa commande vide ne bloque pas la Z
        let other = try #require(try await api.activeOrder(table: "T1"))
        _ = try await api.voidReceipt(receiptId: try #require(try receiptIds(db).first))
        #expect(try await api.activeOrder(table: "T1")?.orderId == other.orderId)
        do {
            _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
            Issue.record("la Z aurait dû être refusée : la commande rouverte doit encore 10,00 €")
        } catch let APIError.server(status, message) {
            #expect(status == 409)
            #expect(message?.contains("T1 (10.00)") == true)
        }
        let repaid = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1000)]))
        #expect(repaid.remainingBalance == .zero)
        #expect(try await api.activeOrder(table: "T1")?.orderId == other.orderId)
        _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
    }

    @Test func voidAllSplitReceiptsKeepsOrderBillRequestedAndPayable() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api) // 19,50 €
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 650)]))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 650)]))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 650)]))
        let ids = try receiptIds(db)
        #expect(ids.count == 3)
        for id in ids { _ = try await api.voidReceipt(receiptId: id) }
        #expect(try status(db, order) == LocalOrderStatus.billRequested.rawValue)
        let resettled = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1950)]))
        #expect(resettled.remainingBalance == .zero)
        #expect(try status(db, order) == LocalOrderStatus.paid.rawValue)
        #expect(try await api.verifyChains().isValid)
    }

    @Test func voidWithGlobalDiscountAndTipHandlesFullAndPartial() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let burger = try await localProduct(api, "Burger Gourmet Rossini") // 19,50 €
        let managerId = try #require(try await api.login(pin: "1234").operatorId)

        // 1. Reçu intégral avec remise 20 % (15,60 €) + pourboire 2,00 € = 17,60 € -> Cancelled
        try await seatTable(api, "D1", items: [localInput(burger)])
        let o1 = try #require(try await api.activeOrder(table: "D1"))
        try await api.applyDiscount(orderId: o1.orderId, type: .percentage, value: 20, reason: "Geste", operatorId: managerId)
        _ = try await api.pay(PaymentRequest(
            orderId: o1.orderId, tableNumber: "D1", operatorId: managerId, terminalId: "T01",
            tenders: [localTender(.creditCard, 1760)], requestReceiptPrint: false, tipAmount: Money(cents: 200)))
        _ = try await api.voidReceipt(receiptId: try #require(try receiptIds(db).last))
        #expect(try status(db, o1) == LocalOrderStatus.cancelled.rawValue)

        // 2. Note partagée avec remise 20 % (15,60 €) + pourboire 2,00 € -> annuler la part pourboire rouvre et remet TipAmount à 0 : re-solde sans pourboire (10,00 €)
        try await seatTable(api, "D2", items: [localInput(burger)])
        let o2 = try #require(try await api.activeOrder(table: "D2"))
        try await api.applyDiscount(orderId: o2.orderId, type: .percentage, value: 20, reason: "Geste", operatorId: managerId)
        _ = try await api.pay(localPayment(o2, table: "D2", tenders: [localTender(.cash, 560)]))
        let r2 = try #require(try await api.pay(PaymentRequest(
            orderId: o2.orderId, tableNumber: "D2", operatorId: managerId, terminalId: "T01",
            tenders: [localTender(.creditCard, 1200)], requestReceiptPrint: false, tipAmount: Money(cents: 200))).receiptNumber)
        let r2Id = try #require(try db.query("SELECT Id FROM FiscalReceipts WHERE ReceiptNumber = ?", [.text(r2)]).first?.uuid("Id"))
        _ = try await api.voidReceipt(receiptId: r2Id)
        #expect(try status(db, o2) == LocalOrderStatus.billRequested.rawValue)
        #expect(try db.query("SELECT TipAmount FROM Orders WHERE Id = ?", [.uuid(o2.orderId)]).first?.int("TipAmount") == 0)
        let repaid = try await api.pay(localPayment(o2, table: "D2", tenders: [localTender(.cash, 1000)]))
        #expect(repaid.remainingBalance == .zero)
        #expect(try status(db, o2) == LocalOrderStatus.paid.rawValue)
        #expect(try await api.verifyChains().isValid)
    }

    @Test func doubleVoidAndVoidOfVoidReceiptAreRefused() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        let id = try #require(try receiptIds(db).first)
        _ = try await api.voidReceipt(receiptId: id)
        let voidId = try #require(try db.query("SELECT Id FROM FiscalReceipts WHERE VoidedReceiptId = ?", [.uuid(id)]).first?.uuid("Id"))

        await #expect(throws: APIError.self) { _ = try await api.voidReceipt(receiptId: id) }
        await #expect(throws: APIError.self) { _ = try await api.voidReceipt(receiptId: voidId) }
        #expect(try await api.verifyChains().isValid)
    }
}
