import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : catalogue")
struct LocalCatalogTests {
    @Test func seedsTheDemoCatalogue() async throws {
        let api = try await makeLocalAPI()
        let categories = try await api.categories()
        let products = try await api.products()
        #expect(categories.count == 5)
        #expect(products.count == 11)
        let burger = try #require(products.first { $0.name == "Burger Gourmet Rossini" })
        #expect(burger.modifierGroups.count == 2)
        let cooking = try #require(burger.modifierGroups.first)
        #expect(cooking.groupName == "Cuisson de la Viande")
        #expect(cooking.isMandatory && cooking.isSingleChoice)
        #expect(cooking.options.map(\.name) == ["Bleu", "Saignant", "À Point", "Bien Cuit"])
        #expect(cooking.options.first { $0.isDefault }?.name == "À Point")
        let pizza = try #require(products.first { $0.name == "Pizza Margherita AOP" })
        #expect(pizza.taxRatePercent == 10 && pizza.taxRateTakeawayPercent == 5.5)
        #expect(pizza.price == Money(cents: 1250))
    }

    @Test func waiterCannotEditCatalogue() async throws {
        let api = try await makeLocalAPI(pin: "2468")
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) {
            try await api.createCategory(name: "X", colorHex: "#000000", displayOrder: 9)
        }
    }

    @Test func createdCategoryAppearsInOrder() async throws {
        let api = try await makeLocalAPI()
        try await api.createCategory(name: "Brunch", colorHex: "#112233", displayOrder: 99)
        let last = try #require(try await api.categories().last)
        #expect(last.name == "Brunch" && last.colorHex == "#112233" && last.id.hasPrefix("CAT_"))
    }

    @Test func updateCategoryStationRules() async throws {
        let api = try await makeLocalAPI()
        try await api.updateCategory(id: "CAT_DRINKS", name: "Boissons", colorHex: "#000001", displayOrder: 5, preparationStationId: "BAR")
        #expect(try await api.categories().first { $0.id == "CAT_DRINKS" }?.preparationStationId == "BAR")
        // nil = inchangé
        try await api.updateCategory(id: "CAT_DRINKS", name: "Boissons", colorHex: "#000001", displayOrder: 5, preparationStationId: nil)
        #expect(try await api.categories().first { $0.id == "CAT_DRINKS" }?.preparationStationId == "BAR")
        // "" = effacer
        try await api.updateCategory(id: "CAT_DRINKS", name: "Boissons", colorHex: "#000001", displayOrder: 5, preparationStationId: "")
        #expect(try await api.categories().first { $0.id == "CAT_DRINKS" }?.preparationStationId == nil)
        await #expect(throws: APIError.notFound(nil)) {
            try await api.updateCategory(id: "NOPE", name: "x", colorHex: "#000000", displayOrder: 0, preparationStationId: nil)
        }
    }

    @Test func unicodeProductNameRoundTrips() async throws {
        let api = try await makeLocalAPI()
        try await api.createProduct(ProductDraft(name: "Crêpe Suzette 🍊 قهوة", categoryId: "CAT_DESSERTS", price: Money(cents: 700), description: "Flambée à l'Grand-Marnier"))
        let created = try #require(try await api.products().first { $0.name.hasPrefix("Crêpe") })
        #expect(created.name == "Crêpe Suzette 🍊 قهوة" && created.description == "Flambée à l'Grand-Marnier")
    }

    @Test func productLifecycle() async throws {
        let api = try await makeLocalAPI()
        let draft = ProductDraft(name: "Mojito", categoryId: "CAT_DRINKS", price: Money(cents: 850), taxRatePercent: 20, stationId: "BAR", isQuickKey: true, displayOrder: 7, description: "Menthe")
        try await api.createProduct(draft)
        var created = try #require(try await api.products().first { $0.name == "Mojito" })
        #expect(created.price == Money(cents: 850) && created.taxRatePercent == 20 && created.isQuickKey)
        #expect(created.description == "Menthe" && created.preparationStationId == "BAR")

        var edited = ProductDraft(product: created)
        edited.name = "Mojito Maison"
        edited.price = Money(cents: 900)
        try await api.updateProduct(id: created.id, edited)
        created = try #require(try await api.products().first { $0.id == created.id })
        #expect(created.name == "Mojito Maison" && created.price == Money(cents: 900))

        try await api.archiveProduct(id: created.id)
        #expect(try await api.products().contains { $0.id == created.id } == false)
        await #expect(throws: APIError.notFound(nil)) { try await api.archiveProduct(id: UUID()) }
    }
}
