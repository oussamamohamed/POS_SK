import Foundation

public enum APIError: Error, Equatable, Sendable, LocalizedError {
    case unauthorized
    case forbidden(String?)
    case notFound(String?)
    case rateLimited(String?)
    case server(status: Int, message: String?)
    case transport(String)
    case decoding(String)

    public var errorDescription: String? {
        switch self {
        case .unauthorized: "Session expirée : veuillez saisir votre code PIN."
        case .forbidden(let m): m ?? "Action réservée à un responsable."
        case .notFound(let m): m ?? "Élément introuvable."
        case .rateLimited(let m): m ?? "Trop de tentatives, patientez."
        case .server(let status, let m): m ?? "Erreur serveur (\(status))."
        case .transport(let m): "Serveur injoignable : \(m)"
        case .decoding(let m): "Réponse inattendue du serveur : \(m)"
        }
    }

    public var isNotFound: Bool { if case .notFound = self { true } else { false } }
}

/// Contrat de l'API REST `RestaurantPos.Api`. Toute l'app dépend de ce protocole :
/// `HTTPPosAPI` en production, `InMemoryPosAPI` pour les tests unitaires, les tests UI et les aperçus.
public protocol PosAPI: Sendable {
    // Auth
    func login(pin: String) async throws -> LoginResponse
    func setToken(_ token: String?) async

    // Catalogue
    func categories() async throws -> [MenuCategory]
    func products() async throws -> [Product]
    func createCategory(name: String, colorHex: String, displayOrder: Int) async throws
    func updateCategory(id: String, name: String, colorHex: String, displayOrder: Int) async throws
    func createProduct(_ draft: ProductDraft) async throws
    func updateProduct(id: UUID, _ draft: ProductDraft) async throws
    func archiveProduct(id: UUID) async throws

    // Grille tactile
    func gridLayout(categoryId: String, page: Int) async throws -> TouchGridLayout
    func saveGridLayout(_ request: UpdateGridLayoutRequest) async throws -> TouchGridLayout
    func swapGridSlots(layoutId: UUID, from: GridPosition, to: GridPosition) async throws -> TouchGridLayout
    func updateGridDimensions(categoryId: String, columns: Int, rows: Int, applyToAll: Bool) async throws -> [TouchGridLayout]

    // Salle & commandes
    func tables() async throws -> [DiningTable]
    func createTable(number: String, capacity: Int) async throws
    func openTable(number: String, covers: Int, operatorId: UUID?, waiterName: String?) async throws
    func activeOrder(table: String) async throws -> ActiveOrder?
    func addItems(table: String, items: [OrderItemInput]) async throws -> ActiveOrder
    func dispatch(table: String) async throws
    func fireSuite(table: String) async throws
    func transfer(from: String, to: String, merge: Bool) async throws -> OperationResult
    func setDestination(orderId: UUID, destination: OrderDestination) async throws
    func applyDiscount(orderId: UUID, type: DiscountType, value: Decimal, reason: String, operatorId: UUID?) async throws
    func removeDiscount(orderId: UUID) async throws
    func compItem(orderId: UUID, lineId: UUID, reason: String, operatorId: UUID?) async throws

    // Encaissement
    func pay(_ request: PaymentRequest) async throws -> PaymentResult
    func hotelRooms() async throws -> [HotelRoom]
    func chargeRoom(_ request: RoomChargeRequest) async throws -> OperationResult

    // Comptoir & vente à emporter
    func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder
    func holdOrder(orderId: UUID, terminalId: String, label: String) async throws
    func heldOrders(terminalId: String) async throws -> [HeldOrder]
    func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder
    func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws
    func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult

    // Cuisine
    func kitchenTickets() async throws -> [KitchenTicket]
    func bumpTicket(id: UUID) async throws

    // Fiscal
    func xReport(terminalId: String) async throws -> FiscalReport
    func latestClosure(terminalId: String) async throws -> FiscalReport?
    func zClosure(terminalId: String, managerId: UUID, managerName: String) async throws -> FiscalReport
    func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data)
    func dashboard(from: Date, to: Date) async throws -> FinancialDashboard

    // Personnel & imprimantes
    func staff() async throws -> [StaffMember]
    func createStaff(name: String, role: UserRole, pin: String) async throws
    func updateStaff(id: UUID, name: String, role: UserRole, pin: String?, isActive: Bool) async throws
    func deactivateStaff(id: UUID) async throws
    func printers() async throws -> [Printer]
    func savePrinter(_ printer: Printer, isNew: Bool) async throws
    func testPrinter(id: UUID) async throws -> TestPrintResult

    // Happy Hour
    func happyHourStatus(terminalId: String) async throws -> HappyHourStatus
    func happyHourPricing(terminalId: String) async throws -> HappyHourPricingTable
    func activateHappyHourOverride(terminalId: String, pin: String, minutes: Int, reason: String) async throws -> OperationResult
    func stopHappyHourOverride(terminalId: String, pin: String, reason: String) async throws -> OperationResult
    func happyHourSchedules() async throws -> [HappyHourSchedule]
    func createHappyHourSchedule(_ schedule: HappyHourSchedule) async throws -> UUID?
    func deleteHappyHourSchedule(id: UUID) async throws
    func applyHappyHourRules(scheduleId: UUID, _ request: BatchPriceRulesRequest) async throws -> Int
    func deleteHappyHourRules(scheduleId: UUID, ruleIds: [UUID]) async throws

    // Réseau
    func health() async throws -> TimeInterval
    func networkInfo() async throws -> NetworkInfo
    func syncStatus() async throws -> SyncStatus
    func forceSync() async throws
}

/// Saisie back-office d'un article.
public struct ProductDraft: Hashable, Sendable {
    public var name: String
    public var categoryId: String
    public var price: Money
    public var taxRatePercent: Decimal
    public var stationId: String
    public var isQuickKey: Bool
    public var colorHex: String
    public var displayOrder: Int
    public var description: String

    public init(name: String = "", categoryId: String = "", price: Money = .zero, taxRatePercent: Decimal = 10, stationId: String = "HOT_KITCHEN", isQuickKey: Bool = false, colorHex: String = "#3B82F6", displayOrder: Int = 0, description: String = "") {
        self.name = name
        self.categoryId = categoryId
        self.price = price
        self.taxRatePercent = taxRatePercent
        self.stationId = stationId
        self.isQuickKey = isQuickKey
        self.colorHex = colorHex
        self.displayOrder = displayOrder
        self.description = description
    }

    public init(product: Product) {
        self.init(
            name: product.name, categoryId: product.categoryId, price: product.price,
            taxRatePercent: product.taxRatePercent, stationId: product.station, isQuickKey: product.isQuickKey,
            colorHex: product.colorHex ?? "#3B82F6", displayOrder: product.displayOrder ?? 0,
            description: product.description ?? ""
        )
    }

    public var isValid: Bool { !name.trimmingCharacters(in: .whitespaces).isEmpty && !categoryId.isEmpty && price.cents > 0 }

    public static let stations = ["HOT_KITCHEN", "COLD", "GRILL", "DESSERT", "BAR"]
    public static let vatRates: [Decimal] = [5.5, 10, 20]
}
