import Foundation

// Entrées des constructeurs de tickets. Les membres (camelCase) reprennent les `input`
// de la fixture `ticket_document_reference.json`, générée par le .NET.

struct TicketSettings: Codable, Equatable, Sendable {
    var companyName: String
    var addressLines: String
    var siret: String
    var vatNumber: String
    var certificateNumber: String?
}

struct TicketOrderItem: Codable, Equatable, Sendable {
    var quantity: Int
    var productName: String
    var productId: UUID?
    var totalTtcCents: Int
    var selectedModifiers: [String]
    var course: CourseType
}

struct TicketOrder: Codable, Equatable, Sendable {
    var destination: OrderDestination
    var tableNumber: String
    var pickupNumber: String?
    var pickupBuzzer: String?
    var items: [TicketOrderItem]
}

struct TicketReceipt: Codable, Equatable, Sendable {
    var receiptNumber: String
    var createdAtUtc: Date
    var terminalId: String
    var totalTtcCents: Int
    var totalHtCents: Int
    var taxBreakdownJson: String
    var signatureHash: String
}

struct TicketKitchenItem: Codable, Equatable, Sendable {
    var quantity: Int
    var productName: String
    var productId: UUID?
    var modifiersSummary: String?
    var kitchenComment: String?
}

/// Allergènes d'un plat tels que déclarés au catalogue ; `declared` faux = « non renseignés » (≠ « aucun »).
struct TicketProductAllergens: Codable, Equatable, Sendable {
    var declared: Bool
    var mask: Int
}

/// Option « allergènes sur le ticket de cuisine » : par produit (identifiant en texte, comme le JSON du .NET) et mention légale facultative.
struct TicketKitchenAllergens: Codable, Equatable, Sendable {
    var byProduct: [String: TicketProductAllergens]
    var disclaimer: String?
}

struct TicketKitchen: Codable, Equatable, Sendable {
    var serverName: String
    var coversCount: Int
    var dispatchedAtUtc: Date
    var items: [TicketKitchenItem]
}

/// Le taux est une chaîne décimale dans le JSON (`{"rate":"5.5","cents":123}`).
struct TicketRateAmount: Codable, Equatable, Sendable {
    var rate: Decimal
    var cents: Int

    init(rate: Decimal, cents: Int) {
        self.rate = rate
        self.cents = cents
    }

    private enum Keys: String, CodingKey { case rate, cents }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        let text = try c.decode(String.self, forKey: .rate)
        guard let value = Decimal(string: text, locale: Locale(identifier: "en_US_POSIX")) else {
            throw DecodingError.dataCorruptedError(forKey: .rate, in: c, debugDescription: "Taux invalide : \(text)")
        }
        rate = value
        cents = try c.decode(Int.self, forKey: .cents)
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: Keys.self)
        try c.encode(rate.description, forKey: .rate)
        try c.encode(cents, forKey: .cents)
    }
}

struct TicketMethodAmount: Codable, Equatable, Sendable {
    var method: Int
    var cents: Int
}

struct TicketReportSummary: Codable, Equatable, Sendable {
    var terminalId: String
    var periodStartUtc: Date?
    var periodEndUtc: Date
    var totalSalesTtcCents: Int
    var totalSalesHtCents: Int
    var receiptCount: Int
    var vatBreakdownCents: [TicketRateAmount]
    var paymentTotalsCents: [TicketMethodAmount]
    var perpetualGrandTotalCents: Int
}

struct TicketDailyClosure: Codable, Equatable, Sendable {
    var terminalId: String
    var closureSequence: Int
    var totalSalesTtcCents: Int
    var totalSalesHtCents: Int
    var receiptCount: Int
    var vatBreakdownCents: [TicketRateAmount]
    var paymentTotalsCents: [TicketMethodAmount]
    var perpetualGrandTotalCents: Int
    var signatureHash: String
    var closedAtUtc: Date
    var periodStartUtc: Date?
    var sealedByUserName: String
}

struct TicketPeriodClosure: Codable, Equatable, Sendable {
    var terminalId: String
    var isMonthly: Bool
    var periodKey: String
    var closureSequence: Int
    var periodStartUtc: Date?
    var periodEndUtc: Date
    var totalTtcCents: Int
    var totalHtCents: Int
    var taxesSummaryJson: String
    var tenderTotalsJson: String
    var perpetualGrandTotalCents: Int
    var dailyClosureCount: Int
    var signatureHash: String
    var sealedByUserName: String
}

struct ReportItemLine: Codable, Equatable, Sendable {
    var productName: String
    var quantity: Int
    var totalTtcCents: Int
}

struct ReportCategoryGroup: Codable, Equatable, Sendable {
    var categoryName: String?
    var items: [ReportItemLine]
    var subtotalTtcCents: Int
}

struct ReportTipLine: Codable, Equatable, Sendable {
    var serverName: String?
    var tipCents: Int
}

/// Remises, offerts et annulations (avoirs) de la période ; informatif, comme le reste de `ReportPrintData`.
struct ReportActivity: Codable, Equatable, Sendable {
    var discountsCents: Int
    var compsCents: Int
    var voidCount: Int
    var voidCents: Int
}

/// Dernier comptage de caisse de la période (fond, compté, écart).
struct ReportCash: Codable, Equatable, Sendable {
    var floatCents: Int
    var countedCents: Int
    var varianceCents: Int
}

struct ReportPrintData: Codable, Equatable, Sendable {
    var categories: [ReportCategoryGroup]
    var tips: [ReportTipLine]
    var totalTipsCents: Int
    var hasGlobalDiscount: Bool
    var activity: ReportActivity?
    var cash: ReportCash?
    static let empty = ReportPrintData(categories: [], tips: [], totalTipsCents: 0, hasGlobalDiscount: false)
}
