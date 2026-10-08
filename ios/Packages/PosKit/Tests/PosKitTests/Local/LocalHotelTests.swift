import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : chambres d'hôtel")
struct LocalHotelTests {
    private func seat(_ api: LocalPosAPI, _ table: String = "T3", quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger, quantity: quantity)])
        return try #require(try await api.activeOrder(table: table))
    }

    private func charge(_ order: ActiveOrder, table: String = "T3", room: String = "101", amount: Int, tip: Int = 0) -> RoomChargeRequest {
        RoomChargeRequest(
            orderId: order.orderId, tableNumber: table, roomNumber: room, guestName: "Jean Dujardin", amount: Money(cents: amount),
            tipAmount: Money(cents: tip), signatureDataUrl: nil, notes: nil
        )
    }

    @Test func hotelRoomsListsTheOccupiedRooms() async throws {
        let api = try await makeLocalAPI()
        let rooms = try await api.hotelRooms()
        #expect(rooms.map(\.roomNumber) == ["101", "204", "305"])
        #expect(rooms[0].maxCreditLimit == Money(cents: 30000) && rooms[0].currentBalance == .zero && rooms[0].availableCredit == Money(cents: 30000))
    }

    @Test func chargeRoomClosesTheOrderAndRaisesTheBalance() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        let result = try await api.chargeRoom(charge(order, amount: 1950, tip: 100))
        #expect(result.success == true && result.message == "Facturation de 20.50 € enregistrée sur la chambre 101 (Jean Dujardin)")
        #expect(try await api.hotelRooms().first { $0.roomNumber == "101" }?.currentBalance == Money(cents: 2050))
        #expect(try await api.activeOrder(table: "T3") == nil)
        #expect(try await api.tables().first { $0.tableNumber == "T3" }?.status == .free)
        // La commande est clôturée : on ne peut plus la remiser.
        await #expect(throws: APIError.server(status: 409, message: "Commande déjà réglée ou annulée.")) {
            try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 10, reason: "Test", operatorId: nil)
        }
    }

    @Test func chargeRoomRefusalsLeaveBalanceAndOrderUntouched() async throws {
        let api = try await makeLocalAPI()
        let big = try await seat(api, "T3", quantity: 16)  // 312,00 € > plafond de la chambre 101 (300,00 €)
        await #expect(throws: APIError.server(status: 400, message: "Plafond de crédit chambre dépassé.")) { try await api.chargeRoom(charge(big, amount: 31200)) }
        await #expect(throws: APIError.server(status: 400, message: "Chambre 999 introuvable ou non occupée.")) { try await api.chargeRoom(charge(big, room: "999", amount: 31200)) }
        await #expect(throws: APIError.server(status: 400, message: "Facturation chambre échouée.")) { try await api.chargeRoom(charge(big, room: "204", amount: 1000)) }
        await #expect(throws: APIError.server(status: 400, message: "Facturation chambre échouée.")) { try await api.chargeRoom(charge(big, room: "204", amount: 0)) }
        #expect(try await api.hotelRooms().allSatisfy { $0.currentBalance == .zero })
        #expect(try await api.activeOrder(table: "T3")?.orderId == big.orderId)
        // La même commande se facture normalement sur une chambre au plafond suffisant.
        let result = try await api.chargeRoom(charge(big, room: "204", amount: 31200))
        #expect(result.success == true)
        #expect(try await api.hotelRooms().first { $0.roomNumber == "204" }?.currentBalance == Money(cents: 31200))
    }

    @Test func roomCallsNeedLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.hotelRooms() }
        await #expect(throws: APIError.unauthorized) { try await api.chargeRoom(RoomChargeRequest(orderId: nil, tableNumber: "T1", roomNumber: "101", guestName: "X", amount: Money(cents: 100), tipAmount: .zero, signatureDataUrl: nil, notes: nil)) }
    }
}
