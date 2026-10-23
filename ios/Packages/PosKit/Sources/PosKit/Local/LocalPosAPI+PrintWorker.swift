import Foundation
import os

/// Moteur de la file d'impression : port de `PrintQueueProcessor` et `PrintWorker` du .NET.
/// Seul `transport.send` est attendu ; le job est marqué (tentative comptée) avant, et relu après (réentrance de l'acteur).
extension LocalPosAPI {
    static let printLog = Logger(subsystem: "RestaurantPOS", category: "Impression")
    static let printPollInterval: Duration = .seconds(5)
    static let printPurgeAge: TimeInterval = 7 * 86_400

    static func retryDelay(attempts: Int) -> TimeInterval {
        switch attempts {
        case ...1: 5
        case 2: 15
        case 3: 30
        default: 60
        }
    }

    /// Les dates de la file sont stockées à la milliseconde la plus proche : l'instant courant est arrondi de même (arrondi monotone),
    /// sinon un job mis en file à l'instant pourrait paraître dû dans le futur et attendre la scrutation suivante.
    static func printNow(_ date: Date) -> Date { FiscalHashing.roundedToMilliseconds(date) }

    /// Un cycle complet : annulations d'imprimantes inactives, échéances, puis envoi des jobs échus de chaque imprimante (en parallèle).
    func runPrintCycle() async {
        let due: [UUID]
        do { due = try preparePrintQueue() } catch {
            Self.printLog.error("Préparation de la file d'impression : \(error.localizedDescription, privacy: .public)")
            return
        }
        await withTaskGroup(of: Void.self) { group in
            for printerId in due { group.addTask { await self.processPrinter(printerId) } }
        }
    }

    /// Lance la boucle (idempotent) : un cycle tout de suite (reprise des `Pending`), puis à chaque signal ou toutes les 5 s.
    public func startPrintWorker() {
        guard printWorker == nil else { return }
        let (stream, wake) = AsyncStream.makeStream(of: Void.self, bufferingPolicy: .bufferingNewest(1))
        printWake = wake
        wake.yield()
        printTicker = Task {
            while !Task.isCancelled {
                try? await Task.sleep(for: Self.printPollInterval)
                wake.yield()
            }
        }
        printWorker = Task { [weak self] in
            for await _ in stream {
                guard let self, !Task.isCancelled else { return }
                await self.printWorkerTick()
            }
        }
    }

    public func stopPrintWorker() {
        printWorker?.cancel()
        printTicker?.cancel()
        printWake?.finish()
        printWorker = nil
        printTicker = nil
        printWake = nil
    }

    /// Réveille la boucle après une mise en file ; sans effet si elle n'est pas lancée.
    func notifyPrintQueue() { printWake?.yield() }

    /// Supprime les `Sent` et `Cancelled` de plus de 7 jours ; les `Failed` restent.
    func purgePrintQueue() throws {
        try printQueue.purge(olderThan: clock().addingTimeInterval(-Self.printPurgeAge))
    }

    /// Le cycle n'est pas attendu : une imprimante lente ne retarde pas le réveil suivant (le garde par imprimante évite les doublons).
    private func printWorkerTick() {
        let now = clock()
        if now >= nextPrintPurge {
            do { try purgePrintQueue() } catch {
                Self.printLog.error("Purge de la file d'impression : \(error.localizedDescription, privacy: .public)")
            }
            nextPrintPurge = now.addingTimeInterval(86_400)
        }
        Task { await self.runPrintCycle() }
    }

    /// `PrepareAsync` : renvoie les imprimantes ayant un job `Pending` échu.
    private func preparePrintQueue() throws -> [UUID] {
        let pending = try printQueue.pendingJobs()
        guard !pending.isEmpty else { return [] }
        let now = Self.printNow(clock())
        let printers = Dictionary(try printerRepository.all().map { ($0.id, $0) }, uniquingKeysWith: { first, _ in first })
        var due: [UUID] = []
        try db.transaction {
            for var job in pending {
                if let printer = printers[job.printerId], printer.isActive {
                    if now >= job.deadlineAtUtc {
                        job.status = .failed
                        job.lastError = job.lastError ?? "Deadline exceeded"
                        try printQueue.save(job)
                    } else if job.nextAttemptAtUtc <= now, !due.contains(job.printerId) {
                        due.append(job.printerId)
                    }
                } else {
                    job.status = .cancelled
                    job.lastError = printers[job.printerId] == nil ? "Printer deleted" : "Printer disabled"
                    try printQueue.save(job)
                }
            }
        }
        return due
    }

    /// `ProcessPrinterAsync` : jobs dans l'ordre de création, un à la fois ; le premier échec arrête la file de cette imprimante.
    private func processPrinter(_ printerId: UUID) async {
        guard !printersInFlight.contains(printerId) else { return }
        printersInFlight.insert(printerId)
        defer { printersInFlight.remove(printerId) }
        do {
            guard let printer = try printerRepository.printer(id: printerId), printer.isActive else { return }
            for listed in try printQueue.pendingJobs() where listed.printerId == printerId {
                // Relu : il a pu être annulé depuis la liste.
                guard var job = try printQueue.job(id: listed.id), job.status == .pending else { continue }
                let now = Self.printNow(clock())
                if job.nextAttemptAtUtc > now { break }
                job.attempts += 1
                try printQueue.save(job)

                let failure: (any Error)?
                do {
                    let document = try TicketDocumentJSON.deserialize(job.documentJson)
                    try await transport.send(TicketPayload.build(printer: printer, document: document, openCashDrawer: job.openCashDrawer), to: printer)
                    failure = nil
                } catch {
                    failure = error
                }

                _ = printerTracker.record(printerId, success: failure == nil, at: now)
                // Relu après l'attente : un job annulé pendant l'envoi reste `Cancelled`.
                guard var current = try printQueue.job(id: job.id) else { if failure == nil { continue } else { break } }
                if let failure {
                    Self.printLog.error("Impression \(job.id, privacy: .public) sur \(printer.name, privacy: .public), tentative \(current.attempts) : \(failure.localizedDescription, privacy: .public)")
                    if current.status != .cancelled {
                        current.lastError = failure.localizedDescription
                        if now >= current.deadlineAtUtc { current.status = .failed } else { current.nextAttemptAtUtc = now.addingTimeInterval(Self.retryDelay(attempts: current.attempts)) }
                        try printQueue.save(current)
                    }
                    break
                }
                if current.status != .cancelled {
                    current.status = .sent
                    current.sentAtUtc = now
                    current.lastError = nil
                    try printQueue.save(current)
                }
            }
        } catch {
            Self.printLog.error("File de l'imprimante \(printerId, privacy: .public) : \(error.localizedDescription, privacy: .public)")
        }
    }
}
