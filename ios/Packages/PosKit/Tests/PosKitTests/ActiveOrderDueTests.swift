import Foundation
import Testing
@testable import PosKit

/// Reste dû de la commande active (D5), mêmes cas que `ActiveOrderDueTests` (.NET) : `amountPaidTtc` = règlements des reçus actifs,
/// `amountDueTtc` = TTC + pourboire conservé − réglé (règle du règlement et de la Z). Burger 19,50 €.
@Suite("Reste dû de la commande active")
struct ActiveOrderDueTests {
    enum Step: Sendable { case pay(Int, tip: Int = 0), voidFirst, voidLast, discountPercent(Decimal), compFirst }

    struct Case: Sendable, CustomTestStringConvertible {
        let name: String
        let steps: [Step]
        /// nil : plus de commande active sur la table (soldée).
        let expected: (paid: Int, due: Int)?
        /// L'annulation de reçu n'est qu'un bouchon dans `InMemoryPosAPI` : ces cas ne tournent qu'en mode autonome.
        var needsVoid: Bool { steps.contains { if case .voidFirst = $0 { true } else if case .voidLast = $0 { true } else { false } } }
        var testDescription: String { name }
    }

    static let cases: [Case] = [
        Case(name: "non réglée", steps: [], expected: (0, 1950)),
        Case(name: "en partie réglée", steps: [.pay(800)], expected: (800, 1150)),
        Case(name: "rouverte après annulation d'une part", steps: [.pay(1000), .pay(950), .voidFirst], expected: (950, 1000)),
        Case(name: "rouverte puis re-réglée", steps: [.pay(1000), .pay(950), .voidFirst, .pay(1000)], expected: nil),
        Case(name: "pourboire conservé sur la part active", steps: [.pay(500), .pay(2050, tip: 600), .voidFirst], expected: (2050, 500)),
        Case(name: "pourboire retiré avec sa part", steps: [.pay(500), .pay(2050, tip: 600), .voidLast], expected: (500, 1450)),
        Case(name: "remise 10 %", steps: [.discountPercent(10), .pay(755)], expected: (755, 1000)),
        Case(name: "offert", steps: [.compFirst], expected: (0, 0))
    ]

    private func play(_ api: some PosAPI, _ steps: [Step], voidReceipt: (Bool) async throws -> Void) async throws -> ActiveOrder? {
        let burger = try #require(try await api.products().first { $0.name == "Burger Gourmet Rossini" })
        try await api.openTable(number: "T1", covers: 2, operatorId: nil, waiterName: "Sophie")
        let order = try await api.addItems(table: "T1", items: [localInput(burger)])
        for step in steps {
            switch step {
            case let .pay(cents, tip):
                _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, cents)], tip: tip))
            case .voidFirst: try await voidReceipt(true)
            case .voidLast: try await voidReceipt(false)
            case let .discountPercent(value):
                try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: value, reason: "Geste", operatorId: nil)
            case .compFirst:
                try await api.compItem(orderId: order.orderId, lineId: try #require(order.lines.first).lineId, reason: "Geste", operatorId: nil)
            }
        }
        return try await api.activeOrder(table: "T1")
    }

    private func check(_ order: ActiveOrder?, _ c: Case) {
        guard let expected = c.expected else { #expect(order == nil); return }
        #expect(order?.amountPaidTtc == Money(cents: expected.paid))
        #expect(order?.amountDueTtc == Money(cents: expected.due))
    }

    @Test(arguments: cases) func standalone(_ c: Case) async throws {
        let (api, db) = try await makeLocalAPIWithDatabase()
        let order = try await play(api, c.steps) { first in
            let ids = try db.query(
                "SELECT Id FROM FiscalReceipts WHERE VoidedReceiptId IS NULL AND Id NOT IN (SELECT VoidedReceiptId FROM FiscalReceipts WHERE VoidedReceiptId IS NOT NULL) ORDER BY SequenceNumber"
            ).compactMap { $0.uuid("Id") }
            _ = try await api.voidReceipt(receiptId: try #require(first ? ids.first : ids.last))
        }
        check(order, c)
    }

    @Test(arguments: cases.filter { !$0.needsVoid }) func inMemory(_ c: Case) async throws {
        let api = InMemoryPosAPI()
        _ = try await api.login(pin: "1234")
        let order = try await play(api, c.steps) { _ in Issue.record("annulation non simulée") }
        check(order, c)
    }

    /// Après rechargement d'une commande en partie réglée, l'écran d'encaissement propose le reste dû, pas le TTC entier.
    @MainActor @Test func ticketProposesTheDueAfterReloadingAPartlyPaidOrder() async throws {
        let (model, api) = await makeModel()
        await model.ticket.load(table: "T1")
        model.ticket.add(product(model, "Burger Gourmet Rossini"))
        let outcome = await model.ticket.pay(method: .creditCard, amount: Money(cents: 800), tendered: Money(cents: 800))
        #expect(outcome?.isComplete == false)

        await model.ticket.load(table: "T2")
        await model.ticket.load(table: "T1") // rechargement : seul le DTO du serveur porte le règlement
        #expect(model.ticket.remainingBalance == Money(cents: 1150))
        #expect(model.ticket.amountDue == Money(cents: 1150))
        #expect(PaymentPlan(due: model.ticket.amountDue).amountToCollect == Money(cents: 1150))
        #expect(await api.calls.contains("activeOrder"))
    }

}
