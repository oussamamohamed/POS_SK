import Foundation

extension LocalPosAPI {
    private static var archiveExists: APIError { .server(status: 409, message: L10n.string("fiscal.archive_error_exists")) }
    private static var archiveChainCorrupt: APIError { .server(status: 500, message: L10n.string("fiscal.archive_error_chain_unreadable")) }
    private static var archiveFileMissing: APIError { .notFound(L10n.string("fiscal.archive_error_file_missing")) }
    private static var closureMissing: APIError { .notFound(L10n.string("fiscal.archive_error_closure_missing")) }
    private static var emptyArchiveFile: APIError { .server(status: 400, message: L10n.string("fiscal.archive_error_empty_file")) }

    /// Archives fiscales, la plus récente d'abord (`GET /api/fiscal/archives`). Réservé aux responsables.
    public func archives() async throws -> [FiscalArchive] {
        try requireManager()
        return try fiscalRepository.fiscalArchives()
    }

    /// Crée l'archive d'une clôture de période (`POST /api/fiscal/archives`) : ZIP écrit en `.tmp`, ligne et JET dans une transaction,
    /// puis renommage. Un refus (clôture inconnue, archive existante) n'écrit rien.
    public func createArchive(periodClosureId: UUID) async throws -> FiscalArchive {
        let manager = try requireManager()
        let fiscal = fiscalRepository
        guard let closure = try fiscal.periodClosure(id: periodClosureId), closure.terminalId == Self.standaloneTerminalId else {
            throw Self.closureMissing
        }
        guard try fiscal.fiscalArchive(periodClosureId: periodClosureId) == nil else { throw Self.archiveExists }

        let now = FiscalHashing.roundedToMilliseconds(clock())
        let archiveId = UUID()
        let zip = try fiscal.archiveZip(for: closure, archiveId: archiveId, createdAt: now)
        let fileName = LocalFiscalRepository.archiveFileName(closure)
        let folder = archivesDirectory
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        let temporary = folder.appendingPathComponent("\(FiscalHashing.guidText(archiveId)).tmp")
        try zip.write(to: temporary, options: .withoutOverwriting)

        let archive: FiscalArchive
        do {
            archive = try db.transaction {
                try fiscal.insertArchive(
                    id: archiveId, closure: closure, fileName: fileName, fileSha256: FiscalHashing.sha256Hex(zip),
                    fileSizeBytes: zip.count, userId: manager.id, createdAt: now
                )
            }
        } catch FiscalArchiveError.exists {
            try? FileManager.default.removeItem(at: temporary)
            throw Self.archiveExists
        } catch FiscalArchiveError.corruptChain {
            try? FileManager.default.removeItem(at: temporary)
            throw Self.archiveChainCorrupt
        } catch {
            try? FileManager.default.removeItem(at: temporary)
            throw error
        }
        // Comme le .NET (`File.Move(..., overwrite: true)`) : le fichier définitif n'apparaît qu'après le COMMIT.
        let target = folder.appendingPathComponent(fileName)
        try? FileManager.default.removeItem(at: target)
        try FileManager.default.moveItem(at: temporary, to: target)
        return archive
    }

    /// Vérifie une archive (`POST /api/fiscal/archives/verify`) : `unknown_archive`, `hash_mismatch`, `chain_break` ou valide.
    /// Fichier vide refusé en 400, comme la route .NET.
    public func verifyArchive(data: Data, fileName: String) async throws -> ArchiveVerificationResult {
        try requireManager()
        guard !data.isEmpty else { throw Self.emptyArchiveFile }
        return try fiscalRepository.verifyArchive(data: data, fileName: fileName)
    }

    /// Fichier d'une archive (`GET /api/fiscal/archives/{id}/file`), pour le partage. Journalise `ARCHIVE_EXPORTED` sans poste ni opérateur,
    /// comme le .NET ; un échec du journal ne bloque pas l'export.
    public func archiveFile(id: UUID) async throws -> URL {
        try requireManager()
        let fiscal = fiscalRepository
        guard let archive = try fiscal.fiscalArchive(id: id) else { throw Self.archiveFileMissing }
        let folder = archivesDirectory.standardizedFileURL
        let file = folder.appendingPathComponent(archive.fileName).standardizedFileURL
        guard file.path.hasPrefix(folder.path + "/"), FileManager.default.fileExists(atPath: file.path) else { throw Self.archiveFileMissing }
        let now = FiscalHashing.roundedToMilliseconds(clock())
        _ = try? db.transaction {
            try fiscal.appendJournal(
                eventType: LocalJournalEvents.archiveExported,
                payloadJson: FiscalJSON.object([
                    ("archiveId", FiscalJSON.guid(archive.id)),
                    ("periodClosureId", FiscalJSON.guid(archive.periodClosureId)),
                    ("fileName", FiscalJSON.string(archive.fileName)),
                    ("fileSha256", FiscalJSON.string(archive.fileSha256)),
                ]),
                terminalId: nil, operatorId: nil, now: now
            )
        }
        return file
    }
}
