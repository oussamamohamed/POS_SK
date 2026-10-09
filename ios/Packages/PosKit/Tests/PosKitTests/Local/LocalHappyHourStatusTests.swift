import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : Happy Hour (statut, tarifs, dérogations)")
struct LocalHappyHourStatusTests {
    private static let insufficient = APIError.forbidden("Autorisation insuffisante : code PIN superviseur ou gérant requis.")

    private func fixedRule(_ product: Product, cents: Int) -> HappyHourRule {
        HappyHourRule(targetType: .product, targetId: product.id.uuidString.lowercased(), targetName: product.name, pricingMode: .fixedPrice, fixedPrice: Money(cents: cents))
    }

    private func clearSchedules(_ api: LocalPosAPI) async throws {
        for schedule in try await api.happyHourSchedules() {
            if let id = schedule.id { try await api.deleteHappyHourSchedule(id: id) }
        }
    }

    @discardableResult
    private func addSchedule(_ api: LocalPosAPI, _ name: String, days: [Int] = [1, 2, 3, 4, 5], start: String = "17:00", end: String = "20:00",
                             priority: Int = 1, isActive: Bool = true, rules: [HappyHourRule] = []) async throws -> UUID {
        let schedule = HappyHourSchedule(name: name, daysOfWeek: days, startTime: start, endTime: end, isActive: isActive, priority: priority, priceRules: rules)
        guard let id = try await api.createHappyHourSchedule(schedule) else { throw APIError.notFound(name) }
        return id
    }

    @Test func statusInsideTheWindow() async throws {
        let clock = LocalTestClock()  // lundi 18 h 30 UTC
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        let id = try await addSchedule(api, "Afterwork")

        let status = try await api.happyHourStatus(terminalId: "T01")
        #expect(status.isActive && !status.isOverride && !status.appliesToTakeaway)
        #expect(status.activeScheduleName == "Afterwork" && status.activeScheduleId == id)
        #expect(status.currentWindow?.startTime == "17:00:00" && status.currentWindow?.endTime == "20:00:00" && status.currentWindow?.remainingMinutes == 90)
    }

    @Test func statusIsInactiveOutsideTheWindowTheDaysOrWhenDisabled() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        try await addSchedule(api, "Afterwork")
        let moments: [(String, Bool)] = [
            ("2026-10-12T16:59:59Z", false), ("2026-10-12T17:00:00Z", true), ("2026-10-12T19:59:59Z", true), ("2026-10-12T20:00:00Z", false),
            ("2026-10-11T18:30:00Z", false), ("2026-10-17T18:30:00Z", false),
        ]
        for (moment, expected) in moments {
            clock.set(moment)
            #expect(try await api.happyHourStatus(terminalId: "T01").isActive == expected, "\(moment)")
        }

        try await clearSchedules(api)
        try await addSchedule(api, "Éteint", isActive: false)
        clock.set("2026-10-12T18:30:00Z")
        #expect(try await api.happyHourStatus(terminalId: "T01").isActive == false)
    }

    @Test func higherPriorityWinsAndTiesKeepTheFirst() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        try await addSchedule(api, "Basse", priority: 1)
        try await addSchedule(api, "Haute", start: "18:00", end: "19:00", priority: 5)
        #expect(try await api.happyHourStatus(terminalId: "T01").activeScheduleName == "Haute")
        clock.set("2026-10-12T17:30:00Z")
        #expect(try await api.happyHourStatus(terminalId: "T01").activeScheduleName == "Basse")

        try await clearSchedules(api)
        try await addSchedule(api, "A", priority: 2)
        try await addSchedule(api, "B", priority: 2)
        #expect(try await api.happyHourStatus(terminalId: "T01").activeScheduleName == "A")
    }

    @Test func pricingTableAppliesProductThenCategoryRules() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        let ipa = try await localProduct(api, "Bière Artisanale IPA 33cl")
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        let pizza = try await localProduct(api, "Pizza Margherita AOP")
        let bordeaux = try await localProduct(api, "Verre Bordeaux AOP 12cl")
        let eau = try await localProduct(api, "Eau Pétillante 50cl")
        try await addSchedule(api, "Afterwork", rules: [
            fixedRule(ipa, cents: 500),
            fixedRule(burger, cents: 2500),  // plus cher que le tarif normal : ignorée
            HappyHourRule(targetType: .product, targetId: pizza.id.uuidString, targetName: pizza.name, pricingMode: .percentageDiscount, discountPercent: 10),
            HappyHourRule(targetType: .category, targetId: "CAT_DRINKS", targetName: "Boissons & Vins", pricingMode: .percentageDiscount, discountPercent: 15),
        ])

        let table = try await api.happyHourPricing(terminalId: "T01")
        #expect(table.isActive && table.items.count == 4)
        func price(_ product: Product) -> HappyHourPrice? { table.items.first { $0.productId == product.id } }
        #expect(price(ipa)?.standardPrice == Money(cents: 600) && price(ipa)?.happyHourPrice == Money(cents: 500) && price(ipa)?.ruleType == "FixedPrice")
        #expect(price(burger) == nil)
        #expect(price(pizza)?.happyHourPrice == Money(cents: 1125) && price(pizza)?.ruleType == "CategoryDiscount")
        #expect(price(bordeaux)?.happyHourPrice == Money(cents: 468) && price(bordeaux)?.ruleType == "CategoryDiscount")  // 550 × 0,85 = 467,5 → 468
        #expect(price(eau)?.happyHourPrice == Money(cents: 383))  // 450 × 0,85 = 382,5 → 383

        clock.set("2026-10-12T21:00:00Z")
        let closed = try await api.happyHourPricing(terminalId: "T01")
        #expect(!closed.isActive && closed.items.isEmpty)

        try await clearSchedules(api)
        try await addSchedule(api, "Vide")
        clock.set("2026-10-12T18:30:00Z")
        let empty = try await api.happyHourPricing(terminalId: "T01")
        #expect(empty.isActive && empty.items.isEmpty)
    }

    @Test func overrideDurationIsCappedAtOneDay() async throws {
        let clock = LocalTestClock("2026-10-14T10:00:00Z")
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        let result = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 100000, reason: "")
        #expect(result.message == "Happy Hour activé/prolongé de 1440 minutes avec succès.")
        #expect(try await api.happyHourStatus(terminalId: "T01").currentWindow?.remainingMinutes == 1440)
    }

    @Test func supervisorOverrideStartsExtendsAndExpires() async throws {
        let clock = LocalTestClock("2026-10-14T10:00:00Z")  // mercredi, hors plage
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        #expect(try await api.happyHourStatus(terminalId: "T01").isActive == false)

        let started = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 30, reason: "Test")
        #expect(started.success == true && started.message == "Happy Hour activé/prolongé de 30 minutes avec succès.")
        var status = try await api.happyHourStatus(terminalId: "T01")
        #expect(status.isActive && status.isOverride && status.activeScheduleName == "Dérogation Responsable" && status.activeScheduleId == nil)
        #expect(status.currentWindow?.remainingMinutes == 30 && status.currentWindow?.startTime == "10:00:00" && status.currentWindow?.endTime == "10:30:00")
        clock.advance(29 * 60)
        #expect(try await api.happyHourStatus(terminalId: "T01").currentWindow?.remainingMinutes == 1)
        clock.advance(2 * 60)
        #expect(try await api.happyHourStatus(terminalId: "T01").isActive == false)

        // Une nouvelle dérogation remplace la précédente ; sans durée, elle dure 60 minutes.
        _ = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 90, reason: "")
        _ = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 10, reason: "")
        #expect(try await api.happyHourStatus(terminalId: "T01").currentWindow?.remainingMinutes == 10)
        _ = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 0, reason: "")
        status = try await api.happyHourStatus(terminalId: "T01")
        #expect(status.currentWindow?.remainingMinutes == 60)

        // Avec un planning actif dans la base, la dérogation en reprend le nom et les règles.
        let ipa = try await localProduct(api, "Bière Artisanale IPA 33cl")
        let scheduleId = try await addSchedule(api, "Mercredi", days: [3], rules: [fixedRule(ipa, cents: 500)])
        status = try await api.happyHourStatus(terminalId: "T01")
        #expect(status.isOverride && status.activeScheduleName == "Mercredi" && status.activeScheduleId == scheduleId)
        let table = try await api.happyHourPricing(terminalId: "T01")
        #expect(table.isActive && table.items.first { $0.productId == ipa.id }?.happyHourPrice == Money(cents: 500))

        let stopped = try await api.stopHappyHourOverride(terminalId: "T01", pin: "1234", reason: "Fin")
        #expect(stopped.success == true && stopped.message == "Dérogation Happy Hour désactivée.")
        #expect(try await api.happyHourStatus(terminalId: "T01").isActive == false)
    }

    @Test func overrideNeedsASupervisorPinAndSharesTheLockout() async throws {
        let clock = LocalTestClock()
        let api = try await makeLocalAPI(clock: clock)
        try await clearSchedules(api)
        // Un serveur (PIN valide sans droit de responsable) est refusé sans compter comme échec.
        await #expect(throws: Self.insufficient) { try await api.activateHappyHourOverride(terminalId: "T01", pin: "2468", minutes: 30, reason: "") }
        await #expect(throws: Self.insufficient) { try await api.stopHappyHourOverride(terminalId: "T01", pin: "2468", reason: "") }
        #expect(try await api.happyHourStatus(terminalId: "T01").isActive == false)

        // Cinq PIN inconnus verrouillent aussi la connexion (compteur partagé), pendant 30 s.
        for _ in 0..<5 {
            await #expect(throws: Self.insufficient) { try await api.activateHappyHourOverride(terminalId: "T01", pin: "0000", minutes: 30, reason: "") }
        }
        await #expect(throws: APIError.rateLimited(nil)) { try await api.login(pin: "1234") }
        await #expect(throws: APIError.rateLimited(nil)) { try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 30, reason: "") }
        clock.advance(31)
        let result = try await api.activateHappyHourOverride(terminalId: "T01", pin: "1234", minutes: 30, reason: "")
        #expect(result.success == true)
    }
}
