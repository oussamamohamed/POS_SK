import Foundation

/// Méthodes de `PosAPI` pas encore portées sur la base locale. Chaque plan suivant retire ses lignes d'ici.
extension LocalPosAPI {
    // MARK: Appairage : sans objet en mode autonome (le poste est son propre terminal)
    public func pair(code: String) async throws -> PairResponse { throw unsupported() }

    // MARK: Fiscal (sous-projets 2 et 3)
    public func xReport(terminalId: String) async throws -> FiscalReport { throw unsupported() }
    public func latestClosure(terminalId: String) async throws -> FiscalReport? { throw unsupported() }
    public func zClosure(terminalId: String, managerId: UUID, managerName: String) async throws -> FiscalReport { throw unsupported() }
    public func printXReport(terminalId: String) async throws -> Bool { throw unsupported() }
    public func reprintLatestClosure(terminalId: String) async throws -> ReprintResult { throw unsupported() }
    public func reprintReceipt(receiptIdentifier: String) async throws -> ReprintResult { throw unsupported() }
    public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data) { throw unsupported() }
    public func dashboard(from: Date, to: Date) async throws -> FinancialDashboard { throw unsupported() }
    public func verifyChains() async throws -> FiscalVerificationResult { throw unsupported() }
    public func periodClosures(terminalId: String?, periodType: FiscalPeriodType?) async throws -> [FiscalPeriodClosure] { throw unsupported() }
    public func executePeriodClosure(terminalId: String, periodType: FiscalPeriodType, periodKey: String) async throws -> FiscalPeriodClosure { throw unsupported() }
    public func archives() async throws -> [FiscalArchive] { throw unsupported() }
    public func createArchive(periodClosureId: UUID) async throws -> FiscalArchive { throw unsupported() }
    public func verifyArchive(data: Data, fileName: String) async throws -> ArchiveVerificationResult { throw unsupported() }
}
