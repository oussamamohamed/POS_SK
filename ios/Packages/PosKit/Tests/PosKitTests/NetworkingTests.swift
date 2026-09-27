import Foundation
import Testing
@testable import PosKit

/// Intercepte les requêtes `URLSession` pour vérifier méthode, chemin, en-têtes et corps.
final class StubURLProtocol: URLProtocol, @unchecked Sendable {
    struct Response { var status: Int; var body: String; var headers: [String: String] = [:] }
    nonisolated(unsafe) static var handler: ((URLRequest) -> Response)?
    nonisolated(unsafe) static var captured: [URLRequest] = []
    static let lock = NSLock()

    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }

    override func startLoading() {
        var request = self.request
        if request.httpBody == nil, let stream = request.httpBodyStream {
            stream.open()
            var data = Data()
            var buffer = [UInt8](repeating: 0, count: 4096)
            while stream.hasBytesAvailable {
                let read = stream.read(&buffer, maxLength: buffer.count)
                if read <= 0 { break }
                data.append(buffer, count: read)
            }
            stream.close()
            request.httpBody = data
        }
        let response = Self.lock.withLock {
            Self.captured.append(request)
            return Self.handler?(request) ?? Response(status: 200, body: "{}")
        }
        let http = HTTPURLResponse(url: request.url!, statusCode: response.status, httpVersion: nil, headerFields: response.headers.merging(["Content-Type": "application/json"]) { a, _ in a })!
        client?.urlProtocol(self, didReceive: http, cacheStoragePolicy: .notAllowed)
        client?.urlProtocol(self, didLoad: Data(response.body.utf8))
        client?.urlProtocolDidFinishLoading(self)
    }

    override func stopLoading() {}
}

@Suite("HTTPPosAPI — requêtes émises", .serialized)
struct HTTPPosAPITests {
    func makeAPI(_ handler: @escaping (URLRequest) -> StubURLProtocol.Response) -> HTTPPosAPI {
        StubURLProtocol.lock.withLock {
            StubURLProtocol.handler = handler
            StubURLProtocol.captured = []
        }
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [StubURLProtocol.self]
        return HTTPPosAPI(baseURL: URL(string: "http://pos.local:5080")!, session: URLSession(configuration: config))
    }

    var last: URLRequest { StubURLProtocol.lock.withLock { StubURLProtocol.captured.last! } }

    func body(_ request: URLRequest) throws -> [String: Any] {
        try #require(try JSONSerialization.jsonObject(with: request.httpBody ?? Data()) as? [String: Any])
    }

    @Test func loginStoresTokenAndSendsItAfterwards() async throws {
        let api = makeAPI { request in
            if request.url!.path == "/api/auth/login" {
                return .init(status: 200, body: #"{"success":true,"operatorId":"01a0d511-8720-7f5d-81ce-0844b9372ef1","operatorName":"Alex","role":"FloorManager","token":"jwt-123"}"#)
            }
            return .init(status: 200, body: "[]")
        }
        let login = try await api.login(pin: "1234")
        #expect(login.success)
        #expect(try body(last)["pin"] as? String == "1234")
        _ = try await api.tables()
        #expect(last.value(forHTTPHeaderField: "Authorization") == "Bearer jwt-123")
    }

    @Test func invalidPinIsNotAnError() async throws {
        let api = makeAPI { _ in .init(status: 400, body: #"{"success":false,"errorMessage":"Code PIN ou identifiants incorrects"}"#) }
        let login = try await api.login(pin: "0000")
        #expect(!login.success)
        #expect(login.errorMessage == "Code PIN ou identifiants incorrects")
    }

    @Test func addItemsPayloadMatchesOrderItemInputDto() async throws {
        let api = makeAPI { _ in .init(status: 200, body: #"{"orderId":"01a0d512-74a3-755d-acbf-42fa8a59abce","tableNumber":"T1","lines":[],"destination":0}"#) }
        let line = CartLine(productId: UUID(), name: "Burger", unitPrice: Money(cents: 1950), taxRatePercent: 10, station: "HOT_KITCHEN", modifiers: ["À Point"], modifiersExtra: Money(cents: 250), course: .suite, quantity: 2)
        _ = try await api.addItems(table: "T1", items: [line.asInput])
        #expect(last.httpMethod == "POST")
        #expect(last.url?.path == "/api/tables/T1/items")
        let items = try #require(try body(last)["items"] as? [[String: Any]])
        #expect(items[0]["quantity"] as? Int == 2)
        #expect(items[0]["unitPrice"] as? Double == 19.5)
        #expect(items[0]["modifiersPriceExtra"] as? Double == 2.5)
        #expect(items[0]["course"] as? Int == 1)
        #expect(items[0]["modifiers"] as? [String] == ["À Point"])
    }

    @Test func destinationUsesServerEnumValues() async throws {
        let api = makeAPI { _ in .init(status: 200, body: "{}") }
        try await api.setDestination(orderId: UUID(), destination: .eatIn)
        #expect(try body(last)["destination"] as? Int == 1)
    }

    @Test func activeOrderNotFoundReturnsNil() async throws {
        let api = makeAPI { _ in .init(status: 404, body: #"{"message":"Aucune commande active sur la table T9"}"#) }
        #expect(try await api.activeOrder(table: "T9") == nil)
    }

    @Test func counterNameIsEncodedInPath() async throws {
        let api = makeAPI { _ in .init(status: 200, body: "{}") }
        try await api.dispatch(table: "Terrasse 2")
        #expect(last.url?.absoluteString.contains("Terrasse%202") == true)
    }

    @Test(arguments: [(401, "unauthorized"), (403, "forbidden"), (429, "rateLimited"), (500, "server")])
    func mapsHTTPErrors(status: Int, expected: String) async {
        let api = makeAPI { _ in .init(status: status, body: #"{"message":"Refusé"}"#) }
        do {
            _ = try await api.kitchenTickets()
            Issue.record("Une erreur était attendue")
        } catch let error as APIError {
            #expect(String(describing: error).hasPrefix(expected))
        } catch {
            Issue.record("Erreur inattendue \(error)")
        }
    }

    @Test func staffAndPrinterRoutesMatchServer() async throws {
        let api = makeAPI { _ in .init(status: 200, body: "{}") }
        try await api.createStaff(name: "Léa", role: .waiter, pin: "4321")
        #expect(last.url?.path == "/api/staff")
        #expect(try body(last)["role"] as? String == "Waiter")
        let printer = Printer(name: "Bar", ipAddress: "10.0.0.2", openCashDrawerOnReceipt: true, assignedStationIds: ["BAR"])
        try await api.savePrinter(printer, isNew: true)
        #expect(try body(last)["assignedStationIds"] as? [String] == ["BAR"])
        #expect(try body(last)["openCashDrawerOnReceipt"] as? Bool == true)
        try await api.savePrinter(printer, isNew: false)
        #expect(last.httpMethod == "PUT")
        #expect(try body(last)["targetStations"] as? [String] == ["BAR"])
    }

    @Test func fecExportUsesContentDispositionFileName() async throws {
        let api = makeAPI { _ in .init(status: 200, body: "JournalCode|JournalLib", headers: ["Content-Disposition": "attachment; filename=123456789FEC20260924.txt; filename*=UTF-8''x"]) }
        let result = try await api.exportFec(from: Date(timeIntervalSince1970: 0), to: Date(), siren: "123456789")
        #expect(result.fileName == "123456789FEC20260924.txt")
        #expect(last.url?.query?.contains("siren=123456789") == true)
    }
}

@Suite("SignalR")
struct SignalRTests {
    @Test func parsesInvocationsAndPings() {
        let frame = "{}\u{1e}{\"type\":1,\"target\":\"ReceiveKitchenUpdate\",\"arguments\":[\"SUITE_CLAIMED\",\"T1\"]}\u{1e}{\"type\":6}\u{1e}"
        let messages = SignalRMessage.parse(frame: frame)
        #expect(messages == [
            .invocation(target: "ReceiveKitchenUpdate", arguments: [.string("SUITE_CLAIMED"), .string("T1")]),
            .ping,
        ])
    }

    @Test func mapsHubEventsToAppEvents() throws {
        let status = #"{"type":1,"target":"OnHappyHourStatusChanged","arguments":[{"isActive":true,"isOverride":false,"activeScheduleName":"Afterwork","activeScheduleId":null,"appliesToTakeaway":false,"currentWindow":{"startTime":"17:00","endTime":"20:00","remainingMinutes":42},"overrideDetails":null}]}"# + "\u{1e}"
        guard case let .invocation(target, arguments)? = SignalRMessage.parse(frame: status).first else {
            Issue.record("Invocation attendue"); return
        }
        let event = RealtimeEvent.from(target: target, arguments: arguments)
        guard case let .happyHourChanged(decoded)? = event else { Issue.record("Événement Happy Hour attendu"); return }
        #expect(decoded?.currentWindow?.remainingMinutes == 42)
        #expect(RealtimeEvent.from(target: "OnNewTicketReceived", arguments: []) == .kitchenChanged)
        #expect(RealtimeEvent.from(target: "Unknown", arguments: []) == nil)
    }

    @Test func closeMessage() {
        #expect(SignalRMessage.parse(frame: "{\"type\":7,\"error\":\"bye\"}\u{1e}") == [.close(error: "bye")])
    }
}

/// Tests de contrat contre une vraie API (désactivés par défaut).
/// Lancer avec : `POS_API_URL=http://localhost:5080 swift test --filter LiveAPI`
@Suite("LiveAPI", .enabled(if: ProcessInfo.processInfo.environment["POS_API_URL"] != nil), .serialized)
struct LiveAPITests {
    let api = HTTPPosAPI(baseURL: URL(string: ProcessInfo.processInfo.environment["POS_API_URL"] ?? "http://localhost:5080")!)

    @Test func endToEndTableFlow() async throws {
        let login = try await api.login(pin: ProcessInfo.processInfo.environment["POS_API_PIN"] ?? "1234")
        try #require(login.success)
        let products = try await api.products()
        let product = try #require(products.first { !$0.hasModifiers })
        let tables = try await api.tables()
        let table = try #require(tables.first { $0.status == .free && !$0.isCounter })
        let line = CartLine(productId: product.id, name: product.name, unitPrice: product.price, taxRatePercent: product.taxRatePercent, station: product.station, quantity: 2)
        let order = try await api.addItems(table: table.tableNumber, items: [line.asInput])
        try await api.setDestination(orderId: order.orderId, destination: .eatIn)
        try await api.dispatch(table: table.tableNumber)
        let refreshed = try #require(try await api.activeOrder(table: table.tableNumber))
        #expect(refreshed.destination == .eatIn)
        #expect(refreshed.lines.allSatisfy { $0.isDispatched })
        let total = OrderMath.totals(lines: refreshed.lines.map(CartLine.init(serverLine:)), destination: .eatIn, discount: nil).totalTtc
        #expect(total == refreshed.totalTtcAmount)
        let paid = try await api.pay(PaymentRequest(orderId: order.orderId, tableNumber: table.tableNumber, operatorId: login.operatorId, terminalId: "IPAD_TEST", tenders: [TenderInput(method: .creditCard, amount: total, tendered: total, changeGiven: .zero)]))
        #expect(paid.remainingBalance == .zero)
        #expect(try await api.activeOrder(table: table.tableNumber) == nil)
    }

    @Test func readEndpointsDecode() async throws {
        _ = try await api.login(pin: ProcessInfo.processInfo.environment["POS_API_PIN"] ?? "1234")
        _ = try await api.categories()
        _ = try await api.kitchenTickets()
        _ = try await api.xReport(terminalId: "IPAD_TEST")
        _ = try await api.happyHourSchedules()
        _ = try await api.happyHourStatus(terminalId: "IPAD_TEST")
        _ = try await api.gridLayout(categoryId: CatalogStore.allCategoryId, page: 0)
        _ = try await api.printers()
        _ = try await api.staff()
        _ = try await api.hotelRooms()
        _ = try await api.heldOrders(terminalId: "IPAD_TEST")
        let interval = DashboardRange.today.interval()
        _ = try await api.dashboard(from: interval.from, to: interval.to)
    }
}
