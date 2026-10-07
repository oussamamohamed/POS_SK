import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : paiement des tables")
struct LocalPaymentTests {
    private static let paymentFailed = APIError.server(status: 400, message: "Échec de l'encaissement.")

    private func seat(_ api: LocalPosAPI, _ table: String = "T1", quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, table, items: [localInput(burger, quantity: quantity)])
        return try #require(try await api.activeOrder(table: table))
    }

    @Test func payingTheFullAmountClosesTheOrderAndFreesTheTable() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950, tendered: 2000)]))
        #expect(result.receiptNumber == "NF-T01-000001" && result.fiscalSignature == nil && result.printQueued == false)
        #expect(result.totalPaid == Money(cents: 1950) && result.changeGiven == Money(cents: 50) && result.remainingBalance == .zero)
        let table = try #require(try await api.tables().first { $0.tableNumber == "T1" })
        #expect(table.status == .free && table.activeOrderId == nil && table.coversCount == 0)
        #expect(try await api.activeOrder(table: "T1") == nil)
    }

    @Test func splitPaymentsAccumulateUntilTheOrderIsSettled() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        let first = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1000)]))
        #expect(first.receiptNumber == "NF-T01-000001" && first.remainingBalance == Money(cents: 950) && first.changeGiven == .zero)
        #expect(try await api.tables().first { $0.tableNumber == "T1" }?.status == .occupied)
        let second = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 950, tendered: 1000)]))
        #expect(second.receiptNumber == "NF-T01-000002" && second.remainingBalance == .zero && second.changeGiven == Money(cents: 50))
        #expect(try await api.activeOrder(table: "T1") == nil)
    }

    @Test func badTendersAreRefusedWithoutWriting() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        let attempts: [[TenderInput]] = [[], [localTender(.cash, 0)], [localTender(.cash, -100)], [localTender(.cash, 2000)], [localTender(.cash, 1000), localTender(.creditCard, 1000)]]
        for tenders in attempts {
            await #expect(throws: Self.paymentFailed) { try await api.pay(localPayment(order, table: "T1", tenders: tenders)) }
        }
        // Rien n'a été écrit : le premier vrai règlement porte le premier numéro de reçu et solde la note.
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        #expect(result.receiptNumber == "NF-T01-000001" && result.remainingBalance == .zero)
    }

    @Test func aPaidOrderCannotBePaidAgain() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        await #expect(throws: Self.paymentFailed) { try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 100)])) }
        await #expect(throws: APIError.server(status: 400, message: "Commande introuvable pour ce règlement.")) {
            try await api.pay(localPayment(nil, table: "T404", tenders: [localTender(.cash, 100)]))
        }
    }

    @Test func tipIsOnlyAcceptedOnTheFinalPayment() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api)
        await #expect(throws: APIError.server(status: 400, message: "Le pourboire ne peut être ajouté qu'au paiement qui solde la note.")) {
            try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1000)], tip: 200))
        }
        await #expect(throws: APIError.server(status: 400, message: "Montant de pourboire invalide.")) {
            try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)], tip: -1))
        }
        // Aucun pourboire n'est resté enregistré : le paiement final avec pourboire solde exactement la note.
        let final = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 2150, tendered: 2200)], tip: 200))
        #expect(final.totalPaid == Money(cents: 2150) && final.changeGiven == Money(cents: 50) && final.remainingBalance == .zero)
    }

    @Test func payResolvesTheOrderFromTheTable() async throws {
        let api = try await makeLocalAPI()
        _ = try await seat(api, "T3")
        let result = try await api.pay(localPayment(nil, table: "T3", tenders: [localTender(.cash, 1950)]))
        #expect(result.remainingBalance == .zero)
        #expect(try await api.activeOrder(table: "T3") == nil)
    }

    @Test func payNeedsLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await api.pay(localPayment(nil, table: "T1", tenders: [localTender(.cash, 100)])) }
    }
}
