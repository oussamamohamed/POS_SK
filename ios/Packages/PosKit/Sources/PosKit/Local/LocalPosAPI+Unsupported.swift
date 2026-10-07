import Foundation

/// Méthodes de `PosAPI` pas encore portées sur la base locale. Chaque plan suivant retire ses lignes d'ici.
extension LocalPosAPI {
    // MARK: Appairage : sans objet en mode autonome (le poste est son propre terminal)
    public func pair(code: String) async throws -> PairResponse { throw unsupported() }

    // MARK: Transfert et fusion (retiré par la tâche 4)
    public func transfer(from: String, to: String, merge: Bool) async throws -> OperationResult { throw unsupported() }

    // MARK: Remises et gratuités (retiré par la tâche 5)
    public func applyDiscount(orderId: UUID, type: DiscountType, value: Decimal, reason: String, operatorId: UUID?) async throws { throw unsupported() }
    public func removeDiscount(orderId: UUID) async throws { throw unsupported() }
    public func compItem(orderId: UUID, lineId: UUID, reason: String, operatorId: UUID?) async throws { throw unsupported() }

    // MARK: Encaissement (plan 1b)
    public func pay(_ request: PaymentRequest) async throws -> PaymentResult { throw unsupported() }
    public func hotelRooms() async throws -> [HotelRoom] { throw unsupported() }
    public func chargeRoom(_ request: RoomChargeRequest) async throws -> OperationResult { throw unsupported() }

    // MARK: Comptoir & vente à emporter (plan 1b)
    public func openCounterOrder(terminalId: String, destination: OrderDestination) async throws -> ActiveOrder { throw unsupported() }
    public func holdOrder(orderId: UUID, terminalId: String, label: String) async throws { throw unsupported() }
    public func heldOrders(terminalId: String) async throws -> [HeldOrder] { throw unsupported() }
    public func recallHeldOrder(holdId: UUID) async throws -> ActiveOrder { throw unsupported() }
    public func voidHeldOrder(holdId: UUID, supervisorPin: String, reason: String, terminalId: String) async throws { throw unsupported() }
    public func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult { throw unsupported() }

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

    // MARK: Imprimantes (plan 1c)
    public func printers() async throws -> [Printer] { throw unsupported() }
    public func savePrinter(_ printer: Printer, isNew: Bool) async throws { throw unsupported() }
    public func testPrinter(id: UUID) async throws -> TestPrintResult { throw unsupported() }
    public func printerStatuses() async throws -> [PrinterStatus] { throw unsupported() }
    public func printJobs(printerId: UUID) async throws -> [PrintJobInfo] { throw unsupported() }
    public func retryPrintJob(id: UUID) async throws { throw unsupported() }
    public func cancelPrintJob(id: UUID) async throws { throw unsupported() }

    // MARK: Happy Hour (plan 1c)
    public func happyHourStatus(terminalId: String) async throws -> HappyHourStatus { throw unsupported() }
    public func happyHourPricing(terminalId: String) async throws -> HappyHourPricingTable { throw unsupported() }
    public func activateHappyHourOverride(terminalId: String, pin: String, minutes: Int, reason: String) async throws -> OperationResult { throw unsupported() }
    public func stopHappyHourOverride(terminalId: String, pin: String, reason: String) async throws -> OperationResult { throw unsupported() }
    public func happyHourSchedules() async throws -> [HappyHourSchedule] { throw unsupported() }
    public func createHappyHourSchedule(_ schedule: HappyHourSchedule) async throws -> UUID? { throw unsupported() }
    public func deleteHappyHourSchedule(id: UUID) async throws { throw unsupported() }
    public func applyHappyHourRules(scheduleId: UUID, _ request: BatchPriceRulesRequest) async throws -> Int { throw unsupported() }
    public func deleteHappyHourRules(scheduleId: UUID, ruleIds: [UUID]) async throws { throw unsupported() }

    // MARK: Réseau (plan 1c)
    public func networkInfo() async throws -> NetworkInfo { throw unsupported() }
    public func syncStatus() async throws -> SyncStatus { throw unsupported() }
    public func forceSync() async throws { throw unsupported() }
}
