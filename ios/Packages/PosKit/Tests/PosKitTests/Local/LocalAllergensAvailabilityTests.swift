import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : allergènes et ruptures")
struct LocalAllergensAvailabilityTests {
    @Test func allergenMaskIsStoredAndUndeclaredResetsIt() async throws {
        let api = try await makeLocalAPI()
        let product = try await localProduct(api, "Burger Gourmet Rossini")
        #expect(!product.allergensDeclared && product.allergenMask == 0)

        try await api.setAllergens(productId: product.id, declared: true, mask: 0b1000001, overridden: true)
        var stored = try #require(try await api.products().first { $0.id == product.id })
        #expect(stored.allergensDeclared && stored.allergensOverridden)
        #expect(stored.declaredAllergens == ["gluten", "milk"])

        try await api.setAllergens(productId: product.id, declared: false, mask: 0b1, overridden: false)
        stored = try #require(try await api.products().first { $0.id == product.id })
        #expect(!stored.allergensDeclared && stored.allergenMask == 0)
    }

    @Test func invalidMaskAndWaiterAreRefused() async throws {
        let api = try await makeLocalAPI()
        let product = try await localProduct(api, "Burger Gourmet Rossini")
        await #expect(throws: LocalPosAPI.invalidAllergenMask) {
            try await api.setAllergens(productId: product.id, declared: true, mask: Allergens.allMask + 1, overridden: false)
        }
        _ = try await api.login(pin: "2468")
        await #expect(throws: APIError.self) {
            try await api.setAllergens(productId: product.id, declared: true, mask: 1, overridden: false)
        }
    }

    @Test func outOfStockProductRefusesAddAndKeepsSentLines() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger)])
        try await api.dispatch(table: "T1")

        _ = try await api.login(pin: "2468") // un serveur peut signaler une rupture
        try await api.setAvailability(productId: burger.id, isAvailable: false)
        await #expect(throws: LocalPosAPI.productUnavailable) {
            _ = try await api.addItems(table: "T1", items: [localInput(burger)])
        }
        let order = try #require(try await api.activeOrder(table: "T1"))
        #expect(order.lines.count == 1 && order.lines[0].quantity == 1 && order.lines[0].isDispatched)

        try await api.setAvailability(productId: burger.id, isAvailable: true)
        _ = try await api.addItems(table: "T1", items: [localInput(burger)])
    }
}
