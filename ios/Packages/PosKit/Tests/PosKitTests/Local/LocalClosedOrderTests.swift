import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : commandes réglées ou annulées")
struct LocalClosedOrderTests {
    @Test func discountsDestinationAndCompsRefuseACancelledOrder() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        try await seatTable(api, "T1", items: [localInput(burger)])
        try await seatTable(api, "T2", items: [localInput(cafe)])
        let cancelled = try #require(try await api.activeOrder(table: "T1"))
        let line = try #require(cancelled.lines.first)
        // La fusion annule la commande de T1.
        #expect(try await api.transfer(from: "T1", to: "T2", merge: true).success == true)

        let closed = APIError.server(status: 409, message: "Commande déjà réglée ou annulée.")
        await #expect(throws: closed) { try await api.applyDiscount(orderId: cancelled.orderId, type: .percentage, value: 10, reason: "Test", operatorId: nil) }
        await #expect(throws: closed) { try await api.removeDiscount(orderId: cancelled.orderId) }
        await #expect(throws: closed) { try await api.compItem(orderId: cancelled.orderId, lineId: line.lineId, reason: "Test", operatorId: nil) }
        await #expect(throws: closed) { try await api.setDestination(orderId: cancelled.orderId, destination: .takeaway) }
        // Une commande inconnue reste une erreur « introuvable ».
        await #expect(throws: APIError.notFound("Commande introuvable.")) { try await api.setDestination(orderId: UUID(), destination: .takeaway) }
        // La commande vivante n'est pas concernée.
        let alive = try #require(try await api.activeOrder(table: "T2"))
        try await api.applyDiscount(orderId: alive.orderId, type: .percentage, value: 10, reason: "Test", operatorId: nil)
    }
}
