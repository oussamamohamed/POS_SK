import Foundation

extension LocalPosAPI {
    var cashRepository: LocalCashRepository { LocalCashRepository(db: db) }
    var alertRepository: LocalAlertRepository { LocalAlertRepository(db: db, fiscal: fiscalRepository) }

    private func isManager(_ member: StaffMember) -> Bool { member.role.isManager }

    public func openCashSession(openingFloatCents: Int) async throws -> CashSession {
        let member = try requireAuth()
        guard openingFloatCents >= 0 else { throw CashError.invalid }
        let now = clock()
        return try db.transaction {
            guard try cashRepository.current(terminalId: Self.standaloneTerminalId) == nil else { throw CashError.sessionAlreadyOpen }
            let session = CashSession(terminalId: Self.standaloneTerminalId, openedAtUtc: now, openedByOperatorId: member.id, openingFloatCents: openingFloatCents, expectedCents: 0)
            try cashRepository.insertSession(session)
            try fiscalRepository.appendJournal(eventType: LocalJournalEvents.cashSessionOpened,
                                               payloadJson: FiscalJSON.object([("sessionId", FiscalJSON.guid(session.id)), ("openingFloatCents", "\(openingFloatCents)")]),
                                               terminalId: Self.standaloneTerminalId, operatorId: member.id, now: now)
            return try cashRepository.session(id: session.id) ?? session
        }
    }

    public func currentCashSession() async throws -> CashSession? {
        let member = try requireAuth()
        guard let session = try cashRepository.current(terminalId: Self.standaloneTerminalId) else { return nil }
        // Lisible par l'opérateur qui l'a ouverte et par le responsable.
        guard session.openedByOperatorId == member.id || isManager(member) else { throw APIError.forbidden(nil) }
        return session
    }

    public func addCashMovement(sessionId: UUID, kind: String, amountCents: Int, reason: String) async throws -> CashMovement {
        let member = try requireAuth()
        let text = reason.trimmingCharacters(in: .whitespacesAndNewlines)
        return try db.transaction {
            guard let session = try cashRepository.session(id: sessionId) else { throw APIError.notFound(nil) }
            guard session.openedByOperatorId == member.id || isManager(member) else { throw APIError.forbidden(nil) }
            guard amountCents > 0, kind == "in" || kind == "out", (3...200).contains(text.utf16.count) else { throw CashError.movementInvalid }
            guard session.count == nil else { throw CashError.sessionClosed }
            let movement = CashMovement(kind: kind, amountCents: amountCents, reason: text, operatorId: member.id, createdAtUtc: clock())
            try cashRepository.insert(movement, session: sessionId)
            try fiscalRepository.appendJournal(eventType: LocalJournalEvents.cashMovement,
                                               payloadJson: FiscalJSON.object([("sessionId", FiscalJSON.guid(sessionId)), ("kind", FiscalJSON.string(kind)), ("amountCents", "\(amountCents)"), ("reason", FiscalJSON.string(text))]),
                                               terminalId: session.terminalId, operatorId: member.id, now: movement.createdAtUtc)
            return movement
        }
    }

    public func countCash(sessionId: UUID, countedCents: Int, denominations: [CashDenomination], varianceNote: String?) async throws -> CashCountResult {
        let member = try requireAuth()
        let now = clock()
        let note = varianceNote?.trimmingCharacters(in: .whitespacesAndNewlines).nilIfEmpty
        let (result, session) = try db.transaction { () throws -> (CashCountResult, CashSession) in
            guard let session = try cashRepository.session(id: sessionId) else { throw APIError.notFound(nil) }
            guard session.openedByOperatorId == member.id || isManager(member) else { throw APIError.forbidden(nil) }
            if let existing = session.count { return (CashCountResult(expectedCents: existing.expectedCents, countedCents: existing.countedCents, varianceCents: existing.varianceCents), session) } // idempotent
            guard countedCents >= 0 else { throw CashError.invalid }
            let expected = try cashRepository.expectedCents(session, until: now)
            let variance = CashMath.variance(counted: countedCents, expected: expected)
            let rule = try alertRepository.rules().first { $0.type == AlertRuleType.cashVariance }
            if let rule, rule.enabled, try alertRepository.settings().cashVarianceNoteRequired, abs(variance) > rule.threshold, note == nil { throw CashError.varianceNoteRequired }
            let json = denominations.isEmpty ? nil : String(decoding: try JSONEncoder().encode(denominations), as: UTF8.self)
            try cashRepository.insertCount(session: sessionId, count: CashCount(expectedCents: expected, countedCents: countedCents, varianceCents: variance, varianceNote: note, denominationsJson: json, closedAtUtc: now), operatorId: member.id)
            try fiscalRepository.appendJournal(eventType: LocalJournalEvents.cashCounted,
                                               payloadJson: FiscalJSON.object([("sessionId", FiscalJSON.guid(sessionId)), ("expectedCents", "\(expected)"), ("countedCents", "\(countedCents)"), ("varianceCents", "\(variance)")]),
                                               terminalId: session.terminalId, operatorId: member.id, now: now)
            // Alerte d'écart : une panne ne remet jamais en cause le comptage.
            let created = (try? alertRepository.evaluateCashCount(session: try cashRepository.session(id: sessionId) ?? session, now: now)) ?? false
            return (CashCountResult(expectedCents: expected, countedCents: countedCents, varianceCents: variance, alertCreated: created), session)
        }
        _ = session
        return result
    }

    public func cashSessions(from: Date?, to: Date?) async throws -> [CashSession] {
        try requireManager()
        return try cashRepository.all(from: from, to: to)
    }

    // MARK: Alertes

    public func alerts(state: String?) async throws -> [PilotAlert] {
        try requireManager()
        return try alertRepository.list(state: state, from: nil, to: nil)
    }

    public func resolveAlert(id: UUID, note: String) async throws -> PilotAlert {
        let manager = try requireManager()
        guard let alert = try db.transaction({ try alertRepository.resolve(id: id, note: note, operatorId: manager.id, now: clock()) }) else { throw APIError.notFound(nil) }
        return alert
    }

    public func alertRules() async throws -> [AlertRule] {
        try requireManager()
        return try alertRepository.rules()
    }

    public func setAlertRule(type: String, threshold: Int, enabled: Bool) async throws -> AlertRule {
        let manager = try requireManager()
        guard let rule = try alertRepository.setRule(type: type, threshold: threshold, enabled: enabled, operatorId: manager.id, now: clock()) else { throw APIError.notFound(nil) }
        return rule
    }

    public func alertSettings() async throws -> AlertSettings {
        try requireManager()
        return try alertRepository.settings()
    }

    public func saveAlertSettings(_ settings: AlertSettings) async throws -> AlertSettings {
        try requireManager()
        try alertRepository.save(settings)
        return try alertRepository.settings()
    }
}

extension String {
    var nilIfEmpty: String? { isEmpty ? nil : self }
}
