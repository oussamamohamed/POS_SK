import Foundation

/// File d'impression (`PrintJobs`). Pas de logique de relance ici : le moteur décide, le dépôt stocke.
struct LocalPrintQueueRepository {
    let db: SQLiteDatabase
    /// Durée de vie d'un job (`PrintJob.Lifetime` du .NET).
    static let lifetime: TimeInterval = 30 * 60

    @discardableResult
    func enqueue(printerId: UUID, kind: PrintJobKind, documentJson: String, openCashDrawer: Bool,
                 duplicateOfDocumentId: UUID?, duplicateNumber: Int?, now instant: Date) throws -> PrintJobRecord {
        let now = FiscalHashing.roundedToMilliseconds(instant) // l'enregistrement renvoyé est celui que la base relira
        let job = PrintJobRecord(
            id: UUID(), printerId: printerId, kind: kind, documentJson: documentJson, openCashDrawer: openCashDrawer, status: .pending, attempts: 0,
            nextAttemptAtUtc: now, deadlineAtUtc: now.addingTimeInterval(Self.lifetime), lastError: nil, createdAtUtc: now, sentAtUtc: nil,
            duplicateOfDocumentId: duplicateOfDocumentId, duplicateNumber: duplicateNumber
        )
        try db.run(
            """
            INSERT INTO PrintJobs (Id, PrinterId, Kind, DocumentJson, OpenCashDrawer, Status, Attempts, NextAttemptAtUtc, DeadlineAtUtc, LastError,
                CreatedAtUtc, SentAtUtc, DuplicateOfDocumentId, DuplicateNumber)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(job.id), .uuid(printerId), .integer(kind.rawValue), .text(documentJson), .bool(openCashDrawer), .integer(job.status.rawValue),
             .integer(0), Self.date(job.nextAttemptAtUtc), Self.date(job.deadlineAtUtc), .null, Self.date(now), .null, .uuid(duplicateOfDocumentId),
             .integer(duplicateNumber)]
        )
        return job
    }

    func job(id: UUID) throws -> PrintJobRecord? {
        try db.query("SELECT * FROM PrintJobs WHERE Id = ?", [.uuid(id)]).first.flatMap(Self.decode)
    }

    /// Tous les `Pending`, dans l'ordre de création puis `Id`.
    func pendingJobs() throws -> [PrintJobRecord] {
        try db.query("SELECT * FROM PrintJobs WHERE Status = ? ORDER BY CreatedAtUtc, rowid", [.integer(PrintJobStatus.pending.rawValue)])
            .compactMap(Self.decode)
    }

    /// Du plus récent au plus ancien.
    func jobs(printerId: UUID, statuses: [PrintJobStatus], limit: Int) throws -> [PrintJobRecord] {
        guard !statuses.isEmpty else { return [] }
        let marks = statuses.map { _ in "?" }.joined(separator: ", ")
        return try db.query(
            "SELECT * FROM PrintJobs WHERE PrinterId = ? AND Status IN (\(marks)) ORDER BY CreatedAtUtc DESC, rowid DESC LIMIT ?",
            [.uuid(printerId)] + statuses.map { .integer($0.rawValue) } + [.integer(limit)]
        ).compactMap(Self.decode)
    }

    /// Met à jour ce qui change au fil des tentatives (statut, tentatives, échéances, erreur, envoi).
    func save(_ job: PrintJobRecord) throws {
        try db.run(
            "UPDATE PrintJobs SET Status = ?, Attempts = ?, NextAttemptAtUtc = ?, DeadlineAtUtc = ?, LastError = ?, SentAtUtc = ? WHERE Id = ?",
            [.integer(job.status.rawValue), .integer(job.attempts), Self.date(job.nextAttemptAtUtc), Self.date(job.deadlineAtUtc), .string(job.lastError),
             Self.date(job.sentAtUtc), .uuid(job.id)]
        )
    }

    func counts() throws -> [UUID: (pending: Int, failed: Int)] {
        let rows = try db.query(
            "SELECT PrinterId, Status, COUNT(*) AS n FROM PrintJobs WHERE Status IN (?, ?) GROUP BY PrinterId, Status",
            [.integer(PrintJobStatus.pending.rawValue), .integer(PrintJobStatus.failed.rawValue)]
        )
        var result: [UUID: (pending: Int, failed: Int)] = [:]
        for row in rows {
            guard let printer = row.uuid("PrinterId") else { continue }
            var entry = result[printer] ?? (pending: 0, failed: 0)
            if row.int("Status") == PrintJobStatus.pending.rawValue { entry.pending = row.int("n") ?? 0 } else { entry.failed = row.int("n") ?? 0 }
            result[printer] = entry
        }
        return result
    }

    /// Supprime les `Sent` et `Cancelled` créés avant la limite ; les `Failed` restent.
    @discardableResult
    func purge(olderThan limit: Date) throws -> Int {
        try db.run(
            "DELETE FROM PrintJobs WHERE Status IN (?, ?) AND CreatedAtUtc < ?",
            [.integer(PrintJobStatus.sent.rawValue), .integer(PrintJobStatus.cancelled.rawValue), Self.date(limit)]
        )
    }

    /// Milliseconde entière (même écriture que les dates fiscales), pour que l'aller-retour ne perde rien.
    private static func date(_ d: Date?) -> SQLValue { d.map { .text(FiscalHashing.storedText($0)) } ?? .null }

    private static func decode(_ row: SQLRow) -> PrintJobRecord? {
        guard let id = row.uuid("Id"), let printerId = row.uuid("PrinterId"), let kind = row.int("Kind").flatMap(PrintJobKind.init(rawValue:)),
              let status = row.int("Status").flatMap(PrintJobStatus.init(rawValue:)), let next = row.date("NextAttemptAtUtc"),
              let deadline = row.date("DeadlineAtUtc"), let created = row.date("CreatedAtUtc") else { return nil }
        return PrintJobRecord(
            id: id, printerId: printerId, kind: kind, documentJson: row.string("DocumentJson") ?? "", openCashDrawer: row.bool("OpenCashDrawer"),
            status: status, attempts: row.int("Attempts") ?? 0, nextAttemptAtUtc: next, deadlineAtUtc: deadline, lastError: row.string("LastError"),
            createdAtUtc: created, sentAtUtc: row.date("SentAtUtc"), duplicateOfDocumentId: row.uuid("DuplicateOfDocumentId"),
            duplicateNumber: row.int("DuplicateNumber")
        )
    }
}
