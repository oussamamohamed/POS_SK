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
    func makeAPI(deviceToken: String? = nil, _ handler: @escaping (URLRequest) -> StubURLProtocol.Response) -> HTTPPosAPI {
        StubURLProtocol.lock.withLock {
            StubURLProtocol.handler = handler
            StubURLProtocol.captured = []
        }
        let config = URLSessionConfiguration.ephemeral
        config.protocolClasses = [StubURLProtocol.self]
        return HTTPPosAPI(baseURL: URL(string: "http://pos.local:5080")!, deviceToken: deviceToken, session: URLSession(configuration: config))
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

    @Test func paymentRequestEncodesTip() throws {
        let req = PaymentRequest(orderId: nil, tableNumber: "T5", operatorId: nil, terminalId: "T01", tenders: [], tipAmount: Money(cents: 250))
        let json = try #require(try JSONSerialization.jsonObject(with: JSONEncoder().encode(req)) as? [String: Any])
        #expect((json["tipAmount"] as? NSNumber)?.decimalValue == Decimal(string: "2.5"))
    }

    @Test func printerTextModeIsSentOnCreateAndUpdate() async throws {
        let api = makeAPI { _ in .init(status: 200, body: "{}") }
        let printer = Printer(name: "Bar", ipAddress: "10.0.0.2", textMode: true)
        try await api.savePrinter(printer, isNew: true)
        #expect(try body(last)["textMode"] as? Bool == true)
        try await api.savePrinter(printer, isNew: false)
        #expect(try body(last)["textMode"] as? Bool == true)
    }

    @Test func reportPrintRoutesReturnPrintQueued() async throws {
        let api = makeAPI { req in
            if req.url?.path == "/api/fiscal/x-report/print" {
                return .init(status: 200, body: #"{"printQueued":true}"#)
            } else {
                return .init(status: 200, body: #"{"printQueued":true,"duplicateNumber":1}"#)
            }
        }
        #expect(try await api.printXReport(terminalId: "T01"))
        #expect(last.httpMethod == "POST")
        #expect(last.url?.path == "/api/fiscal/x-report/print")
        #expect(last.url?.query == "terminalId=T01")
        let closureReprint = try await api.reprintLatestClosure(terminalId: "T01")
        #expect(closureReprint.printQueued == true)
        #expect(closureReprint.duplicateNumber == 1)
        #expect(last.url?.path == "/api/fiscal/latest-closure/print")
        let receiptReprint = try await api.reprintReceipt(receiptIdentifier: "REC-123")
        #expect(receiptReprint.printQueued == true)
        #expect(receiptReprint.duplicateNumber == 1)
        #expect(last.httpMethod == "POST")
        #expect(last.url?.path == "/api/checkout/receipts/REC-123/reprint")
    }

    @Test func fecExportUsesContentDispositionFileName() async throws {
        let api = makeAPI { _ in .init(status: 200, body: "JournalCode|JournalLib", headers: ["Content-Disposition": "attachment; filename=123456789FEC20260924.txt; filename*=UTF-8''x"]) }
        let result = try await api.exportFec(from: Date(timeIntervalSince1970: 0), to: Date(), siren: "123456789")
        #expect(result.fileName == "123456789FEC20260924.txt")
        #expect(last.url?.query?.contains("siren=123456789") == true)
    }

    @Test func fiscalArchivesNetworkingRoutes() async throws {
        let sampleArchiveJson = """
        [{
            "id": "11111111-1111-1111-1111-111111111111",
            "periodClosureId": "22222222-2222-2222-2222-222222222222",
            "periodType": "Monthly",
            "periodKey": "2026-08",
            "fileName": "ARCHIVE-M-2026-08-000001.zip",
            "fileSha256": "abc123def456",
            "fileSizeBytes": 1024,
            "archiveSequence": 1,
            "previousSignatureHash": "00000000",
            "signatureHash": "11111111",
            "createdByUserId": "33333333-3333-3333-3333-333333333333",
            "createdAtUtc": "2026-09-01T00:00:00Z"
        }]
        """

        let singleArchiveJson = """
        {
            "id": "11111111-1111-1111-1111-111111111111",
            "periodClosureId": "22222222-2222-2222-2222-222222222222",
            "periodType": "Monthly",
            "periodKey": "2026-08",
            "fileName": "ARCHIVE-M-2026-08-000001.zip",
            "fileSha256": "abc123def456",
            "fileSizeBytes": 1024,
            "archiveSequence": 1,
            "previousSignatureHash": "00000000",
            "signatureHash": "11111111",
            "createdByUserId": "33333333-3333-3333-3333-333333333333",
            "createdAtUtc": "2026-09-01T00:00:00Z"
        }
        """

        let verifyResultJson = """
        {
            "isValid": true,
            "archiveId": "11111111-1111-1111-1111-111111111111",
            "reason": null
        }
        """

        let api = makeAPI { req in
            if req.url?.path == "/api/fiscal/archives" && req.httpMethod == "GET" {
                return .init(status: 200, body: sampleArchiveJson)
            } else if req.url?.path == "/api/fiscal/archives" && req.httpMethod == "POST" {
                return .init(status: 200, body: singleArchiveJson)
            } else if req.url?.path == "/api/fiscal/archives/verify" && req.httpMethod == "POST" {
                return .init(status: 200, body: verifyResultJson)
            } else {
                return .init(status: 404, body: "{}")
            }
        }

        let archives = try await api.archives()
        #expect(archives.count == 1)
        #expect(archives[0].periodKey == "2026-08")
        #expect(archives[0].fileName == "ARCHIVE-M-2026-08-000001.zip")
        #expect(last.httpMethod == "GET")
        #expect(last.url?.path == "/api/fiscal/archives")

        let created = try await api.createArchive(periodClosureId: UUID(uuidString: "22222222-2222-2222-2222-222222222222")!)
        #expect(created.archiveSequence == 1)
        #expect(last.httpMethod == "POST")
        #expect(last.url?.path == "/api/fiscal/archives")

        let verify = try await api.verifyArchive(data: Data([1, 2, 3]), fileName: "test.zip")
        #expect(verify.isValid == true)
        #expect(verify.archiveId == UUID(uuidString: "11111111-1111-1111-1111-111111111111"))
        #expect(last.httpMethod == "POST")
        #expect(last.url?.path == "/api/fiscal/archives/verify")
        #expect(last.value(forHTTPHeaderField: "Content-Type")?.contains("multipart/form-data") == true)
    }

    @Test func deviceTokenHeaderIsSentOnEveryRequest() async throws {
        let api = makeAPI(deviceToken: "dev-123") { _ in .init(status: 200, body: "[]") }
        _ = try await api.tables()
        #expect(last.value(forHTTPHeaderField: "X-Device-Token") == "dev-123")
    }

    @Test func deviceNotPairedIsDistinctFromExpiredSession() async throws {
        let revoked = makeAPI { _ in .init(status: 401, body: #"{"code":"device_not_paired","message":"Ce poste n'est pas appairé au serveur."}"#) }
        await #expect(throws: APIError.deviceNotPaired) { _ = try await revoked.tables() }
        let expired = makeAPI { _ in .init(status: 401, body: "") }
        await #expect(throws: APIError.unauthorized) { _ = try await expired.tables() }
    }

    @Test func pairPostsCodeWithoutOperatorToken() async throws {
        let api = makeAPI { _ in .init(status: 200, body: #"{"deviceId":"01a0d511-8720-7f5d-81ce-0844b9372ef1","token":"tok","terminalId":"T03","name":"Caisse comptoir","role":"Caisse","serverName":"Serveur salle"}"#) }
        await api.setToken("jwt-old")
        let paired = try await api.pair(code: "ABCD2345")
        #expect(paired.terminalId == "T03")
        #expect(paired.serverName == "Serveur salle")
        #expect(last.httpMethod == "POST")
        #expect(last.url?.path == "/api/devices/pair")
        #expect(try body(last)["code"] as? String == "ABCD2345")
        #expect(last.value(forHTTPHeaderField: "Authorization") == nil)
    }

    @Test func invalidPairingCodeSurfacesServerMessage() async throws {
        let api = makeAPI { _ in .init(status: 400, body: #"{"code":"pairing_code_invalid","message":"Code invalide ou expiré"}"#) }
        await #expect(throws: APIError.server(status: 400, message: "Code invalide ou expiré")) { _ = try await api.pair(code: "WRONG") }
    }

    @Test func sendsAcceptLanguage() async throws {
        let api = makeAPI { _ in .init(status: 200, body: #"{"receiptLanguage":"fr"}"#) }
        _ = try await api.settings()
        let header = try #require(last.value(forHTTPHeaderField: "Accept-Language"))
        #expect(["en", "fr", "ar"].contains(header))
    }

    @Test func savesSettings() async throws {
        let api = makeAPI { _ in .init(status: 200, body: #"{"receiptLanguage":"ar"}"#) }
        let saved = try await api.saveSettings(RestaurantSettings(receiptLanguage: "ar"))
        #expect(last.httpMethod == "PUT")
        #expect(last.url?.path == "/api/settings")
        #expect(try body(last)["receiptLanguage"] as? String == "ar")
        #expect(saved.receiptLanguage == "ar")
    }

    @Test func categoryUpdateStationSemantics() async throws {
        let api = makeAPI { _ in .init(status: 200, body: "{}") }
        try await api.updateCategory(id: "C1", name: "N", colorHex: "#111111", displayOrder: 1, preparationStationId: nil)
        let unchanged = try body(last)
        #expect(unchanged["preparationStationId"] == nil || unchanged["preparationStationId"] is NSNull)
        try await api.updateCategory(id: "C1", name: "N", colorHex: "#111111", displayOrder: 1, preparationStationId: "")
        #expect(try body(last)["preparationStationId"] as? String == "")
        try await api.updateCategory(id: "C1", name: "N", colorHex: "#111111", displayOrder: 1, preparationStationId: "BAR")
        #expect(try body(last)["preparationStationId"] as? String == "BAR")
    }

    @Test func productWithoutStationSendsNoStation() async throws {
        let api = makeAPI { _ in .init(status: 200, body: "{}") }
        let product = Product(name: "X", categoryId: "C", price: Money(cents: 100), preparationStationId: nil)
        try await api.updateProduct(id: product.id, ProductDraft(product: product))
        let sent = try body(last)
        #expect(sent["stationId"] == nil || sent["stationId"] is NSNull)
        let own = Product(name: "Y", categoryId: "C", price: Money(cents: 100), preparationStationId: "BAR")
        try await api.updateProduct(id: own.id, ProductDraft(product: own))
        #expect(try body(last)["stationId"] as? String == "BAR")
    }
}

@Suite("Lien d'appairage (QR)")
struct PairingLinkTests {
    @Test func parsesBackOfficeQrPayload() throws {
        let link = try #require(PairingLink(string: "posdevice://pair?url=http%3A%2F%2F192.168.1.10%3A5080&code=ABCD2345"))
        #expect(link.serverURL.absoluteString == "http://192.168.1.10:5080")
        #expect(link.code == "ABCD2345")
    }

    @Test func rejectsForeignOrIncompletePayloads() {
        #expect(PairingLink(string: "https://example.com") == nil)
        #expect(PairingLink(string: "posdevice://pair?code=ABCD2345") == nil)
        #expect(PairingLink(string: "posdevice://pair?url=http%3A%2F%2F192.168.1.10%3A5080") == nil)
        #expect(PairingLink(string: "posdevice://pair?url=pas-une-url&code=ABCD2345") == nil)
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
    static let env = ProcessInfo.processInfo.environment
    static let baseURL = URL(string: env["POS_API_URL"] ?? "http://localhost:5080")!
    static let pin = env["POS_API_PIN"] ?? "1234"

    /// Crée un code avec le PIN gérant, appaire ce client de test, puis ouvre une session opérateur.
    static func pairedSession() async throws -> (api: HTTPPosAPI, login: LoginResponse, device: PairResponse) {
        let manager = HTTPPosAPI(baseURL: baseURL)
        let managerLogin = try await manager.login(pin: pin)
        var request = URLRequest(url: baseURL.appendingPathComponent("api/devices/pairing-codes"))
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        request.setValue("Bearer \(managerLogin.token ?? "")", forHTTPHeaderField: "Authorization")
        request.httpBody = Data(#"{"name":"iPad contrat","role":"Caisse"}"#.utf8)
        let (data, _) = try await URLSession.shared.data(for: request)
        let code = try #require((try JSONSerialization.jsonObject(with: data) as? [String: Any])?["code"] as? String)
        let device = try await manager.pair(code: code)
        let api = HTTPPosAPI(baseURL: baseURL, deviceToken: device.token)
        let login = try await api.login(pin: pin)
        try #require(login.success)
        return (api, login, device)
    }

    @Test func endToEndTableFlow() async throws {
        let (api, login, device) = try await Self.pairedSession()
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
        #expect(paid.receiptNumber?.hasPrefix("\(device.terminalId)-") == true)
        #expect(try await api.activeOrder(table: table.tableNumber) == nil)
    }

    @Test func readEndpointsDecode() async throws {
        let (api, _, _) = try await Self.pairedSession()
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

    @Test func realtimePrinterStatusEvent() {
        let id = "3f2504e0-4f89-11d3-9a0c-0305e82c3301"
        let e = RealtimeEvent.from(target: "OnPrinterStatusChanged", arguments: [.string(id), .string("Cuisine chaude"), .bool(false), .number(3)])
        #expect(e == .printerStatusChanged(printerId: UUID(uuidString: id), name: "Cuisine chaude", isOnline: false, pendingCount: 3))
    }

    @Test func paymentRequestEncodesReceiptFlag() throws {
        let req = PaymentRequest(orderId: nil, tableNumber: "T05", operatorId: nil, terminalId: "T01", tenders: [], requestReceiptPrint: true)
        let json = try #require(try JSONSerialization.jsonObject(with: JSONEncoder().encode(req)) as? [String: Any])
        #expect(json["requestReceiptPrint"] as? Bool == true)
    }

    @Test func cartLineWithoutProductStationSendsNil() throws {
        let line = CartLine(productId: UUID(), name: "Mojito", unitPrice: Money(cents: 800), taxRatePercent: 10)
        #expect(line.station == nil)
        #expect(line.asInput.preparationStationId == nil)
        let json = try #require(try JSONSerialization.jsonObject(with: JSONEncoder().encode(line.asInput)) as? [String: Any])
        #expect(json["preparationStationId"] == nil || json["preparationStationId"] is NSNull)
    }

    @Test func cartLineFromServerLineKeepsNilStation() {
        let product = Product(name: "X", categoryId: "C", price: Money(cents: 100), preparationStationId: nil)
        #expect(product.station == nil)
    }
}
