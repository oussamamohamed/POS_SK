import Foundation

/// Refus métier d'une archive : une seule archive par clôture (`ArchiveExistsException`, code `archive_exists`).
enum FiscalArchiveError: Error, Equatable {
    case exists
    /// Une ligne de `FiscalArchives` est illisible : refuser plutôt que de forker la chaîne.
    case corruptChain
}

extension LocalFiscalRepository {
    /// ZIP stocké des cinq fichiers de l'archive, horodaté à la création.
    func archiveZip(for closure: PeriodClosureRecord, archiveId: UUID, createdAt: Date) throws -> Data {
        try ZipStoreWriter.archive(archiveContent(for: closure, archiveId: archiveId, createdAt: createdAt).entries, modifiedAt: createdAt)
    }

    /// Enregistre l'archive en fin de chaîne globale et journalise `ARCHIVE_CREATED` (charge utile du .NET). N'ouvre pas de transaction.
    func insertArchive(
        id: UUID, closure: PeriodClosureRecord, fileName: String, fileSha256: String, fileSizeBytes: Int, userId: UUID, createdAt: Date
    ) throws -> FiscalArchive {
        guard try fiscalArchive(periodClosureId: closure.id) == nil else { throw FiscalArchiveError.exists }
        let last = try db.query("SELECT * FROM FiscalArchives ORDER BY ArchiveSequence DESC LIMIT 1").first.map(archiveRecord)
        let sequence = (last?.archiveSequence ?? 0) + 1
        let previousHash = last?.signatureHash ?? FiscalHashing.genesisHash
        let timestamp = FiscalHashing.roundedToMilliseconds(createdAt)
        let signature = FiscalHashing.archiveHash(
            previousHash: previousHash, periodType: closure.periodType.storedValue, periodKey: closure.periodKey,
            fileSha256: fileSha256, createdAt: timestamp
        )
        try db.run(
            """
            INSERT INTO FiscalArchives (Id, PeriodClosureId, PeriodType, PeriodKey, FileName, FileSha256, FileSizeBytes, ArchiveSequence,
                PreviousSignatureHash, SignatureHash, CreatedByUserId, CreatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                .uuid(id), .uuid(closure.id), .integer(closure.periodType.storedValue), .text(closure.periodKey), .text(fileName),
                .text(fileSha256), .integer(fileSizeBytes), .integer(sequence), .text(previousHash), .text(signature), .uuid(userId),
                .text(FiscalHashing.storedText(timestamp)),
            ]
        )
        try appendJournal(
            eventType: LocalJournalEvents.archiveCreated,
            payloadJson: FiscalJSON.object([
                ("archiveId", FiscalJSON.guid(id)),
                ("periodClosureId", FiscalJSON.guid(closure.id)),
                ("periodType", "\(closure.periodType.storedValue)"),
                ("periodKey", FiscalJSON.string(closure.periodKey)),
                ("fileName", FiscalJSON.string(fileName)),
                ("fileSha256", FiscalJSON.string(fileSha256)),
                ("fileSizeBytes", "\(fileSizeBytes)"),
                ("archiveSequence", "\(sequence)"),
                ("signatureHash", FiscalJSON.string(signature)),
            ]),
            terminalId: closure.terminalId, operatorId: userId, now: timestamp
        )
        return FiscalArchive(
            id: id, periodClosureId: closure.id, periodType: closure.periodType, periodKey: closure.periodKey, fileName: fileName,
            fileSha256: fileSha256, fileSizeBytes: fileSizeBytes, archiveSequence: sequence, previousSignatureHash: previousHash,
            signatureHash: signature, createdByUserId: userId, createdAtUtc: timestamp
        )
    }

    func fiscalArchives() throws -> [FiscalArchive] {
        try db.query("SELECT * FROM FiscalArchives ORDER BY ArchiveSequence DESC").map(archiveRecord)
    }

    func fiscalArchive(id: UUID) throws -> FiscalArchive? {
        try firstArchive(where: "Id = ?", .uuid(id))
    }

    func fiscalArchive(periodClosureId: UUID) throws -> FiscalArchive? {
        try firstArchive(where: "PeriodClosureId = ?", .uuid(periodClosureId))
    }

    /// `VerifyArchiveAsync` : retrouve l'archive par l'identifiant du manifeste, puis la clôture du manifeste, puis le SHA-256 du fichier,
    /// puis le nom de fichier ; contrôle l'empreinte, recalcule la signature et le lien avec l'archive précédente (ou la genèse).
    func verifyArchive(
        data: Data, fileName: String?, previousArchive: ((Int) throws -> FiscalArchive?)? = nil
    ) throws -> ArchiveVerificationResult {
        guard !data.isEmpty else { return ArchiveVerificationResult(isValid: false, archiveId: nil, reason: "unknown_archive") }
        let sha = FiscalHashing.sha256Hex(data)
        var manifestArchiveId: UUID?
        var manifestClosureId: UUID?
        if let manifest = ZipStoreReader.entry(named: "manifest.json", in: data),
           let root = (try? JSONSerialization.jsonObject(with: manifest)) as? [String: Any] {
            manifestArchiveId = (root["archiveId"] as? String).flatMap { UUID(uuidString: $0) }
            manifestClosureId = (root["periodClosureId"] as? String).flatMap { UUID(uuidString: $0) }
        }

        var found = try manifestArchiveId.flatMap { try fiscalArchive(id: $0) }
        if found == nil, let manifestClosureId { found = try fiscalArchive(periodClosureId: manifestClosureId) }
        if found == nil { found = try firstArchive(where: "lower(FileSha256) = ?", .text(sha)) }
        if found == nil, let fileName, !fileName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            found = try firstArchive(where: "FileName = ?", .text(fileName))
        }
        guard let archive = found else { return ArchiveVerificationResult(isValid: false, archiveId: nil, reason: "unknown_archive") }

        guard archive.fileSha256.lowercased() == sha else {
            return ArchiveVerificationResult(isValid: false, archiveId: archive.id, reason: "hash_mismatch")
        }
        let computed = FiscalHashing.archiveHash(
            previousHash: archive.previousSignatureHash, periodType: archive.periodType == .monthly ? 1 : 2, periodKey: archive.periodKey,
            fileSha256: archive.fileSha256, createdAt: archive.createdAtUtc
        )
        let chainBreak = ArchiveVerificationResult(isValid: false, archiveId: archive.id, reason: "chain_break")
        guard archive.signatureHash.lowercased() == computed else { return chainBreak }
        if archive.archiveSequence > 1 {
            // Archive précédente illisible : le lien ne peut pas être prouvé, c'est une rupture (verdict, pas une erreur).
            let previous: FiscalArchive?
            do {
                previous = try previousArchive?(archive.archiveSequence - 1)
                    ?? firstArchive(where: "ArchiveSequence = ?", .integer(archive.archiveSequence - 1))
            } catch FiscalArchiveError.corruptChain {
                previous = nil
            }
            guard previous?.signatureHash.lowercased() == archive.previousSignatureHash.lowercased() else { return chainBreak }
        } else if archive.previousSignatureHash.lowercased() != FiscalHashing.genesisHash.lowercased() {
            return chainBreak
        }
        return ArchiveVerificationResult(isValid: true, archiveId: archive.id, reason: nil)
    }

    private func firstArchive(where clause: String, _ value: SQLValue) throws -> FiscalArchive? {
        try db.query("SELECT * FROM FiscalArchives WHERE \(clause) LIMIT 1", [value]).first.map(archiveRecord)
    }

    private func archiveRecord(_ row: SQLRow) throws -> FiscalArchive {
        guard let id = row.uuid("Id"), let closureId = row.uuid("PeriodClosureId") else { throw FiscalArchiveError.corruptChain }
        return FiscalArchive(
            id: id, periodClosureId: closureId, periodType: row.int("PeriodType") == 2 ? .annual : .monthly, periodKey: row.string("PeriodKey") ?? "",
            fileName: row.string("FileName") ?? "", fileSha256: row.string("FileSha256") ?? "", fileSizeBytes: row.int("FileSizeBytes") ?? 0,
            archiveSequence: row.int("ArchiveSequence") ?? 0, previousSignatureHash: row.string("PreviousSignatureHash") ?? "",
            signatureHash: row.string("SignatureHash") ?? "", createdByUserId: row.uuid("CreatedByUserId") ?? UUID(),
            createdAtUtc: row.date("CreatedAtUtc") ?? Date(timeIntervalSince1970: 0)
        )
    }
}
