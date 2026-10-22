import Foundation

extension LocalPosAPI {
    /// Clôtures mensuelles et annuelles du poste autonome (`GET /api/fiscal/period-closures`). Réservé aux responsables ; le poste demandé est ignoré (toujours `T01`).
    public func periodClosures(terminalId: String?, periodType: FiscalPeriodType?) async throws -> [FiscalPeriodClosure] {
        try requireManager()
        return try fiscalRepository.periodClosures(terminalId: Self.standaloneTerminalId, type: periodType).map { Self.dto($0) }
    }

    /// Clôture une période (`POST /api/fiscal/period-closures`). Refus métier en 409 (période en cours, déjà clôturée, Z ou mensuelles manquantes),
    /// clé invalide en 400. Un refus n'écrit rien.
    public func executePeriodClosure(terminalId: String, periodType: FiscalPeriodType, periodKey: String) async throws -> FiscalPeriodClosure {
        let manager = try requireManager()
        let fiscal = fiscalRepository, floor = floorRepository
        let now = clock(), calendar = calendar
        let record: PeriodClosureRecord
        do {
            record = try db.transaction { () throws -> PeriodClosureRecord in
                let settings = try floor.settings()
                return try fiscal.executePeriodClosure(
                    terminalId: Self.standaloneTerminalId, type: periodType, key: periodKey, managerId: manager.id, managerName: manager.name,
                    fiscalYearStartMonth: settings.fiscalYearStartMonth ?? 1, fiscalYearStartDay: settings.fiscalYearStartDay ?? 1, calendar: calendar, now: now
                )
            }
        } catch let error as PeriodClosureError {
            throw Self.apiError(error)
        }
        // Après la validation : l'impression ne défait jamais la clôture.
        return Self.dto(record, printQueued: queuePeriodClosure(record))
    }

    private static func dto(_ r: PeriodClosureRecord, printQueued: Bool = false) -> FiscalPeriodClosure {
        FiscalPeriodClosure(
            id: r.id, terminalId: r.terminalId, periodType: r.periodType, periodKey: r.periodKey, closureSequence: r.sequence,
            periodStartUtc: r.periodStartUtc, periodEndUtc: r.periodEndUtc, totalTtc: Money(cents: r.totalTtcCents), totalHt: Money(cents: r.totalHtCents),
            totalTtcCents: r.totalTtcCents, totalHtCents: r.totalHtCents, taxesSummaryJson: r.taxesSummaryJson, tenderTotalsJson: r.tenderTotalsJson,
            perpetualGrandTotal: Money(cents: r.perpetualGrandTotalCents), perpetualGrandTotalCents: r.perpetualGrandTotalCents,
            dailyClosureCount: r.dailyClosureCount, previousSignatureHash: r.previousSignatureHash, signatureHash: r.signatureHash,
            sealedByUserId: r.sealedByUserId, sealedByUserName: r.sealedByUserName, createdAtUtc: r.createdAtUtc, printQueued: printQueued
        )
    }

    /// Refus métier lisibles dans la langue de l'app (le .NET renvoie un code et un message anglais) ; statuts identiques au serveur.
    private static func apiError(_ error: PeriodClosureError) -> APIError {
        switch error {
        case .invalidKey:
            .server(status: 400, message: L10n.string("fiscal.period_error_invalid_key"))
        case .notEnded:
            .server(status: 409, message: L10n.string("fiscal.period_error_not_ended"))
        case .alreadyClosed:
            .server(status: 409, message: L10n.string("fiscal.period_error_already_closed"))
        case .missingDailyClosures(let days):
            .server(status: 409, message: String(format: L10n.string("fiscal.period_error_missing_daily"), days.joined(separator: ", ")))
        case .missingMonthlyClosures(let months):
            .server(status: 409, message: String(format: L10n.string("fiscal.period_error_missing_monthly"), months.joined(separator: ", ")))
        }
    }
}
