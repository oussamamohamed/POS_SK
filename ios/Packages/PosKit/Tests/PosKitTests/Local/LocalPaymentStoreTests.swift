import Foundation
import Testing
@testable import PosKit

@Suite("Schéma v3 et dépôts paiement / attente / chambres")
struct LocalPaymentStoreTests {
    private func makeDB() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return db
    }

    @Test func migrationV3CreatesTheTables() throws {
        let db = try makeDB()
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        #expect(LocalMigrator.steps.count >= 3)
        let tables = try db.query("SELECT name FROM sqlite_master WHERE type = 'table'").compactMap { $0.string("name") }
        for expected in ["HeldOrders", "CustomerCreditVouchers", "HotelRooms", "RoomFolioCharges", "NonFiscalPayments", "LocalCounters"] {
            #expect(tables.contains(expected))
        }
    }

    @Test func paymentRepositoryTracksPaidAmountAndReceiptNumbers() throws {
        let db = try makeDB()
        let orders = LocalOrderRepository(db: db), payments = LocalPaymentRepository(db: db)
        let order = ActiveOrder(tableNumber: "T1", destination: .eatIn)
        try orders.insert(order, operatorId: localNoOperator, status: .open)
        #expect(try payments.paidCents(orderId: order.orderId) == 0)
        try payments.insertTender(orderId: order.orderId, terminalId: "T01", receiptNumber: "NF-T01-000001", method: .cash, amount: 1000, tendered: 1200, change: 200)
        try payments.insertTender(orderId: order.orderId, terminalId: "T01", receiptNumber: "NF-T01-000001", method: .creditCard, amount: 500, tendered: 500, change: 0)
        #expect(try payments.paidCents(orderId: order.orderId) == 1500)

        #expect(try payments.nextReceiptNumber(terminalId: "T01") == "NF-T01-000001")
        #expect(try payments.nextReceiptNumber(terminalId: "T01") == "NF-T01-000002")
        #expect(try payments.nextReceiptNumber(terminalId: "T02") == "NF-T02-000001")

        // Pas de cascade : une commande portant des encaissements ne peut pas être supprimée (la clé étrangère refuse).
        #expect(throws: SQLiteError.self) { try db.run("DELETE FROM Orders WHERE Id = ?", [.uuid(order.orderId)]) }
        #expect(try payments.paidCents(orderId: order.orderId) == 1500)
    }

    @Test func pickupNumbersWrapAtNinetyNineAndResetEachDay() throws {
        let db = try makeDB()
        let payments = LocalPaymentRepository(db: db)
        let day = Date(timeIntervalSince1970: 1_790_000_000)
        #expect(try (1...3).map { _ in try payments.nextPickupNumber(terminalId: "T01", now: day) } == ["#A-01", "#A-02", "#A-03"])
        #expect(try payments.nextPickupNumber(terminalId: "T01", now: day.addingTimeInterval(86_400)) == "#A-01")
        // Bouclage à 99 : après #A-99 on repart à #A-01.
        let other = Date(timeIntervalSince1970: 1_800_000_000)
        var last = ""
        for _ in 1...100 { last = try payments.nextPickupNumber(terminalId: "T01", now: other) }
        #expect(last == "#A-01")
    }

    @Test func pickupPrefixFollowsTheTerminal() {
        let cases: [(String, String)] = [("", "A"), ("T01", "A"), ("MAIN", "A"), ("T02", "B"), ("T03", "C"), ("T04", "D"), ("POS-B", "B"), ("pos03", "C")]
        for (terminal, prefix) in cases { #expect(LocalPaymentRepository.pickupPrefix(for: terminal) == prefix) }
    }

    @Test func holdRepositoryRoundTripsAndFiltersByTerminal() throws {
        let db = try makeDB()
        let holds = LocalHoldRepository(db: db)
        let first = UUID(), second = UUID(), staff = UUID()
        let holdA = try holds.insert(orderId: first, terminalId: "T01", label: "Dupont", destination: .takeaway, itemCount: 3, total: Money(cents: 4500), snapshot: "{}", staffId: staff)
        let holdB = try holds.insert(orderId: second, terminalId: "T02", label: nil, destination: .eatIn, itemCount: 1, total: Money(cents: 900), snapshot: "{}", staffId: staff)
        let t01 = try holds.active(terminalId: "T01")
        #expect(t01.count == 1 && t01[0].holdId == holdA && t01[0].orderId == first && t01[0].customerLabel == "Dupont")
        #expect(t01[0].itemCount == 3 && t01[0].totalTtc.amountInCents == 4500 && t01[0].destination == .takeaway)
        #expect(try holds.active(terminalId: "").count == 2)
        #expect(try holds.hasActiveHold(orderId: first) && !(try holds.hasActiveHold(orderId: UUID())))
        #expect(try holds.activeOrderId(holdId: holdB) == second)

        #expect(try holds.markRecalled(id: holdA))
        #expect(try holds.markRecalled(id: holdA) == false)
        #expect(try holds.activeOrderId(holdId: holdA) == nil)
        #expect(try holds.markVoided(id: holdB, staffId: staff, reason: "Erreur"))
        #expect(try holds.active(terminalId: "").isEmpty)
        #expect(try holds.markVoided(id: holdB, staffId: staff, reason: "Encore") == false)
    }

    @Test func hotelRepositoryRoundTripsRoomsAndBalance() throws {
        let db = try makeDB()
        let hotel = LocalHotelRepository(db: db)
        let now = Date()
        try hotel.insert(HotelRoom(roomNumber: "204", guestName: "Alexandre", maxCreditLimit: Money(cents: 60000)), checkIn: now, checkOut: now.addingTimeInterval(86_400))
        try hotel.insert(HotelRoom(roomNumber: "101", guestName: "Jean", maxCreditLimit: Money(cents: 30000), currentBalance: Money(cents: 1250)), checkIn: now, checkOut: now.addingTimeInterval(86_400))
        try db.run("INSERT INTO HotelRooms (Id, RoomNumber, GuestName, CheckInDateUtc, CheckOutDateUtc, IsOccupied, MaxCreditLimit, CurrentBalance) VALUES (?, '999', 'Parti', ?, ?, 0, '100', '0')", [.uuid(UUID()), .date(now), .date(now)])

        #expect(try hotel.occupiedRooms().map(\.roomNumber) == ["101", "204"])
        let room = try #require(try hotel.occupiedRoom(" 101 "))
        #expect(room.guestName == "Jean" && room.maxCreditLimit == Money(cents: 30000) && room.currentBalance == Money(cents: 1250))
        #expect(try hotel.occupiedRoom("999") == nil)
        #expect(try hotel.occupiedRoom("404") == nil)

        try hotel.addToBalance(roomNumber: "101", Money(cents: 2050))
        #expect(try hotel.occupiedRoom("101")?.currentBalance == Money(cents: 3300))
        try hotel.insertCharge(orderId: UUID(), roomNumber: "101", guestName: "Jean", amount: Money(cents: 1950), tip: Money(cents: 100), signature: nil, notes: "RAS")
        #expect(try db.query("SELECT * FROM RoomFolioCharges").count == 1)
    }

    @Test func orderRepositoryTracksStatusTipAndPickup() throws {
        let db = try makeDB()
        let orders = LocalOrderRepository(db: db)
        let order = ActiveOrder(tableNumber: "Comptoir", destination: .takeaway)
        try orders.insert(order, operatorId: localNoOperator, status: .open)
        #expect(try orders.status(of: order.orderId) == .open && LocalOrderStatus.open.isModifiable)
        #expect(try orders.status(of: UUID()) == nil)
        try orders.setStatus(orderId: order.orderId, .paid)
        #expect(try orders.status(of: order.orderId) == .paid && !LocalOrderStatus.paid.isModifiable && !LocalOrderStatus.cancelled.isModifiable)

        #expect(try orders.tipCents(of: order.orderId) == 0)
        try orders.setTip(orderId: order.orderId, cents: 250)
        #expect(try orders.tipCents(of: order.orderId) == 250)

        try orders.setPickup(orderId: order.orderId, number: "#B-07", buzzer: "12", destination: .eatIn)
        let fetched = try #require(try orders.order(id: order.orderId))
        #expect(fetched.pickupNumber == "#B-07" && fetched.pickupBuzzer == "12" && fetched.destination == .eatIn)

        let eligible = OrderLine(productId: UUID(), productName: "A", quantity: 1, unitPrice: Money(cents: 100), taxRatePercent: 10)
        let other = OrderLine(productId: UUID(), productName: "B", quantity: 1, unitPrice: Money(cents: 100), taxRatePercent: 10)
        try orders.insert(eligible, orderId: order.orderId)
        try orders.insert(other, orderId: order.orderId)
        try db.run("UPDATE OrderItems SET IsFoodVoucherEligible = 0 WHERE Id = ?", [.uuid(other.lineId)])
        #expect(try orders.foodVoucherEligibleLineIds(orderId: order.orderId) == [eligible.lineId])
    }
}
