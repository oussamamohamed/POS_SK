import Foundation

/// Table du plan de salle (`GET /api/tables`, DTO `DiningTableDto`).
public struct DiningTable: Identifiable, Hashable, Codable, Sendable {
    public var tableNumber: String
    public var capacity: Int
    public var status: TableStatus
    public var positionX: Double
    public var positionY: Double
    public var assignedWaiterName: String?
    public var coversCount: Int
    public var activeOrderId: UUID?
    public var openedAtUtc: Date?
    public var activeOrderTotalTtc: Money

    public var id: String { tableNumber }

    /// La table « Comptoir » sert de support technique aux ventes directes : on la masque en salle.
    public var isCounter: Bool { tableNumber.caseInsensitiveCompare(OrderContext.counterTableNumber) == .orderedSame }

    public init(
        tableNumber: String,
        capacity: Int = 4,
        status: TableStatus = .free,
        positionX: Double = 0,
        positionY: Double = 0,
        assignedWaiterName: String? = nil,
        coversCount: Int = 0,
        activeOrderId: UUID? = nil,
        openedAtUtc: Date? = nil,
        activeOrderTotalTtc: Money = .zero
    ) {
        self.tableNumber = tableNumber
        self.capacity = capacity
        self.status = status
        self.positionX = positionX
        self.positionY = positionY
        self.assignedWaiterName = assignedWaiterName
        self.coversCount = coversCount
        self.activeOrderId = activeOrderId
        self.openedAtUtc = openedAtUtc
        self.activeOrderTotalTtc = activeOrderTotalTtc
    }

    enum CodingKeys: String, CodingKey {
        case tableNumber, capacity, status, positionX, positionY, assignedWaiterName
        case coversCount, activeOrderId, openedAtUtc, activeOrderTotalTtc
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        tableNumber = try c.decode(String.self, forKey: .tableNumber)
        capacity = try c.decodeIfPresent(Int.self, forKey: .capacity) ?? 4
        status = try c.decodeIfPresent(TableStatus.self, forKey: .status) ?? .free
        positionX = try c.decodeIfPresent(Double.self, forKey: .positionX) ?? 0
        positionY = try c.decodeIfPresent(Double.self, forKey: .positionY) ?? 0
        assignedWaiterName = try c.decodeIfPresent(String.self, forKey: .assignedWaiterName)
        coversCount = try c.decodeIfPresent(Int.self, forKey: .coversCount) ?? 0
        activeOrderId = try c.decodeIfPresent(UUID.self, forKey: .activeOrderId)
        openedAtUtc = try c.decodeIfPresent(Date.self, forKey: .openedAtUtc)
        activeOrderTotalTtc = try c.decodeIfPresent(Money.self, forKey: .activeOrderTotalTtc) ?? .zero
    }
}

/// Commande active d'une table (`ActiveTableOrderDto`).
public struct ActiveOrder: Hashable, Codable, Sendable {
    public var orderId: UUID
    public var tableNumber: String
    public var waiterName: String?
    public var coversCount: Int
    public var openedAtUtc: Date?
    public var lines: [OrderLine]
    public var totalHtAmount: Money
    public var totalVatAmount: Money
    public var totalTtcAmount: Money
    public var globalDiscountType: DiscountType?
    public var globalDiscountValue: Decimal
    public var globalDiscountReason: String?
    public var destination: OrderDestination
    public var pickupNumber: String?
    public var pickupBuzzer: String?

    public init(
        orderId: UUID,
        tableNumber: String,
        waiterName: String? = nil,
        coversCount: Int = 0,
        openedAtUtc: Date? = nil,
        lines: [OrderLine] = [],
        totalHtAmount: Money = .zero,
        totalVatAmount: Money = .zero,
        totalTtcAmount: Money = .zero,
        globalDiscountType: DiscountType? = nil,
        globalDiscountValue: Decimal = 0,
        globalDiscountReason: String? = nil,
        destination: OrderDestination = .eatIn,
        pickupNumber: String? = nil,
        pickupBuzzer: String? = nil
    ) {
        self.orderId = orderId
        self.tableNumber = tableNumber
        self.waiterName = waiterName
        self.coversCount = coversCount
        self.openedAtUtc = openedAtUtc
        self.lines = lines
        self.totalHtAmount = totalHtAmount
        self.totalVatAmount = totalVatAmount
        self.totalTtcAmount = totalTtcAmount
        self.globalDiscountType = globalDiscountType
        self.globalDiscountValue = globalDiscountValue
        self.globalDiscountReason = globalDiscountReason
        self.destination = destination
        self.pickupNumber = pickupNumber
        self.pickupBuzzer = pickupBuzzer
    }

    enum CodingKeys: String, CodingKey {
        case orderId, tableNumber, waiterName, coversCount, openedAtUtc, lines
        case totalHtAmount, totalVatAmount, totalTtcAmount
        case globalDiscountType, globalDiscountValue, globalDiscountReason
        case destination, pickupNumber, pickupBuzzer
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        orderId = try c.decode(UUID.self, forKey: .orderId)
        tableNumber = try c.decodeIfPresent(String.self, forKey: .tableNumber) ?? ""
        waiterName = try c.decodeIfPresent(String.self, forKey: .waiterName)
        coversCount = try c.decodeIfPresent(Int.self, forKey: .coversCount) ?? 0
        openedAtUtc = try c.decodeIfPresent(Date.self, forKey: .openedAtUtc)
        lines = try c.decodeIfPresent([OrderLine].self, forKey: .lines) ?? []
        totalHtAmount = try c.decodeIfPresent(Money.self, forKey: .totalHtAmount) ?? .zero
        totalVatAmount = try c.decodeIfPresent(Money.self, forKey: .totalVatAmount) ?? .zero
        totalTtcAmount = try c.decodeIfPresent(Money.self, forKey: .totalTtcAmount) ?? .zero
        globalDiscountType = try c.decodeIfPresent(DiscountType.self, forKey: .globalDiscountType)
        globalDiscountValue = try c.decodeIfPresent(Decimal.self, forKey: .globalDiscountValue) ?? 0
        globalDiscountReason = try c.decodeIfPresent(String.self, forKey: .globalDiscountReason)
        destination = try c.decodeIfPresent(OrderDestination.self, forKey: .destination) ?? .eatIn
        pickupNumber = try c.decodeIfPresent(String.self, forKey: .pickupNumber)
        pickupBuzzer = try c.decodeIfPresent(String.self, forKey: .pickupBuzzer)
    }
}

/// Ligne de commande persistée côté serveur (`ActiveOrderLineDto`).
public struct OrderLine: Identifiable, Hashable, Codable, Sendable {
    public var lineId: UUID
    public var productId: UUID
    public var productName: String
    public var quantity: Int
    public var unitPrice: Money
    public var totalPrice: Money
    public var taxRatePercent: Decimal
    public var preparationStationId: String?
    public var isDispatched: Bool
    public var modifiersSummary: [String]
    public var course: CourseType
    public var isComp: Bool
    public var discountPercent: Decimal
    public var modifiersPriceExtra: Money
    public var isHappyHourApplied: Bool
    public var originalUnitPrice: Money?

    public var id: UUID { lineId }

    public init(
        lineId: UUID = UUID(),
        productId: UUID,
        productName: String,
        quantity: Int,
        unitPrice: Money,
        totalPrice: Money,
        taxRatePercent: Decimal,
        preparationStationId: String? = nil,
        isDispatched: Bool = false,
        modifiersSummary: [String] = [],
        course: CourseType = .direct,
        isComp: Bool = false,
        discountPercent: Decimal = 0,
        modifiersPriceExtra: Money = .zero,
        isHappyHourApplied: Bool = false,
        originalUnitPrice: Money? = nil
    ) {
        self.lineId = lineId
        self.productId = productId
        self.productName = productName
        self.quantity = quantity
        self.unitPrice = unitPrice
        self.totalPrice = totalPrice
        self.taxRatePercent = taxRatePercent
        self.preparationStationId = preparationStationId
        self.isDispatched = isDispatched
        self.modifiersSummary = modifiersSummary
        self.course = course
        self.isComp = isComp
        self.discountPercent = discountPercent
        self.modifiersPriceExtra = modifiersPriceExtra
        self.isHappyHourApplied = isHappyHourApplied
        self.originalUnitPrice = originalUnitPrice
    }

    enum CodingKeys: String, CodingKey {
        case lineId, productId, productName, quantity, unitPrice, totalPrice, taxRatePercent
        case preparationStationId, isDispatched, modifiersSummary, course, isComp, discountPercent
        case modifiersPriceExtra, isHappyHourApplied, originalUnitPrice
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        lineId = try c.decode(UUID.self, forKey: .lineId)
        productId = try c.decode(UUID.self, forKey: .productId)
        productName = try c.decode(String.self, forKey: .productName)
        quantity = try c.decodeIfPresent(Int.self, forKey: .quantity) ?? 1
        unitPrice = try c.decodeIfPresent(Money.self, forKey: .unitPrice) ?? .zero
        totalPrice = try c.decodeIfPresent(Money.self, forKey: .totalPrice) ?? .zero
        taxRatePercent = try c.decodeIfPresent(Decimal.self, forKey: .taxRatePercent) ?? 10
        preparationStationId = try c.decodeIfPresent(String.self, forKey: .preparationStationId)
        isDispatched = try c.decodeIfPresent(Bool.self, forKey: .isDispatched) ?? false
        modifiersSummary = try c.decodeIfPresent([String].self, forKey: .modifiersSummary) ?? []
        course = try c.decodeIfPresent(CourseType.self, forKey: .course) ?? .direct
        isComp = try c.decodeIfPresent(Bool.self, forKey: .isComp) ?? false
        discountPercent = try c.decodeIfPresent(Decimal.self, forKey: .discountPercent) ?? 0
        modifiersPriceExtra = try c.decodeIfPresent(Money.self, forKey: .modifiersPriceExtra) ?? .zero
        isHappyHourApplied = try c.decodeIfPresent(Bool.self, forKey: .isHappyHourApplied) ?? false
        originalUnitPrice = try c.decodeIfPresent(Money.self, forKey: .originalUnitPrice)
    }
}

/// Ligne envoyée à `POST /api/tables/{table}/items` (`OrderItemInputDto`).
public struct OrderItemInput: Hashable, Codable, Sendable {
    public var productId: UUID
    public var productName: String
    public var quantity: Int
    public var unitPrice: Money
    public var taxRatePercent: Decimal
    public var preparationStationId: String?
    public var modifiers: [String]
    public var course: CourseType
    public var modifiersPriceExtra: Money

    public init(
        productId: UUID,
        productName: String,
        quantity: Int,
        unitPrice: Money,
        taxRatePercent: Decimal,
        preparationStationId: String?,
        modifiers: [String],
        course: CourseType,
        modifiersPriceExtra: Money
    ) {
        self.productId = productId
        self.productName = productName
        self.quantity = quantity
        self.unitPrice = unitPrice
        self.taxRatePercent = taxRatePercent
        self.preparationStationId = preparationStationId
        self.modifiers = modifiers
        self.course = course
        self.modifiersPriceExtra = modifiersPriceExtra
    }
}

struct AddOrderItemsRequest: Encodable {
    var items: [OrderItemInput]
}

struct OpenTableRequest: Encodable {
    var waiterName: String?
    var coversCount: Int
    var operatorId: UUID?
}
