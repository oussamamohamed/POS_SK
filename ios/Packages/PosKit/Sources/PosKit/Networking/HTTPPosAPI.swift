import Foundation

/// Implémentation réseau du contrat `PosAPI` au-dessus de `URLSession`.
public actor HTTPPosAPI: PosAPI {
    public nonisolated let baseURL: URL
    private let session: URLSession
    private var token: String?
    private let decoder = PosJSON.makeDecoder()
    private let encoder = PosJSON.makeEncoder()

    public init(baseURL: URL, token: String? = nil, session: URLSession? = nil) {
        self.baseURL = baseURL
        self.token = token
        if let session {
            self.session = session
        } else {
            let config = URLSessionConfiguration.default
            config.timeoutIntervalForRequest = 10
            config.waitsForConnectivity = false
            config.httpCookieStorage = nil
            config.httpShouldSetCookies = false
            self.session = URLSession(configuration: config)
        }
    }

    public func setToken(_ token: String?) { self.token = token }

    // MARK: - Transport

    struct Empty: Codable {}

    private func makeRequest(_ method: String, _ path: String, query: [String: String] = [:], body: Data? = nil) throws -> URLRequest {
        var components = URLComponents(url: baseURL.appendingPathComponent("api").appendingPathComponent(path), resolvingAgainstBaseURL: false)!
        // `appendingPathComponent` encode déjà les segments ; on force l'encodage des espaces/accents.
        if !query.isEmpty {
            components.queryItems = query.sorted { $0.key < $1.key }.map { URLQueryItem(name: $0.key, value: $0.value) }
        }
        guard let url = components.url else { throw APIError.transport("URL invalide") }
        var request = URLRequest(url: url)
        request.httpMethod = method
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        if let body {
            request.httpBody = body
            request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        }
        if let token { request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization") }
        return request
    }

    private func send(_ request: URLRequest) async throws -> (Data, HTTPURLResponse) {
        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw APIError.transport(error.localizedDescription)
        }
        guard let http = response as? HTTPURLResponse else { throw APIError.transport("Réponse non HTTP") }
        guard (200..<300).contains(http.statusCode) else {
            let message = Self.extractMessage(from: data)
            switch http.statusCode {
            case 401: throw APIError.unauthorized
            case 403: throw APIError.forbidden(message)
            case 404: throw APIError.notFound(message)
            case 429: throw APIError.rateLimited(message)
            default: throw APIError.server(status: http.statusCode, message: message)
            }
        }
        return (data, http)
    }

    static func extractMessage(from data: Data) -> String? {
        guard let object = try? JSONSerialization.jsonObject(with: data) as? [String: Any] else { return nil }
        return (object["message"] ?? object["errorMessage"] ?? object["Message"]) as? String
    }

    private func call<Response: Decodable>(_ method: String, _ path: String, query: [String: String] = [:], as type: Response.Type = Response.self) async throws -> Response {
        let (data, _) = try await send(makeRequest(method, path, query: query))
        return try decode(data)
    }

    private func call<Body: Encodable, Response: Decodable>(_ method: String, _ path: String, body: Body, query: [String: String] = [:], as type: Response.Type = Response.self) async throws -> Response {
        let payload = try encoder.encode(body)
        let (data, _) = try await send(makeRequest(method, path, query: query, body: payload))
        return try decode(data)
    }

    private func perform(_ method: String, _ path: String, query: [String: String] = [:]) async throws {
        _ = try await send(makeRequest(method, path, query: query))
    }

    private func perform<Body: Encodable>(_ method: String, _ path: String, body: Body) async throws {
        _ = try await send(makeRequest(method, path, body: try encoder.encode(body)))
    }

    private func decode<T: Decodable>(_ data: Data) throws -> T {
        do {
            return try decoder.decode(T.self, from: data)
        } catch {
            throw APIError.decoding(String(describing: error))
        }
    }

    private static func iso(_ date: Date) -> String {
        let f = ISO8601DateFormatter()
        f.formatOptions = [.withInternetDateTime]
        return f.string(from: date)
    }

    // MARK: - Auth

    public func login(pin: String) async throws -> LoginResponse {
        var request = try makeRequest("POST", "auth/login", body: try encoder.encode(["pin": pin]))
        request.setValue(nil, forHTTPHeaderField: "Authorization")
        let data: Data
        let response: URLResponse
        do {
            (data, response) = try await session.data(for: request)
        } catch {
            throw APIError.transport(error.localizedDescription)
        }
        let status = (response as? HTTPURLResponse)?.statusCode ?? 0
        if status == 429 { throw APIError.rateLimited(Self.extractMessage(from: data)) }
        if status >= 500 { throw APIError.server(status: status, message: Self.extractMessage(from: data)) }
        let result: LoginResponse = try decode(data)
        if result.success { token = result.token }
        return result
    }

    // MARK: - Catalogue

    public func categories() async throws -> [MenuCategory] { try await call("GET", "catalog/categories") }
    public func products() async throws -> [Product] { try await call("GET", "catalog/products") }

    struct CategoryBody: Encodable { let name: String; let colorHex: String; let displayOrder: Int; let iconName: String?; let isActive: Bool? }

    public func createCategory(name: String, colorHex: String, displayOrder: Int) async throws {
        try await perform("POST", "catalog/categories", body: CategoryBody(name: name, colorHex: colorHex, displayOrder: displayOrder, iconName: "utensils", isActive: nil))
    }

    public func updateCategory(id: String, name: String, colorHex: String, displayOrder: Int) async throws {
        try await perform("PUT", "catalog/categories/\(id)", body: CategoryBody(name: name, colorHex: colorHex, displayOrder: displayOrder, iconName: nil, isActive: true))
    }

    struct ProductBody: Encodable {
        let name: String, categoryId: String, price: Money, taxRatePercent: Decimal, description: String, colorHex: String
        let displayOrder: Int, isQuickKey: Bool, stationId: String, isAvailable: Bool?, isActive: Bool?
        init(_ d: ProductDraft, update: Bool) {
            name = d.name; categoryId = d.categoryId; price = d.price; taxRatePercent = d.taxRatePercent
            description = d.description; colorHex = d.colorHex; displayOrder = d.displayOrder
            isQuickKey = d.isQuickKey; stationId = d.stationId
            isAvailable = update ? true : nil; isActive = update ? true : nil
        }
    }

    public func createProduct(_ draft: ProductDraft) async throws {
        try await perform("POST", "catalog/products", body: ProductBody(draft, update: false))
    }

    public func updateProduct(id: UUID, _ draft: ProductDraft) async throws {
        try await perform("PUT", "catalog/products/\(id.uuidString.lowercased())", body: ProductBody(draft, update: true))
    }

    public func archiveProduct(id: UUID) async throws {
        try await perform("DELETE", "catalog/products/\(id.uuidString.lowercased())")
    }

    // MARK: - Grille

    public func gridLayout(categoryId: String, page: Int) async throws -> TouchGridLayout {
        try await call("GET", "grid-layouts/\(categoryId)", query: ["page": "\(page)"])
    }

    public func saveGridLayout(_ request: UpdateGridLayoutRequest) async throws -> TouchGridLayout {
        try await call("POST", "grid-layouts", body: request)
    }

    struct SwapBody: Encodable { let layoutId: UUID; let sourceRow: Int; let sourceCol: Int; let targetRow: Int; let targetCol: Int }

    public func swapGridSlots(layoutId: UUID, from: GridPosition, to: GridPosition) async throws -> TouchGridLayout {
        try await call("POST", "grid-layouts/swap", body: SwapBody(layoutId: layoutId, sourceRow: from.row, sourceCol: from.column, targetRow: to.row, targetCol: to.column))
    }

    struct DimensionsBody: Encodable { let categoryId: String; let columnsCount: Int; let rowsCount: Int; let applyToAllCategories: Bool }

    public func updateGridDimensions(categoryId: String, columns: Int, rows: Int, applyToAll: Bool) async throws -> [TouchGridLayout] {
        try await call("POST", "grid-layouts/dimensions", body: DimensionsBody(categoryId: categoryId, columnsCount: columns, rowsCount: rows, applyToAllCategories: applyToAll))
    }

    // MARK: - Salle & commandes

    public func tables() async throws -> [DiningTable] { try await call("GET", "tables") }

    struct CreateTableBody: Encodable { let tableNumber: String; let capacity: Int }
    public func createTable(number: String, capacity: Int) async throws {
        try await perform("POST", "tables", body: CreateTableBody(tableNumber: number, capacity: capacity))
    }

    struct OpenTableBody: Encodable { let waiterName: String?; let coversCount: Int; let operatorId: UUID? }
    public func openTable(number: String, covers: Int, operatorId: UUID?, waiterName: String?) async throws {
        try await perform("POST", "tables/\(number)/open", body: OpenTableBody(waiterName: waiterName, coversCount: covers, operatorId: operatorId))
    }

    public func activeOrder(table: String) async throws -> ActiveOrder? {
        do {
            return try await call("GET", "tables/\(table)/order")
        } catch let error as APIError where error.isNotFound {
            return nil
        }
    }

    struct ItemsBody: Encodable { let items: [OrderItemInput] }
    public func addItems(table: String, items: [OrderItemInput]) async throws -> ActiveOrder {
        try await call("POST", "tables/\(table)/items", body: ItemsBody(items: items))
    }

    public func dispatch(table: String) async throws { try await perform("POST", "tables/\(table)/dispatch") }
    public func fireSuite(table: String) async throws { try await perform("POST", "tables/\(table)/fire-suite") }

    struct TargetBody: Encodable { let targetTableNumber: String }
    public func transfer(from: String, to: String, merge: Bool) async throws -> OperationResult {
        try await call("POST", "tables/\(from)/\(merge ? "merge" : "transfer")", body: TargetBody(targetTableNumber: to))
    }

    struct DestinationBody: Encodable { let destination: OrderDestination }
    public func setDestination(orderId: UUID, destination: OrderDestination) async throws {
        try await perform("POST", "orders/\(orderId.uuidString.lowercased())/destination", body: DestinationBody(destination: destination))
    }

    struct DiscountBody: Encodable { let type: DiscountType; let value: Decimal; let reason: String; let operatorId: UUID? }
    public func applyDiscount(orderId: UUID, type: DiscountType, value: Decimal, reason: String, operatorId: UUID?) async throws {
        try await perform("POST", "orders/\(orderId.uuidString.lowercased())/discount", body: DiscountBody(type: type, value: value, reason: reason, operatorId: operatorId))
    }

    public func removeDiscount(orderId: UUID) async throws {
        try await perform("DELETE", "orders/\(orderId.uuidString.lowercased())/discount")
    }

    struct CompBody: Encodable { let reason: String; let operatorId: UUID? }
    public func compItem(orderId: UUID, lineId: UUID, reason: String, operatorId: UUID?) async throws {
        try await perform("POST", "orders/\(orderId.uuidString.lowercased())/items/\(lineId.uuidString.lowercased())/comp", body: CompBody(reason: reason, operatorId: operatorId))
    }

    // MARK: - Encaissement

    public func pay(_ request: PaymentRequest) async throws -> PaymentResult { try await call("POST", "checkout/pay", body: request) }
    public func hotelRooms() async throws -> [HotelRoom] { try await call("GET", "hotel/rooms") }
    public func chargeRoom(_ request: RoomChargeRequest) async throws -> OperationResult { try await call("POST", "hotel/room-charge", body: request) }

    // MARK: - Comptoir

    struct CounterOpenBody: Encodable { let terminalId: String; let destination: OrderDestination }
    public func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder {
        try await call("POST", "orders/counter/direct", body: CounterOpenBody(terminalId: terminalId, destination: destination))
    }

    struct HoldBody: Encodable { let orderId: UUID; let terminalId: String; let customerLabel: String }
    public func holdOrder(orderId: UUID, terminalId: String, label: String) async throws {
        try await perform("POST", "orders/counter/hold", body: HoldBody(orderId: orderId, terminalId: terminalId, customerLabel: label))
    }

    public func heldOrders(terminalId: String) async throws -> [HeldOrder] {
        try await call("GET", "orders/counter/held", query: ["terminalId": terminalId])
    }

    public func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder {
        try await call("POST", "orders/counter/held/\(holdId.uuidString.lowercased())/recall", body: Empty())
    }

    struct VoidBody: Encodable { let supervisorPin: String; let voidReason: String; let terminalId: String }
    public func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws {
        try await perform("POST", "orders/counter/held/\(holdId.uuidString.lowercased())/void", body: VoidBody(supervisorPin: supervisorPin, voidReason: reason, terminalId: terminalId))
    }

    public func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult {
        try await call("POST", "orders/counter/checkout", body: request)
    }

    // MARK: - Cuisine

    public func kitchenTickets() async throws -> [KitchenTicket] { try await call("GET", "kds/tickets") }
    public func bumpTicket(id: UUID) async throws { try await perform("POST", "kds/tickets/\(id.uuidString.lowercased())/bump") }

    // MARK: - Fiscal

    public func xReport(terminalId: String) async throws -> FiscalReport {
        try await call("GET", "fiscal/x-report", query: ["terminalId": terminalId])
    }

    public func latestClosure(terminalId: String) async throws -> FiscalReport? {
        do {
            return try await call("GET", "fiscal/latest-closure", query: ["terminalId": terminalId])
        } catch let error as APIError where error.isNotFound {
            return nil
        }
    }

    struct ZBody: Encodable { let terminalId: String; let managerId: UUID; let managerName: String }
    public func zClosure(terminalId: String, managerId: UUID, managerName: String) async throws -> FiscalReport {
        try await call("POST", "fiscal/z-closure", body: ZBody(terminalId: terminalId, managerId: managerId, managerName: managerName))
    }

    public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data) {
        let request = try makeRequest("GET", "fiscal/fec", query: ["from": Self.iso(from), "to": Self.iso(to), "siren": siren])
        let (data, response) = try await send(request)
        var fileName = "\(siren)FEC.txt"
        if let disposition = response.value(forHTTPHeaderField: "Content-Disposition"),
           let range = disposition.range(of: "filename=") {
            fileName = disposition[range.upperBound...]
                .split(separator: ";").first.map(String.init)?
                .trimmingCharacters(in: CharacterSet(charactersIn: "\"' ")) ?? fileName
        }
        return (fileName, data)
    }

    public func dashboard(from: Date, to: Date) async throws -> FinancialDashboard {
        try await call("GET", "dashboard/financial", query: ["from": Self.iso(from), "to": Self.iso(to)])
    }

    // MARK: - Personnel & imprimantes

    public func staff() async throws -> [StaffMember] { try await call("GET", "staff/operators") }

    struct StaffBody: Encodable { let name: String; let role: String; let pin: String?; let isActive: Bool? }
    public func createStaff(name: String, role: UserRole, pin: String) async throws {
        try await perform("POST", "staff", body: StaffBody(name: name, role: role.rawValue, pin: pin, isActive: nil))
    }

    public func updateStaff(id: UUID, name: String, role: UserRole, pin: String?, isActive: Bool) async throws {
        try await perform("PUT", "staff/\(id.uuidString.lowercased())", body: StaffBody(name: name, role: role.rawValue, pin: pin?.isEmpty == false ? pin : nil, isActive: isActive))
    }

    public func deactivateStaff(id: UUID) async throws {
        try await perform("DELETE", "staff/\(id.uuidString.lowercased())")
    }

    public func printers() async throws -> [Printer] { try await call("GET", "printers") }

    struct PrinterCreateBody: Encodable { let name: String; let ipAddress: String; let port: Int; let paperWidthMm: Int; let openCashDrawerOnReceipt: Bool; let assignedStationIds: [String] }
    struct PrinterUpdateBody: Encodable { let name: String; let ipAddress: String; let port: Int; let paperWidthMm: Int; let hasCashDrawer: Bool; let targetStations: [String]; let isActive: Bool }

    public func savePrinter(_ p: Printer, isNew: Bool) async throws {
        if isNew {
            try await perform("POST", "printers", body: PrinterCreateBody(name: p.name, ipAddress: p.ipAddress, port: p.port, paperWidthMm: p.paperWidthMm, openCashDrawerOnReceipt: p.openCashDrawerOnReceipt, assignedStationIds: p.assignedStationIds))
        } else {
            try await perform("PUT", "printers/\(p.id.uuidString.lowercased())", body: PrinterUpdateBody(name: p.name, ipAddress: p.ipAddress, port: p.port, paperWidthMm: p.paperWidthMm, hasCashDrawer: p.openCashDrawerOnReceipt, targetStations: p.assignedStationIds, isActive: p.isActive))
        }
    }

    public func testPrinter(id: UUID) async throws -> TestPrintResult {
        try await call("POST", "printers/\(id.uuidString.lowercased())/test", body: Empty())
    }

    // MARK: - Happy Hour

    public func happyHourStatus(terminalId: String) async throws -> HappyHourStatus {
        try await call("GET", "happy-hour/status", query: ["terminalId": terminalId])
    }

    public func happyHourPricing(terminalId: String) async throws -> HappyHourPricingTable {
        try await call("GET", "happy-hour/pricing-table", query: ["terminalId": terminalId])
    }

    struct OverrideBody: Encodable { let terminalId: String; let supervisorPin: String; let durationMinutes: Int?; let reason: String }
    public func activateHappyHourOverride(terminalId: String, pin: String, minutes: Int, reason: String) async throws -> OperationResult {
        try await call("POST", "happy-hour/override/activate", body: OverrideBody(terminalId: terminalId, supervisorPin: pin, durationMinutes: minutes, reason: reason))
    }

    public func stopHappyHourOverride(terminalId: String, pin: String, reason: String) async throws -> OperationResult {
        try await call("POST", "happy-hour/override/stop", body: OverrideBody(terminalId: terminalId, supervisorPin: pin, durationMinutes: nil, reason: reason))
    }

    public func happyHourSchedules() async throws -> [HappyHourSchedule] { try await call("GET", "happy-hour/schedules") }

    struct CreatedId: Decodable { let id: UUID? }
    public func createHappyHourSchedule(_ schedule: HappyHourSchedule) async throws -> UUID? {
        let created: CreatedId = try await call("POST", "happy-hour/schedules", body: schedule)
        return created.id
    }

    public func deleteHappyHourSchedule(id: UUID) async throws {
        try await perform("DELETE", "happy-hour/schedules/\(id.uuidString.lowercased())")
    }

    struct AppliedCount: Decodable { let appliedCount: Int? }
    public func applyHappyHourRules(scheduleId: UUID, _ request: BatchPriceRulesRequest) async throws -> Int {
        let result: AppliedCount = try await call("POST", "happy-hour/schedules/\(scheduleId.uuidString.lowercased())/rules/batch", body: request)
        return result.appliedCount ?? request.targetIds.count
    }

    struct RuleIdsBody: Encodable { let ruleIds: [UUID] }
    public func deleteHappyHourRules(scheduleId: UUID, ruleIds: [UUID]) async throws {
        try await perform("DELETE", "happy-hour/schedules/\(scheduleId.uuidString.lowercased())/rules/batch", body: RuleIdsBody(ruleIds: ruleIds))
    }

    // MARK: - Réseau

    public func health() async throws -> TimeInterval {
        let start = Date()
        try await perform("GET", "health")
        return Date().timeIntervalSince(start)
    }

    public func networkInfo() async throws -> NetworkInfo { try await call("GET", "network/info") }
    public func syncStatus() async throws -> SyncStatus { try await call("GET", "sync/status") }

    struct SyncBody: Encodable { let messages: [String] }
    public func forceSync() async throws { try await perform("POST", "sync/batch", body: SyncBody(messages: [])) }
}
