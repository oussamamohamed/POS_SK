import Foundation
import Testing
@testable import PosKit

/// Rejoue la journée de stock `stock_day_reference.json` (générée par le .NET : `WRITE_STOCK_DAY_FIXTURE=1 dotnet test tests/RestaurantPos.Api.Tests
/// --filter StockDaySimulationTests`) : le stock final, les prix moyens et le coût des fiches doivent égaler l'oracle, au centime.
@Suite("LocalPosAPI : journée de stock")
struct LocalStockDaySimulationTests {
    struct Line: Decodable { let ingredient: String, quantityMilli: Int64 }
    struct IngredientSpec: Decodable { let name: String, priceMilli: Int64, isPreparation: Bool, yieldMilli: Int64?, lines: [Line]? }
    struct ProductSpec: Decodable { let name: String, priceCents: Int, recipe: [Line] }
    struct Op: Decodable {
        let type: String
        let product: String?, ingredient: String?
        let quantity: Int?, quantityMilli: Int64?, unitCostMilliCents: Int64?, saleIndex: Int?
        let restock: Bool?
    }
    struct Expected: Decodable { let stockMilli: [String: Int64], unitCostMilli: [String: Int64], productCostCents: [String: Int] }
    struct Day: Decodable { let ingredients: [IngredientSpec], products: [ProductSpec], operations: [Op], expected: Expected }

    @Test func theDayEndsWithTheOracleStockAndCosts() async throws {
        let url = try #require(Bundle.module.url(forResource: "stock_day_reference", withExtension: "json", subdirectory: "Fixtures"))
        let day = try JSONDecoder().decode(Day.self, from: Data(contentsOf: url))
        let (api, db) = try await makeLocalAPIWithDatabase()

        var ids: [String: UUID] = [:]
        for spec in day.ingredients {
            ids[spec.name] = try await api.createIngredient(IngredientDraft(name: spec.name, unitCostMilliCents: spec.priceMilli, isPreparation: spec.isPreparation, yieldMilli: spec.yieldMilli)).id
        }
        for prep in day.ingredients where prep.isPreparation {
            _ = try await api.setPreparationRecipe(id: ids[prep.name]!, lines: prep.lines!.map { RecipeLine(ingredientId: ids[$0.ingredient]!, quantityMilli: $0.quantityMilli) })
        }
        var products: [String: Product] = [:]
        for spec in day.products {
            try await api.createProduct(ProductDraft(name: spec.name, categoryId: try #require(try await api.categories().first?.id), price: Money(cents: spec.priceCents), taxRatePercent: 10))
            let product = try await localProduct(api, spec.name)
            products[spec.name] = product
            if !spec.recipe.isEmpty {
                _ = try await api.setRecipe(productId: product.id, lines: spec.recipe.map { RecipeLine(ingredientId: ids[$0.ingredient]!, quantityMilli: $0.quantityMilli) })
            }
        }

        var sales: [UUID] = []   // reçu de chaque vente, dans l'ordre
        func lastReceipt() throws -> UUID {
            try #require(try db.query("SELECT Id FROM FiscalReceipts WHERE VoidedReceiptId IS NULL ORDER BY rowid DESC LIMIT 1").first?.uuid("Id"))
        }
        for op in day.operations {
            switch op.type {
            case "receipt": _ = try await api.receiveStock(ingredientId: ids[op.ingredient!]!, quantityMilli: op.quantityMilli!, unitCostMilliCents: op.unitCostMilliCents!, note: nil)
            case "loss": _ = try await api.recordStockLoss(ingredientId: ids[op.ingredient!]!, quantityMilli: op.quantityMilli!, note: nil)
            case "count": _ = try await api.countStock(ingredientId: ids[op.ingredient!]!, countedMilli: op.quantityMilli!, note: nil)
            case "sale":
                let table = "J\(sales.count)"
                let product = products[op.product!]!
                try await seatTable(api, table, items: [localInput(product, quantity: op.quantity!)])
                let order = try #require(try await api.activeOrder(table: table))
                let cents = product.price.cents * op.quantity!
                _ = try await api.pay(localPayment(order, table: table, tenders: [localTender(.cash, cents)]))
                sales.append(try lastReceipt())
            case "void": _ = try await api.voidReceipt(receiptId: sales[op.saleIndex!], restock: op.restock ?? false)
            default: Issue.record("opération inconnue : \(op.type)")
            }
        }

        let final = Dictionary(uniqueKeysWithValues: try await api.ingredients().map { ($0.name, $0) })
        for (name, expected) in day.expected.stockMilli { #expect(final[name]?.stockMilli == expected, "stock final de \(name)") }
        for (name, expected) in day.expected.unitCostMilli { #expect(final[name]?.unitCostMilliCents == expected, "prix moyen de \(name)") }
        for (name, expected) in day.expected.productCostCents { #expect(try await api.recipe(productId: products[name]!.id).costCents == Int64(expected), "coût de la fiche \(name)") }
        // Le stock ne touche à aucune chaîne fiscale.
        #expect(try await api.verifyChains().isValid)
    }
}
