import Foundation
import Testing
@testable import PosKit

@Suite("Analyse du menu : classes et mode autonome")
struct LocalMenuAnalysisTests {
    private func sale(_ name: String, _ quantity: Int, costCents: Int?) -> MenuSale {
        // Prix 11,00 € TTC à 10 % = 10,00 € HT l'unité (même scénario que `MenuAnalysisServiceTests` côté .NET).
        MenuSale(productId: UUID(), name: name, categoryName: "Plats", quantity: quantity, revenueHtCents: Decimal(quantity * 1000), unitCostCents: costCents)
    }

    @Test func classifiesLikeTheDotNetService() {
        let now = Date()
        let report = MenuAnalysisCalculator.report(from: now, to: now, sales: [
            sale("Star", 10, costCents: 300), sale("Plow", 10, costCents: 900), sale("Puzzle", 1, costCents: 200),
            sale("Dog", 1, costCents: 950), sale("NoCost", 5, costCents: nil)
        ])
        let byName = Dictionary(uniqueKeysWithValues: report.items.map { ($0.name, $0) })
        #expect(byName["Star"]?.class == .star && byName["Plow"]?.class == .plowhorse)
        #expect(byName["Puzzle"]?.class == .puzzle && byName["Dog"]?.class == .dog)
        #expect(byName["NoCost"]?.class == nil && byName["NoCost"]?.missingCost == true && byName["NoCost"]?.popular == true)
        #expect(byName["Star"]?.unitMarginHt == Money(cents: 700))
        #expect(report.averageUnitMarginHt == Money(cents: 402))
        #expect(report.popularityThreshold == Decimal(string: "0.14"))
        #expect(report.missingCostCount == 1)
        #expect(report.items.first?.quantity == 10)
    }

    @Test func emptyPeriodGivesAnEmptyReport() {
        let report = MenuAnalysisCalculator.report(from: Date(), to: Date(), sales: [])
        #expect(report.items.isEmpty && report.averageUnitMarginHt == nil)
    }

    @Test func aggregatesPaidOrdersAndHonoursCosts() async throws {
        let api = try await makeLocalAPI()
        let now = Date()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let pizza = try await localProduct(api, "Pizza Margherita AOP")
        var draft = ProductDraft(product: burger)
        draft.cost = Money(cents: 950)
        try await api.updateProduct(id: burger.id, draft)

        try await seatTable(api, "T1", items: [localInput(burger, quantity: 3), localInput(pizza)])
        let order = try #require(try await api.activeOrder(table: "T1"))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, order.totalTtcAmount?.cents ?? 0)]))

        let report = try await api.menuAnalysis(from: now.addingTimeInterval(-3600), to: now.addingTimeInterval(3600))
        let burgerRow = try #require(report.items.first { $0.productId == burger.id })
        let pizzaRow = try #require(report.items.first { $0.productId == pizza.id })
        #expect(burgerRow.quantity == 3 && burgerRow.unitCost == Money(cents: 950) && burgerRow.class != nil)
        // 19,50 € TTC à 10 % : 17,727… € HT l'unité, soit 53,18 € pour 3.
        #expect(burgerRow.revenueHt == Money(cents: 5318))
        #expect(pizzaRow.missingCost && pizzaRow.class == nil)
        #expect(report.missingCostCount == 1)

        let outside = try await api.menuAnalysis(from: now.addingTimeInterval(86_400), to: now.addingTimeInterval(2 * 86_400))
        #expect(outside.items.isEmpty)
    }

    @Test func categoryFilterKeepsOnlyThatFamily() async throws {
        let api = try await makeLocalAPI()
        let now = Date()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let pizza = try await localProduct(api, "Pizza Margherita AOP")
        try await seatTable(api, "T1", items: [localInput(burger, quantity: 2), localInput(pizza)])
        let order = try #require(try await api.activeOrder(table: "T1"))
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, order.totalTtcAmount?.cents ?? 0)]))

        let range = (now.addingTimeInterval(-3600), now.addingTimeInterval(3600))
        let all = try await api.menuAnalysis(from: range.0, to: range.1)
        let onlyBurgerFamily = try await api.menuAnalysis(from: range.0, to: range.1, categoryId: burger.categoryId)

        #expect(all.items.count == 2)
        #expect(onlyBurgerFamily.items.map(\.productId) == [burger.id])
    }

    @Test func costIsHiddenFromWaitersAndKeptOrClearedOnUpdate() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        var draft = ProductDraft(product: burger)
        draft.cost = Money(cents: 400)
        try await api.updateProduct(id: burger.id, draft)
        #expect(try await localProduct(api, "Burger Gourmet Rossini").cost == Money(cents: 400))

        // Mise à jour sans coût (client qui ne l'envoie pas) : conservé.
        try await api.updateProduct(id: burger.id, ProductDraft(product: burger))
        #expect(try await localProduct(api, "Burger Gourmet Rossini").cost == Money(cents: 400))

        _ = try await api.login(pin: "2468")
        #expect(try await localProduct(api, "Burger Gourmet Rossini").cost == nil)
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await api.menuAnalysis(from: Date(), to: Date()) }

        _ = try await api.login(pin: "1234")
        var cleared = ProductDraft(product: try await localProduct(api, "Burger Gourmet Rossini"))
        cleared.cost = nil
        try await api.updateProduct(id: burger.id, cleared)
        #expect(try await localProduct(api, "Burger Gourmet Rossini").cost == nil)

        var negative = ProductDraft(product: burger)
        negative.cost = Money(cents: -1)
        await #expect(throws: LocalPosAPI.negativeProductCost) { try await api.updateProduct(id: burger.id, negative) }
    }
}
