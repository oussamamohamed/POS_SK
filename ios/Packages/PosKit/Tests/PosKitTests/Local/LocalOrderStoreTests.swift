import Foundation
import Testing
@testable import PosKit

@Suite("Schéma v2 et dépôts commandes / cuisine")
struct LocalOrderStoreTests {
    private func makeDB() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return db
    }

    @Test func migrationV2CreatesOrderTables() throws {
        let db = try makeDB()
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        #expect(LocalMigrator.steps.count >= 2)
        let tables = try db.query("SELECT name FROM sqlite_master WHERE type = 'table'").compactMap { $0.string("name") }
        for expected in ["Orders", "OrderItems", "KitchenTickets", "KitchenTicketItems", "TableTransferLogs", "OrderDiscountAudits"] {
            #expect(tables.contains(expected))
        }
    }

    @Test func deletingAnOrderCascadesToItsLines() throws {
        let db = try makeDB()
        let repo = LocalOrderRepository(db: db)
        let order = ActiveOrder(tableNumber: "T1", destination: .eatIn)
        try repo.insert(order, operatorId: localNoOperator, status: .open)
        try repo.insert(OrderLine(productId: UUID(), productName: "Café", quantity: 1, unitPrice: Money(cents: 300), taxRatePercent: 10), orderId: order.orderId)
        #expect(try repo.lines(orderId: order.orderId).count == 1)
        try db.run("DELETE FROM Orders WHERE Id = ?", [.uuid(order.orderId)])
        #expect(try repo.lines(orderId: order.orderId).isEmpty)
    }

    @Test func orderRepositoryRoundTripsAnOrderAndItsLines() throws {
        let db = try makeDB()
        let repo = LocalOrderRepository(db: db)
        let scheduleId = UUID()
        let line = OrderLine(
            productId: UUID(), productName: "Café ☕", quantity: 3, unitPrice: Money(cents: 450), taxRatePercent: 5.5,
            preparationStationId: "BAR", modifiersSummary: ["Sans sucre, svp", "Lait d'avoine"], course: .dessert,
            discountPercent: 12.5, modifiersPriceExtra: Money(cents: 30), taxRateTakeawayPercent: 5.5,
            isHappyHourApplied: true, originalUnitPrice: Money(cents: 500), appliedHappyHourScheduleId: scheduleId
        )
        let order = ActiveOrder(tableNumber: "T9", globalDiscountType: .fixedAmount, globalDiscountValue: 5, globalDiscountReason: "Geste", destination: .takeaway, pickupNumber: "#A-01", pickupBuzzer: "12")
        try repo.insert(order, operatorId: localNoOperator, status: .open)
        try repo.insert(line, orderId: order.orderId)

        let fetched = try #require(try repo.order(id: order.orderId))
        #expect(fetched.tableNumber == "T9" && fetched.destination == .takeaway)
        #expect(fetched.globalDiscountType == .fixedAmount && fetched.globalDiscountValue == 5 && fetched.globalDiscountReason == "Geste")
        #expect(fetched.pickupNumber == "#A-01" && fetched.pickupBuzzer == "12")
        #expect(fetched.lines == [line])

        try repo.setQuantity(lineId: line.lineId, quantity: 7)
        try repo.markDispatched(orderId: order.orderId)
        let updated = try #require(try repo.lines(orderId: order.orderId).first)
        #expect(updated.quantity == 7 && updated.isDispatched)
        #expect(try repo.order(id: UUID()) == nil)
    }

    @Test func kitchenRepositoryRoundTripsATicketAndBumpsIt() throws {
        let db = try makeDB()
        let repo = LocalKitchenRepository(db: db)
        let orderId = UUID()
        let productIds = [UUID(), UUID()]
        let ticket = KitchenTicket(
            orderId: orderId, tableNumber: "T3", serverName: "Sophie", coversCount: 4, stationId: "BAR",
            items: [KitchenTicketItem(productName: "Café", quantity: 2, modifiersSummary: "Sans sucre"), KitchenTicketItem(productName: "Thé", quantity: 1)]
        )
        try repo.insert(ticket, productIds: productIds)
        let fetched = try #require(try repo.recentTickets(limit: 50).first)
        #expect(fetched.id == ticket.id && fetched.orderId == orderId && fetched.tableNumber == "T3")
        #expect(fetched.serverName == "Sophie" && fetched.coversCount == 4 && fetched.stationId == "BAR" && fetched.status == .pending)
        #expect(fetched.items.map(\.productName) == ["Café", "Thé"] && fetched.items.map(\.quantity) == [2, 1])
        #expect(fetched.items[0].modifiersSummary == "Sans sucre" && fetched.items[1].modifiersSummary == nil)

        #expect(try repo.bump(id: ticket.id))
        #expect(try repo.recentTickets(limit: 50).first?.status == .inPreparation)
        #expect(try repo.bump(id: UUID()) == false)
        #expect(throws: SQLiteError.self) { try repo.insert(ticket, productIds: [UUID()]) }
    }

    @Test func floorRepositoryUpdatesTableState() throws {
        let db = try makeDB()
        let floor = LocalFloorRepository(db: db)
        try floor.insert(DiningTable(tableNumber: "T1", capacity: 4))
        let waiter = UUID(), order = UUID(), opened = Date()
        try floor.update("T1", status: .occupied, covers: 3, waiterName: "Sophie", waiterId: waiter, activeOrderId: order, openedAt: opened)
        let table = try #require(try floor.table("T1"))
        #expect(table.status == .occupied && table.coversCount == 3 && table.assignedWaiterName == "Sophie")
        #expect(table.activeOrderId == order && table.capacity == 4)
        #expect(table.openedAtUtc.map { abs($0.timeIntervalSince(opened)) < 0.002 } == true)
        #expect(try floor.waiterId(of: "T1") == waiter)
        try floor.update("T1", status: .free, covers: 0, waiterName: nil, waiterId: nil, activeOrderId: nil, openedAt: nil)
        #expect(try floor.table("T1")?.activeOrderId == nil)
        #expect(try floor.waiterId(of: "T1") == nil)
        #expect(try floor.table("T404") == nil)
    }
}
