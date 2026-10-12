import Foundation

/// Méthodes de `PosAPI` pas encore portées sur la base locale. Chaque plan suivant retire ses lignes d'ici.
extension LocalPosAPI {
    // MARK: Appairage : sans objet en mode autonome (le poste est son propre terminal)
    public func pair(code: String) async throws -> PairResponse { throw unsupported() }

    // MARK: Fiscal (sous-projet 3)
    public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data) { throw unsupported() }
    public func dashboard(from: Date, to: Date) async throws -> FinancialDashboard { throw unsupported() }
    public func periodClosures(terminalId: String?, periodType: FiscalPeriodType?) async throws -> [FiscalPeriodClosure] { throw unsupported() }
    public func executePeriodClosure(terminalId: String, periodType: FiscalPeriodType, periodKey: String) async throws -> FiscalPeriodClosure { throw unsupported() }
    public func archives() async throws -> [FiscalArchive] { throw unsupported() }
    public func createArchive(periodClosureId: UUID) async throws -> FiscalArchive { throw unsupported() }
    public func verifyArchive(data: Data, fileName: String) async throws -> ArchiveVerificationResult { throw unsupported() }
}
