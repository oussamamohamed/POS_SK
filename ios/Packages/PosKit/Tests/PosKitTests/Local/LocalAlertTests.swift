import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : alertes de pilotage")
struct LocalAlertTests {
    /// Vend un burger en espèces ; renvoie la commande payée.
    @discardableResult
    private func sell(_ api: LocalPosAPI, table: String, discount: (DiscountType, Decimal)? = nil) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger)])
        var order = try #require(try await api.activeOrder(table: table))
        if let (type, value) = discount {
            try await api.applyDiscount(orderId: order.orderId, type: type, value: value, reason: "Geste commercial", operatorId: nil)
            order = try #require(try await api.activeOrder(table: table))
        }
        let due = order.totalTtcAmount?.cents ?? 1_950
        _ = try await api.pay(localPayment(order, table: table, tenders: due > 0 ? [localTender(.cash, due)] : []))
        return order
    }

    private func void(_ db: SQLiteDatabase, _ api: LocalPosAPI) async throws {
        let id = try #require(try db.query("SELECT Id FROM FiscalReceipts WHERE VoidedReceiptId IS NULL AND Id NOT IN (SELECT VoidedReceiptId FROM FiscalReceipts WHERE VoidedReceiptId IS NOT NULL) ORDER BY rowid DESC LIMIT 1").first?.uuid("Id"))
        _ = try await api.voidReceipt(receiptId: id)
    }

    @Test func anOpenAlertFollowsTheCurrentCount() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        _ = try await api.setAlertRule(type: AlertRuleType.voidsCount, threshold: 3, enabled: true)
        for i in 0..<4 { try await sell(api, table: "V\(i)"); try await void(db, api) }
        #expect(try await api.alerts(state: "open").first?.detailInt("count") == 4)

        for i in 4..<7 { try await sell(api, table: "V\(i)"); try await void(db, api) }
        try await sell(api, table: "V7") // relance l'évaluation

        let alerts = try await api.alerts(state: "open")
        #expect(alerts.count == 1 && alerts[0].detailInt("count") == 7) // mise à jour de l'alerte ouverte, pas de doublon
    }

    @Test func voidsRaiseOneAlertWhenTheThresholdIsExceededWithoutDuplicates() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        _ = try await api.setAlertRule(type: AlertRuleType.voidsCount, threshold: 3, enabled: true)
        for i in 0..<3 { try await sell(api, table: "V\(i)"); try await void(db, api) }
        #expect(try await api.alerts(state: "open").isEmpty) // 3 pour un seuil de 3 : pas dépassé

        try await sell(api, table: "V3"); try await void(db, api)
        try await sell(api, table: "V4") // une vente de plus relance l'évaluation : aucun doublon

        let alerts = try await api.alerts(state: "open")
        #expect(alerts.count == 1 && alerts[0].ruleType == AlertRuleType.voidsCount && alerts[0].periodKey == "T01:0")
        #expect(alerts[0].detailInt("count") == 4 && alerts[0].detailInt("totalCents") == 4 * 1_950)
        #expect(alerts[0].operatorId != nil)
    }

    @Test func disabledRulesRaiseNothing() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        for i in 0..<5 { try await sell(api, table: "D\(i)"); try await void(db, api) }
        #expect(try await api.alerts(state: nil).isEmpty)
        #expect(try await api.alertRules().allSatisfy { !$0.enabled })
    }

    @Test func aZStartsANewPeriodSoTheSameOperatorCanAlertAgain() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        _ = try await api.setAlertRule(type: AlertRuleType.voidsCount, threshold: 1, enabled: true)
        for i in 0..<2 { try await sell(api, table: "Z\(i)"); try await void(db, api) }
        try await sell(api, table: "Z9")
        #expect(try await api.alerts(state: "open").count == 1)

        _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
        try await Task.sleep(for: .milliseconds(20))
        for i in 0..<2 { try await sell(api, table: "Y\(i)"); try await void(db, api) }
        try await sell(api, table: "Y9")

        #expect(Set(try await api.alerts(state: nil).map(\.periodKey)) == ["T01:0", "T01:1"])
    }

    @Test func discountsAboveThePercentAndCompsAreRaised() async throws {
        let api = try await makeLocalAPI()
        _ = try await api.setAlertRule(type: AlertRuleType.discountPercent, threshold: 30, enabled: true)
        _ = try await api.setAlertRule(type: AlertRuleType.compsTotal, threshold: 1_000, enabled: true)

        try await sell(api, table: "R1", discount: (.percentage, 10))
        #expect(try await api.alerts(state: "open").isEmpty)
        try await sell(api, table: "R2", discount: (.percentage, 40))
        try await sell(api, table: "R3", discount: (.comp, 0))

        let types = Set(try await api.alerts(state: "open").map(\.ruleType))
        #expect(types == [AlertRuleType.discountPercent, AlertRuleType.compsTotal])
    }

    @Test func resolvingNeedsANoteAndKeepsTheAlertInHistory() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        _ = try await api.setAlertRule(type: AlertRuleType.voidsCount, threshold: 0, enabled: true)
        try await sell(api, table: "H1"); try await void(db, api)
        try await sell(api, table: "H2")
        let alert = try #require(try await api.alerts(state: "open").first)

        await #expect(throws: CashError.noteRequired) { _ = try await api.resolveAlert(id: alert.id, note: "  ") }
        let resolved = try await api.resolveAlert(id: alert.id, note: "Erreur de saisie corrigée")

        #expect(resolved.state == "resolved" && resolved.resolutionNote == "Erreur de saisie corrigée")
        try await sell(api, table: "H3")
        let stillOpen = try await api.alerts(state: "open")
        let history = try await api.alerts(state: "resolved")
        #expect(stillOpen.isEmpty && history.count == 1)
        #expect(try await api.verifyChains().isValid)
    }

    @Test func rulesAndSettingsAreValidatedAndManagerOnly() async throws {
        let api = try await makeLocalAPI()
        await #expect(throws: CashError.ruleInvalid) { _ = try await api.setAlertRule(type: AlertRuleType.voidsCount, threshold: -1, enabled: true) }
        await #expect(throws: APIError.self) { _ = try await api.setAlertRule(type: "inconnue", threshold: 1, enabled: true) }
        #expect(try await api.alertSettings() == AlertSettings(trendThresholdPercent: 15, cashVarianceNoteRequired: true))
        #expect(try await api.saveAlertSettings(AlertSettings(trendThresholdPercent: 20, cashVarianceNoteRequired: false)).trendThresholdPercent == 20)
        await #expect(throws: CashError.ruleInvalid) { _ = try await api.saveAlertSettings(AlertSettings(trendThresholdPercent: 0)) }
        _ = try await api.login(pin: "2468")
        await #expect(throws: APIError.self) { _ = try await api.alerts(state: nil) }
    }
}
