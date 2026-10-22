import Foundation
import Testing
@testable import PosKit

/// Rejoue `Fixtures/report_print_data_reference.json`, générée par le .NET (`ReportPrintDataReferenceFixtureTests`) :
/// tri français, puis chaque scénario (mêmes lignes en base, même relevé attendu). La section `day` est rejouée par `LocalDaySimulationTests`.
@Suite("Données de rapport : parité .NET")
struct LocalReportPrintDataTests {
    struct Reference: Decodable {
        struct Collation: Decodable { var input: [String]; var sorted: [String] }
        struct Pair: Decodable { var a: String; var b: String; var sign: Int }
        struct Day: Decodable { var seed: Int; var terminals: [String: [ReportPrintData]] }
        var collation: Collation
        var collationPairs: [Pair]
        var scenarios: [Scenario]
        var day: Day
    }

    struct Scenario: Decodable {
        struct Category: Decodable { var id: String; var name: String }
        struct Product: Decodable { var id: UUID; var name: String; var categoryId: String }
        struct User: Decodable { var id: UUID; var name: String }
        struct Item: Decodable {
            var productId: UUID
            var productName: String
            var quantity: Int
            var unitPriceCents: Int
            var modifiersExtraCents: Int
            var discountPercent: Decimal
            var isComp: Bool
        }
        struct Order: Decodable {
            var id: UUID
            var operatorId: UUID
            var status: Int
            var tipCents: Int
            var globalDiscountType: Int?
            var globalDiscountValue: Decimal
            var items: [Item]
        }
        struct Receipt: Decodable {
            var id: UUID
            var terminalId: String
            var orderId: UUID
            var ttcCents: Int
            var createdAtUnixMs: Int64
            var voidedReceiptId: UUID?
        }
        var name: String
        var terminalId: String
        var periodStartUnixMs: Int64?
        var periodEndUnixMs: Int64
        var categories: [Category]
        var products: [Product]
        var users: [User]
        var orders: [Order]
        var receipts: [Receipt]
        var expected: ReportPrintData
    }

    static func reference() throws -> Reference {
        try JSONDecoder().decode(Reference.self, from: ContractDecodingTests.fixture("report_print_data_reference"))
    }

    private static func date(_ ms: Int64) -> Date { Date(timeIntervalSince1970: Double(ms) / 1000) }

    // MARK: - Tri français

    @Test func leTriFrancaisEgaleCeluiDuNet() throws {
        let collation = try Self.reference().collation
        #expect(FrenchCollation.sorted(collation.input, by: { $0 }) == collation.sorted)
    }

    @Test func chaquePaireSeCompareCommeLeNet() throws {
        for pair in try Self.reference().collationPairs {
            let sign = FrenchCollation.compare(pair.a, pair.b).rawValue
            #expect(sign == pair.sign, "\(pair.a) | \(pair.b)")
            #expect(FrenchCollation.compare(pair.b, pair.a).rawValue == -pair.sign, "\(pair.b) | \(pair.a)")
        }
    }

    @Test func leTriNeDependPasDeLaLocaleDeLAppareil() {
        // Même résultat quelle que soit la langue courante : la locale « fr » est figée dans FrenchCollation.
        #expect(FrenchCollation.compare("Éclair", "eclair") == .orderedDescending)
        #expect(FrenchCollation.compare("Ecuelle", "Éclair") == .orderedDescending)
    }

    // MARK: - Scénarios

    private static func seed(_ s: Scenario, into db: SQLiteDatabase) throws {
        let now = Date(timeIntervalSince1970: 1_700_000_000)
        for c in s.categories {
            try db.run(
                "INSERT INTO Categories (Id, Name, DisplayOrder, IsActive, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 0, 1, ?, ?)",
                [.text(c.id), .text(c.name), .date(now), .date(now)]
            )
        }
        for p in s.products {
            try db.run(
                """
                INSERT INTO Products (Id, Name, CategoryId, Price, TaxRatePercent, IsFoodVoucherEligible, DisplayOrder, IsAvailable, IsActive, IsQuickKey,
                    CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, 100, '10', 1, 0, 1, 1, 0, ?, ?)
                """,
                [.uuid(p.id), .text(p.name), .text(p.categoryId), .date(now), .date(now)]
            )
        }
        for u in s.users {
            try db.run(
                "INSERT INTO Users (Id, Name, Role, PinHash, PinSalt, IsActive, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, 1, 'x', 'x', 1, ?, ?)",
                [.uuid(u.id), .text(u.name), .date(now), .date(now)]
            )
        }
        for (o, order) in s.orders.enumerated() {
            let orderedAt = now.addingTimeInterval(Double(o))
            try db.run(
                """
                INSERT INTO Orders (Id, TableNumber, OperatorId, Status, Destination, CreatedAtUtc, GlobalDiscountType, GlobalDiscountValue, TipAmount)
                VALUES (?, '1', ?, ?, 1, ?, ?, ?, ?)
                """,
                [.uuid(order.id), .uuid(order.operatorId), .integer(order.status), .date(orderedAt), .integer(order.globalDiscountType),
                 .decimal(order.globalDiscountValue), .integer(order.tipCents)]
            )
            for (i, item) in order.items.enumerated() {
                try db.run(
                    """
                    INSERT INTO OrderItems (Id, OrderId, ProductId, ProductName, Quantity, UnitPrice, TaxRatePercent, IsFoodVoucherEligible, IsDispatched,
                        SelectedModifiers, ModifiersPriceExtra, Course, DiscountPercent, IsComp, IsHappyHourApplied, OrderedAtUtc)
                    VALUES (?, ?, ?, ?, ?, ?, '10', 1, 1, '[]', ?, 0, ?, ?, 0, ?)
                    """,
                    [.uuid(UUID()), .uuid(order.id), .uuid(item.productId), .text(item.productName), .integer(item.quantity), .integer(item.unitPriceCents),
                     .integer(item.modifiersExtraCents), .decimal(item.discountPercent), .bool(item.isComp), .date(orderedAt.addingTimeInterval(Double(i) / 100))]
                )
            }
        }
        var sequences: [String: Int] = [:]
        for r in s.receipts {
            sequences[r.terminalId, default: 0] += 1
            let sequence = sequences[r.terminalId]!
            try db.run(
                """
                INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                    PreviousSignatureHash, SignatureHash, CreatedAtUtc, VoidedReceiptId) VALUES (?, ?, ?, ?, ?, ?, 0, '{}', '', '', ?, ?)
                """,
                [.uuid(r.id), .text(r.terminalId), .text("\(r.terminalId)-\(sequence)"), .uuid(r.orderId), .integer(sequence), .integer(r.ttcCents),
                 .text(FiscalHashing.storedText(date(r.createdAtUnixMs))), .uuid(r.voidedReceiptId)]
            )
        }
    }

    @Test(arguments: ["familles_tri_offert_remise", "pourboires", "perimetre", "sans_debut", "vide"])
    func chaqueScenarioEgaleLeNet(name: String) throws {
        let scenario = try #require(try Self.reference().scenarios.first { $0.name == name })
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try Self.seed(scenario, into: db)

        let data = try LocalFiscalRepository(db: db).reportPrintData(
            terminalId: scenario.terminalId, periodStart: scenario.periodStartUnixMs.map(Self.date), periodEnd: Self.date(scenario.periodEndUnixMs)
        )

        let expected = scenario.expected
        #expect(data.categories.map(\.categoryName) == expected.categories.map(\.categoryName), "familles")
        for (actual, wanted) in zip(data.categories, expected.categories) {
            #expect(actual.items == wanted.items, "articles de \(wanted.categoryName ?? "nil")")
            #expect(actual.subtotalTtcCents == wanted.subtotalTtcCents, "sous-total de \(wanted.categoryName ?? "nil")")
        }
        #expect(data.tips == expected.tips, "pourboires")
        #expect(data.totalTipsCents == expected.totalTipsCents, "total des pourboires")
        #expect(data.hasGlobalDiscount == expected.hasGlobalDiscount, "remise globale")
        #expect(data == expected)
    }

    @Test func lesFamillesInconnuesEtLesServeursInconnusSontEnDernier() throws {
        let scenarios = try Self.reference().scenarios
        let familles = try #require(scenarios.first { $0.name == "familles_tri_offert_remise" }).expected
        #expect(familles.categories.last?.categoryName == nil && familles.categories.dropLast().allSatisfy { $0.categoryName != nil })
        let tips = try #require(scenarios.first { $0.name == "pourboires" }).expected.tips
        #expect(tips.suffix(2).allSatisfy { $0.serverName == nil } && tips.dropLast(2).allSatisfy { $0.serverName != nil })
    }
}
