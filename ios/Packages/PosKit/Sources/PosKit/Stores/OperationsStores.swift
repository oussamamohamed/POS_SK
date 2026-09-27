import Foundation
import Observation

/// Plan de salle.
@MainActor @Observable
public final class FloorStore {
    public private(set) var tables: [DiningTable] = []
    public private(set) var isLoading = false

    private let api: PosAPI
    private let notifier: Notifier
    private let session: SessionStore

    public init(api: PosAPI, notifier: Notifier, session: SessionStore) {
        self.api = api
        self.notifier = notifier
        self.session = session
    }

    /// Tables de salle (le comptoir est géré à part).
    public var diningTables: [DiningTable] {
        tables.filter { !$0.isCounter }.sorted { $0.tableNumber.localizedStandardCompare($1.tableNumber) == .orderedAscending }
    }

    public var occupiedCount: Int { diningTables.filter { $0.status != .free }.count }
    public var openTotal: Money { diningTables.reduce(.zero) { $0 + $1.activeOrderTotalTtc } }

    public func load() async {
        isLoading = true
        defer { isLoading = false }
        do { tables = try await api.tables() } catch { notifier.error("Erreur de chargement du plan de salle") }
    }

    public func addTable(number: String, capacity: Int) async -> Bool {
        let trimmed = number.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { notifier.warning("Le numéro de table est requis"); return false }
        guard !tables.contains(where: { $0.tableNumber.caseInsensitiveCompare(trimmed) == .orderedSame }) else {
            notifier.warning("La table \(trimmed) existe déjà")
            return false
        }
        do {
            try await api.createTable(number: trimmed, capacity: max(1, capacity))
            notifier.success("Table \(trimmed) créée")
            await load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    /// Ouvre une table libre avec un nombre de couverts.
    public func open(_ table: DiningTable, covers: Int) async -> Bool {
        do {
            try await api.openTable(number: table.tableNumber, covers: max(1, covers), operatorId: session.currentOperator?.id, waiterName: session.currentOperator?.name)
            await load()
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }
}

/// Écran cuisine (KDS).
@MainActor @Observable
public final class KitchenStore {
    public private(set) var tickets: [KitchenTicket] = []
    public var stationFilter: String?
    public private(set) var lastRefresh: Date?

    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public var stations: [String] { Array(Set(tickets.map(\.stationId))).sorted() }

    public func tickets(in status: TicketStatus) -> [KitchenTicket] {
        tickets
            .filter { $0.status == status && (stationFilter == nil || $0.stationId == stationFilter) }
            .sorted { ($0.dispatchedAtUtc ?? .distantPast) < ($1.dispatchedAtUtc ?? .distantPast) }
    }

    public func load() async {
        do {
            tickets = try await api.kitchenTickets()
            lastRefresh = Date()
        } catch {
            notifier.error("Erreur de chargement des bons cuisine")
        }
    }

    /// Fait avancer un bon : En attente → En préparation → Prêt → Servi.
    public func bump(_ ticket: KitchenTicket) async {
        do {
            try await api.bumpTicket(id: ticket.id)
            notifier.success("Bon \(ticket.tableNumber) : \(ticket.status == .ready ? "servi" : "avancé")")
            await load()
        } catch {
            notifier.error(error)
        }
    }
}

/// Rapports X / clôtures Z (NF525) et export FEC.
@MainActor @Observable
public final class FiscalStore {
    public private(set) var report: FiscalReport?
    public private(set) var isSealed = false
    public private(set) var isWorking = false
    public private(set) var fecStatus: String?
    public private(set) var exportedFile: URL?

    private let api: PosAPI
    private let notifier: Notifier
    private let session: SessionStore
    private let settings: TerminalSettings

    public init(api: PosAPI, notifier: Notifier, session: SessionStore, settings: TerminalSettings) {
        self.api = api
        self.notifier = notifier
        self.session = session
        self.settings = settings
    }

    public func loadLatest() async {
        do {
            if let closure = try await api.latestClosure(terminalId: settings.terminalId) {
                report = closure
                isSealed = true
            } else {
                await previewX()
            }
        } catch {
            await previewX()
        }
    }

    public func previewX() async {
        do {
            report = try await api.xReport(terminalId: settings.terminalId)
            isSealed = false
        } catch {
            notifier.error(error)
        }
    }

    public func executeZ() async -> Bool {
        guard let op = session.currentOperator, op.role.isManager else {
            notifier.error("Clôture Z réservée aux responsables")
            return false
        }
        isWorking = true
        defer { isWorking = false }
        do {
            var closure = try await api.zClosure(terminalId: settings.terminalId, managerId: op.id, managerName: op.name)
            closure.terminalId = closure.terminalId ?? settings.terminalId
            report = closure
            isSealed = true
            notifier.success("Clôture Z n°\(closure.closureSequence ?? 0) exécutée et scellée")
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func exportFec(from: Date, to: Date) async {
        isWorking = true
        fecStatus = "Génération du fichier FEC…"
        defer { isWorking = false }
        do {
            let (name, data) = try await api.exportFec(from: from, to: to, siren: settings.siren)
            let url = FileManager.default.temporaryDirectory.appendingPathComponent(name)
            try data.write(to: url, options: .atomic)
            exportedFile = url
            fecStatus = "Fichier \(name) généré"
            notifier.success("Fichier FEC \(name) prêt")
        } catch APIError.forbidden, APIError.unauthorized {
            fecStatus = "Privilèges insuffisants (responsable requis)"
            notifier.error("Export FEC refusé : privilèges insuffisants")
        } catch {
            fecStatus = "Erreur lors de la génération du FEC"
            notifier.error(error)
        }
    }
}
