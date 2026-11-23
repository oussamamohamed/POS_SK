import Foundation
import Testing
@testable import PosKit

extension LocalPosAPI {
    func testVoidRows() throws -> [SQLRow] { try db.query("SELECT * FROM OrderLineVoids ORDER BY rowid") }
    func testTicketItems() throws -> [SQLRow] { try db.query("SELECT * FROM KitchenTicketItems ORDER BY rowid") }
    func testJetCount(_ type: String) throws -> Int { try journalRows(type).count }
}

@Suite("LocalPosAPI : annulation d'une ligne envoyée et correction")
struct LocalLineVoidTests {
    private static let burger = "Burger Gourmet Rossini"
    private static let voided = "ORDER_LINE_VOIDED"

    private func sentLine(_ api: LocalPosAPI, table: String = "T1", quantity: Int = 1) async throws -> (order: ActiveOrder, line: OrderLine) {
        let burger = try await localProduct(api, Self.burger)
        try await seatTable(api, table, items: [localInput(burger, quantity: quantity)])
        try await api.dispatch(table: table)
        let order = try #require(try await api.activeOrder(table: table))
        return (order, try #require(order.lines.first))
    }

    @Test func voidSentLineByManagerWritesJetAndVoidRow() async throws {
        let api = try await makeLocalAPI()
        let (order, line) = try await sentLine(api)
        try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "  Erreur de saisie ", operatorId: nil)

        #expect(try await api.activeOrder(table: "T1")?.lines.isEmpty == true)
        let rows = try await api.testVoidRows()
        let row = try #require(rows.first)
        #expect(rows.count == 1 && row.string("Reason") == "Erreur de saisie" && row.int("Quantity") == 1 && row.int("UnitPrice") == 1950)
        #expect(row.string("ProductName") == Self.burger && row.string("TableNumber") == "T1" && row.string("ServerName") == "Sophie")
        #expect(row.uuid("OrderItemId") == line.lineId && row.uuid("TicketId") != nil)
        let jet = try await api.journalRows(Self.voided)
        #expect(jet.count == 1 && jet[0].payload.contains("\"reason\":\"Erreur de saisie\"") && jet[0].payload.contains("\"unitPriceCents\":1950"))
        let item = try #require(try await api.testTicketItems().first)
        #expect(item.int("CancelledQuantity") == 1 && item.int("Status") == TicketItemStatus.cancelled.rawValue)
        let ticketItem = try #require(try await api.kitchenTickets().first?.items.first)
        #expect(ticketItem.status == .cancelled && ticketItem.cancelledQuantity == 1)
    }

    @Test func partialVoidKeepsRemainder() async throws {
        let api = try await makeLocalAPI()
        let (order, line) = try await sentLine(api, quantity: 3)
        try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "Client parti", operatorId: nil)

        #expect(try await api.activeOrder(table: "T1")?.lines.first?.quantity == 2)
        let item = try #require(try await api.testTicketItems().first)
        #expect(item.int("CancelledQuantity") == 1 && item.int("Status") == TicketItemStatus.pending.rawValue)
        await #expect(throws: APIError.self) { try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 3, reason: "Trop", operatorId: nil) }
    }

    @Test func waiterCannotVoidSentLine() async throws {
        let api = try await makeLocalAPI(pin: "2468")
        let (order, line) = try await sentLine(api)
        await #expect(throws: APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")) {
            try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "Test", operatorId: nil)
        }
        #expect(try await api.activeOrder(table: "T1")?.lines.count == 1)
        #expect(try await api.testVoidRows().isEmpty)
        #expect(try await api.testJetCount(Self.voided) == 0)
    }

    @Test func adminCanVoidSentLineAndKitchenIsForbidden() async throws {
        let api = try await makeLocalAPI(pin: "5678")
        let (order, line) = try await sentLine(api, quantity: 2)
        await #expect(throws: APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")) {
            try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "Test", operatorId: nil)
        }
        _ = try await api.login(pin: "9999")
        try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "Admin", operatorId: nil)
        #expect(try await api.testVoidRows().count == 1)
    }

    @Test func voidOnPaidOrCancelledOrderReturns409NotModifiable() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let (paidOrder, paidLine) = try await sentLine(api, table: "T3")
        _ = try await api.pay(localPayment(paidOrder, table: "T3", tenders: [localTender(.cash, 1950)]))
        await #expect {
            try await api.voidLine(orderId: paidOrder.orderId, lineId: paidLine.lineId, quantity: 1, reason: "Tardif", operatorId: nil)
        } throws: { error in
            guard case .server(let status, _) = error as? APIError else { return false }
            return status == 409
        }

        let (cancelledOrder, cancelledLine) = try await sentLine(api, table: "T4")
        _ = try await api.pay(localPayment(cancelledOrder, table: "T4", tenders: [localTender(.cash, 1950)]))
        let rid = try #require(try db.query("SELECT Id FROM FiscalReceipts WHERE OrderId = ?", [.uuid(cancelledOrder.orderId)]).first?.uuid("Id"))
        _ = try await api.voidReceipt(receiptId: rid)
        await #expect {
            try await api.voidLine(orderId: cancelledOrder.orderId, lineId: cancelledLine.lineId, quantity: 1, reason: "Après annul", operatorId: nil)
        } throws: { error in
            guard case .server(let status, _) = error as? APIError else { return false }
            return status == 409
        }
    }

    @Test func voidWithReceiptIsRefused() async throws {
        let api = try await makeLocalAPI()
        let (order, line) = try await sentLine(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 500)]))
        await #expect {
            try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "Test", operatorId: nil)
        } throws: { error in
            guard case .server(let status, let message?) = error as? APIError else { return false }
            return status == 409 && message.contains("a déjà des reçus (") && message.contains("annulez-les")
        }
        #expect(try await api.activeOrder(table: "T1")?.lines.count == 1)
        #expect(try await api.testVoidRows().isEmpty)
    }

    @Test func voidedReceiptPairDoesNotBlockVoidAndChainsStayValid() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let (order, line) = try await sentLine(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 500)]))
        let receiptId = try #require(try db.query("SELECT Id FROM FiscalReceipts WHERE VoidedReceiptId IS NULL").first?.uuid("Id"))
        _ = try await api.voidReceipt(receiptId: receiptId)
        try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "Après annulation du reçu", operatorId: nil)
        #expect(try await api.testVoidRows().count == 1)
        #expect(try await api.verifyChains().isValid)
    }

    @Test func unsentLineCorrectionWritesNoJet() async throws {
        let api = try await makeLocalAPI(pin: "2468")
        let burger = try await localProduct(api, Self.burger)
        try await seatTable(api, "T2", items: [localInput(burger, quantity: 2)])
        let order = try #require(try await api.activeOrder(table: "T2"))
        let line = try #require(order.lines.first)
        try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: nil, operatorId: nil)
        #expect(try await api.activeOrder(table: "T2")?.lines.first?.quantity == 1)
        try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: nil, operatorId: nil)
        #expect(try await api.activeOrder(table: "T2")?.lines.isEmpty == true)
        #expect(try await api.testVoidRows().isEmpty)
        #expect(try await api.testJetCount(Self.voided) == 0)
    }

    private func rig(station: String = "HOT_KITCHEN") async throws -> (api: LocalPosAPI, fake: FakePrinterTransport, hot: Printer) {
        let fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: LocalTestClock(), transport: fake)
        for seeded in try await api.printers() { try await api.setTestPrinterActive(seeded.id, false) }
        let hot = Printer(name: "Chaud", ipAddress: "10.0.0.1", openCashDrawerOnReceipt: true, assignedStationIds: [station], textMode: true)
        try await api.addTestPrinter(hot)
        return (api, fake, hot)
    }

    @Test func legacyLineWithoutOrderItemIdStillPrintsAndWritesJet() async throws {
        let (api, fake, hot) = try await rig()
        let (order, line) = try await sentLine(api)
        try await api.testExec("UPDATE KitchenTicketItems SET OrderItemId = NULL")
        try await api.deleteTestJobs()
        try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "Ancienne ligne", operatorId: nil)

        let row = try #require(try await api.testVoidRows().first)
        #expect(row.uuid("TicketId") == nil)
        #expect(try await api.testJetCount(Self.voided) == 1)
        let item = try #require(try await api.testTicketItems().first)
        #expect(item.int("CancelledQuantity") == 0 && item.int("Status") == TicketItemStatus.pending.rawValue)
        let jobs = try await api.allTestJobs()
        #expect(jobs.count == 1 && jobs[0].printerId == hot.id && !jobs[0].openCashDrawer)
        await api.runPrintCycle()
        #expect(fake.sent.count == 1 && payload(fake.sent[0].payload, contains: "Burger") && payload(fake.sent[0].payload, contains: "Ancienne"))
    }

    /// Comme le .NET (`PrintDispatcher.QueueKitchenCancellationAsync`) : l'en-tête suit la destination et le retrait de la commande.
    @Test(arguments: [(OrderDestination.takeaway, "C03" as String?, true), (.takeaway, nil, true), (.eatIn, nil, false)])
    func cancellationSlipHeaderFollowsTheOrder(destination: OrderDestination, pickup: String?, takeaway: Bool) async throws {
        let (api, fake, _) = try await rig()
        let (order, line) = try await sentLine(api, table: DiningTable.counterNumber)
        try await api.testExec("UPDATE Orders SET Destination = \(destination.rawValue), PickupNumber = \(pickup.map { "'\($0)'" } ?? "NULL"), PickupBuzzer = \(pickup == nil ? "NULL" : "'Z9'")")
        try await api.deleteTestJobs()
        try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "Erreur", operatorId: nil)
        await api.runPrintCycle()
        let bytes = try #require(fake.sent.first?.payload)
        #expect(payload(bytes, contains: "EMPORTER") == takeaway)
        #expect(payload(bytes, contains: "TABLE \(DiningTable.counterNumber)") == !takeaway)
        #expect(payload(bytes, contains: "C03") == (pickup != nil) && payload(bytes, contains: "Z9") == (pickup != nil))
    }

    @Test func voidQueuesCancellationJobAfterCommit() async throws {
        let (api, fake, hot) = try await rig()
        let (order, line) = try await sentLine(api, quantity: 2)
        try await api.deleteTestJobs()
        // Refus (quantité invalide) : rien n'est mis en file.
        await #expect(throws: APIError.self) { try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 5, reason: "Non", operatorId: nil) }
        #expect(try await api.allTestJobs().isEmpty)

        try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "Plat refusé", operatorId: nil)
        let jobs = try await api.allTestJobs()
        #expect(jobs.count == 1 && jobs[0].kind == .kitchenTicket && jobs[0].printerId == hot.id)
        await api.runPrintCycle()
        #expect(fake.sent.count == 1 && payload(fake.sent[0].payload, contains: "Plat refus") && payload(fake.sent[0].payload, contains: "T1"))

        // Une panne de file ne fait jamais échouer l'annulation.
        try await api.testExec("CREATE TRIGGER fail_print BEFORE INSERT ON PrintJobs BEGIN SELECT RAISE(ABORT, 'disque plein'); END;")
        try await api.voidLine(orderId: order.orderId, lineId: line.lineId, quantity: 1, reason: "Encore", operatorId: nil)
        #expect(try await api.testVoidRows().count == 2)
    }

    @Test func migrationV17AppliesOnV16Database() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try db.transaction {
            for (i, script) in LocalMigrator.steps.prefix(16).enumerated() {
                try db.exec(script)
                try db.exec("PRAGMA user_version = \(i + 1)")
            }
        }
        try db.run("INSERT INTO KitchenTickets (Id, OrderId, TableNumber, ServerName, CoversCount, StationId, Status, DispatchedAtUtc) VALUES ('K1', 'O1', 'T1', 'S', 1, 'HOT_KITCHEN', 0, '2026-01-01T00:00:00.000Z')")
        try db.run("INSERT INTO KitchenTicketItems (Id, TicketId, ProductId, ProductName, Quantity, Status) VALUES ('I1', 'K1', 'P1', 'X', 1, 0)")
        try LocalMigrator.migrate(db)

        #expect(try db.userVersion() == LocalMigrator.steps.count && LocalMigrator.steps.count >= 17)
        let item = try #require(try db.query("SELECT OrderItemId, CancelledQuantity FROM KitchenTicketItems").first)
        #expect(item.string("OrderItemId") == nil && item.int("CancelledQuantity") == 0)
        let columns = try db.query("PRAGMA table_info(OrderLineVoids)").compactMap { $0.string("name") }
        #expect(columns == ["Id", "OrderId", "OrderItemId", "TicketId", "ProductId", "ProductName", "Quantity", "UnitPrice", "ModifiersSummary", "KitchenComment",
                            "PreparationStationId", "Reason", "OperatorId", "OperatorName", "ServerName", "TableNumber", "VoidedAtUtc"])
    }
}
