import Foundation

// MARK: - Authentification

public struct LoginResponse: Codable, Hashable, Sendable {
    public var success: Bool
    public var operatorId: UUID?
    public var operatorName: String?
    public var role: UserRole?
    public var token: String?
    public var errorMessage: String?

    public init(success: Bool, operatorId: UUID?, operatorName: String?, role: UserRole?, token: String?, errorMessage: String? = nil) {
        self.success = success
        self.operatorId = operatorId
        self.operatorName = operatorName
        self.role = role
        self.token = token
        self.errorMessage = errorMessage
    }
}

public struct Operator: Hashable, Sendable, Codable {
    public var id: UUID
    public var name: String
    public var role: UserRole

    public init(id: UUID, name: String, role: UserRole) {
        self.id = id
        self.name = name
        self.role = role
    }
}

// MARK: - Cuisine (KDS)

public struct KitchenTicketItem: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID
    public var productName: String
    public var quantity: Int
    public var modifiersSummary: String?
    public var kitchenComment: String?

    public init(id: UUID = UUID(), productName: String, quantity: Int, modifiersSummary: String? = nil, kitchenComment: String? = nil) {
        self.id = id
        self.productName = productName
        self.quantity = quantity
        self.modifiersSummary = modifiersSummary
        self.kitchenComment = kitchenComment
    }

    enum CodingKeys: String, CodingKey { case id, itemId, productName, quantity, modifiersSummary, kitchenComment }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(UUID.self, forKey: .id) ?? c.decodeIfPresent(UUID.self, forKey: .itemId) ?? UUID()
        productName = try c.decode(String.self, forKey: .productName)
        quantity = try c.decodeIfPresent(Int.self, forKey: .quantity) ?? 1
        modifiersSummary = try c.decodeIfPresent(String.self, forKey: .modifiersSummary)
        kitchenComment = try c.decodeIfPresent(String.self, forKey: .kitchenComment)
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encode(productName, forKey: .productName)
        try c.encode(quantity, forKey: .quantity)
        try c.encodeIfPresent(modifiersSummary, forKey: .modifiersSummary)
        try c.encodeIfPresent(kitchenComment, forKey: .kitchenComment)
    }
}

public struct KitchenTicket: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID
    public var orderId: UUID?
    public var tableNumber: String
    public var serverName: String?
    public var coversCount: Int?
    public var stationId: String
    public var status: TicketStatus
    public var dispatchedAtUtc: Date?
    public var items: [KitchenTicketItem]

    public init(id: UUID = UUID(), orderId: UUID? = nil, tableNumber: String, serverName: String? = nil, coversCount: Int? = nil, stationId: String, status: TicketStatus = .pending, dispatchedAtUtc: Date? = Date(), items: [KitchenTicketItem]) {
        self.id = id
        self.orderId = orderId
        self.tableNumber = tableNumber
        self.serverName = serverName
        self.coversCount = coversCount
        self.stationId = stationId
        self.status = status
        self.dispatchedAtUtc = dispatchedAtUtc
        self.items = items
    }

    enum CodingKeys: String, CodingKey { case id, ticketId, orderId, tableNumber, serverName, coversCount, stationId, status, dispatchedAtUtc, items }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        id = try c.decodeIfPresent(UUID.self, forKey: .id) ?? c.decode(UUID.self, forKey: .ticketId)
        orderId = try c.decodeIfPresent(UUID.self, forKey: .orderId)
        tableNumber = try c.decodeIfPresent(String.self, forKey: .tableNumber) ?? "?"
        serverName = try c.decodeIfPresent(String.self, forKey: .serverName)
        coversCount = try c.decodeIfPresent(Int.self, forKey: .coversCount)
        stationId = try c.decodeIfPresent(String.self, forKey: .stationId) ?? "HOT_KITCHEN"
        status = try c.decodeIfPresent(TicketStatus.self, forKey: .status) ?? .pending
        dispatchedAtUtc = try c.decodeIfPresent(Date.self, forKey: .dispatchedAtUtc)
        items = try c.decodeIfPresent([KitchenTicketItem].self, forKey: .items) ?? []
    }

    public func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: CodingKeys.self)
        try c.encode(id, forKey: .id)
        try c.encodeIfPresent(orderId, forKey: .orderId)
        try c.encode(tableNumber, forKey: .tableNumber)
        try c.encodeIfPresent(serverName, forKey: .serverName)
        try c.encodeIfPresent(coversCount, forKey: .coversCount)
        try c.encode(stationId, forKey: .stationId)
        try c.encode(status, forKey: .status)
        try c.encodeIfPresent(dispatchedAtUtc, forKey: .dispatchedAtUtc)
        try c.encode(items, forKey: .items)
    }
}

// MARK: - Fiscal NF525

public struct FiscalReport: Codable, Hashable, Sendable {
    public var terminalId: String?
    public var closureSequence: Int?
    public var totalSalesTtc: Money
    public var totalSalesHt: Money
    public var receiptCount: Int
    public var vatBreakdown: [String: Money]
    public var paymentTotals: [String: Money]
    public var perpetualGrandTotal: Money
    public var signatureHash: String?
    public var closedAtUtc: Date?
    public var periodEndUtc: Date?

    public init(terminalId: String?, closureSequence: Int? = nil, totalSalesTtc: Money, totalSalesHt: Money, receiptCount: Int, vatBreakdown: [String: Money], paymentTotals: [String: Money], perpetualGrandTotal: Money, signatureHash: String? = nil, closedAtUtc: Date? = nil, periodEndUtc: Date? = nil) {
        self.terminalId = terminalId
        self.closureSequence = closureSequence
        self.totalSalesTtc = totalSalesTtc
        self.totalSalesHt = totalSalesHt
        self.receiptCount = receiptCount
        self.vatBreakdown = vatBreakdown
        self.paymentTotals = paymentTotals
        self.perpetualGrandTotal = perpetualGrandTotal
        self.signatureHash = signatureHash
        self.closedAtUtc = closedAtUtc
        self.periodEndUtc = periodEndUtc
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        terminalId = try c.decodeIfPresent(String.self, forKey: .terminalId)
        closureSequence = try c.decodeIfPresent(Int.self, forKey: .closureSequence)
        totalSalesTtc = try c.decodeIfPresent(Money.self, forKey: .totalSalesTtc) ?? .zero
        totalSalesHt = try c.decodeIfPresent(Money.self, forKey: .totalSalesHt) ?? .zero
        receiptCount = try c.decodeIfPresent(Int.self, forKey: .receiptCount) ?? 0
        vatBreakdown = try c.decodeIfPresent([String: Money].self, forKey: .vatBreakdown) ?? [:]
        paymentTotals = try c.decodeIfPresent([String: Money].self, forKey: .paymentTotals) ?? [:]
        perpetualGrandTotal = try c.decodeIfPresent(Money.self, forKey: .perpetualGrandTotal) ?? .zero
        signatureHash = try c.decodeIfPresent(String.self, forKey: .signatureHash)
        closedAtUtc = try c.decodeIfPresent(Date.self, forKey: .closedAtUtc)
        periodEndUtc = try c.decodeIfPresent(Date.self, forKey: .periodEndUtc)
    }

    /// Lignes de TVA triées par taux croissant (« 5.5 », « 10.0 », « 20.0 »).
    public var sortedVat: [(rate: String, amount: Money)] {
        vatBreakdown
            .map { (rate: $0.key, amount: $0.value) }
            .sorted { (Double($0.rate) ?? 0) < (Double($1.rate) ?? 0) }
    }

    public var sortedPayments: [(method: String, amount: Money)] {
        paymentTotals
            .map { (method: PaymentMethod.label(forServerName: $0.key), amount: $0.value) }
            .sorted { $0.method < $1.method }
    }
}

// MARK: - Tableau de bord

public struct FinancialDashboard: Codable, Hashable, Sendable {
    public struct Kpis: Codable, Hashable, Sendable {
        public var totalSalesTtc: Money
        public var totalSalesHt: Money
        public var averageOrderTtc: Money
        public var averageCoverTtc: Money
        public var totalOrdersCount: Int
        public var totalCoversCount: Int

        public init(totalSalesTtc: Money = .zero, totalSalesHt: Money = .zero, averageOrderTtc: Money = .zero, averageCoverTtc: Money = .zero, totalOrdersCount: Int = 0, totalCoversCount: Int = 0) {
            self.totalSalesTtc = totalSalesTtc
            self.totalSalesHt = totalSalesHt
            self.averageOrderTtc = averageOrderTtc
            self.averageCoverTtc = averageCoverTtc
            self.totalOrdersCount = totalOrdersCount
            self.totalCoversCount = totalCoversCount
        }
    }

    public struct Service: Codable, Hashable, Sendable, Identifiable {
        public var serviceName: String
        public var salesTtc: Money
        public var ordersCount: Int
        public var coversCount: Int
        public var averageCoverTtc: Money
        public var id: String { serviceName }
    }

    public struct TopProduct: Codable, Hashable, Sendable, Identifiable {
        public var productId: UUID
        public var productName: String
        public var quantitySold: Int
        public var totalSalesTtc: Money
        public var percentageOfTotal: Double
        public var id: UUID { productId }
    }

    public struct StaffPerformance: Codable, Hashable, Sendable, Identifiable {
        public var serverName: String
        public var tablesServedCount: Int
        public var totalSalesTtc: Money
        public var averageTableTtc: Money
        public var id: String { serverName }
    }

    public struct PaymentShare: Codable, Hashable, Sendable, Identifiable {
        public var methodName: String
        public var totalAmount: Money
        public var transactionsCount: Int
        public var percentageOfTotal: Double
        public var id: String { methodName }
    }

    public var kpis: Kpis
    public var services: [Service]
    public var topProducts: [TopProduct]
    public var staffPerformance: [StaffPerformance]
    public var paymentMethods: [PaymentShare]

    public init(kpis: Kpis = Kpis(), services: [Service] = [], topProducts: [TopProduct] = [], staffPerformance: [StaffPerformance] = [], paymentMethods: [PaymentShare] = []) {
        self.kpis = kpis
        self.services = services
        self.topProducts = topProducts
        self.staffPerformance = staffPerformance
        self.paymentMethods = paymentMethods
    }
}

public enum DashboardRange: String, CaseIterable, Identifiable, Sendable {
    case today, yesterday, week, month
    public var id: String { rawValue }

    public var label: String {
        switch self {
        case .today: "Aujourd'hui"
        case .yesterday: "Hier"
        case .week: "7 jours"
        case .month: "30 jours"
        }
    }

    public func interval(now: Date = Date(), calendar: Calendar = .current) -> (from: Date, to: Date) {
        let startOfToday = calendar.startOfDay(for: now)
        switch self {
        case .today:
            return (startOfToday, now)
        case .yesterday:
            let start = calendar.date(byAdding: .day, value: -1, to: startOfToday)!
            return (start, startOfToday.addingTimeInterval(-0.001))
        case .week:
            return (calendar.date(byAdding: .day, value: -7, to: startOfToday)!, now)
        case .month:
            return (calendar.date(byAdding: .day, value: -30, to: startOfToday)!, now)
        }
    }
}

// MARK: - Personnel & imprimantes

public struct StaffMember: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID
    public var name: String
    public var role: UserRole
    public var isActive: Bool

    public init(id: UUID = UUID(), name: String, role: UserRole, isActive: Bool = true) {
        self.id = id
        self.name = name
        self.role = role
        self.isActive = isActive
    }
}

public struct Printer: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID
    public var name: String
    public var ipAddress: String
    public var port: Int
    public var paperWidthMm: Int
    public var openCashDrawerOnReceipt: Bool
    public var assignedStationIds: [String]
    public var isActive: Bool

    public init(id: UUID = UUID(), name: String, ipAddress: String, port: Int = 9100, paperWidthMm: Int = 80, openCashDrawerOnReceipt: Bool = false, assignedStationIds: [String] = [], isActive: Bool = true) {
        self.id = id
        self.name = name
        self.ipAddress = ipAddress
        self.port = port
        self.paperWidthMm = paperWidthMm
        self.openCashDrawerOnReceipt = openCashDrawerOnReceipt
        self.assignedStationIds = assignedStationIds
        self.isActive = isActive
    }
}

public struct TestPrintResult: Codable, Hashable, Sendable {
    public var success: Bool
    public var message: String
}

// MARK: - Réseau

public struct NetworkInfo: Codable, Hashable, Sendable {
    public var hostName: String?
    public var primaryIp: String?
    public var ipAddresses: [String]?
    public var port: Int?
    public var discoveryPort: Int?
    public var serverName: String?
    public var status: String?
    public var version: String?
}

public struct SyncStatus: Codable, Hashable, Sendable {
    public var totalMessages: Int
    public var completedMessages: Int
    public var pendingMessages: Int
    public var status: String?
    public var lastSyncUtc: Date?
}

// MARK: - Happy Hour

public struct HappyHourWindow: Codable, Hashable, Sendable {
    public var startTime: String?
    public var endTime: String?
    public var remainingMinutes: Int
}

public struct HappyHourStatus: Codable, Hashable, Sendable {
    public var isActive: Bool
    public var isOverride: Bool
    public var activeScheduleName: String?
    public var activeScheduleId: UUID?
    public var appliesToTakeaway: Bool
    public var currentWindow: HappyHourWindow?

    public init(isActive: Bool = false, isOverride: Bool = false, activeScheduleName: String? = nil, activeScheduleId: UUID? = nil, appliesToTakeaway: Bool = false, currentWindow: HappyHourWindow? = nil) {
        self.isActive = isActive
        self.isOverride = isOverride
        self.activeScheduleName = activeScheduleName
        self.activeScheduleId = activeScheduleId
        self.appliesToTakeaway = appliesToTakeaway
        self.currentWindow = currentWindow
    }

    public static let inactive = HappyHourStatus()
}

public struct HappyHourPrice: Codable, Hashable, Sendable {
    public var productId: UUID
    public var productName: String?
    public var standardPrice: Money
    public var happyHourPrice: Money
    public var ruleType: String?

    public init(productId: UUID, productName: String? = nil, standardPrice: Money, happyHourPrice: Money, ruleType: String? = nil) {
        self.productId = productId
        self.productName = productName
        self.standardPrice = standardPrice
        self.happyHourPrice = happyHourPrice
        self.ruleType = ruleType
    }
}

public struct HappyHourPricingTable: Codable, Hashable, Sendable {
    public var isActive: Bool
    public var items: [HappyHourPrice]

    public init(isActive: Bool, items: [HappyHourPrice]) {
        self.isActive = isActive
        self.items = items
    }
}

public struct HappyHourRule: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID?
    public var targetType: HappyHourTargetType
    public var targetId: String
    public var targetName: String
    public var pricingMode: HappyHourPricingMode
    public var fixedPrice: Money?
    public var discountPercent: Decimal?

    public init(id: UUID? = UUID(), targetType: HappyHourTargetType, targetId: String, targetName: String, pricingMode: HappyHourPricingMode, fixedPrice: Money? = nil, discountPercent: Decimal? = nil) {
        self.id = id
        self.targetType = targetType
        self.targetId = targetId
        self.targetName = targetName
        self.pricingMode = pricingMode
        self.fixedPrice = fixedPrice
        self.discountPercent = discountPercent
    }

    public var valueLabel: String {
        if let fixedPrice { return fixedPrice.formatted }
        if let discountPercent { return "-\(discountPercent)%" }
        return "—"
    }
}

public struct HappyHourSchedule: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID?
    public var name: String
    /// 0 = dimanche … 6 = samedi (`System.DayOfWeek`).
    public var daysOfWeek: [Int]
    public var startTime: String
    public var endTime: String
    public var isActive: Bool
    public var appliesToTakeaway: Bool
    public var priority: Int
    public var priceRules: [HappyHourRule]

    public init(id: UUID? = nil, name: String, daysOfWeek: [Int], startTime: String, endTime: String, isActive: Bool = true, appliesToTakeaway: Bool = false, priority: Int = 1, priceRules: [HappyHourRule] = []) {
        self.id = id
        self.name = name
        self.daysOfWeek = daysOfWeek
        self.startTime = startTime
        self.endTime = endTime
        self.isActive = isActive
        self.appliesToTakeaway = appliesToTakeaway
        self.priority = priority
        self.priceRules = priceRules
    }

    public static let dayLabels = ["Dim", "Lun", "Mar", "Mer", "Jeu", "Ven", "Sam"]

    public var daysLabel: String {
        daysOfWeek.sorted { ($0 + 6) % 7 < ($1 + 6) % 7 }
            .map { HappyHourSchedule.dayLabels[safe: $0] ?? "\($0)" }
            .joined(separator: ", ")
    }
}

public struct BatchPriceRulesRequest: Codable, Hashable, Sendable {
    public var targetType: HappyHourTargetType
    public var targetIds: [String]
    public var pricingMode: HappyHourPricingMode
    public var fixedPrice: Money?
    public var discountPercent: Decimal?

    public init(targetType: HappyHourTargetType, targetIds: [String], pricingMode: HappyHourPricingMode, fixedPrice: Money?, discountPercent: Decimal?) {
        self.targetType = targetType
        self.targetIds = targetIds
        self.pricingMode = pricingMode
        self.fixedPrice = fixedPrice
        self.discountPercent = discountPercent
    }
}

public extension Array {
    subscript(safe index: Int) -> Element? { indices.contains(index) ? self[index] : nil }
}
