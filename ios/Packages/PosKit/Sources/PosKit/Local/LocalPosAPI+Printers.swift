import Foundation

extension LocalPosAPI {
    static let printerNotFound = APIError.notFound("Imprimante introuvable.")

    /// Liste anonyme, comme `GET /api/printers`.
    public func printers() async throws -> [Printer] { try printerRepository.all() }

    /// Création (`isNew`) ou mise à jour. Comme le serveur : nom et adresse nettoyés, port 9100 par défaut, papier 58 ou 80 mm (sinon 80),
    /// imprimante créée active. Écarts : nom ou IP vide refusé en 400 (le .NET lève une exception), identifiant inconnu en 404.
    public func savePrinter(_ printer: Printer, isNew: Bool) async throws {
        try requireManager()
        let name = printer.name.trimmingCharacters(in: .whitespacesAndNewlines)
        let address = printer.ipAddress.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !name.isEmpty, !address.isEmpty else {
            throw APIError.server(status: 400, message: "Le nom et l'adresse IP de l'imprimante sont requis.")
        }
        var clean = printer
        clean.name = name
        clean.ipAddress = address
        clean.port = printer.port > 0 ? printer.port : 9100
        clean.paperWidthMm = [58, 80].contains(printer.paperWidthMm) ? printer.paperWidthMm : 80
        if isNew {
            clean.isActive = true
            guard try printerRepository.printer(id: clean.id) == nil else { throw APIError.server(status: 409, message: "Imprimante déjà enregistrée.") }
            try printerRepository.insert(clean)
        } else {
            guard try printerRepository.update(clean) else { throw Self.printerNotFound }
        }
    }

    static let printJobNotFound = APIError.notFound("Impression introuvable.")
    static let printJobNotRetryable = APIError.server(status: 409, message: "Seules les impressions en échec peuvent être relancées.")
    static let printJobNotCancellable = APIError.server(status: 409, message: "Cette impression ne peut plus être annulée.")

    /// Impression immédiate, hors file, de la page de test (`SendTestPrintAsync` du .NET, tiroir compris si l'imprimante l'ouvre).
    /// Écart : imprimante inconnue en 404 (le .NET lève une exception).
    public func testPrinter(id: UUID) async throws -> TestPrintResult {
        try requireManager()
        guard let printer = try printerRepository.printer(id: id) else { throw Self.printerNotFound }
        let page = TicketBuilder.testPage(printer: printer, language: try floorRepository.settings().receiptLanguage, now: clock())
        do {
            try await transport.send(TicketPayload.build(printer: printer, document: page, openCashDrawer: printer.openCashDrawerOnReceipt), to: printer)
            return TestPrintResult(success: true, message: "Test d'impression réussi (\(printer.name) @ \(printer.ipAddress):\(printer.port))")
        } catch {
            return TestPrintResult(success: false, message: "Échec de connexion imprimante (\(printer.ipAddress):\(printer.port)) : \(error.localizedDescription)")
        }
    }

    /// `GET /api/printers/status` : état du suivi et nombre de jobs en attente ou en échec.
    public func printerStatuses() async throws -> [PrinterStatus] {
        try requireAuth()
        let counts = try printQueue.counts()
        return try printerRepository.all().map {
            let state = printerTracker.get($0.id)
            return PrinterStatus(printerId: $0.id, name: $0.name, isActive: $0.isActive, isOnline: state.isOnline, sinceUtc: state.sinceUtc,
                                 pendingCount: counts[$0.id]?.pending ?? 0, failedCount: counts[$0.id]?.failed ?? 0)
        }
    }

    /// Jobs ouverts (`Pending`, `Failed`), les 100 plus récents. Écart conservé : imprimante inconnue en 404 (le .NET renvoie une liste vide).
    public func printJobs(printerId: UUID) async throws -> [PrintJobInfo] {
        try requireManager()
        guard try printerRepository.printer(id: printerId) != nil else { throw Self.printerNotFound }
        return try printQueue.jobs(printerId: printerId, statuses: [.pending, .failed], limit: 100).map(Self.info)
    }

    /// `Failed` → remis en file pour une nouvelle durée de vie. `Sent` d'un reçu ou d'un rapport → duplicata numéroté, journalisé
    /// (`DUPLICATE_PRINTED`) dans la même transaction, sans tiroir.
    public func retryPrintJob(id: UUID) async throws {
        let manager = try requireManager()
        guard var job = try printQueue.job(id: id) else { throw Self.printJobNotFound }
        switch (job.status, job.kind) {
        case (.failed, _):
            let now = clock()
            job.status = .pending
            job.attempts = 0
            job.nextAttemptAtUtc = now
            job.deadlineAtUtc = now.addingTimeInterval(LocalPrintQueueRepository.lifetime)
            job.lastError = nil
            try printQueue.save(job)
        case (.sent, .receipt), (.sent, .report):
            guard let original = try? TicketDocumentJSON.deserialize(job.documentJson) else { throw Self.printJobNotRetryable }
            let documentId = job.duplicateOfDocumentId ?? job.id
            let now = clock()
            try db.transaction {
                let number = try fiscalRepository.nextDuplicateNumber(documentId: documentId)
                let duplicate = try TicketDocumentJSON.serialize(TicketBuilder.withDuplicateNotice(original, duplicateNumber: number))
                try printQueue.enqueue(printerId: job.printerId, kind: job.kind, documentJson: duplicate, openCashDrawer: false,
                                       duplicateOfDocumentId: documentId, duplicateNumber: number, now: now)
                try fiscalRepository.appendJournal(
                    eventType: LocalJournalEvents.duplicatePrinted,
                    payloadJson: FiscalJSON.object([
                        ("documentId", FiscalJSON.guid(documentId)),
                        ("documentType", FiscalJSON.string("print_job")),
                        ("originalJobId", FiscalJSON.guid(job.id)),
                        ("duplicateNumber", "\(number)"),
                        ("printerId", FiscalJSON.guid(job.printerId)),
                    ]),
                    terminalId: nil, operatorId: manager.id, now: now
                )
            }
        default:
            throw Self.printJobNotRetryable
        }
        notifyPrintQueue()
    }

    /// `Pending` ou `Failed` → `Cancelled` (`LastError` inchangé, comme le .NET).
    public func cancelPrintJob(id: UUID) async throws {
        try requireManager()
        guard var job = try printQueue.job(id: id) else { throw Self.printJobNotFound }
        guard job.status == .pending || job.status == .failed else { throw Self.printJobNotCancellable }
        job.status = .cancelled
        try printQueue.save(job)
    }

    private static func info(_ job: PrintJobRecord) -> PrintJobInfo {
        PrintJobInfo(id: job.id, printerId: job.printerId, kind: job.kind.apiName, status: job.status.apiName, attempts: job.attempts,
                     createdAtUtc: job.createdAtUtc, sentAtUtc: job.sentAtUtc, lastError: job.lastError,
                     duplicateNumber: job.duplicateNumber, duplicateOfDocumentId: job.duplicateOfDocumentId)
    }
}
