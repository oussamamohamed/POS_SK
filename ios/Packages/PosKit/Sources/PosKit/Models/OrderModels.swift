import Foundation

public struct DiningTable: Codable, Identifiable, Hashable, Sendable {
    public var tableNumber: String
    public var capacity: Int
    public var status: TableStatus
    public var positionX: Double?
    public var positionY: Double?
    public var assignedWaiterName: String?
    public var coversCount: Int
    public var activeOrderId: UUID?
    public var openedAtUtc: Date?
    public var activeOrderTotalTtc: Money

    public var id: String { tableNumber }

    public init(tableNumber: String, capacity: Int, status: TableStatus = .free, positionX: Double? = 0, positionY: Double? = 0, assignedWaiterName: String? = nil, coversCount: Int = 0, activeOrderId: UUID? = nil, openedAtUtc: Date? = nil, activeOrderTotalTtc: Money = .zero) {
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

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        tableNumber = try c.decode(String.self, forKey: .tableNumber)
        capacity = try c.decodeIfPresent(Int.self, forKey: .capacity) ?? 2
        status = try c.decodeIfPresent(TableStatus.self, forKey: .status) ?? .free
        positionX = try c.decodeIfPresent(Double.self, forKey: .positionX)
        positionY = try c.decodeIfPresent(Double.self, forKey: .positionY)
        assignedWaiterName = try c.decodeIfPresent(String.self, forKey: .assignedWaiterName)
        coversCount = try c.decodeIfPresent(Int.self, forKey: .coversCount) ?? 0
        activeOrderId = try c.decodeIfPresent(UUID.self, forKey: .activeOrderId)
        openedAtUtc = try c.decodeIfPresent(Date.self, forKey: .openedAtUtc)
        activeOrderTotalTtc = try c.decodeIfPresent(Money.self, forKey: .activeOrderTotalTtc) ?? .zero
    }

    public static let counterNumber = "Comptoir"
    public var isCounter: Bool { tableNumber == DiningTable.counterNumber }
}

/// Ligne d'une commande telle que persistée côté serveur.
public struct OrderLine: Codable, Identifiable, Hashable, Sendable {
    public var lineId: UUID
    public var productId: UUID
    public var productName: String
    public var quantity: Int
    public var unitPrice: Money
    public var totalPrice: Money?
    public var taxRatePercent: Decimal
    public var preparationStationId: String?
    public var isDispatched: Bool
    public var modifiersSummary: [String]
    public var course: CourseType
    public var isComp: Bool
    public var discountPercent: Decimal
    public var modifiersPriceExtra: Money
    public var taxRateTakeawayPercent: Decimal?
    public var isHappyHourApplied: Bool
    public var originalUnitPrice: Money?
    public var appliedHappyHourScheduleId: UUID?

    public var id: UUID { lineId }

    public init(lineId: UUID = UUID(), productId: UUID, productName: String, quantity: Int, unitPrice: Money, taxRatePercent: Decimal, preparationStationId: String? = nil, isDispatched: Bool = false, modifiersSummary: [String] = [], course: CourseType = .direct, isComp: Bool = false, discountPercent: Decimal = 0, modifiersPriceExtra: Money = .zero, taxRateTakeawayPercent: Decimal? = nil, isHappyHourApplied: Bool = false, originalUnitPrice: Money? = nil, appliedHappyHourScheduleId: UUID? = nil) {
        self.lineId = lineId
        self.productId = productId
        self.productName = productName
        self.quantity = quantity
        self.unitPrice = unitPrice
        self.taxRatePercent = taxRatePercent
        self.preparationStationId = preparationStationId
        self.isDispatched = isDispatched
        self.modifiersSummary = modifiersSummary
        self.course = course
        self.isComp = isComp
        self.discountPercent = discountPercent
        self.modifiersPriceExtra = modifiersPriceExtra
        self.taxRateTakeawayPercent = taxRateTakeawayPercent
        self.isHappyHourApplied = isHappyHourApplied
        self.originalUnitPrice = originalUnitPrice
        self.appliedHappyHourScheduleId = appliedHappyHourScheduleId
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        lineId = try c.decode(UUID.self, forKey: .lineId)
        productId = try c.decode(UUID.self, forKey: .productId)
        productName = try c.decode(String.self, forKey: .productName)
        quantity = try c.decodeIfPresent(Int.self, forKey: .quantity) ?? 1
        unitPrice = try c.decodeIfPresent(Money.self, forKey: .unitPrice) ?? .zero
        totalPrice = try c.decodeIfPresent(Money.self, forKey: .totalPrice)
        taxRatePercent = try c.decodeIfPresent(Decimal.self, forKey: .taxRatePercent) ?? 10
        preparationStationId = try c.decodeIfPresent(String.self, forKey: .preparationStationId)
        isDispatched = try c.decodeIfPresent(Bool.self, forKey: .isDispatched) ?? false
        modifiersSummary = try c.decodeIfPresent([String].self, forKey: .modifiersSummary) ?? []
        course = try c.decodeIfPresent(CourseType.self, forKey: .course) ?? .direct
        isComp = try c.decodeIfPresent(Bool.self, forKey: .isComp) ?? false
        discountPercent = try c.decodeIfPresent(Decimal.self, forKey: .discountPercent) ?? 0
        modifiersPriceExtra = try c.decodeIfPresent(Money.self, forKey: .modifiersPriceExtra) ?? .zero
        taxRateTakeawayPercent = try c.decodeIfPresent(Decimal.self, forKey: .taxRateTakeawayPercent)
        isHappyHourApplied = try c.decodeIfPresent(Bool.self, forKey: .isHappyHourApplied) ?? false
        originalUnitPrice = try c.decodeIfPresent(Money.self, forKey: .originalUnitPrice)
        appliedHappyHourScheduleId = try c.decodeIfPresent(UUID.self, forKey: .appliedHappyHourScheduleId)
    }
}

public struct ActiveOrder: Codable, Hashable, Sendable {
    public var orderId: UUID
    public var tableNumber: String
    public var waiterName: String?
    public var coversCount: Int
    public var openedAtUtc: Date?
    public var lines: [OrderLine]
    public var totalTtcAmount: Money?
    public var globalDiscountType: DiscountType?
    public var globalDiscountValue: Decimal
    public var globalDiscountReason: String?
    public var destination: OrderDestination
    public var pickupNumber: String?
    public var pickupBuzzer: String?

    public init(orderId: UUID = UUID(), tableNumber: String, waiterName: String? = nil, coversCount: Int = 0, openedAtUtc: Date? = Date(), lines: [OrderLine] = [], totalTtcAmount: Money? = nil, globalDiscountType: DiscountType? = nil, globalDiscountValue: Decimal = 0, globalDiscountReason: String? = nil, destination: OrderDestination = .takeaway, pickupNumber: String? = nil, pickupBuzzer: String? = nil) {
        self.orderId = orderId
        self.tableNumber = tableNumber
        self.waiterName = waiterName
        self.coversCount = coversCount
        self.openedAtUtc = openedAtUtc
        self.lines = lines
        self.totalTtcAmount = totalTtcAmount
        self.globalDiscountType = globalDiscountType
        self.globalDiscountValue = globalDiscountValue
        self.globalDiscountReason = globalDiscountReason
        self.destination = destination
        self.pickupNumber = pickupNumber
        self.pickupBuzzer = pickupBuzzer
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        orderId = try c.decode(UUID.self, forKey: .orderId)
        tableNumber = try c.decodeIfPresent(String.self, forKey: .tableNumber) ?? ""
        waiterName = try c.decodeIfPresent(String.self, forKey: .waiterName)
        coversCount = try c.decodeIfPresent(Int.self, forKey: .coversCount) ?? 0
        openedAtUtc = try c.decodeIfPresent(Date.self, forKey: .openedAtUtc)
        lines = try c.decodeIfPresent([OrderLine].self, forKey: .lines) ?? []
        totalTtcAmount = try c.decodeIfPresent(Money.self, forKey: .totalTtcAmount)
        globalDiscountType = try c.decodeIfPresent(DiscountType.self, forKey: .globalDiscountType)
        globalDiscountValue = try c.decodeIfPresent(Decimal.self, forKey: .globalDiscountValue) ?? 0
        globalDiscountReason = try c.decodeIfPresent(String.self, forKey: .globalDiscountReason)
        destination = try c.decodeIfPresent(OrderDestination.self, forKey: .destination) ?? .takeaway
        pickupNumber = try c.decodeIfPresent(String.self, forKey: .pickupNumber)
        pickupBuzzer = try c.decodeIfPresent(String.self, forKey: .pickupBuzzer)
    }

    public var globalDiscount: GlobalDiscount? {
        guard let type = globalDiscountType, globalDiscountValue > 0 else { return nil }
        return GlobalDiscount(type: type, value: globalDiscountValue, reason: globalDiscountReason)
    }
}

public struct GlobalDiscount: Hashable, Sendable {
    public var type: DiscountType
    public var value: Decimal
    public var reason: String?

    public init(type: DiscountType, value: Decimal, reason: String? = nil) {
        self.type = type
        self.value = value
        self.reason = reason
    }

    public var label: String {
        switch type {
        case .percentage: "-\(value)%"
        case .fixedAmount: "-\(Money(euros: value).formatted)"
        case .comp: "Offert"
        }
    }
}

/// Ligne envoyée au serveur (`OrderItemInputDto`).
public struct OrderItemInput: Codable, Hashable, Sendable {
    public var productId: UUID
    public var productName: String
    public var quantity: Int
    public var unitPrice: Money
    public var taxRatePercent: Decimal
    public var preparationStationId: String?
    public var modifiers: [String]
    public var course: CourseType
    public var modifiersPriceExtra: Money
    public var taxRateTakeawayPercent: Decimal?
    public var isHappyHourApplied: Bool
    public var originalUnitPrice: Money?
    public var appliedHappyHourScheduleId: UUID?

    public init(productId: UUID, productName: String, quantity: Int, unitPrice: Money, taxRatePercent: Decimal, preparationStationId: String?, modifiers: [String], course: CourseType, modifiersPriceExtra: Money, taxRateTakeawayPercent: Decimal?, isHappyHourApplied: Bool, originalUnitPrice: Money?, appliedHappyHourScheduleId: UUID?) {
        self.productId = productId
        self.productName = productName
        self.quantity = quantity
        self.unitPrice = unitPrice
        self.taxRatePercent = taxRatePercent
        self.preparationStationId = preparationStationId
        self.modifiers = modifiers
        self.course = course
        self.modifiersPriceExtra = modifiersPriceExtra
        self.taxRateTakeawayPercent = taxRateTakeawayPercent
        self.isHappyHourApplied = isHappyHourApplied
        self.originalUnitPrice = originalUnitPrice
        self.appliedHappyHourScheduleId = appliedHappyHourScheduleId
    }
}

// MARK: - Encaissement

public struct TenderInput: Codable, Hashable, Sendable {
    public var method: PaymentMethod
    public var amount: Money
    public var tendered: Money
    public var changeGiven: Money

    public init(method: PaymentMethod, amount: Money, tendered: Money, changeGiven: Money) {
        self.method = method
        self.amount = amount
        self.tendered = tendered
        self.changeGiven = changeGiven
    }
}

public struct PaymentRequest: Codable, Hashable, Sendable {
    public var orderId: UUID?
    public var tableNumber: String
    public var operatorId: UUID?
    public var terminalId: String
    public var tenders: [TenderInput]

    public init(orderId: UUID?, tableNumber: String, operatorId: UUID?, terminalId: String, tenders: [TenderInput]) {
        self.orderId = orderId
        self.tableNumber = tableNumber
        self.operatorId = operatorId
        self.terminalId = terminalId
        self.tenders = tenders
    }
}

public struct PaymentResult: Codable, Hashable, Sendable {
    public var receiptNumber: String?
    public var totalPaid: Money
    public var changeGiven: Money
    public var remainingBalance: Money
    public var fiscalSignature: String?

    public init(receiptNumber: String?, totalPaid: Money, changeGiven: Money, remainingBalance: Money, fiscalSignature: String?) {
        self.receiptNumber = receiptNumber
        self.totalPaid = totalPaid
        self.changeGiven = changeGiven
        self.remainingBalance = remainingBalance
        self.fiscalSignature = fiscalSignature
    }
}

public struct CounterTender: Codable, Hashable, Sendable {
    public var method: PaymentMethod
    public var amount: Money
    public var tendered: Money
    public var facialValue: Money?

    public init(method: PaymentMethod, amount: Money, tendered: Money, facialValue: Money? = nil) {
        self.method = method
        self.amount = amount
        self.tendered = tendered
        self.facialValue = facialValue
    }
}

public struct CounterCheckoutRequest: Codable, Hashable, Sendable {
    public var orderId: UUID
    public var terminalId: String
    public var destination: OrderDestination
    public var pickupBuzzer: String?
    public var tipAmount: Money
    public var requestFiscalReceiptPrint: Bool
    public var mealVoucherPolicy: MealVoucherPolicy
    public var tenders: [CounterTender]

    public init(orderId: UUID, terminalId: String, destination: OrderDestination, pickupBuzzer: String?, tipAmount: Money, requestFiscalReceiptPrint: Bool, mealVoucherPolicy: MealVoucherPolicy, tenders: [CounterTender]) {
        self.orderId = orderId
        self.terminalId = terminalId
        self.destination = destination
        self.pickupBuzzer = pickupBuzzer
        self.tipAmount = tipAmount
        self.requestFiscalReceiptPrint = requestFiscalReceiptPrint
        self.mealVoucherPolicy = mealVoucherPolicy
        self.tenders = tenders
    }
}

public struct CreditVoucher: Codable, Hashable, Sendable {
    public var voucherCode: String
    public var amount: Money
    public var expiresAtUtc: Date?
}

public struct CounterCheckoutResult: Codable, Hashable, Sendable {
    public var orderId: UUID
    public var pickupNumber: String
    public var totalPaid: Money
    public var changeGiven: Money
    public var remainingBalance: Money
    public var receiptNumber: String?
    public var fiscalSignature: String?
    public var issuedCreditVoucher: CreditVoucher?
    public var openCashDrawer: Bool?

    public init(orderId: UUID, pickupNumber: String, totalPaid: Money, changeGiven: Money, remainingBalance: Money, receiptNumber: String?, fiscalSignature: String?, issuedCreditVoucher: CreditVoucher?, openCashDrawer: Bool?) {
        self.orderId = orderId
        self.pickupNumber = pickupNumber
        self.totalPaid = totalPaid
        self.changeGiven = changeGiven
        self.remainingBalance = remainingBalance
        self.receiptNumber = receiptNumber
        self.fiscalSignature = fiscalSignature
        self.issuedCreditVoucher = issuedCreditVoucher
        self.openCashDrawer = openCashDrawer
    }
}

public struct HeldOrder: Codable, Identifiable, Hashable, Sendable {
    public var holdId: UUID
    public var terminalId: String?
    public var orderId: UUID
    public var customerLabel: String?
    public var destination: OrderDestination
    public var itemCount: Int
    public var totalTtc: CentsAmount
    public var heldAtUtc: Date

    public var id: UUID { holdId }

    public init(holdId: UUID = UUID(), terminalId: String?, orderId: UUID, customerLabel: String?, destination: OrderDestination, itemCount: Int, totalTtc: Money, heldAtUtc: Date = Date()) {
        self.holdId = holdId
        self.terminalId = terminalId
        self.orderId = orderId
        self.customerLabel = customerLabel
        self.destination = destination
        self.itemCount = itemCount
        self.totalTtc = CentsAmount(amountInCents: totalTtc.cents, currency: "EUR")
        self.heldAtUtc = heldAtUtc
    }
}

public struct HotelRoom: Codable, Identifiable, Hashable, Sendable {
    public var roomNumber: String
    public var guestName: String
    public var maxCreditLimit: Money
    public var currentBalance: Money

    public var id: String { roomNumber }
    public var availableCredit: Money { (maxCreditLimit - currentBalance).clampedAtZero() }

    public init(roomNumber: String, guestName: String, maxCreditLimit: Money, currentBalance: Money = .zero) {
        self.roomNumber = roomNumber
        self.guestName = guestName
        self.maxCreditLimit = maxCreditLimit
        self.currentBalance = currentBalance
    }
}

public struct RoomChargeRequest: Codable, Hashable, Sendable {
    public var orderId: UUID?
    public var tableNumber: String
    public var roomNumber: String
    public var guestName: String
    public var amount: Money
    public var tipAmount: Money
    public var signatureDataUrl: String?
    public var notes: String?

    public init(orderId: UUID?, tableNumber: String, roomNumber: String, guestName: String, amount: Money, tipAmount: Money, signatureDataUrl: String?, notes: String?) {
        self.orderId = orderId
        self.tableNumber = tableNumber
        self.roomNumber = roomNumber
        self.guestName = guestName
        self.amount = amount
        self.tipAmount = tipAmount
        self.signatureDataUrl = signatureDataUrl
        self.notes = notes
    }
}

/// Réponse générique `{ success, message }` utilisée par plusieurs endpoints.
public struct OperationResult: Codable, Hashable, Sendable {
    public var success: Bool?
    public var message: String?

    public init(success: Bool? = true, message: String? = nil) {
        self.success = success
        self.message = message
    }
}
