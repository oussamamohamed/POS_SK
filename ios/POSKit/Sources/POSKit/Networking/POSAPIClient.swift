import Foundation
#if canImport(FoundationNetworking)
import FoundationNetworking
#endif

/// Transport HTTP minimal, injectable pour les tests.
public protocol HTTPTransport: Sendable {
    func send(_ request: URLRequest) async throws -> (Data, Int)
}

public struct URLSessionTransport: HTTPTransport {
    private let session: URLSession

    public init(timeout: TimeInterval = 10) {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = timeout
        #if !canImport(FoundationNetworking)
        configuration.waitsForConnectivity = false
        #endif
        // L'API pose aussi le JWT en cookie : on l'ignore pour qu'un verrouillage
        // de session coupe réellement l'accès (seul l'en-tête Bearer fait foi).
        configuration.httpCookieStorage = nil
        configuration.httpShouldSetCookies = false
        session = URLSession(configuration: configuration)
    }

    public func send(_ request: URLRequest) async throws -> (Data, Int) {
        let (data, response) = try await session.data(for: request)
        return (data, (response as? HTTPURLResponse)?.statusCode ?? 0)
    }
}

/// Client HTTP de l'API ASP.NET Core `RestaurantPos.Api`.
public actor POSAPIClient: POSAPI {
    public let baseURL: URL
    private let transport: HTTPTransport
    private var token: String?

    public init(baseURL: URL, transport: HTTPTransport = URLSessionTransport(), token: String? = nil) {
        self.baseURL = baseURL
        self.transport = transport
        self.token = token
    }

    /// Normalise une saisie utilisateur (`192.168.1.10:5000` → `http://192.168.1.10:5000`).
    public static func normalizedURL(from text: String) -> URL? {
        var value = text.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !value.isEmpty else { return nil }
        if !value.lowercased().hasPrefix("http://") && !value.lowercased().hasPrefix("https://") {
            value = "http://" + value
        }
        while value.hasSuffix("/") { value.removeLast() }
        guard let url = URL(string: value), url.host != nil else { return nil }
        return url
    }

    // MARK: Plomberie HTTP

    private struct Empty: Encodable {}

    private func url(_ path: String, query: [String: String] = [:]) throws -> URL {
        guard var components = URLComponents(url: baseURL, resolvingAgainstBaseURL: false) else {
            throw APIError.invalidConfiguration("Adresse du serveur invalide.")
        }
        // `path` contient des segments déjà encodés (cf. `pathComponent`) : on évite le double encodage.
        let basePath = components.percentEncodedPath.hasSuffix("/") ? String(components.percentEncodedPath.dropLast()) : components.percentEncodedPath
        components.percentEncodedPath = basePath + path
        if !query.isEmpty {
            components.queryItems = query.sorted { $0.key < $1.key }.map { URLQueryItem(name: $0.key, value: $0.value) }
        }
        guard let url = components.url else {
            throw APIError.invalidConfiguration("Adresse du serveur invalide.")
        }
        return url
    }

    private func perform(
        _ method: String,
        _ path: String,
        query: [String: String] = [:],
        body: (any Encodable)? = nil
    ) async throws -> (Data, Int) {
        var request = URLRequest(url: try url(path, query: query))
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let token {
            request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        }
        if let body {
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
            request.httpBody = try POSJSON.encoder().encode(body)
        }

        let data: Data
        let status: Int
        do {
            (data, status) = try await transport.send(request)
        } catch let error as APIError {
            throw error
        } catch {
            throw APIError.network(error.localizedDescription)
        }
        return (data, status)
    }

    private func check(_ data: Data, _ status: Int) throws {
        switch status {
        case 200..<300:
            return
        case 401:
            throw APIError.unauthorized
        case 403:
            throw APIError.forbidden(APIError.message(from: data))
        case 404:
            throw APIError.notFound(APIError.message(from: data))
        case 429:
            throw APIError.rateLimited(APIError.message(from: data))
        default:
            throw APIError.server(status: status, message: APIError.message(from: data))
        }
    }

    private func decode<T: Decodable>(_ type: T.Type, from data: Data) throws -> T {
        do {
            return try POSJSON.decoder().decode(type, from: data)
        } catch {
            throw APIError.decoding(String(describing: error))
        }
    }

    private func request<T: Decodable>(
        _ type: T.Type,
        _ method: String,
        _ path: String,
        query: [String: String] = [:],
        body: (any Encodable)? = nil
    ) async throws -> T {
        let (data, status) = try await perform(method, path, query: query, body: body)
        try check(data, status)
        return try decode(type, from: data)
    }

    private func send(_ method: String, _ path: String, query: [String: String] = [:], body: (any Encodable)? = nil) async throws {
        let (data, status) = try await perform(method, path, query: query, body: body)
        try check(data, status)
    }

    private static func pathComponent(_ value: String) -> String {
        value.addingPercentEncoding(withAllowedCharacters: .urlPathAllowed.subtracting(CharacterSet(charactersIn: "/;?#%"))) ?? value
    }

    // MARK: POSAPI

    public func health() async throws -> Bool {
        let response = try await request(HealthResponse.self, "GET", "/api/health")
        return (response.status ?? "").caseInsensitiveCompare("Healthy") == .orderedSame
    }

    public func login(pin: String) async throws -> OperatorSession {
        let (data, status) = try await perform("POST", "/api/auth/login", body: PinLoginRequest(pin: pin))
        if status == 400 || status == 401 {
            throw APIError.forbidden(APIError.message(from: data) ?? "Code PIN invalide.")
        }
        try check(data, status)
        let response = try decode(LoginResponse.self, from: data)
        guard let token = response.token, let id = response.operatorId else {
            throw APIError.forbidden(APIError.message(from: data) ?? "Code PIN invalide.")
        }
        self.token = token
        let op = Operator(id: id, name: response.operatorName ?? "Opérateur", role: response.role ?? .waiter)
        return OperatorSession(operator: op, token: token)
    }

    public func logout() async {
        token = nil
    }

    public func categories() async throws -> [MenuCategory] {
        try await request([MenuCategory].self, "GET", "/api/catalog/categories")
    }

    public func products() async throws -> [Product] {
        try await request([Product].self, "GET", "/api/catalog/products")
    }

    public func tables() async throws -> [DiningTable] {
        try await request([DiningTable].self, "GET", "/api/tables")
    }

    public func openTable(_ tableNumber: String, covers: Int, operator op: Operator) async throws -> DiningTable {
        try await request(
            DiningTable.self, "POST", "/api/tables/\(Self.pathComponent(tableNumber))/open",
            body: OpenTableRequest(waiterName: op.name, coversCount: covers, operatorId: op.id)
        )
    }

    public func activeOrder(tableNumber: String) async throws -> ActiveOrder? {
        do {
            return try await request(ActiveOrder.self, "GET", "/api/tables/\(Self.pathComponent(tableNumber))/order")
        } catch APIError.notFound {
            return nil
        }
    }

    public func addItems(tableNumber: String, items: [OrderItemInput]) async throws -> ActiveOrder {
        try await request(
            ActiveOrder.self, "POST", "/api/tables/\(Self.pathComponent(tableNumber))/items",
            body: AddOrderItemsRequest(items: items)
        )
    }

    public func dispatch(tableNumber: String) async throws {
        try await send("POST", "/api/tables/\(Self.pathComponent(tableNumber))/dispatch")
    }

    public func pay(_ settlement: PaymentSettlementRequest) async throws -> PaymentResult {
        try await request(PaymentResult.self, "POST", "/api/checkout/pay", body: settlement)
    }

    public func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder {
        try await request(
            ActiveOrder.self, "POST", "/api/orders/counter/direct",
            body: DirectCounterOpenRequest(terminalId: terminalId, destination: destination)
        )
    }

    public func switchDestination(orderId: UUID, to destination: OrderDestination) async throws -> ActiveOrder {
        try await request(
            ActiveOrder.self, "POST", "/api/orders/\(orderId.uuidString.lowercased())/destination",
            body: SwitchDestinationRequest(destination: destination)
        )
    }

    public func holdCounterOrder(orderId: UUID, terminalId: String, label: String?) async throws -> HeldOrder {
        try await request(
            HeldOrder.self, "POST", "/api/orders/counter/hold",
            body: HoldCounterOrderRequest(orderId: orderId, terminalId: terminalId, staffId: nil, customerLabel: label)
        )
    }

    public func heldOrders(terminalId: String) async throws -> [HeldOrder] {
        try await request([HeldOrder].self, "GET", "/api/orders/counter/held", query: ["terminalId": terminalId])
    }

    public func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder {
        try await request(ActiveOrder.self, "POST", "/api/orders/counter/held/\(holdId.uuidString.lowercased())/recall")
    }

    public func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws {
        try await send(
            "POST", "/api/orders/counter/held/\(holdId.uuidString.lowercased())/void",
            body: VoidHeldOrderRequest(supervisorPin: supervisorPin, voidReason: reason, terminalId: terminalId)
        )
    }

    public func counterCheckout(_ checkout: CounterCheckoutRequest) async throws -> CounterCheckoutResult {
        try await request(CounterCheckoutResult.self, "POST", "/api/orders/counter/checkout", body: checkout)
    }

    public func kitchenTickets() async throws -> [KitchenTicket] {
        try await request([KitchenTicket].self, "GET", "/api/kds/tickets")
    }

    public func bumpTicket(id: UUID) async throws -> KitchenTicket {
        try await request(KitchenTicket.self, "POST", "/api/kds/tickets/\(id.uuidString.lowercased())/bump")
    }

    public func xReport(terminalId: String) async throws -> FiscalReport {
        try await request(FiscalReport.self, "GET", "/api/fiscal/x-report", query: ["terminalId": terminalId])
    }

    public func latestClosure(terminalId: String) async throws -> FiscalReport? {
        do {
            return try await request(FiscalReport.self, "GET", "/api/fiscal/latest-closure", query: ["terminalId": terminalId])
        } catch APIError.notFound {
            return nil
        }
    }

    public func zClosure(terminalId: String, manager: Operator) async throws -> FiscalReport {
        try await request(
            FiscalReport.self, "POST", "/api/fiscal/z-closure",
            body: ZClosureRequest(terminalId: terminalId, managerId: manager.id, managerName: manager.name)
        )
    }
}
