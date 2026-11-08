import Foundation
import Observation

/// Tiroir-caisse : session en cours, sorties et entrées, comptage de clôture, historique (responsable).
@MainActor @Observable
public final class CashStore {
    public private(set) var session: CashSession?
    public private(set) var history: [CashSession] = []
    public private(set) var isManagerView = false
    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func load() async {
        do { session = try await api.currentCashSession() } catch { session = nil; if !Self.isForbidden(error) { notifier.error(error) } }
        do { history = try await api.cashSessions(from: nil, to: nil).filter { $0.count != nil }; isManagerView = true } catch { history = []; isManagerView = false }
    }

    private static func isForbidden(_ error: Error) -> Bool { if case .forbidden = error as? APIError { true } else { false } }

    public func open(floatCents: Int) async -> Bool {
        await run { self.session = try await self.api.openCashSession(openingFloatCents: floatCents) }
    }

    public func move(kind: String, amountCents: Int, reason: String) async -> Bool {
        guard let id = session?.id else { return false }
        return await run { _ = try await self.api.addCashMovement(sessionId: id, kind: kind, amountCents: amountCents, reason: reason); self.session = try await self.api.currentCashSession() }
    }

    /// Compte la caisse ; le résultat (attendu, compté, écart) est annoncé dans un toast. `false` si refusé (note obligatoire, etc.).
    public func count(countedCents: Int, denominations: [CashDenomination], note: String?) async -> CashCountResult? {
        guard let id = session?.id else { return nil }
        do {
            let result = try await api.countCash(sessionId: id, countedCents: countedCents, denominations: denominations, varianceNote: note)
            let message = String(format: L10n.string("cash.count_result"), Money(cents: result.expectedCents).formatted, Money(cents: result.countedCents).formatted, Money(cents: result.varianceCents).formatted)
            if result.varianceCents == 0 { notifier.success(message) } else { notifier.warning(message) }
            await load()
            return result
        } catch {
            notifier.error(error)
            return nil
        }
    }

    private func run(_ operation: () async throws -> Void) async -> Bool {
        do { try await operation(); return true } catch { notifier.error(error); return false }
    }
}

/// Alertes de pilotage : seuils, liste à traiter ou traitée, clôture avec note.
@MainActor @Observable
public final class AlertStore {
    public private(set) var alerts: [PilotAlert] = []
    public private(set) var rules: [AlertRule] = []
    public private(set) var settings = AlertSettings()
    public var state = "open"
    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func load() async {
        do {
            alerts = try await api.alerts(state: state)
            rules = try await api.alertRules()
            settings = try await api.alertSettings()
        } catch { notifier.error(error) }
    }

    public func select(_ state: String) async {
        self.state = state
        await load()
    }

    public func resolve(_ alert: PilotAlert, note: String) async -> Bool {
        do {
            _ = try await api.resolveAlert(id: alert.id, note: note)
            await load()
            return true
        } catch { notifier.error(error); return false }
    }

    public func setRule(_ rule: AlertRule) async {
        do { _ = try await api.setAlertRule(type: rule.type, threshold: rule.threshold, enabled: rule.enabled); rules = try await api.alertRules() } catch { notifier.error(error) }
    }

    public func updateSettings(_ change: (inout AlertSettings) -> Void) async {
        var updated = settings
        change(&updated)
        do { settings = try await api.saveAlertSettings(updated) } catch { notifier.error(error) }
    }

    /// Phrase de synthèse d'une alerte (serveur, nombre, montant), dans la langue de l'app.
    public static func summary(of alert: PilotAlert) -> String {
        let who = alert.operatorName ?? "—"
        let euros = { (cents: Int?) in Money(cents: cents ?? 0).formatted }
        switch alert.ruleType {
        case AlertRuleType.voidsCount: return String(format: L10n.string("alerts.sum_voids"), who, alert.detailInt("count") ?? 0, euros(alert.detailInt("totalCents")))
        case AlertRuleType.discountPercent: return String(format: L10n.string("alerts.sum_discount"), who, alert.detailInt("count") ?? 0, alert.detailInt("maxPercent") ?? 0)
        case AlertRuleType.compsTotal: return String(format: L10n.string("alerts.sum_comps"), who, euros(alert.detailInt("totalCents")))
        default: return String(format: L10n.string("alerts.sum_cash"), who, alert.terminalId, euros(alert.detailInt("varianceCents")))
        }
    }
}

/// Tendances « à surveiller » et résumé de fin de journée (responsable).
@MainActor @Observable
public final class InsightsStore {
    public private(set) var insights: Insights?
    public private(set) var summary: DailySummary?
    public private(set) var summaryLoaded = false
    private let api: PosAPI
    private let notifier: Notifier
    private let calendar: Calendar

    public init(api: PosAPI, notifier: Notifier, calendar: Calendar = .current) {
        self.api = api
        self.notifier = notifier
        self.calendar = calendar
    }

    private var offsetMinutes: Int { calendar.timeZone.secondsFromGMT() / 60 }

    public func loadInsights() async {
        do { insights = try await api.insights(tzOffsetMinutes: offsetMinutes) } catch { insights = nil; notifier.error(error) }
    }

    public func loadSummary(terminalId: String) async {
        do { summary = try await api.dailySummary(terminalId: terminalId, tzOffsetMinutes: offsetMinutes) } catch { summary = nil; notifier.error(error) }
        summaryLoaded = true
    }
}

public extension InsightCard {
    /// Clé de texte de la métrique (`insights.avg_ticket`…).
    var titleKey: String {
        switch metric {
        case "cumulativeTtc": "insights.cumulative_ttc"
        case "avgTicket": "insights.avg_ticket"
        case "perCover": "insights.per_cover"
        default: "insights.takeaway_share"
        }
    }

    /// Valeur affichable : centimes en montant, part à emporter en pourcentage.
    func formatted(_ cents: Int) -> String {
        metric == "takeawayShare" ? String(format: "%.1f %%", Double(cents) / 100) : Money(cents: cents).formatted
    }
}
