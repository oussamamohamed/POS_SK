import Foundation

/// État de la sauvegarde : dernière réussite (d'après le JET) et retard (jamais, ou plus de 24 h).
public struct BackupStatus: Equatable, Sendable {
    public let lastSuccess: Date?
    public let isOverdue: Bool

    public init(lastSuccess: Date?, isOverdue: Bool) {
        self.lastSuccess = lastSuccess
        self.isOverdue = isOverdue
    }
}

extension LocalPosAPI {
    public static let minimumBackupPassphraseLength = 8
    static let backupInterval: TimeInterval = 24 * 3600

    /// Copie **non chiffrée** de la base, réservée aux tests et au dépannage : ne jamais la proposer à l'utilisateur (elle contient les empreintes
    /// de PIN et la chaîne fiscale). L'export utilisateur passe par `exportBackup`.
    public func backup(to url: URL) async throws {
        try requireManager()
        try db.backup(to: url.path)
    }

    public func backupStatus() async throws -> BackupStatus {
        let last = try fiscalRepository.lastJournalDate(eventType: LocalJournalEvents.backupCompleted)
        let overdue = last.map { clock().timeIntervalSince($0) > Self.backupInterval } ?? true
        return BackupStatus(lastSuccess: last, isOverdue: overdue)
    }

    /// Export manuel : réservé aux responsables, phrase redemandée à chaque fois.
    @discardableResult
    public func exportBackup(to folder: URL, passphrase: String) async throws -> URL {
        let manager = try requireManager()
        return try performBackup(to: folder, passphrase: passphrase, operatorId: manager.id)
    }

    /// Sauvegarde automatique (application au premier plan, dernière sauvegarde de plus de 24 h) : pas de session requise.
    @discardableResult
    public func runScheduledBackup(to folder: URL, passphrase: String) async throws -> URL {
        try performBackup(to: folder, passphrase: passphrase, operatorId: nil)
    }

    func setBackupIterations(_ iterations: Int) { backupIterations = iterations }

    /// Écrit la sauvegarde, puis la journalise (réussite ou échec, dans sa propre transaction). La phrase n'apparaît jamais dans le journal.
    private func performBackup(to folder: URL, passphrase: String, operatorId: UUID?) throws -> URL {
        guard passphrase.count >= Self.minimumBackupPassphraseLength else {
            throw APIError.server(status: 400, message: "La phrase de sauvegarde doit comporter au moins \(Self.minimumBackupPassphraseLength) caractères.")
        }
        let now = clock()
        let fiscal = fiscalRepository
        do {
            let file = try LocalBackupService.write(db: db, to: folder, passphrase: passphrase, now: now, iterations: backupIterations)
            let size = (try? FileManager.default.attributesOfItem(atPath: file.path)[.size] as? Int) ?? 0
            try db.transaction {
                try fiscal.appendJournal(
                    eventType: LocalJournalEvents.backupCompleted,
                    payloadJson: FiscalJSON.object([("fileName", FiscalJSON.string(file.lastPathComponent)), ("sizeBytes", "\(size)")]),
                    terminalId: Self.standaloneTerminalId, operatorId: operatorId, now: now
                )
            }
            return file
        } catch {
            try? db.transaction {
                try fiscal.appendJournal(
                    eventType: LocalJournalEvents.backupFailed,
                    payloadJson: FiscalJSON.object([("reason", FiscalJSON.string((error as? LocalizedError)?.errorDescription ?? "Écriture impossible"))]),
                    terminalId: Self.standaloneTerminalId, operatorId: operatorId, now: now
                )
            }
            throw error
        }
    }
}
