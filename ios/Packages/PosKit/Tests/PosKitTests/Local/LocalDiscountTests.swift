import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : remises et gratuités")
struct LocalDiscountTests {
    private static let discountFailed = APIError.server(status: 400, message: "Opération de remise échouée.")
    private static let compFailed = APIError.server(status: 400, message: "Opération de gratuité échouée.")

    private func seat(_ api: LocalPosAPI, _ table: String, _ items: [OrderItemInput]) async throws -> ActiveOrder {
        try await seatTable(api, table, items: items)
        return try #require(try await api.activeOrder(table: table))
    }

    @Test func percentageDiscountReducesTheTotal() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let order = try await seat(api, "T1", [localInput(burger)])
        try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 10, reason: "  Anniversaire  ", operatorId: nil)
        let discounted = try #require(try await api.activeOrder(table: "T1"))
        #expect(discounted.totalTtcAmount == Money(cents: 1755))
        #expect(discounted.globalDiscountType == .percentage && discounted.globalDiscountValue == 10 && discounted.globalDiscountReason == "Anniversaire")
    }

    @Test func fixedDiscountClampsAtZero() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let order = try await seat(api, "T1", [localInput(burger)])
        try await api.applyDiscount(orderId: order.orderId, type: .fixedAmount, value: 5, reason: "Geste", operatorId: nil)
        #expect(try await api.activeOrder(table: "T1")?.totalTtcAmount == Money(cents: 1450))
        try await api.applyDiscount(orderId: order.orderId, type: .fixedAmount, value: 100, reason: "Geste", operatorId: nil)
        #expect(try await api.activeOrder(table: "T1")?.totalTtcAmount == .zero)
    }

    @Test func discountValidationLeavesTheOrderUntouched() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let order = try await seat(api, "T1", [localInput(burger)])
        let invalid: [(DiscountType, Decimal, String)] = [(.percentage, 101, "Trop"), (.percentage, -5, "Négatif"), (.fixedAmount, -1, "Négatif"), (.percentage, 10, "   ")]
        for (type, value, reason) in invalid {
            await #expect(throws: Self.discountFailed) { try await api.applyDiscount(orderId: order.orderId, type: type, value: value, reason: reason, operatorId: nil) }
        }
        await #expect(throws: Self.discountFailed) { try await api.applyDiscount(orderId: UUID(), type: .percentage, value: 10, reason: "Inconnue", operatorId: nil) }
        let after = try #require(try await api.activeOrder(table: "T1"))
        #expect(after.totalTtcAmount == Money(cents: 1950) && after.globalDiscountType == nil && after.globalDiscountValue == 0)
    }

    @Test func removeDiscountRestoresTheTotal() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let order = try await seat(api, "T1", [localInput(burger)])
        try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 50, reason: "Test", operatorId: nil)
        try await api.removeDiscount(orderId: order.orderId)
        let restored = try #require(try await api.activeOrder(table: "T1"))
        #expect(restored.totalTtcAmount == Money(cents: 1950) && restored.globalDiscountType == nil && restored.globalDiscountReason == nil)
        await #expect(throws: APIError.notFound("Commande introuvable.")) { try await api.removeDiscount(orderId: UUID()) }
    }

    @Test func compItemZeroesTheLine() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let cafe = try await localProduct(api, "Café Gourmand")
        let order = try await seat(api, "T1", [localInput(burger), localInput(cafe)])
        let burgerLine = try #require(order.lines.first { $0.productName == burger.name })
        try await api.compItem(orderId: order.orderId, lineId: burgerLine.lineId, reason: "Erreur de cuisine", operatorId: nil)
        let after = try #require(try await api.activeOrder(table: "T1"))
        #expect(after.totalTtcAmount == Money(cents: 850))
        #expect(after.lines.first { $0.lineId == burgerLine.lineId }?.isComp == true)
        await #expect(throws: Self.compFailed) { try await api.compItem(orderId: order.orderId, lineId: burgerLine.lineId, reason: "  ", operatorId: nil) }
        await #expect(throws: Self.compFailed) { try await api.compItem(orderId: order.orderId, lineId: UUID(), reason: "Inconnue", operatorId: nil) }
        await #expect(throws: Self.compFailed) { try await api.compItem(orderId: UUID(), lineId: burgerLine.lineId, reason: "Inconnue", operatorId: nil) }
    }

    @Test func discountsAreAudited() async throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        let api = try LocalPosAPI(path: path)
        _ = try await api.login(pin: "1234")
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let order = try await seat(api, "T1", [localInput(burger, quantity: 2)])
        let operatorId = UUID()
        try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 10, reason: "  Geste commercial  ", operatorId: operatorId)
        let lineId = try #require(order.lines.first?.lineId)
        try await api.compItem(orderId: order.orderId, lineId: lineId, reason: "Erreur de cuisine", operatorId: operatorId)

        let reader = try SQLiteDatabase(path: path)
        let audits = try reader.query("SELECT * FROM OrderDiscountAudits ORDER BY rowid")
        #expect(audits.count == 2)
        #expect(audits[0].int("DiscountType") == 0 && audits[0].decimal("Value") == 10 && audits[0].int("AmountSaved") == 390)
        #expect(audits[0].string("Reason") == "Geste commercial" && audits[0].uuid("AuthorizedByOperatorId") == operatorId && audits[0].uuid("OrderItemId") == nil)
        #expect(audits[1].int("DiscountType") == 2 && audits[1].int("AmountSaved") == 3900 && audits[1].uuid("OrderItemId") == lineId)
        #expect(audits[1].string("Reason") == "Erreur de cuisine")
    }

    @Test func discountCallsNeedLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.applyDiscount(orderId: UUID(), type: .percentage, value: 10, reason: "x", operatorId: nil) }
        await #expect(throws: APIError.unauthorized) { try await api.removeDiscount(orderId: UUID()) }
        await #expect(throws: APIError.unauthorized) { try await api.compItem(orderId: UUID(), lineId: UUID(), reason: "x", operatorId: nil) }
    }
}
