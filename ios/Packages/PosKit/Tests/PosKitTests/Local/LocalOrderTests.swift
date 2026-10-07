import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : commandes de salle")
struct LocalOrderTests {
    @Test func openTableCreatesEatInOrderAndOccupiesTable() async throws {
        let api = try await makeLocalAPI()
        try await api.openTable(number: "T1", covers: 3, operatorId: UUID(), waiterName: "Sophie")
        let table = try #require(try await api.tables().first { $0.tableNumber == "T1" })
        #expect(table.status == .occupied && table.coversCount == 3 && table.assignedWaiterName == "Sophie" && table.openedAtUtc != nil)
        let order = try #require(try await api.activeOrder(table: "T1"))
        #expect(order.lines.isEmpty && order.destination == .eatIn && order.waiterName == "Sophie" && order.coversCount == 3)
        #expect(table.activeOrderId == order.orderId)
    }

    @Test func openTableDefaultsCoversAndWaiter() async throws {
        let api = try await makeLocalAPI()
        try await api.openTable(number: "T2", covers: 0, operatorId: nil, waiterName: nil)
        let order = try #require(try await api.activeOrder(table: "T2"))
        #expect(order.coversCount == 2 && order.waiterName == "Serveur")
    }

    @Test func reopeningKeepsTheExistingOrder() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", covers: 2, items: [localInput(burger)])
        let before = try #require(try await api.activeOrder(table: "T1"))
        try await api.openTable(number: "T1", covers: 5, operatorId: nil, waiterName: "Léa")
        let after = try #require(try await api.activeOrder(table: "T1"))
        #expect(after.orderId == before.orderId && after.lines.count == 1)
        #expect(after.coversCount == 5 && after.waiterName == "Léa")
    }

    @Test func addItemsMergesIdenticalUndispatchedLines() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        try await seatTable(api, "T2")
        _ = try await api.addItems(table: "T2", items: [localInput(burger), localInput(burger, quantity: 2)])
        let order = try await api.addItems(table: "T2", items: [localInput(burger, modifiers: ["Bleu"]), localInput(cafe)])
        #expect(order.lines.count == 3)
        #expect(order.lines[0].quantity == 3 && order.lines[0].totalPrice == Money(cents: 3 * 1950))
        #expect(order.lines[1].modifiersSummary == ["Bleu"])
        #expect(order.totalTtcAmount == Money(cents: 3 * 1950 + 1950 + 850))
    }

    @Test func nonPositiveQuantityIsRejected() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        for bad in [0, -1] {
            await #expect(throws: APIError.server(status: 400, message: "La quantité doit être positive.")) {
                try await api.addItems(table: "T3", items: [localInput(burger, quantity: bad)])
            }
        }
        #expect(try await api.activeOrder(table: "T3") == nil)
    }

    @Test func addItemsCreatesAnUnknownTable() async throws {
        let api = try await makeLocalAPI()
        let cafe = try await localProduct(api, "Café Gourmand")
        let order = try await api.addItems(table: "T99", items: [localInput(cafe)])
        #expect(order.tableNumber == "T99" && order.lines.count == 1)
        let table = try #require(try await api.tables().first { $0.tableNumber == "T99" })
        #expect(table.capacity == 2 && table.status == .occupied && table.activeOrderId == order.orderId)
    }

    @Test func counterTableDefaultsToTakeaway() async throws {
        let api = try await makeLocalAPI()
        let cafe = try await localProduct(api, "Café Gourmand")
        #expect(try await api.addItems(table: "Comptoir", items: [localInput(cafe)]).destination == .takeaway)
        #expect(try await api.addItems(table: "T3", items: [localInput(cafe)]).destination == .eatIn)
    }

    @Test func modifiersWithCommasRoundTrip() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let modifiers = ["Sans oignon, sans sel", "Bleu"]
        _ = try await api.addItems(table: "T4", items: [localInput(burger, modifiers: modifiers, extra: Money(cents: 250))])
        let line = try #require(try await api.activeOrder(table: "T4")?.lines.first)
        #expect(line.modifiersSummary == modifiers && line.modifiersPriceExtra == Money(cents: 250))
        #expect(line.totalPrice == Money(cents: 1950 + 250))
    }

    @Test func setDestinationUpdatesTheOrderAndReportsUnknownOnes() async throws {
        let api = try await makeLocalAPI()
        let pizza = try await localProduct(api, "Pizza Margherita AOP")
        try await seatTable(api, "T5", items: [localInput(pizza)])
        let order = try #require(try await api.activeOrder(table: "T5"))
        #expect(order.destination == .eatIn)
        try await api.setDestination(orderId: order.orderId, destination: .takeaway)
        #expect(try await api.activeOrder(table: "T5")?.destination == .takeaway)
        await #expect(throws: APIError.notFound("Commande introuvable.")) {
            try await api.setDestination(orderId: UUID(), destination: .eatIn)
        }
    }

    @Test func orderCallsNeedLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.openTable(number: "T1", covers: 2, operatorId: nil, waiterName: nil) }
        await #expect(throws: APIError.unauthorized) { try await api.activeOrder(table: "T1") }
        await #expect(throws: APIError.unauthorized) { try await api.addItems(table: "T1", items: []) }
        await #expect(throws: APIError.unauthorized) { try await api.setDestination(orderId: UUID(), destination: .eatIn) }
    }

    @Test func tablesReportTheTotalOfTheirActiveOrder() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        try await seatTable(api, "T5", items: [localInput(burger), localInput(cafe)])
        let tables = try await api.tables()
        #expect(tables.first { $0.tableNumber == "T5" }?.activeOrderTotalTtc == Money(cents: 2800))
        #expect(tables.filter { $0.tableNumber != "T5" }.allSatisfy { $0.activeOrderTotalTtc == .zero })
    }
}
