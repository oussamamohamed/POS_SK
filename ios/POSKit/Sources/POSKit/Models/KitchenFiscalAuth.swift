import Foundation

// MARK: - Cuisine (KDS)

/// Bon cuisine (`GET /api/kds/tickets`, entité `KitchenTicket`).
public struct KitchenTicket: Identifiable, Hashable, Decodable, Sendable {
    public var id: UUID
    public var orderId: UUID
    public var tableNumber: String
    public var serverName: String
    public var coversCount: Int
    public var stationId: String
    public var status: TicketStatus
    public var dispatchedAtUtc: Date?
    public var items: [KitchenTicketItem]

    public init(
        id: UUID = UUID(),
        orderId: UUID,
        tableNumber: String,
        serverName: String = "",
        coversCount: Int = 1,
        stationId: String,
        status: TicketStatus = .pending,
        dispatchedAtUtc: Date? = nil,
        items: [KitchenTicketItem]
    ) {
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

    enum CodingKeys: String, CodingKey {
        case id, ticketId, orderId, tableNumber, serverName, coversCount, stationId, status, dispatchedAtUtc, items
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        // `GET /api/kds/tickets` renvoie l'entité (`id`), le bump renvoie `KitchenTicketDto` (`ticketId`).
        if let ticketId = try c.decodeIfPresent(UUID.self, forKey: .ticketId) {
            id = ticketId
        } else {
            id = try c.decode(UUID.self, forKey: .id)
        }
        orderId = try c.decodeIfPresent(UUID.self, forKey: .orderId) ?? UUID()
        tableNumber = try c.decodeIfPresent(String.self, forKey: .tableNumber) ?? "?"
        serverName = try c.decodeIfPresent(String.self, forKey: .serverName) ?? ""
        coversCount = try c.decodeIfPresent(Int.self, forKey: .coversCount) ?? 1
        stationId = try c.decodeIfPresent(String.self, forKey: .stationId) ?? ""
        status = try c.decodeIfPresent(TicketStatus.self, forKey: .status) ?? .pending
        dispatchedAtUtc = try c.decodeIfPresent(Date.self, forKey: .dispatchedAtUtc)
        items = try c.decodeIfPresent([KitchenTicketItem].self, forKey: .items) ?? []
    }

    /// Minutes écoulées depuis l'envoi en cuisine.
    public func elapsedMinutes(now: Date = Date()) -> Int {
        guard let dispatchedAtUtc else { return 0 }
        return max(0, Int(now.timeIntervalSince(dispatchedAtUtc) / 60))
    }

    /// Libellé lisible du poste de préparation (`HOT_KITCHEN` → `Cuisine chaude`).
    public var stationLabel: String { KitchenTicket.stationLabel(for: stationId) }

    public static func stationLabel(for stationId: String) -> String {
        switch stationId.uppercased() {
        case "HOT_KITCHEN", "HOT": "Cuisine chaude"
        case "COLD_KITCHEN", "COLD": "Cuisine froide"
        case "BAR": "Bar"
        case "PASTRY", "DESSERT": "Pâtisserie"
        case "": "Cuisine"
        default: stationId
        }
    }
}

public struct KitchenTicketItem: Identifiable, Hashable, Decodable, Sendable {
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
        id = try c.decodeIfPresent(UUID.self, forKey: .itemId)
            ?? c.decodeIfPresent(UUID.self, forKey: .id)
            ?? UUID()
        productName = try c.decodeIfPresent(String.self, forKey: .productName) ?? "?"
        quantity = try c.decodeIfPresent(Int.self, forKey: .quantity) ?? 1
        modifiersSummary = try c.decodeIfPresent(String.self, forKey: .modifiersSummary)
        kitchenComment = try c.decodeIfPresent(String.self, forKey: .kitchenComment)
    }
}

// MARK: - Fiscal (NF525)

/// Rapport X (en cours) ou clôture Z (scellée) — même forme JSON côté API.
public struct FiscalReport: Hashable, Codable, Sendable {
    public var closureSequence: Int?
    public var terminalId: String?
    public var totalSalesTtc: Money
    public var totalSalesHt: Money
    public var receiptCount: Int
    /// Clé : taux de TVA (`"10"`, `"20"`, `"5.5"`).
    public var vatBreakdown: [String: Money]
    /// Clé : nom C# du moyen de paiement (`"Cash"`, `"CreditCard"`).
    public var paymentTotals: [String: Money]
    public var perpetualGrandTotal: Money
    public var signatureHash: String?
    public var closedAtUtc: Date?
    public var periodStartUtc: Date?
    public var periodEndUtc: Date?

    public init(
        closureSequence: Int? = nil,
        terminalId: String? = nil,
        totalSalesTtc: Money,
        totalSalesHt: Money,
        receiptCount: Int,
        vatBreakdown: [String: Money] = [:],
        paymentTotals: [String: Money] = [:],
        perpetualGrandTotal: Money,
        signatureHash: String? = nil,
        closedAtUtc: Date? = nil,
        periodStartUtc: Date? = nil,
        periodEndUtc: Date? = nil
    ) {
        self.closureSequence = closureSequence
        self.terminalId = terminalId
        self.totalSalesTtc = totalSalesTtc
        self.totalSalesHt = totalSalesHt
        self.receiptCount = receiptCount
        self.vatBreakdown = vatBreakdown
        self.paymentTotals = paymentTotals
        self.perpetualGrandTotal = perpetualGrandTotal
        self.signatureHash = signatureHash
        self.closedAtUtc = closedAtUtc
        self.periodStartUtc = periodStartUtc
        self.periodEndUtc = periodEndUtc
    }

    enum CodingKeys: String, CodingKey {
        case closureSequence, terminalId, totalSalesTtc, totalSalesHt, receiptCount, vatBreakdown
        case paymentTotals, perpetualGrandTotal, signatureHash, closedAtUtc, periodStartUtc, periodEndUtc
    }

    public init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: CodingKeys.self)
        closureSequence = try c.decodeIfPresent(Int.self, forKey: .closureSequence)
        terminalId = try c.decodeIfPresent(String.self, forKey: .terminalId)
        totalSalesTtc = try c.decodeIfPresent(Money.self, forKey: .totalSalesTtc) ?? .zero
        totalSalesHt = try c.decodeIfPresent(Money.self, forKey: .totalSalesHt) ?? .zero
        receiptCount = try c.decodeIfPresent(Int.self, forKey: .receiptCount) ?? 0
        vatBreakdown = try c.decodeIfPresent([String: Money].self, forKey: .vatBreakdown) ?? [:]
        paymentTotals = try c.decodeIfPresent([String: Money].self, forKey: .paymentTotals) ?? [:]
        perpetualGrandTotal = try c.decodeIfPresent(Money.self, forKey: .perpetualGrandTotal) ?? .zero
        signatureHash = try c.decodeIfPresent(String.self, forKey: .signatureHash)
        closedAtUtc = try c.decodeIfPresent(Date.self, forKey: .closedAtUtc)
        periodStartUtc = try c.decodeIfPresent(Date.self, forKey: .periodStartUtc)
        periodEndUtc = try c.decodeIfPresent(Date.self, forKey: .periodEndUtc)
    }

    public var isSealedClosure: Bool { signatureHash != nil && closureSequence != nil }
    public var totalVat: Money { totalSalesTtc - totalSalesHt }

    /// Lignes TVA triées par taux croissant.
    public var sortedVat: [(rate: String, amount: Money)] {
        vatBreakdown
            .map { (rate: $0.key, amount: $0.value) }
            .sorted { (Decimal(string: $0.rate) ?? 0) < (Decimal(string: $1.rate) ?? 0) }
    }

    /// Lignes de règlement triées par montant décroissant, avec libellés français.
    public var sortedPayments: [(label: String, amount: Money)] {
        paymentTotals
            .map { (label: PaymentMethod.label(forAPIName: $0.key), amount: $0.value) }
            .sorted { $0.amount > $1.amount }
    }
}

struct ZClosureRequest: Encodable {
    var terminalId: String
    var managerId: UUID
    var managerName: String
}

// MARK: - Authentification

/// Opérateur authentifié par code PIN.
public struct Operator: Hashable, Codable, Sendable {
    public var id: UUID
    public var name: String
    public var role: UserRole

    public init(id: UUID, name: String, role: UserRole) {
        self.id = id
        self.name = name
        self.role = role
    }

    /// Prénom + initiale pour l'en-tête (« Alexandre D. »).
    public var shortName: String {
        let cleaned = name.components(separatedBy: " (").first ?? name
        let parts = cleaned.split(separator: " ")
        guard parts.count > 1, let initial = parts[1].first else { return cleaned }
        return "\(parts[0]) \(initial)."
    }
}

public struct OperatorSession: Hashable, Sendable {
    public var `operator`: Operator
    public var token: String

    public init(operator: Operator, token: String) {
        self.operator = `operator`
        self.token = token
    }
}

struct PinLoginRequest: Encodable {
    var pin: String
}

struct LoginResponse: Decodable {
    var success: Bool?
    var operatorId: UUID?
    var operatorName: String?
    var role: UserRole?
    var token: String?
}

struct HealthResponse: Decodable {
    var status: String?
    var version: String?
}
