import Foundation

public struct CashMovement: Codable, Hashable, Sendable, Identifiable {
    public var id: UUID
    public var kind: String
    public var amountCents: Int
    public var reason: String
    public var operatorId: UUID
    public var createdAtUtc: Date

    public init(id: UUID = UUID(), kind: String, amountCents: Int, reason: String, operatorId: UUID, createdAtUtc: Date = Date()) {
        self.id = id
        self.kind = kind
        self.amountCents = amountCents
        self.reason = reason
        self.operatorId = operatorId
        self.createdAtUtc = createdAtUtc
    }
}

/// Comptage de clôture : attendu et écart figés.
public struct CashCount: Codable, Hashable, Sendable {
    public var expectedCents: Int
    public var countedCents: Int
    public var varianceCents: Int
    public var varianceNote: String?
    public var denominationsJson: String?
    public var closedAtUtc: Date

    public init(expectedCents: Int, countedCents: Int, varianceCents: Int, varianceNote: String? = nil, denominationsJson: String? = nil, closedAtUtc: Date = Date()) {
        self.expectedCents = expectedCents
        self.countedCents = countedCents
        self.varianceCents = varianceCents
        self.varianceNote = varianceNote
        self.denominationsJson = denominationsJson
        self.closedAtUtc = closedAtUtc
    }
}

/// Session de caisse d'un poste. `expectedCents` : en direct tant qu'elle est ouverte, figé par le comptage ensuite.
public struct CashSession: Codable, Hashable, Sendable, Identifiable {
    public var id: UUID
    public var terminalId: String
    public var openedAtUtc: Date
    public var openedByOperatorId: UUID
    public var openingFloatCents: Int
    public var expectedCents: Int
    public var movements: [CashMovement]
    public var count: CashCount?

    public init(id: UUID = UUID(), terminalId: String, openedAtUtc: Date = Date(), openedByOperatorId: UUID, openingFloatCents: Int, expectedCents: Int,
                movements: [CashMovement] = [], count: CashCount? = nil) {
        self.id = id
        self.terminalId = terminalId
        self.openedAtUtc = openedAtUtc
        self.openedByOperatorId = openedByOperatorId
        self.openingFloatCents = openingFloatCents
        self.expectedCents = expectedCents
        self.movements = movements
        self.count = count
    }
}

public struct CashDenomination: Codable, Hashable, Sendable {
    public var valueCents: Int
    public var quantity: Int

    public init(valueCents: Int, quantity: Int) {
        self.valueCents = valueCents
        self.quantity = quantity
    }

    /// Coupures proposées à l'écran, de la plus grande à la plus petite.
    public static let all = [50_000, 20_000, 10_000, 5_000, 2_000, 1_000, 500, 200, 100, 50, 20, 10, 5, 2, 1]
}

public struct CashCountResult: Codable, Hashable, Sendable {
    public var expectedCents: Int
    public var countedCents: Int
    public var varianceCents: Int
    public var alertCreated: Bool

    public init(expectedCents: Int, countedCents: Int, varianceCents: Int, alertCreated: Bool = false) {
        self.expectedCents = expectedCents
        self.countedCents = countedCents
        self.varianceCents = varianceCents
        self.alertCreated = alertCreated
    }
}

/// Refus métier de la caisse ou des alertes ; `code` est celui renvoyé par l'API.
public enum CashError: Error, Equatable, Sendable {
    case sessionAlreadyOpen, sessionClosed, movementInvalid, varianceNoteRequired, invalid, noteRequired, ruleInvalid

    public var code: String {
        switch self {
        case .sessionAlreadyOpen: "cash_session_already_open"
        case .sessionClosed: "cash_session_closed"
        case .movementInvalid: "movement_invalid"
        case .varianceNoteRequired: "variance_note_required"
        case .invalid: "cash_invalid"
        case .noteRequired: "note_required"
        case .ruleInvalid: "alert_rule_invalid"
        }
    }

    public init?(code: String) {
        switch code {
        case "cash_session_already_open": self = .sessionAlreadyOpen
        case "cash_session_closed": self = .sessionClosed
        case "movement_invalid": self = .movementInvalid
        case "variance_note_required": self = .varianceNoteRequired
        case "cash_invalid": self = .invalid
        case "note_required": self = .noteRequired
        case "alert_rule_invalid": self = .ruleInvalid
        default: return nil
        }
    }
}

extension CashError: LocalizedError {
    public var errorDescription: String? { L10n.string("cash.err.\(code)") }
}

public enum AlertRuleType {
    public static let cashVariance = "cash_variance", voidsCount = "voids_count", discountPercent = "discount_percent", compsTotal = "comps_total"
    public static let all = [cashVariance, voidsCount, discountPercent, compsTotal]
}

/// Alerte de pilotage (le nom `Alert` est pris par SwiftUI). `detail` : JSON du serveur (nombre, montant, identifiants).
public struct PilotAlert: Codable, Hashable, Sendable, Identifiable {
    public var id: UUID
    public var ruleType: String
    public var operatorId: UUID?
    public var operatorName: String?
    public var terminalId: String
    public var periodKey: String
    public var detailJson: String
    public var state: String
    public var createdAtUtc: Date
    public var resolvedAtUtc: Date?
    public var resolutionNote: String?

    public init(id: UUID = UUID(), ruleType: String, operatorId: UUID? = nil, operatorName: String? = nil, terminalId: String, periodKey: String, detailJson: String = "{}",
                state: String = "open", createdAtUtc: Date = Date(), resolvedAtUtc: Date? = nil, resolutionNote: String? = nil) {
        self.id = id
        self.ruleType = ruleType
        self.operatorId = operatorId
        self.operatorName = operatorName
        self.terminalId = terminalId
        self.periodKey = periodKey
        self.detailJson = detailJson
        self.state = state
        self.createdAtUtc = createdAtUtc
        self.resolvedAtUtc = resolvedAtUtc
        self.resolutionNote = resolutionNote
    }

    public var isOpen: Bool { state == "open" }

    /// Valeurs numériques du détail (`count`, `totalCents`, `maxPercent`, `varianceCents`).
    public func detailInt(_ key: String) -> Int? {
        (try? JSONSerialization.jsonObject(with: Data(detailJson.utf8)) as? [String: Any])?[key].flatMap { ($0 as? NSNumber)?.intValue }
    }
}

public struct AlertRule: Codable, Hashable, Sendable, Identifiable {
    public var type: String
    public var threshold: Int
    public var enabled: Bool
    public var id: String { type }

    public init(type: String, threshold: Int, enabled: Bool) {
        self.type = type
        self.threshold = threshold
        self.enabled = enabled
    }
}

public struct AlertSettings: Codable, Hashable, Sendable {
    public var trendThresholdPercent: Int
    public var cashVarianceNoteRequired: Bool

    public init(trendThresholdPercent: Int = 15, cashVarianceNoteRequired: Bool = true) {
        self.trendThresholdPercent = trendThresholdPercent
        self.cashVarianceNoteRequired = cashVarianceNoteRequired
    }
}

/// Comptage de caisse rattaché à un rapport Z (lecture seule, hors de tout contenu signé).
public struct CashZSummary: Hashable, Sendable {
    public var floatCents: Int
    public var countedCents: Int
    public var varianceCents: Int

    public init(floatCents: Int, countedCents: Int, varianceCents: Int) {
        self.floatCents = floatCents
        self.countedCents = countedCents
        self.varianceCents = varianceCents
    }
}
