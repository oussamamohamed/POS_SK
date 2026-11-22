import Foundation
import Testing
@testable import PosKit

/// Miroir de `CashDaySimulationTests` (.NET) : même journée scriptée (avec annulations espèces/carte/fractionnées, annulation de ligne
/// et alerte `voids_count`), même oracle (spec 024, SC-001/SC-002/SC-003).
@Suite("LocalPosAPI : journée de caisse")
struct LocalCashDayTests {
    @Test func scriptedDayGivesTheOracleExpectedVarianceOneAlertAndAValidChain() async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let managerId = try #require(try await api.login(pin: "1234").operatorId)
        _ = try await api.setAlertRule(type: AlertRuleType.cashVariance, threshold: 200, enabled: true)
        _ = try await api.setAlertRule(type: AlertRuleType.voidsCount, threshold: 1, enabled: true)
        let session = try await api.openCashSession(openingFloatCents: 10_000)
        let burger = try await localProduct(api, "Burger Gourmet Rossini") // 19,50 €

        func receiptId(_ number: String) throws -> UUID {
            try #require(try db.query("SELECT Id FROM FiscalReceipts WHERE ReceiptNumber = ?", [.text(number)]).first?.uuid("Id"))
        }

        // J1 espèces + rendu, J2 carte, J3 espèces exact
        for (index, (method, tendered)) in [(PaymentMethod.cash, 2_000), (.creditCard, 1_950), (.cash, 1_950)].enumerated() {
            let table = "J\(index + 1)"
            try await seatTable(api, table, items: [localInput(burger)])
            let order = try #require(try await api.activeOrder(table: table))
            _ = try await api.pay(localPayment(order, table: table, tenders: [localTender(method, 1_950, tendered: tendered)]))
        }

        // J4 espèces avec rendu (25 € remis pour 19,50 €) puis annulé -> baisse l'attendu de 19,50 € (jamais 25 €)
        try await seatTable(api, "J4", items: [localInput(burger)])
        let o4 = try #require(try await api.activeOrder(table: "J4"))
        let r4 = try #require(try await api.pay(localPayment(o4, table: "J4", tenders: [localTender(.cash, 1_950, tendered: 2_500)])).receiptNumber)
        _ = try await api.voidReceipt(receiptId: try receiptId(r4))

        // J5 carte avec remise 50 % (> seuil 30 %) puis annulée -> 0 effet sur l'attendu espèces, franchit voids_count (> 1) ;
        // la règle discount_percent activée après l'annulation ne doit pas lever de fausse alerte lors des évaluations suivantes (J6/J7).
        try await seatTable(api, "J5", items: [localInput(burger)])
        let o5 = try #require(try await api.activeOrder(table: "J5"))
        try await api.applyDiscount(orderId: o5.orderId, type: .percentage, value: 50, reason: "Erreur", operatorId: managerId)
        let r5 = try #require(try await api.pay(localPayment(o5, table: "J5", tenders: [localTender(.creditCard, 975)])).receiptNumber)
        _ = try await api.voidReceipt(receiptId: try receiptId(r5))
        _ = try await api.setAlertRule(type: AlertRuleType.discountPercent, threshold: 30, enabled: true)

        // J6 note partagée (15 € espèces avec rendu + 24 € carte) -> annulation part espèces puis re-règlement en carte
        try await seatTable(api, "J6", items: [localInput(burger, quantity: 2)])
        let o6 = try #require(try await api.activeOrder(table: "J6"))
        let r6Cash = try #require(try await api.pay(localPayment(o6, table: "J6", tenders: [localTender(.cash, 1_500, tendered: 2_000)])).receiptNumber)
        _ = try await api.pay(localPayment(o6, table: "J6", tenders: [localTender(.creditCard, 2_400)]))
        _ = try await api.voidReceipt(receiptId: try receiptId(r6Cash))
        _ = try await api.pay(localPayment(o6, table: "J6", tenders: [localTender(.creditCard, 1_500)]))

        // J7 annulation d'une ligne envoyée avant règlement (2 -> 1 plat) puis encaissement espèces du reste (19,50 €)
        try await seatTable(api, "J7", items: [localInput(burger, quantity: 2)])
        try await api.dispatch(table: "J7")
        let o7 = try #require(try await api.activeOrder(table: "J7"))
        let o7Line = try #require(o7.lines.first)
        try await api.voidLine(orderId: o7.orderId, lineId: o7Line.lineId, quantity: 1, reason: "Erreur", operatorId: nil)
        _ = try await api.pay(localPayment(o7, table: "J7", tenders: [localTender(.cash, 1_950)]))

        _ = try await api.addCashMovement(sessionId: session.id, kind: "out", amountCents: 1_200, reason: "Achat de glace")
        _ = try await api.addCashMovement(sessionId: session.id, kind: "in", amountCents: 500, reason: "Appoint de monnaie")

        #expect(try await api.currentCashSession()?.expectedCents == 15_150)
        await #expect(throws: CashError.varianceNoteRequired) { _ = try await api.countCash(sessionId: session.id, countedCents: 14_700, denominations: [], varianceNote: nil) }
        let counted = try await api.countCash(sessionId: session.id, countedCents: 14_700, denominations: [], varianceNote: "Rendu erroné")
        let again = try await api.countCash(sessionId: session.id, countedCents: 1, denominations: [], varianceNote: nil)
        #expect(counted.varianceCents == -450 && again.varianceCents == -450 && again.countedCents == 14_700)

        let openAlerts = try await api.alerts(state: "open")
        #expect(openAlerts.filter { $0.ruleType == AlertRuleType.cashVariance }.count == 1)
        #expect(openAlerts.filter { $0.ruleType == AlertRuleType.discountPercent }.isEmpty)
        let voidsAlerts = openAlerts.filter { $0.ruleType == AlertRuleType.voidsCount }
        #expect(voidsAlerts.count == 1)
        #expect(voidsAlerts.first?.detailJson.contains("\"count\":3") == true)

        let z = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
        let summary = try #require(try await api.dailySummary(terminalId: "T01", tzOffsetMinutes: 0))
        #expect(summary.totalSalesTtcCents == 11_700 && summary.totalSalesTtcCents == z.totalSalesTtc.cents)
        #expect(summary.cash?.varianceCents == -450)
        #expect(summary.openAlerts.count == 2)
        #expect(try await api.verifyChains().isValid)
    }
}
