import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : cuisine")
struct LocalKitchenTests {
    @Test func dispatchSplitsTheOrderByStationAndMarksLinesSent() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        let tartare = try await localProduct(api, "Tartare de Saumon")
        try await seatTable(api, "T2", covers: 2, waiter: "Sophie", items: [localInput(burger), localInput(cafe), localInput(tartare)])
        try await api.dispatch(table: "T2")
        let tickets = try await api.kitchenTickets()
        #expect(Set(tickets.map(\.stationId)) == ["HOT_KITCHEN", "BAR", "COLD"])
        #expect(tickets.allSatisfy { $0.tableNumber == "T2" && $0.serverName == "Sophie" && $0.coversCount == 2 && $0.status == .pending && $0.items.count == 1 })
        #expect(try await api.activeOrder(table: "T2")?.lines.allSatisfy(\.isDispatched) == true)
        // Second envoi sans nouveauté : aucun bon supplémentaire.
        try await api.dispatch(table: "T2")
        #expect(try await api.kitchenTickets().count == 3)
    }

    @Test func dispatchAfterNewLineOnlySendsNewLines() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T3", items: [localInput(burger)])
        try await api.dispatch(table: "T3")
        let order = try await api.addItems(table: "T3", items: [localInput(burger)])
        #expect(order.lines.count == 2)
        #expect(order.lines[0].isDispatched && !order.lines[1].isDispatched)
        try await api.dispatch(table: "T3")
        let tickets = try await api.kitchenTickets()
        #expect(tickets.count == 2)
        #expect(tickets.allSatisfy { $0.items.count == 1 && $0.items[0].quantity == 1 })
    }

    @Test func stationFallsBackToTheCategoryThenToTheHotKitchen() async throws {
        let api = try await makeLocalAPI()
        try await api.createProduct(ProductDraft(name: "Spritz", categoryId: "CAT_DRINKS", price: Money(cents: 900), stationId: nil))
        try await api.createProduct(ProductDraft(name: "Soupe du jour", categoryId: "CAT_STARTERS", price: Money(cents: 600), stationId: nil))
        try await api.updateCategory(id: "CAT_DRINKS", name: "Boissons & Vins", colorHex: "#3498DB", displayOrder: 5, preparationStationId: "BAR")
        let spritz = try await localProduct(api, "Spritz")
        let soupe = try await localProduct(api, "Soupe du jour")
        try await seatTable(api, "T4", items: [localInput(spritz), localInput(soupe)])
        try await api.dispatch(table: "T4")
        let stations = Dictionary(uniqueKeysWithValues: try await api.kitchenTickets().map { ($0.items[0].productName, $0.stationId) })
        #expect(stations == ["Spritz": "BAR", "Soupe du jour": "HOT_KITCHEN"])
    }

    @Test func takeawayTicketShowsThePrefix() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        _ = try await api.addItems(table: "Comptoir", items: [localInput(burger)])
        try await api.dispatch(table: "Comptoir")
        #expect(try await api.kitchenTickets().first?.tableNumber == "[À EMPORTER] Comptoir")
    }

    @Test func dispatchWithoutAnActiveOrderIsNotFound() async throws {
        let api = try await makeLocalAPI()
        try await api.openTable(number: "T7", covers: 2, operatorId: nil, waiterName: nil)
        try await api.dispatch(table: "T7")  // commande ouverte mais vide : rien à envoyer, pas d'erreur
        #expect(try await api.kitchenTickets().isEmpty)
        await #expect(throws: APIError.notFound(nil)) { try await api.dispatch(table: "T1") }
        await #expect(throws: APIError.notFound(nil)) { try await api.dispatch(table: "T404") }
    }

    @Test func fireSuiteCreatesATicketOnlyForSuiteLines() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let tiramisu = try await localProduct(api, "Tiramisu Maison")
        try await seatTable(api, "T4", items: [localInput(burger), localInput(tiramisu, quantity: 2, course: .suite)])
        try await api.fireSuite(table: "T4")
        let tickets = try await api.kitchenTickets()
        #expect(tickets.count == 1)
        let ticket = try #require(tickets.first)
        #expect(ticket.stationId == "HOT_KITCHEN" && ticket.tableNumber == "T4" && ticket.items.count == 1)
        #expect(ticket.items[0].productName == "[RÉCLAME SUITE] Tiramisu Maison" && ticket.items[0].quantity == 2)
        // Sans ligne « suite » (ou table inconnue), aucun bon vide.
        try await seatTable(api, "T5", items: [localInput(burger)])
        try await api.fireSuite(table: "T5")
        try await api.fireSuite(table: "T404")
        #expect(try await api.kitchenTickets().count == 1)
    }

    @Test func bumpAdvancesTheStatusAndChecksTheRole() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T6", items: [localInput(burger)])
        try await api.dispatch(table: "T6")
        let id = try #require(try await api.kitchenTickets().first?.id)

        _ = try await api.login(pin: "2468")
        await #expect(throws: APIError.forbidden("Réservé à la cuisine et aux responsables.")) { try await api.bumpTicket(id: id) }

        _ = try await api.login(pin: "5678")
        var statuses: [TicketStatus] = []
        for _ in 0..<4 {
            try await api.bumpTicket(id: id)
            statuses.append(try #require(try await api.kitchenTickets().first { $0.id == id }).status)
        }
        #expect(statuses == [.inPreparation, .ready, .served, .served])
        await #expect(throws: APIError.notFound(nil)) { try await api.bumpTicket(id: UUID()) }
    }

    @Test func kitchenTicketsAreNewestFirstAndCappedAtFifty() async throws {
        let api = try await makeLocalAPI()
        let tiramisu = try await localProduct(api, "Tiramisu Maison")
        try await seatTable(api, "T1", items: [localInput(tiramisu, course: .suite)])
        for _ in 0..<51 { try await api.fireSuite(table: "T1") }
        let tickets = try await api.kitchenTickets()
        #expect(tickets.count == 50)
        #expect(zip(tickets, tickets.dropFirst()).allSatisfy { ($0.dispatchedAtUtc ?? .distantPast) >= ($1.dispatchedAtUtc ?? .distantPast) })
    }
}
