import Foundation

/// `POST /api/checkout/pay` (`PaymentSettlementRequest`).
public struct PaymentSettlementRequest: Hashable, Codable, Sendable {
    public var orderId: UUID
    public var tableNumber: String?
    public var operatorId: UUID?
    public var tenders: [TenderItem]
    public var terminalId: String

    public init(orderId: UUID, tableNumber: String?, operatorId: UUID?, tenders: [TenderItem], terminalId: String) {
        self.orderId = orderId
        self.tableNumber = tableNumber
        self.operatorId = operatorId
        self.tenders = tenders
        self.terminalId = terminalId
    }
}

/// Un règlement (`TenderItemRequest`) : `amount` imputé sur la note, `tendered` remis par le client.
public struct TenderItem: Hashable, Codable, Sendable {
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

/// Réponse d'encaissement en salle.
public struct PaymentResult: Hashable, Codable, Sendable {
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

    enum CodingKeys: String, CodingKey { case receiptNumber, totalPaid, changeGiven, remainingBalance, fiscalSignature }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        receiptNumber = try c.decodeIfPresent(String.self, forKey: .receiptNumber)
        totalPaid = try c.decodeIfPresent(Money.self, forKey: .totalPaid) ?? .zero
        changeGiven = try c.decodeIfPresent(Money.self, forKey: .changeGiven) ?? .zero
        remainingBalance = try c.decodeIfPresent(Money.self, forKey: .remainingBalance) ?? .zero
        fiscalSignature = try c.decodeIfPresent(String.self, forKey: .fiscalSignature)
    }
}

/// `POST /api/orders/counter/checkout` (`CounterCheckoutRequest`).
public struct CounterCheckoutRequest: Hashable, Codable, Sendable {
    public var orderId: UUID
    public var terminalId: String
    public var destination: OrderDestination
    public var pickupBuzzer: String?
    public var tipAmount: Money
    public var requestFiscalReceiptPrint: Bool
    public var mealVoucherPolicy: MealVoucherPolicy
    public var tenders: [CounterTender]

    public init(
        orderId: UUID,
        terminalId: String,
        destination: OrderDestination,
        pickupBuzzer: String? = nil,
        tipAmount: Money = .zero,
        requestFiscalReceiptPrint: Bool = false,
        mealVoucherPolicy: MealVoucherPolicy = .capAtBalance,
        tenders: [CounterTender]
    ) {
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

public struct CounterTender: Hashable, Codable, Sendable {
    public var method: PaymentMethod
    public var amount: Money
    public var tendered: Money
    /// Valeur faciale d'un titre-restaurant (contrôle du plafond côté serveur).
    public var facialValue: Money?

    public init(method: PaymentMethod, amount: Money, tendered: Money, facialValue: Money? = nil) {
        self.method = method
        self.amount = amount
        self.tendered = tendered
        self.facialValue = facialValue
    }
}

public struct CounterCheckoutResult: Hashable, Codable, Sendable {
    public var orderId: UUID
    public var pickupNumber: String
    public var totalPaid: Money
    public var changeGiven: Money
    public var remainingBalance: Money
    public var receiptNumber: String?
    public var fiscalSignature: String?
    public var issuedCreditVoucher: CreditVoucher?
    public var openCashDrawer: Bool

    public init(
        orderId: UUID,
        pickupNumber: String,
        totalPaid: Money,
        changeGiven: Money,
        remainingBalance: Money,
        receiptNumber: String?,
        fiscalSignature: String?,
        issuedCreditVoucher: CreditVoucher? = nil,
        openCashDrawer: Bool = false
    ) {
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

    enum CodingKeys: String, CodingKey {
        case orderId, pickupNumber, totalPaid, changeGiven, remainingBalance
        case receiptNumber, fiscalSignature, issuedCreditVoucher, openCashDrawer
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        orderId = try c.decode(UUID.self, forKey: .orderId)
        pickupNumber = try c.decodeIfPresent(String.self, forKey: .pickupNumber) ?? ""
        totalPaid = try c.decodeIfPresent(Money.self, forKey: .totalPaid) ?? .zero
        changeGiven = try c.decodeIfPresent(Money.self, forKey: .changeGiven) ?? .zero
        remainingBalance = try c.decodeIfPresent(Money.self, forKey: .remainingBalance) ?? .zero
        receiptNumber = try c.decodeIfPresent(String.self, forKey: .receiptNumber)
        fiscalSignature = try c.decodeIfPresent(String.self, forKey: .fiscalSignature)
        issuedCreditVoucher = try c.decodeIfPresent(CreditVoucher.self, forKey: .issuedCreditVoucher)
        openCashDrawer = try c.decodeIfPresent(Bool.self, forKey: .openCashDrawer) ?? false
    }
}

public struct CreditVoucher: Hashable, Codable, Sendable {
    public var voucherCode: String
    public var amount: Money
    public var expiresAtUtc: Date?

    public init(voucherCode: String, amount: Money, expiresAtUtc: Date?) {
        self.voucherCode = voucherCode
        self.amount = amount
        self.expiresAtUtc = expiresAtUtc
    }
}

/// Commande comptoir mise en attente (`HeldOrderDto`). `totalTtc` y est un `Money` .NET brut.
public struct HeldOrder: Identifiable, Hashable, Codable, Sendable {
    public var holdId: UUID
    public var terminalId: String
    public var orderId: UUID
    public var customerLabel: String?
    public var destination: OrderDestination
    public var itemCount: Int
    public var totalTtc: Money
    public var heldAtUtc: Date?

    public var id: UUID { holdId }

    public init(
        holdId: UUID,
        terminalId: String,
        orderId: UUID,
        customerLabel: String?,
        destination: OrderDestination,
        itemCount: Int,
        totalTtc: Money,
        heldAtUtc: Date?
    ) {
        self.holdId = holdId
        self.terminalId = terminalId
        self.orderId = orderId
        self.customerLabel = customerLabel
        self.destination = destination
        self.itemCount = itemCount
        self.totalTtc = totalTtc
        self.heldAtUtc = heldAtUtc
    }
}

struct DirectCounterOpenRequest: Encodable {
    var terminalId: String
    var destination: OrderDestination
}

struct SwitchDestinationRequest: Encodable {
    var destination: OrderDestination
}

struct HoldCounterOrderRequest: Encodable {
    var orderId: UUID
    var terminalId: String
    var staffId: UUID?
    var customerLabel: String?
}

struct VoidHeldOrderRequest: Encodable {
    var supervisorPin: String
    var voidReason: String
    var terminalId: String
}
