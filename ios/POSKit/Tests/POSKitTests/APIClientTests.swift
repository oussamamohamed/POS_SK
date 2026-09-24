import Foundation
import Testing
@testable import POSKit

@Suite("Client HTTP")
struct APIClientTests {
    let base = URL(string: "http://192.168.1.20:5000")!

    @Test func normalizesServerAddresses() {
        #expect(POSAPIClient.normalizedURL(from: " 192.168.1.20:5000/ ")?.absoluteString == "http://192.168.1.20:5000")
        #expect(POSAPIClient.normalizedURL(from: "https://caisse.local")?.absoluteString == "https://caisse.local")
        #expect(POSAPIClient.normalizedURL(from: "") == nil)
        #expect(POSAPIClient.normalizedURL(from: "http://") == nil)
    }

    @Test func loginStoresBearerTokenForNextCalls() async throws {
        let stub = StubTransport()
        try stub.on("POST /api/auth/login", fixture: "login")
        try stub.on("GET /api/tables/T2/order", fixture: "order_after_add")
        let client = POSAPIClient(baseURL: base, transport: stub)

        let session = try await client.login(pin: "1234")
        #expect(session.operator.role == .floorManager)
        #expect(stub.last?.body?["pin"] as? String == "1234")

        _ = try await client.activeOrder(tableNumber: "T2")
        #expect(stub.last?.headers["Authorization"] == "Bearer test-token")

        await client.logout()
        _ = try await client.activeOrder(tableNumber: "T2")
        #expect(stub.last?.headers["Authorization"] == nil)
    }

    @Test func wrongPinSurfacesServerMessage() async throws {
        let stub = StubTransport()
        try stub.on("POST /api/auth/login", status: 400, fixture: "login_failure")
        let client = POSAPIClient(baseURL: base, transport: stub)
        await #expect(throws: APIError.forbidden("Code PIN ou identifiants incorrects")) {
            try await client.login(pin: "0000")
        }
    }

    @Test func rateLimitIsReported() async throws {
        let stub = StubTransport()
        stub.on("POST /api/auth/login", status: 429, json: #"{"success":false,"errorMessage":"Trop de tentatives infructueuses. Veuillez patienter 30 secondes."}"#)
        let client = POSAPIClient(baseURL: base, transport: stub)
        await #expect(throws: APIError.rateLimited("Trop de tentatives infructueuses. Veuillez patienter 30 secondes.")) {
            try await client.login(pin: "1111")
        }
    }

    @Test func missingActiveOrderIsNil() async throws {
        let stub = StubTransport()
        stub.on("GET /api/tables/T5/order", status: 404, json: #"{"message":"Aucune commande active sur la table T5"}"#)
        let client = POSAPIClient(baseURL: base, transport: stub)
        #expect(try await client.activeOrder(tableNumber: "T5") == nil)
    }

    @Test func expiredTokenIsUnauthorized() async {
        let stub = StubTransport()
        stub.on("POST /api/tables/T2/dispatch", status: 401, data: Data())
        let client = POSAPIClient(baseURL: base, transport: stub, token: "old")
        await #expect(throws: APIError.unauthorized) {
            try await client.dispatch(tableNumber: "T2")
        }
    }

    @Test func networkFailureIsExplicit() async {
        let client = POSAPIClient(baseURL: base, transport: FailingTransport())
        await #expect {
            try await client.tables()
        } throws: { error in
            guard case APIError.network = error else { return false }
            return (error as? LocalizedError)?.errorDescription?.contains("injoignable") == true
        }
    }

    @Test func addItemsSendsIntegersForEnumsAndEurosForAmounts() async throws {
        let stub = StubTransport()
        try stub.on("POST /api/tables/T2/items", fixture: "order_after_add")
        let client = POSAPIClient(baseURL: base, transport: stub)
        let burger = InMemoryPOSBackend.defaultProducts.first { $0.name.hasPrefix("Burger") }!
        let line = PendingLine(product: burger, quantity: 2, modifiers: [burger.modifierGroups[1].options[0]], course: .suite)

        let order = try await client.addItems(tableNumber: "T2", items: [line.input])
        #expect(order.lines.count == 2)

        let sent = try #require((stub.last?.body?["items"] as? [[String: Any]])?.first)
        #expect(sent["course"] as? Int == 1)
        #expect(sent["unitPrice"] as? Double == 19.5)
        #expect(sent["modifiersPriceExtra"] as? Double == 1.5)
        #expect(sent["quantity"] as? Int == 2)
        #expect(sent["preparationStationId"] as? String == "HOT_KITCHEN")
        #expect(sent["modifiers"] as? [String] == ["Bacon"])
        #expect((sent["productId"] as? String)?.uppercased() == burger.id.uuidString)
        #expect(stub.last?.headers["Content-Type"] == "application/json")
    }

    @Test func tableNumbersArePercentEncoded() async throws {
        let stub = StubTransport()
        stub.on("GET /api/tables/Terrasse 1/order", status: 404, json: "{}")
        let client = POSAPIClient(baseURL: base, transport: stub)
        _ = try await client.activeOrder(tableNumber: "Terrasse 1")
        #expect(stub.last?.path == "/api/tables/Terrasse 1/order")
    }

    @Test func paymentPayload() async throws {
        let stub = StubTransport()
        try stub.on("POST /api/checkout/pay", fixture: "pay_final")
        let client = POSAPIClient(baseURL: base, transport: stub)
        let orderId = UUID()
        let result = try await client.pay(PaymentSettlementRequest(
            orderId: orderId,
            tableNumber: "T2",
            operatorId: nil,
            tenders: [TenderItem(method: .cash, amount: Money(cents: 3233), tendered: Money(cents: 5000), changeGiven: Money(cents: 1767))],
            terminalId: "IPAD_01"
        ))
        #expect(result.changeGiven.cents == 1767)
        let body = try #require(stub.last?.body)
        #expect(body["terminalId"] as? String == "IPAD_01")
        #expect(body["tableNumber"] as? String == "T2")
        let tender = try #require((body["tenders"] as? [[String: Any]])?.first)
        #expect(tender["method"] as? Int == 0)
        #expect(tender["amount"] as? Double == 32.33)
        #expect(tender["tendered"] as? Double == 50)
        #expect(tender["changeGiven"] as? Double == 17.67)
    }

    @Test func counterEndpoints() async throws {
        let stub = StubTransport()
        try stub.on("POST /api/orders/counter/direct", fixture: "counter_open")
        try stub.on("POST /api/orders/counter/hold", fixture: "held_hold")
        try stub.on("GET /api/orders/counter/held", fixture: "held_list")
        try stub.on("POST /api/orders/counter/checkout", fixture: "counter_checkout")
        let client = POSAPIClient(baseURL: base, transport: stub)

        let order = try await client.openCounterOrder(terminalId: "IPAD_01", destination: .eatIn)
        #expect(stub.last?.body?["destination"] as? Int == 1)

        _ = try await client.holdCounterOrder(orderId: order.orderId, terminalId: "IPAD_01", label: "Marc")
        #expect(stub.last?.body?["customerLabel"] as? String == "Marc")

        _ = try await client.heldOrders(terminalId: "IPAD 01")
        #expect(stub.last?.query == "terminalId=IPAD%2001")

        let result = try await client.counterCheckout(CounterCheckoutRequest(
            orderId: order.orderId,
            terminalId: "IPAD_01",
            destination: .takeaway,
            pickupBuzzer: "12",
            mealVoucherPolicy: .customerCreditVoucher,
            tenders: [CounterTender(method: .mealVoucher, amount: Money(cents: 1250), tendered: Money(cents: 1500), facialValue: Money(cents: 1500))]
        ))
        #expect(result.pickupNumber == "#A-01")
        let body = try #require(stub.last?.body)
        #expect(body["mealVoucherPolicy"] as? Int == 2)
        #expect(body["pickupBuzzer"] as? String == "12")
        let tender = try #require((body["tenders"] as? [[String: Any]])?.first)
        #expect(tender["method"] as? Int == 2)
        #expect(tender["facialValue"] as? Double == 15)
    }

    @Test func fiscalEndpoints() async throws {
        let stub = StubTransport()
        try stub.on("GET /api/fiscal/x-report", fixture: "fiscal_x_report")
        stub.on("GET /api/fiscal/latest-closure", status: 404, json: #"{"message":"Aucune clôture trouvée."}"#)
        try stub.on("POST /api/fiscal/z-closure", fixture: "fiscal_z_closure")
        let client = POSAPIClient(baseURL: base, transport: stub)

        #expect(try await client.xReport(terminalId: "IPAD_01").receiptCount == 3)
        #expect(try await client.latestClosure(terminalId: "IPAD_01") == nil)
        let manager = Operator(id: UUID(), name: "Alexandre", role: .floorManager)
        let closure = try await client.zClosure(terminalId: "IPAD_01", manager: manager)
        #expect(closure.closureSequence == 1)
        #expect(stub.last?.body?["managerName"] as? String == "Alexandre")
    }

    @Test func forbiddenFiscalAccessForWaiters() async {
        let stub = StubTransport()
        stub.on("GET /api/fiscal/x-report", status: 403, data: Data())
        let client = POSAPIClient(baseURL: base, transport: stub)
        await #expect(throws: APIError.forbidden(nil)) {
            try await client.xReport(terminalId: "IPAD_01")
        }
    }

    @Test func kitchenBumpDecodesDto() async throws {
        let stub = StubTransport()
        let id = try Fixture.decode([KitchenTicket].self, "kds_tickets")[0].id
        try stub.on("POST /api/kds/tickets/\(id.uuidString.lowercased())/bump", fixture: "kds_bump")
        let client = POSAPIClient(baseURL: base, transport: stub)
        let ticket = try await client.bumpTicket(id: id)
        #expect(ticket.status == .inPreparation)
    }
}
