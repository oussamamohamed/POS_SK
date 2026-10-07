import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : comptoir et mise en attente")
struct LocalCounterTests {
    private static let forbidden = APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")

    /// Panier du comptoir contenant `quantity` burgers.
    private func counterCart(_ api: LocalPosAPI, quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        _ = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        return try await api.addItems(table: "Comptoir", items: [localInput(burger, quantity: quantity)])
    }

    @Test func openCounterOrderCreatesAndReusesTheCounterOrder() async throws {
        let api = try await makeLocalAPI()
        let first = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        #expect(first.tableNumber == "Comptoir" && first.destination == .takeaway && first.lines.isEmpty)
        let again = try await api.openCounterOrder(terminalId: "T01", destination: .eatIn)
        #expect(again.orderId == first.orderId && again.destination == .takeaway)
        let table = try #require(try await api.tables().first { $0.isCounter })
        #expect(table.capacity == 1 && table.status == .occupied && table.activeOrderId == first.orderId)
    }

    @Test func holdDetachesTheOrderAndListsIt() async throws {
        let api = try await makeLocalAPI()
        let empty = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        await #expect(throws: APIError.server(status: 400, message: "Impossible de mettre en attente un panier vide.")) {
            try await api.holdOrder(orderId: empty.orderId, terminalId: "T01", label: "Vide")
        }
        let cart = try await counterCart(api, quantity: 2)
        try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "Dupont")
        let held = try #require(try await api.heldOrders(terminalId: "T01").first)
        #expect(held.orderId == cart.orderId && held.customerLabel == "Dupont" && held.itemCount == 2)
        #expect(held.totalTtc.amountInCents == 3900 && held.destination == .takeaway && held.terminalId == "T01")
        #expect(try await api.activeOrder(table: "Comptoir") == nil)
        #expect(try await api.tables().first { $0.isCounter }?.status == .free)
        await #expect(throws: APIError.server(status: 409, message: "Cette commande est déjà en attente.")) {
            try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "Encore")
        }
    }

    @Test func recallRestoresTheHeldOrderOnTheCounter() async throws {
        let api = try await makeLocalAPI()
        let cart = try await counterCart(api, quantity: 2)
        try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "Dupont")
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)
        let recalled = try await api.recallHeldOrder(holdId: hold.holdId)
        #expect(recalled.orderId == cart.orderId && recalled.lines.first?.quantity == 2 && recalled.tableNumber == "Comptoir")
        #expect(try await api.heldOrders(terminalId: "T01").isEmpty)
        #expect(try await api.tables().first { $0.isCounter }?.activeOrderId == cart.orderId)
        await #expect(throws: APIError.notFound("Commande en attente introuvable ou déjà rappelée.")) { try await api.recallHeldOrder(holdId: hold.holdId) }
    }

    @Test func recallReplacesAnEmptyCounterButRefusesABusyOne() async throws {
        let api = try await makeLocalAPI()
        let held = try await counterCart(api)
        try await api.holdOrder(orderId: held.orderId, terminalId: "T01", label: "A")
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)

        // Un panier de comptoir vide est remplacé…
        let empty = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        #expect(empty.lines.isEmpty && empty.orderId != held.orderId)
        let recalled = try await api.recallHeldOrder(holdId: hold.holdId)
        #expect(recalled.orderId == held.orderId)

        // …mais une vente en cours n'est jamais écrasée : la commande en attente reste disponible.
        try await api.holdOrder(orderId: held.orderId, terminalId: "T01", label: "B")
        let busy = try await counterCart(api)
        let secondHold = try #require(try await api.heldOrders(terminalId: "T01").first)
        await #expect(throws: APIError.server(status: 409, message: "Le comptoir a déjà une vente en cours.")) { try await api.recallHeldOrder(holdId: secondHold.holdId) }
        #expect(try await api.heldOrders(terminalId: "T01").count == 1)
        #expect(try await api.activeOrder(table: "Comptoir")?.orderId == busy.orderId)
    }

    @Test func voidHeldOrderNeedsASupervisorPin() async throws {
        let api = try await makeLocalAPI()
        let cart = try await counterCart(api)
        try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "À annuler")
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)

        await #expect(throws: APIError.server(status: 400, message: "Code PIN superviseur requis.")) {
            try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "  ", reason: "Test", terminalId: "T01")
        }
        await #expect(throws: Self.forbidden) { try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "2468", reason: "Test", terminalId: "T01") }
        await #expect(throws: Self.forbidden) { try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "0000", reason: "Test", terminalId: "T01") }
        #expect(try await api.heldOrders(terminalId: "T01").count == 1)

        try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "1234", reason: "Client parti", terminalId: "T01")
        #expect(try await api.heldOrders(terminalId: "T01").isEmpty)
        await #expect(throws: APIError.notFound("Commande en attente introuvable.")) {
            try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "1234", reason: "Encore", terminalId: "T01")
        }
        // La commande annulée n'est plus modifiable.
        await #expect(throws: APIError.server(status: 409, message: "Commande déjà réglée ou annulée.")) {
            try await api.applyDiscount(orderId: cart.orderId, type: .percentage, value: 10, reason: "Test", operatorId: nil)
        }
    }

    @Test func wrongSupervisorPinsLockLoginToo() async throws {
        let api = try await makeLocalAPI()
        let cart = try await counterCart(api)
        try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "X")
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)
        for _ in 0..<5 {
            await #expect(throws: Self.forbidden) { try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "0000", reason: "Test", terminalId: "T01") }
        }
        await #expect(throws: APIError.rateLimited(nil)) { try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "1234", reason: "Test", terminalId: "T01") }
        await #expect(throws: APIError.rateLimited(nil)) { try await api.login(pin: "1234") }
        #expect(try await api.heldOrders(terminalId: "T01").count == 1)
    }

    @Test func heldOrdersAreFilteredByTerminal() async throws {
        let api = try await makeLocalAPI()
        let first = try await counterCart(api)
        try await api.holdOrder(orderId: first.orderId, terminalId: "T01", label: "A")
        let second = try await counterCart(api)
        try await api.holdOrder(orderId: second.orderId, terminalId: "T02", label: "B")
        #expect(try await api.heldOrders(terminalId: "T01").map(\.customerLabel) == ["A"])
        #expect(try await api.heldOrders(terminalId: "T02").map(\.customerLabel) == ["B"])
        #expect(try await api.heldOrders(terminalId: "").count == 2)
    }
}
