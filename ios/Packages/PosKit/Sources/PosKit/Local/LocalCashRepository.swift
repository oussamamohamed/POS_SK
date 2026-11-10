import Foundation

/// Sessions de caisse, mouvements et comptages (miroir de `CashSessionService` du .NET). En ajout seulement (déclencheurs SQL).
struct LocalCashRepository {
    let db: SQLiteDatabase

    private func movement(_ r: SQLRow) -> CashMovement? {
        guard let id = r.uuid("Id"), let op = r.uuid("OperatorId"), let at = r.date("CreatedAtUtc") else { return nil }
        return CashMovement(id: id, kind: r.string("Kind") ?? "", amountCents: r.int("AmountCents") ?? 0, reason: r.string("Reason") ?? "", operatorId: op, createdAtUtc: at)
    }

    private func sessions(_ clause: String = "", _ params: [SQLValue] = []) throws -> [SQLRow] {
        try db.query("SELECT * FROM CashSessions \(clause)", params)
    }

    func current(terminalId: String) throws -> CashSession? {
        let counted = Set(try db.query("SELECT SessionId FROM CashCounts").compactMap { $0.uuid("SessionId") })
        let open = try sessions("WHERE TerminalId = ?", [.text(terminalId)]).filter { r in r.uuid("Id").map { !counted.contains($0) } ?? false }
            .sorted { ($0.date("OpenedAtUtc") ?? .distantPast) > ($1.date("OpenedAtUtc") ?? .distantPast) }
        return try open.first.flatMap(session)
    }

    func session(id: UUID) throws -> CashSession? {
        try sessions("WHERE Id = ?", [.uuid(id)]).first.flatMap(session)
    }

    func all(from: Date?, to: Date?) throws -> [CashSession] {
        try sessions().filter { r in
            guard let at = r.date("OpenedAtUtc") else { return false }
            return (from == nil || at >= from!) && (to == nil || at < to!)
        }.sorted { ($0.date("OpenedAtUtc") ?? .distantPast) > ($1.date("OpenedAtUtc") ?? .distantPast) }.compactMap(session)
    }

    private func session(_ r: SQLRow) throws -> CashSession? {
        guard let id = r.uuid("Id"), let opener = r.uuid("OpenedByOperatorId"), let opened = r.date("OpenedAtUtc") else { return nil }
        let count = try db.query("SELECT * FROM CashCounts WHERE SessionId = ?", [.uuid(id)]).first
        let movements = try db.query("SELECT * FROM CashMovements WHERE SessionId = ? ORDER BY CreatedAtUtc, rowid", [.uuid(id)]).compactMap(movement)
        let session = CashSession(id: id, terminalId: r.string("TerminalId") ?? "", openedAtUtc: opened, openedByOperatorId: opener, openingFloatCents: r.int("OpeningFloatCents") ?? 0, expectedCents: 0, movements: movements)
        let expected = try count.flatMap { $0.int("ExpectedCents") } ?? expectedCents(session, until: Date.distantFuture)
        var result = session
        result.expectedCents = expected
        result.count = count.flatMap { c in c.date("ClosedAtUtc").map { CashCount(expectedCents: c.int("ExpectedCents") ?? 0, countedCents: c.int("CountedCents") ?? 0, varianceCents: c.int("VarianceCents") ?? 0, varianceNote: c.string("VarianceNote"), denominationsJson: c.string("DenominationsJson"), closedAtUtc: $0) } }
        return result
    }

    /// Espèces attendues à l'instant `until` : règlements espèces du poste depuis l'ouverture (montant imputé, rendu exclu).
    /// `until` est arrondi à la milliseconde comme les dates stockées (sinon un règlement de la même milliseconde disparaît).
    func expectedCents(_ session: CashSession, until: Date) throws -> Int {
        let end = FiscalHashing.roundedToMilliseconds(until)
        let tenders = try db.query(
            "SELECT r.CreatedAtUtc AS At, t.Amount AS Amount FROM PaymentTenders t JOIN FiscalReceipts r ON r.Id = t.FiscalReceiptId WHERE r.TerminalId = ? AND t.Method = ?",
            [.text(session.terminalId), .integer(PaymentMethod.cash.rawValue)]
        ).compactMap { r -> Int? in
            guard let at = r.date("At"), at >= session.openedAtUtc, at <= end else { return nil }
            return r.int("Amount")
        }
        return CashMath.expected(openingFloat: session.openingFloatCents, cashTenders: tenders, movements: session.movements)
    }

    func insertSession(_ s: CashSession) throws {
        try db.run("INSERT INTO CashSessions (Id, TerminalId, OpenedAtUtc, OpenedByOperatorId, OpeningFloatCents) VALUES (?, ?, ?, ?, ?)",
                   [.uuid(s.id), .text(s.terminalId), .date(s.openedAtUtc), .uuid(s.openedByOperatorId), .integer(s.openingFloatCents)])
    }

    func insert(_ m: CashMovement, session: UUID) throws {
        try db.run("INSERT INTO CashMovements (Id, SessionId, Kind, AmountCents, Reason, OperatorId, CreatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?)",
                   [.uuid(m.id), .uuid(session), .text(m.kind), .integer(m.amountCents), .text(m.reason), .uuid(m.operatorId), .date(m.createdAtUtc)])
    }

    func existingCount(session: UUID) throws -> CashCount? { try self.session(id: session)?.count }

    func insertCount(session: UUID, count: CashCount, operatorId: UUID) throws {
        // Même arrondi que les reçus : un format tronqué placerait un comptage de la même milliseconde avant le début de période.
        try db.run("INSERT INTO CashCounts (Id, SessionId, ClosedAtUtc, ClosedByOperatorId, CountedCents, ExpectedCents, VarianceCents, DenominationsJson, VarianceNote) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
                   [.uuid(UUID()), .uuid(session), .text(FiscalHashing.storedText(count.closedAtUtc)), .uuid(operatorId), .integer(count.countedCents), .integer(count.expectedCents), .integer(count.varianceCents),
                    .string(count.denominationsJson), .string(count.varianceNote)])
    }

    /// Comptages du poste clos dans [from, to] (période d'un Z), cumulés : deux services dans la période = deux comptages. Champs non signés du rapport Z.
    /// Bornes arrondies à la milliseconde comme les dates stockées.
    func summary(terminalId: String, from: Date, to: Date) throws -> (float: Int, counted: Int, variance: Int)? {
        let (start, end) = (FiscalHashing.roundedToMilliseconds(from), FiscalHashing.roundedToMilliseconds(to))
        let counted = try all(from: nil, to: nil).filter { $0.terminalId == terminalId }.compactMap { s -> (CashSession, CashCount)? in
            guard let c = s.count, c.closedAtUtc >= start, c.closedAtUtc <= end else { return nil }
            return (s, c)
        }
        guard !counted.isEmpty else { return nil }
        return (counted.reduce(0) { $0 + $1.0.openingFloatCents }, counted.reduce(0) { $0 + $1.1.countedCents }, counted.reduce(0) { $0 + $1.1.varianceCents })
    }
}
