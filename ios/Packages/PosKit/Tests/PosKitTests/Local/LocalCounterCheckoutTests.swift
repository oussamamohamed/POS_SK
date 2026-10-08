import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : encaissement au comptoir")
struct LocalCounterCheckoutTests {
    private func cart(_ api: LocalPosAPI, quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        _ = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        return try await api.addItems(table: "Comptoir", items: [localInput(burger, quantity: quantity)])
    }

    private func checkout(_ order: ActiveOrder, _ tenders: [CounterTender], terminal: String = "T01", tip: Int = 0, policy: MealVoucherPolicy = .capAtBalance) -> CounterCheckoutRequest {
        CounterCheckoutRequest(
            orderId: order.orderId, terminalId: terminal, destination: .takeaway, pickupBuzzer: nil, tipAmount: Money(cents: tip),
            requestFiscalReceiptPrint: false, mealVoucherPolicy: policy, tenders: tenders
        )
    }

    private func cash(_ amount: Int, tendered: Int? = nil) -> CounterTender {
        CounterTender(method: .cash, amount: Money(cents: amount), tendered: Money(cents: tendered ?? amount))
    }

    private func voucher(_ amount: Int, facial: Int? = nil) -> CounterTender {
        CounterTender(method: .mealVoucher, amount: Money(cents: amount), tendered: Money(cents: amount), facialValue: facial.map { Money(cents: $0) })
    }

    @Test func counterCheckoutPaysAssignsPickupAndClosesTheOrder() async throws {
        let api = try await makeLocalAPI()
        let order = try await cart(api)
        var request = checkout(order, [cash(1950, tendered: 2000)])
        request.pickupBuzzer = "12"
        let result = try await api.counterCheckout(request)
        #expect(result.orderId == order.orderId && result.pickupNumber == "#A-01" && result.receiptNumber == "NF-T01-000001")
        #expect(result.totalPaid == Money(cents: 1950) && result.changeGiven == Money(cents: 50) && result.remainingBalance == .zero)
        #expect(result.openCashDrawer == true && result.issuedCreditVoucher == nil && result.fiscalSignature == nil && result.printQueued == false)
        #expect(try await api.tables().first { $0.isCounter }?.status == .free)
        #expect(try await api.activeOrder(table: "Comptoir") == nil)

        let second = try await api.counterCheckout(checkout(try await cart(api), [cash(1950)]))
        #expect(second.pickupNumber == "#A-02" && second.receiptNumber == "NF-T01-000002" && second.openCashDrawer == true)
    }

    @Test func requestedTerminalIsIgnoredAtTheCounter() async throws {
        let api = try await makeLocalAPI()
        let result = try await api.counterCheckout(checkout(try await cart(api), [cash(1950)], terminal: "T02"))
        #expect(result.pickupNumber == "#A-01" && result.receiptNumber == "NF-T01-000001")
    }

    @Test func mealVoucherAboveTheLegalCapIsRefusedWithoutConsumingAPickupNumber() async throws {
        let api = try await makeLocalAPI()
        let order = try await cart(api, quantity: 2)  // 39,00 € dont tout est éligible
        await #expect(throws: APIError.server(status: 400, message: "Le montant par Titre-Restaurant (30.00 €) dépasse le plafond légal éligible (25.00 €).")) {
            try await api.counterCheckout(checkout(order, [voucher(3000)]))
        }
        // Rien n'a été écrit : la vente suivante reçoit le premier numéro de retrait et de reçu.
        let result = try await api.counterCheckout(checkout(order, [cash(3900)]))
        #expect(result.pickupNumber == "#A-01" && result.receiptNumber == "NF-T01-000001" && result.issuedCreditVoucher == nil)
    }

    @Test func mealVoucherOverpaymentFollowsThePolicy() async throws {
        let api = try await makeLocalAPI()
        let order = try await cart(api)  // 19,50 €
        await #expect(throws: APIError.server(status: 400, message: "Surpaiement par Titre-Restaurant refusé : la valeur faciale (25.00 €) dépasse le solde dû (19.50 €).")) {
            try await api.counterCheckout(checkout(order, [voucher(1950, facial: 2500)], policy: .strictRejection))
        }
        // Plafonné au solde : le surplus est absorbé, aucun avoir.
        let capped = try await api.counterCheckout(checkout(order, [voucher(1950, facial: 2500)], policy: .capAtBalance))
        #expect(capped.totalPaid == Money(cents: 1950) && capped.remainingBalance == .zero && capped.issuedCreditVoucher == nil && capped.openCashDrawer == false)

        let credited = try await api.counterCheckout(checkout(try await cart(api), [voucher(1950, facial: 2500)], policy: .customerCreditVoucher))
        let credit = try #require(credited.issuedCreditVoucher)
        #expect(credit.amount == Money(cents: 550) && credit.voucherCode.hasPrefix("CR-") && credit.voucherCode.count == 11)
        let expiry = try #require(credit.expiresAtUtc)
        #expect(abs(expiry.timeIntervalSinceNow - 90 * 86_400) < 60)
    }

    @Test func settleRefusalAfterVoucherChecksRollsBackPickupNumberAndCreditVoucher() async throws {
        let api = try await makeLocalAPI()
        let order = try await cart(api)  // 19,50 €
        // Le retrait et l'avoir sont écrits avant que le règlement soit refusé (surpaiement : 1950 + 100 > solde dû 1950) : tout doit être annulé.
        await #expect(throws: APIError.server(status: 400, message: "Échec de l'encaissement.")) {
            try await api.counterCheckout(checkout(order, [voucher(1950, facial: 2500), cash(100)], policy: .customerCreditVoucher))
        }
        let result = try await api.counterCheckout(checkout(order, [cash(1950)]))
        #expect(result.pickupNumber == "#A-01" && result.receiptNumber == "NF-T01-000001" && result.issuedCreditVoucher == nil)
    }

    @Test func counterCheckoutRefusesEmptyOrUnknownOrders() async throws {
        let api = try await makeLocalAPI()
        let empty = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        let refused = APIError.server(status: 400, message: "Commande introuvable ou panier vide.")
        await #expect(throws: refused) { try await api.counterCheckout(checkout(empty, [cash(100)])) }
        var unknown = checkout(empty, [cash(100)])
        unknown.orderId = UUID()
        await #expect(throws: refused) { try await api.counterCheckout(unknown) }
    }

    @Test func counterTipIsRecordedAndAPartialPaymentLeavesTheBalance() async throws {
        let api = try await makeLocalAPI()
        let order = try await cart(api)
        let result = try await api.counterCheckout(checkout(order, [cash(1950)], tip: 100))
        #expect(result.totalPaid == Money(cents: 1950) && result.remainingBalance == Money(cents: 100))
        #expect(try await api.activeOrder(table: "Comptoir")?.orderId == order.orderId)
        let rest = try await api.pay(localPayment(order, table: "Comptoir", tenders: [localTender(.cash, 100)]))
        #expect(rest.remainingBalance == .zero)
        #expect(try await api.activeOrder(table: "Comptoir") == nil)
    }

    @Test func counterCheckoutNeedsLogin() async throws {
        let api = try LocalPosAPI(path: ":memory:")
        let order = ActiveOrder(tableNumber: "Comptoir")
        await #expect(throws: APIError.unauthorized) { try await api.counterCheckout(checkout(order, [cash(100)])) }
    }
}
