import Foundation

/// Une tendance à surveiller. `metric` : `cumulativeTtc`, `avgTicket`, `perCover` (centimes) ou `takeawayShare` (centièmes de pour cent).
public struct InsightCard: Codable, Hashable, Sendable, Identifiable {
    public var metric: String
    public var currentCents: Int
    public var referenceCents: Int
    public var deltaPercent: Decimal
    public var id: String { metric }

    public init(metric: String, currentCents: Int, referenceCents: Int, deltaPercent: Decimal) {
        self.metric = metric
        self.currentCents = currentCents
        self.referenceCents = referenceCents
        self.deltaPercent = deltaPercent
    }
}

public struct Insights: Codable, Hashable, Sendable {
    public var cards: [InsightCard]
    public var insufficientData: Bool

    public init(cards: [InsightCard] = [], insufficientData: Bool = false) {
        self.cards = cards
        self.insufficientData = insufficientData
    }
}

public struct TipSummary: Codable, Hashable, Sendable {
    public var serverName: String
    public var tipsCents: Int

    public init(serverName: String, tipsCents: Int) {
        self.serverName = serverName
        self.tipsCents = tipsCents
    }
}

public struct DailySummaryCash: Codable, Hashable, Sendable {
    public var floatCents: Int
    public var countedCents: Int
    public var varianceCents: Int

    public init(floatCents: Int, countedCents: Int, varianceCents: Int) {
        self.floatCents = floatCents
        self.countedCents = countedCents
        self.varianceCents = varianceCents
    }
}

/// Résumé de fin de journée : totaux du dernier Z du poste (donc identiques), pourboires par serveur, comptage de caisse, alertes ouvertes.
public struct DailySummary: Codable, Hashable, Sendable {
    public var terminalId: String
    public var closureSequence: Int
    public var closedAtUtc: Date
    public var totalSalesTtcCents: Int
    public var totalSalesHtCents: Int
    public var receiptCount: Int
    public var covers: Int
    public var averageTicketCents: Int
    public var paymentTotalsCents: [String: Int]
    public var tipsByServer: [TipSummary]
    public var cash: DailySummaryCash?
    public var openAlerts: [PilotAlert]

    public init(terminalId: String, closureSequence: Int, closedAtUtc: Date, totalSalesTtcCents: Int, totalSalesHtCents: Int, receiptCount: Int, covers: Int, averageTicketCents: Int,
                paymentTotalsCents: [String: Int] = [:], tipsByServer: [TipSummary] = [], cash: DailySummaryCash? = nil, openAlerts: [PilotAlert] = []) {
        self.terminalId = terminalId
        self.closureSequence = closureSequence
        self.closedAtUtc = closedAtUtc
        self.totalSalesTtcCents = totalSalesTtcCents
        self.totalSalesHtCents = totalSalesHtCents
        self.receiptCount = receiptCount
        self.covers = covers
        self.averageTicketCents = averageTicketCents
        self.paymentTotalsCents = paymentTotalsCents
        self.tipsByServer = tipsByServer
        self.cash = cash
        self.openAlerts = openAlerts
    }

    /// Texte partageable (ShareLink) : mêmes chiffres que le Z.
    public var shareText: String {
        var lines = ["\(terminalId) · Z \(closureSequence)", "\(L10n.string("summary.sales")) : \(Money(cents: totalSalesTtcCents).formatted)",
                     "\(L10n.string("summary.receipts")) : \(receiptCount)", "\(L10n.string("summary.avg_ticket")) : \(Money(cents: averageTicketCents).formatted)",
                     "\(L10n.string("summary.covers")) : \(covers)"]
        lines += paymentTotalsCents.sorted { $0.key < $1.key }.map { "\($0.key) : \(Money(cents: $0.value).formatted)" }
        lines += tipsByServer.map { "\(L10n.string("summary.tips")) \($0.serverName) \(Money(cents: $0.tipsCents).formatted)" }
        if let cash { lines.append("\(L10n.string("summary.cash")) : \(Money(cents: cash.floatCents).formatted) → \(Money(cents: cash.countedCents).formatted) (\(Money(cents: cash.varianceCents).formatted))") }
        lines.append("\(L10n.string("summary.open_alerts")) : \(openAlerts.count)")
        return lines.joined(separator: "\n")
    }
}
