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
}
