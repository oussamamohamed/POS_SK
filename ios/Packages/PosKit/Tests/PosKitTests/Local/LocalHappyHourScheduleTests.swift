import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : plannings Happy Hour")
struct LocalHappyHourScheduleTests {
    private static let unknownSchedule = APIError.server(status: 400, message: "Planning Happy Hour introuvable.")

    private func afterwork(rules: [HappyHourRule] = []) -> HappyHourSchedule {
        HappyHourSchedule(name: "Afterwork", daysOfWeek: [1, 2, 3, 4, 5], startTime: "17:00", endTime: "20:00", priceRules: rules)
    }

    private func newSchedule(_ api: LocalPosAPI, rules: [HappyHourRule] = []) async throws -> UUID {
        guard let id = try await api.createHappyHourSchedule(afterwork(rules: rules)) else { throw APIError.notFound("planning") }
        return id
    }

    private func schedule(_ api: LocalPosAPI, _ id: UUID) async throws -> HappyHourSchedule {
        guard let found = try await api.happyHourSchedules().first(where: { $0.id == id }) else { throw APIError.notFound("planning") }
        return found
    }

    @Test func createListAndDeleteASchedule() async throws {
        let api = try await makeLocalAPI()
        let ipa = try await localProduct(api, "Bière Artisanale IPA 33cl")
        let id = try await newSchedule(api, rules: [
            HappyHourRule(targetType: .product, targetId: ipa.id.uuidString.lowercased(), targetName: ipa.name, pricingMode: .fixedPrice, fixedPrice: Money(cents: 500)),
            HappyHourRule(targetType: .category, targetId: "CAT_DRINKS", targetName: "Boissons & Vins", pricingMode: .percentageDiscount, discountPercent: 20),
        ])

        let created = try await schedule(api, id)
        #expect(created.name == "Afterwork" && created.daysOfWeek.sorted() == [1, 2, 3, 4, 5] && created.startTime == "17:00" && created.endTime == "20:00")
        #expect(created.isActive && !created.appliesToTakeaway && created.priority == 1 && created.priceRules.count == 2)
        let fixed = try #require(created.priceRules.first { $0.targetType == .product })
        #expect(fixed.fixedPrice == Money(cents: 500) && fixed.discountPercent == nil && fixed.pricingMode == .fixedPrice && fixed.id != nil)
        let percent = try #require(created.priceRules.first { $0.targetType == .category })
        #expect(percent.discountPercent == 20 && percent.fixedPrice == nil && percent.pricingMode == .percentageDiscount)

        try await api.deleteHappyHourSchedule(id: id)
        #expect(try await api.happyHourSchedules().contains { $0.id == id } == false)
        await #expect(throws: APIError.notFound("Plage horaire introuvable.")) { try await api.deleteHappyHourSchedule(id: id) }
    }

    @Test func createRejectsInvalidSchedules() async throws {
        let api = try await makeLocalAPI()
        let badTime = APIError.server(status: 400, message: "Format horaire invalide (HH:mm requis).")
        for (start, end) in [("25:00", "20:00"), ("17:00", "abc"), ("", "20:00"), ("17:60", "20:00"), ("17", "20:00"), ("17:00", "24:00")] {
            await #expect(throws: badTime) {
                try await api.createHappyHourSchedule(HappyHourSchedule(name: "X", daysOfWeek: [1], startTime: start, endTime: end))
            }
        }
        await #expect(throws: APIError.server(status: 400, message: "Le nom de la plage est requis.")) {
            try await api.createHappyHourSchedule(HappyHourSchedule(name: "  ", daysOfWeek: [1], startTime: "17:00", endTime: "20:00"))
        }
        await #expect(throws: APIError.server(status: 400, message: "Jour de la semaine invalide (0 à 6).")) {
            try await api.createHappyHourSchedule(HappyHourSchedule(name: "X", daysOfWeek: [7], startTime: "17:00", endTime: "20:00"))
        }
    }

    @Test func applyRulesCreatesThenReplacesRulesOfTheSameTarget() async throws {
        let api = try await makeLocalAPI()
        let ipa = try await localProduct(api, "Bière Artisanale IPA 33cl")
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let id = try await newSchedule(api)

        let products = try await api.applyHappyHourRules(scheduleId: id, BatchPriceRulesRequest(
            targetType: .product, targetIds: [ipa.id.uuidString.lowercased(), burger.id.uuidString.lowercased()],
            pricingMode: .fixedPrice, fixedPrice: Money(cents: 500), discountPercent: nil))
        let categories = try await api.applyHappyHourRules(scheduleId: id, BatchPriceRulesRequest(
            targetType: .category, targetIds: ["CAT_DRINKS"], pricingMode: .percentageDiscount, fixedPrice: nil, discountPercent: 20))
        #expect(products == 2 && categories == 1)

        var current = try await schedule(api, id)
        #expect(current.priceRules.count == 3)
        #expect(current.priceRules.first { $0.targetType == .category }?.targetName == "Boissons & Vins")
        #expect(Set(current.priceRules.filter { $0.targetType == .product }.map(\.targetName)) == [ipa.name, burger.name])

        _ = try await api.applyHappyHourRules(scheduleId: id, BatchPriceRulesRequest(
            targetType: .product, targetIds: [ipa.id.uuidString.uppercased()], pricingMode: .percentageDiscount, fixedPrice: nil, discountPercent: 15))
        current = try await schedule(api, id)
        #expect(current.priceRules.count == 3)
        let replaced = try #require(current.priceRules.first { $0.targetName == ipa.name })
        #expect(replaced.pricingMode == .percentageDiscount && replaced.discountPercent == 15 && replaced.fixedPrice == nil)
    }

    @Test func applyRulesRefusals() async throws {
        let api = try await makeLocalAPI()
        let id = try await newSchedule(api)
        func request(_ mode: HappyHourPricingMode, targets: [String] = ["CAT_DRINKS"], fixed: Int? = nil, percent: Decimal? = nil) -> BatchPriceRulesRequest {
            BatchPriceRulesRequest(targetType: .category, targetIds: targets, pricingMode: mode, fixedPrice: fixed.map { Money(cents: $0) }, discountPercent: percent)
        }
        await #expect(throws: Self.unknownSchedule) { try await api.applyHappyHourRules(scheduleId: UUID(), request(.fixedPrice, fixed: 500)) }
        await #expect(throws: APIError.server(status: 400, message: "Aucun élément sélectionné.")) {
            try await api.applyHappyHourRules(scheduleId: id, request(.fixedPrice, targets: [], fixed: 500))
        }
        let fixedPrice = APIError.server(status: 400, message: "Le prix fixe doit être strictement supérieur à 0.")
        await #expect(throws: fixedPrice) { try await api.applyHappyHourRules(scheduleId: id, request(.fixedPrice)) }
        await #expect(throws: fixedPrice) { try await api.applyHappyHourRules(scheduleId: id, request(.fixedPrice, fixed: 0)) }
        let percentRange = APIError.server(status: 400, message: "Le pourcentage de remise doit être compris entre 0.01% et 100%.")
        await #expect(throws: percentRange) { try await api.applyHappyHourRules(scheduleId: id, request(.percentageDiscount)) }
        await #expect(throws: percentRange) { try await api.applyHappyHourRules(scheduleId: id, request(.percentageDiscount, percent: 0)) }
        await #expect(throws: percentRange) { try await api.applyHappyHourRules(scheduleId: id, request(.percentageDiscount, percent: 101)) }
        #expect(try await schedule(api, id).priceRules.isEmpty)
    }

    @Test func deleteRulesRemovesOnlyTheGivenOnes() async throws {
        let api = try await makeLocalAPI()
        let id = try await newSchedule(api)
        _ = try await api.applyHappyHourRules(scheduleId: id, BatchPriceRulesRequest(
            targetType: .category, targetIds: ["CAT_DRINKS", "CAT_MAINS", "CAT_PIZZAS"], pricingMode: .percentageDiscount, fixedPrice: nil, discountPercent: 10))
        let rules = try await schedule(api, id).priceRules
        #expect(rules.count == 3)
        let toDelete = rules.prefix(2).compactMap(\.id) + [UUID()]
        try await api.deleteHappyHourRules(scheduleId: id, ruleIds: toDelete)
        let remaining = try await schedule(api, id).priceRules
        #expect(remaining.count == 1 && remaining.first?.id == rules.last?.id)

        await #expect(throws: APIError.server(status: 400, message: "Aucune règle sélectionnée pour la suppression.")) {
            try await api.deleteHappyHourRules(scheduleId: id, ruleIds: [])
        }
        await #expect(throws: Self.unknownSchedule) { try await api.deleteHappyHourRules(scheduleId: UUID(), ruleIds: [UUID()]) }
    }

    @Test func administrationNeedsAManagerButReadingDoesNot() async throws {
        let waiter = try await makeLocalAPI(pin: "2468")
        let notManager = APIError.forbidden("Action réservée à un responsable.")
        _ = try await waiter.happyHourSchedules()
        await #expect(throws: notManager) { try await waiter.createHappyHourSchedule(afterwork()) }
        await #expect(throws: notManager) { try await waiter.deleteHappyHourSchedule(id: UUID()) }
        await #expect(throws: notManager) {
            try await waiter.applyHappyHourRules(scheduleId: UUID(), BatchPriceRulesRequest(targetType: .category, targetIds: ["X"], pricingMode: .fixedPrice, fixedPrice: Money(cents: 1), discountPercent: nil))
        }
        await #expect(throws: notManager) { try await waiter.deleteHappyHourRules(scheduleId: UUID(), ruleIds: [UUID()]) }
        let anonymous = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await anonymous.createHappyHourSchedule(afterwork()) }
    }

    @Test func storageFormatMatchesTheServer() async throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        let api = try LocalPosAPI(path: path)
        _ = try await api.login(pin: "1234")
        let id = try await newSchedule(api, rules: [
            HappyHourRule(targetType: .category, targetId: "CAT_DRINKS", targetName: "Boissons & Vins", pricingMode: .percentageDiscount, discountPercent: 20),
            HappyHourRule(targetType: .product, targetId: UUID().uuidString.lowercased(), targetName: "Produit", pricingMode: .fixedPrice, fixedPrice: Money(cents: 500)),
        ])

        let raw = try SQLiteDatabase(path: path)
        let row = try #require(try raw.query("SELECT * FROM HappyHourSchedules WHERE Id = ?", [.uuid(id)]).first)
        #expect(row.string("DaysOfWeek") == "1,2,3,4,5" && row.string("StartTime") == "17:00:00" && row.string("EndTime") == "20:00:00")
        #expect(row.int("IsActive") == 1 && row.int("AppliesToTakeaway") == 0 && row.int("Priority") == 1)
        let rules = try raw.query("SELECT * FROM HappyHourPriceRules WHERE ScheduleId = ? ORDER BY rowid", [.uuid(id)])
        #expect(rules.count == 2)
        #expect(rules[0].int("TargetType") == 1 && rules[0].int("PricingMode") == 1 && rules[0].string("DiscountPercent") == "20" && rules[0].int("FixedPrice") == nil)
        #expect(rules[1].int("TargetType") == 0 && rules[1].int("PricingMode") == 0 && rules[1].int("FixedPrice") == 500 && rules[1].string("DiscountPercent") == nil)

        try await api.deleteHappyHourSchedule(id: id)
        #expect(try raw.query("SELECT COUNT(*) AS n FROM HappyHourPriceRules WHERE ScheduleId = ?", [.uuid(id)]).first?.int("n") == 0)
    }
}
