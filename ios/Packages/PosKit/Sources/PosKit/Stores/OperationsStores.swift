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
        do { tables = try await api.tables() } catch { notifier.error(L10n.string("floor.load_error")) }
    }

    public func addTable(number: String, capacity: Int) async -> Bool {
        let trimmed = number.trimmingCharacters(in: .whitespaces)
        guard !trimmed.isEmpty else { notifier.warning(L10n.string("floor.number_required")); return false }
        guard !tables.contains(where: { $0.tableNumber.caseInsensitiveCompare(trimmed) == .orderedSame }) else {
            notifier.warning(String(format: L10n.string("floor.table_exists"), trimmed))
            return false
        }
        do {
            try await api.createTable(number: trimmed, capacity: max(1, capacity))
            notifier.success(String(format: L10n.string("floor.table_created"), trimmed))
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
            notifier.error(L10n.string("kitchen.load_error"))
        }
    }

    /// Fait avancer un bon : En attente → En préparation → Prêt → Servi.
    public func bump(_ ticket: KitchenTicket) async {
        do {
            try await api.bumpTicket(id: ticket.id)
            let status = ticket.status == .ready ? L10n.string("kitchen.status_served") : L10n.string("kitchen.status_advanced")
            notifier.success(String(format: L10n.string("kitchen.ticket_bumped"), ticket.tableNumber, status))
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
    public private(set) var verificationResult: FiscalVerificationResult?
    public private(set) var lastDuplicateNumber: Int?

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
        lastDuplicateNumber = nil
        do {
            report = try await api.xReport(terminalId: settings.terminalId)
            isSealed = false
        } catch {
            notifier.error(error)
        }
    }

    public func executeZ() async -> Bool {
        guard let op = session.currentOperator, op.role.isManager else {
            notifier.error(L10n.string("fiscal.z_closure_forbidden"))
            return false
        }
        isWorking = true
        defer { isWorking = false }
        do {
            var closure = try await api.zClosure(terminalId: settings.terminalId, managerId: op.id, managerName: op.name)
            closure.terminalId = closure.terminalId ?? settings.terminalId
            report = closure
            isSealed = true
            lastDuplicateNumber = nil
            notifier.success(String(format: L10n.string("fiscal.z_closure_done"), closure.closureSequence ?? 0))
            if closure.printQueued == false { notifier.warning(L10n.string("payment.print_not_queued")) }
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func printX() async { await sendPrint { try await self.api.printXReport(terminalId: self.settings.terminalId) } }

    public func reprintZ() async {
        do {
            let res = try await self.api.reprintLatestClosure(terminalId: self.settings.terminalId)
            lastDuplicateNumber = res.duplicateNumber
            if res.printQueued {
                notifier.success(String(format: L10n.string("fiscal.duplicate_queued"), "\(res.duplicateNumber)"))
            } else {
                notifier.warning(L10n.string("payment.print_not_queued"))
            }
        } catch {
            notifier.error(error)
        }
    }

    @discardableResult
    public func reprintReceipt(receiptIdentifier: String) async -> Bool {
        isWorking = true
        defer { isWorking = false }
        do {
            let res = try await self.api.reprintReceipt(receiptIdentifier: receiptIdentifier)
            lastDuplicateNumber = res.duplicateNumber
            if res.printQueued {
                notifier.success(String(format: L10n.string("fiscal.receipt_reprinted_success"), "\(res.duplicateNumber)"))
            } else {
                notifier.warning(L10n.string("payment.print_not_queued"))
            }
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    private func sendPrint(_ send: () async throws -> Bool) async {
        do {
            if try await send() { notifier.success(L10n.string("fiscal.print_queued")) }
            else { notifier.warning(L10n.string("payment.print_not_queued")) }
        } catch {
            notifier.error(error)
        }
    }

    public func exportFec(from: Date, to: Date) async {
        isWorking = true
        fecStatus = L10n.string("fiscal.fec_generating")
        defer { isWorking = false }
        do {
            let (name, data) = try await api.exportFec(from: from, to: to, siren: settings.siren)
            let url = FileManager.default.temporaryDirectory.appendingPathComponent(name)
            try data.write(to: url, options: .atomic)
            exportedFile = url
            fecStatus = String(format: L10n.string("fiscal.fec_generated"), name)
            notifier.success(String(format: L10n.string("fiscal.fec_ready"), name))
        } catch APIError.forbidden, APIError.unauthorized {
            fecStatus = L10n.string("fiscal.fec_forbidden_status")
            notifier.error(L10n.string("fiscal.fec_forbidden"))
        } catch {
            fecStatus = L10n.string("fiscal.fec_error")
            notifier.error(error)
        }
    }

    public func verify() async -> Bool {
        guard let op = session.currentOperator, op.role.isManager else {
            notifier.error(L10n.string("fiscal.verify_forbidden"))
            return false
        }
        isWorking = true
        defer { isWorking = false }
        do {
            let result = try await api.verifyChains()
            verificationResult = result
            if result.isValid {
                notifier.success(L10n.string("fiscal.verification_success"))
            } else {
                notifier.error(L10n.string("fiscal.verification_failed"))
            }
            return result.isValid
        } catch {
            notifier.error(error)
            return false
        }
    }

    public private(set) var periodClosures: [FiscalPeriodClosure] = []

    public func loadPeriodClosures(type: FiscalPeriodType? = nil) async {
        do {
            periodClosures = try await api.periodClosures(terminalId: settings.terminalId, periodType: type)
        } catch {
            notifier.error(error)
        }
    }

    public func executePeriodClosure(type: FiscalPeriodType, key: String) async -> Bool {
        guard let op = session.currentOperator, op.role.isManager else {
            notifier.error(L10n.string("fiscal.period_closure_forbidden"))
            return false
        }
        isWorking = true
        defer { isWorking = false }
        do {
            let closure = try await api.executePeriodClosure(terminalId: settings.terminalId, periodType: type, periodKey: key)
            periodClosures.insert(closure, at: 0)
            notifier.success(L10n.string("fiscal.period_closure_success"))
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public private(set) var archives: [FiscalArchive] = []

    public func loadArchives() async {
        do {
            archives = try await api.archives()
        } catch {
            notifier.error(error)
        }
    }

    @discardableResult
    public func createArchive(periodClosureId: UUID) async -> Bool {
        guard let op = session.currentOperator, op.role.isManager else {
            notifier.error(L10n.string("fiscal.archive_forbidden"))
            return false
        }
        isWorking = true
        defer { isWorking = false }
        do {
            let archive = try await api.createArchive(periodClosureId: periodClosureId)
            archives.insert(archive, at: 0)
            notifier.success(L10n.string("fiscal.archive_created_success"))
            return true
        } catch {
            notifier.error(error)
            return false
        }
    }

    public func verifyArchive(data: Data, fileName: String) async -> ArchiveVerificationResult? {
        isWorking = true
        defer { isWorking = false }
        do {
            let res = try await api.verifyArchive(data: data, fileName: fileName)
            if res.isValid {
                notifier.success(L10n.string("fiscal.archive_valid"))
            } else if res.reason == "hash_mismatch" {
                notifier.error(L10n.string("fiscal.archive_hash_mismatch"))
            } else if res.reason == "unknown_archive" {
                notifier.error(L10n.string("fiscal.archive_unknown"))
            } else if res.reason == "chain_break" {
                notifier.error(L10n.string("fiscal.archive_chain_break"))
            } else {
                notifier.error(res.reason ?? L10n.string("errors.unknown"))
            }
            return res
        } catch {
            notifier.error(error)
            return nil
        }
    }
}
