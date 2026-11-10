import Foundation
import Testing
@testable import PosKit

private actor ScriptedTerminal: CardTerminal {
    private let script: [CardChargeStatus]
    private(set) var amounts: [Money] = []

    init(_ script: CardChargeStatus...) { self.script = script }

    func charge(amount: Money, reference: String) async throws -> CardChargeResult {
        amounts.append(amount)
        return CardChargeResult(script[min(amounts.count - 1, script.count - 1)], reference: reference)
    }
}

private actor FakeStripe {
    private var polls: [String]
    private(set) var calls: [String] = []

    init(readerStates: [String]) { polls = readerStates }

    func handle(_ request: URLRequest) -> (Data, HTTPURLResponse) {
        let path = request.url!.path
        calls.append("\(request.httpMethod ?? "") \(path)")
        let json: String
        if path.hasSuffix("/payment_intents") { json = #"{"id":"pi_1"}"# }
        else if request.httpMethod == "GET" { json = polls.count > 1 ? polls.removeFirst() : polls[0] }
        else { json = "{}" }
        return (Data(json.utf8), HTTPURLResponse(url: request.url!, statusCode: 200, httpVersion: nil, headerFields: nil)!)
    }
}

@Suite("LocalPosAPI : paiement par TPE")
struct LocalCardPaymentTests {
    private static let declined = APIError.server(status: 402, message: "Carte refusée.")

    private func seat(_ api: LocalPosAPI) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", items: [localInput(burger, quantity: 1)])
        return try #require(try await api.activeOrder(table: "T1"))
    }

    @Test func declinedCardLeavesTheTicketOpenThenRetrySettlesIt() async throws {
        let api = try await makeLocalAPI()
        let terminal = ScriptedTerminal(.declined, .approved)
        await api.setCardTerminal(terminal)
        let order = try await seat(api)

        await #expect(throws: Self.declined) { try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1950)])) }
        #expect(try await api.activeOrder(table: "T1") != nil)

        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1950)]))
        #expect(result.receiptNumber == "T01-000001" && result.remainingBalance == .zero)
        #expect(try await api.activeOrder(table: "T1") == nil)
        #expect(await terminal.amounts == [Money(cents: 1950), Money(cents: 1950)])
    }

    @Test func unavailableTerminalWritesNothing() async throws {
        let api = try await makeLocalAPI()
        await api.setCardTerminal(ScriptedTerminal(.unavailable))
        let order = try await seat(api)

        await #expect(throws: APIError.server(status: 402, message: "Terminal de paiement injoignable.")) {
            try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1950)]))
        }
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        #expect(result.receiptNumber == "T01-000001")
    }

    @Test func cashNeverCallsTheTerminalAndAnOverpayingCardIsRefusedBeforeCharging() async throws {
        let api = try await makeLocalAPI()
        let terminal = ScriptedTerminal(.approved)
        await api.setCardTerminal(terminal)
        let order = try await seat(api)

        await #expect(throws: APIError.server(status: 400, message: "Échec de l'encaissement.")) {
            try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 2500)]))
        }
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        #expect(await terminal.amounts.isEmpty)
    }

    @Test func stripeWaitsForTheReaderThenApproves() async throws {
        let fake = FakeStripe(readerStates: [#"{"action":{"status":"in_progress"}}"#, #"{"action":{"status":"succeeded"}}"#])
        let stripe = StripeCardTerminal(secretKey: "rk_test", readerId: "tmr_1", pollInterval: .milliseconds(1)) { try await fake.handle($0) }

        let result = try await stripe.charge(amount: Money(cents: 1250), reference: "ref")

        #expect(result == CardChargeResult(.approved, reference: "pi_1"))
        #expect(await fake.calls.prefix(2) == ["POST /v1/payment_intents", "POST /v1/terminal/readers/tmr_1/process_payment_intent"])
    }

    @Test func onlyRestrictedKeysAndReaderIdsAreAccepted() {
        #expect(StripeReaderSettings(secretKey: "rk_test_51abcdef", readerId: "tmr_FDOt2wlRZEdpd7").isValid)
        #expect(!StripeReaderSettings(secretKey: "sk_live_51abcdef", readerId: "tmr_FDOt2wlRZEdpd7").isValid)
        #expect(!StripeReaderSettings(secretKey: "rk_test_51abcdef", readerId: "reader1").isValid)
    }

    @Test func stripeFailedActionIsDeclinedWithItsReason() async throws {
        let fake = FakeStripe(readerStates: [#"{"action":{"status":"failed","failure_code":"card_declined"}}"#])
        let stripe = StripeCardTerminal(secretKey: "rk_test", readerId: "tmr_1", pollInterval: .milliseconds(1)) { try await fake.handle($0) }

        let result = try await stripe.charge(amount: Money(cents: 1250), reference: "ref")

        #expect(result.status == .declined && result.reason == "card_declined")
    }

    @Test func stripeCancelAsksTheReaderToCancel() async throws {
        let fake = FakeStripe(readerStates: [#"{"action":{"status":"in_progress"}}"#])
        let stripe = StripeCardTerminal(secretKey: "rk_test", readerId: "tmr_1", pollInterval: .milliseconds(5)) { try await fake.handle($0) }

        let task = Task { try await stripe.charge(amount: Money(cents: 1250), reference: "ref") }
        try await Task.sleep(for: .milliseconds(60))
        task.cancel()
        _ = try? await task.value

        #expect(await fake.calls.contains("POST /v1/terminal/readers/tmr_1/cancel_action"))
    }
}
