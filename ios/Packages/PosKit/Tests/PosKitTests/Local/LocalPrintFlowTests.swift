import Foundation
import Testing
@testable import PosKit

/// Impression branchée sur les parcours, de bout en bout par l'API publique, avec le faux transport.
@Suite("Impression dans les parcours")
struct LocalPrintFlowTests {
    private static let burger = "Burger Gourmet Rossini"
    private static let initSeq: [UInt8] = [0x1B, 0x40]

    private struct Rig {
        let api: LocalPosAPI
        let fake: FakePrinterTransport
        let clock: LocalTestClock
        let till: Printer
    }

    /// Base démo dont les imprimantes sont désactivées, plus une imprimante de ticket texte (et, au besoin, une imprimante cuisine).
    private func makeRig(_ clock: LocalTestClock = LocalTestClock(), receiptPrinter: Bool = true, kitchen: Printer? = nil) async throws -> Rig {
        let fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        for seeded in try await api.printers() { try await api.setTestPrinterActive(seeded.id, false) }
        let till = Printer(name: "Caisse", ipAddress: "10.0.0.9", openCashDrawerOnReceipt: true, assignedStationIds: ["RECEIPT"],
                           isActive: receiptPrinter, textMode: true)
        try await api.addTestPrinter(till)
        if let kitchen { try await api.addTestPrinter(kitchen) }
        return Rig(api: api, fake: fake, clock: clock, till: till)
    }

    private func counterSale(_ api: LocalPosAPI, paid: Int = 1950, fiscalReceipt: Bool = true, method: PaymentMethod = .cash) async throws -> CounterCheckoutResult {
        let burger = try await localProduct(api, Self.burger)
        _ = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        let order = try await api.addItems(table: "Comptoir", items: [localInput(burger)])
        return try await api.counterCheckout(CounterCheckoutRequest(
            orderId: order.orderId, terminalId: "T01", destination: .takeaway, pickupBuzzer: nil, tipAmount: .zero,
            requestFiscalReceiptPrint: fiscalReceipt, mealVoucherPolicy: .capAtBalance,
            tenders: [CounterTender(method: method, amount: Money(cents: paid), tendered: Money(cents: paid))]
        ))
    }

    private func tablePayment(_ api: LocalPosAPI, table: String = "T1", paid: Int = 1950, requestReceipt: Bool) async throws -> PaymentResult {
        let burger = try await localProduct(api, Self.burger)
        if try await api.activeOrder(table: table) == nil { try await seatTable(api, table, items: [localInput(burger)]) }
        let order = try #require(try await api.activeOrder(table: table))
        var request = localPayment(order, table: table, tenders: [localTender(.cash, paid)])
        request.requestReceiptPrint = requestReceipt
        return try await api.pay(request)
    }

    private func startsWithInit(_ bytes: [UInt8]) -> Bool { Array(bytes.prefix(2)) == Self.initSeq }

    // MARK: Comptoir

    @Test func aSettledCounterSaleIsPrintedWithItsReceiptNumber() async throws {
        let rig = try await makeRig()
        let result = try await counterSale(rig.api)
        #expect(result.printQueued == true && result.openCashDrawer == true)
        let number = try #require(result.receiptNumber)

        await rig.api.runPrintCycle()
        let sent = try #require(rig.fake.sent.first)
        #expect(rig.fake.sent.count == 1 && sent.printerId == rig.till.id && startsWithInit(sent.payload))
        #expect(payload(sent.payload, contains: number) && payload(sent.payload, contains: "\u{1B}p"))
    }

    @Test func aCounterSaleWithoutFiscalReceiptStillPrintsThePickupCoupon() async throws {
        let rig = try await makeRig()
        let result = try await counterSale(rig.api, fiscalReceipt: false, method: .creditCard)
        #expect(result.printQueued == true)
        let job = try #require(try await rig.api.allTestJobs().first)
        #expect(job.kind == .pickupVoucher && !job.openCashDrawer)
    }

    @Test func aPartialCounterPaymentPrintsNothing() async throws {
        let rig = try await makeRig()
        let result = try await counterSale(rig.api, paid: 1000)
        #expect(result.remainingBalance == Money(cents: 950) && result.printQueued == false)
        #expect(try await rig.api.allTestJobs().isEmpty)
    }

    @Test func withoutReceiptPrinterTheSaleIsValidatedButNotQueued() async throws {
        let rig = try await makeRig(receiptPrinter: false)
        let result = try await counterSale(rig.api)
        #expect(result.receiptNumber == "T01-000001" && result.printQueued == false)
        #expect(try await rig.api.allTestJobs().isEmpty)
    }

    // MARK: Table

    @Test func aTableReceiptIsPrintedOnlyWhenAskedAndSettled() async throws {
        let rig = try await makeRig()
        let partial = try await tablePayment(rig.api, paid: 1000, requestReceipt: true)
        #expect(partial.printQueued == false)
        let settled = try await tablePayment(rig.api, paid: 950, requestReceipt: true)
        #expect(settled.printQueued == true && settled.remainingBalance == .zero)
        let unasked = try await tablePayment(rig.api, table: "T2", requestReceipt: false)
        #expect(unasked.printQueued == false)

        let jobs = try await rig.api.allTestJobs()
        #expect(jobs.count == 1 && jobs[0].kind == .receipt && jobs[0].openCashDrawer)
        let settledNumber = try #require(settled.receiptNumber)
        await rig.api.runPrintCycle()
        #expect(rig.fake.sent.count == 1 && payload(rig.fake.sent[0].payload, contains: settledNumber))
    }

    // MARK: Cuisine

    @Test func dispatchAndFireSuitePrintTheirKitchenTickets() async throws {
        let hot = Printer(name: "Chaud", ipAddress: "10.0.0.1", openCashDrawerOnReceipt: true, assignedStationIds: ["HOT_KITCHEN"], textMode: true)
        let rig = try await makeRig(kitchen: hot)
        let burger = try await localProduct(rig.api, Self.burger)
        var hotLine = localInput(burger, course: .suite)
        hotLine.preparationStationId = "HOT_KITCHEN"
        try await seatTable(rig.api, "T3", items: [hotLine])

        try await rig.api.dispatch(table: "T3")
        var jobs = try await rig.api.allTestJobs()
        #expect(jobs.count == 1 && jobs[0].kind == .kitchenTicket && jobs[0].printerId == hot.id && !jobs[0].openCashDrawer)

        rig.clock.advance(1)
        try await rig.api.fireSuite(table: "T3")
        jobs = try await rig.api.allTestJobs()
        #expect(jobs.count == 2 && jobs.allSatisfy { $0.kind == .kitchenTicket && $0.printerId == hot.id })

        await rig.api.runPrintCycle()
        try #require(rig.fake.sent.count == 2)
        #expect(rig.fake.sent.allSatisfy { startsWithInit($0.payload) && payload($0.payload, contains: "T3") })
        #expect(payload(rig.fake.sent[1].payload, contains: "SUITE"))
    }

    @Test func aKitchenPrintFailureNeverChangesTheDispatch() async throws {
        let hot = Printer(name: "Chaud", ipAddress: "10.0.0.1", assignedStationIds: ["HOT_KITCHEN"], textMode: true)
        let rig = try await makeRig(kitchen: hot)
        try await rig.api.testExec("CREATE TRIGGER fail_print BEFORE INSERT ON PrintJobs BEGIN SELECT RAISE(ABORT, 'disque plein'); END;")
        let burger = try await localProduct(rig.api, Self.burger)
        var line = localInput(burger)
        line.preparationStationId = "HOT_KITCHEN"
        try await seatTable(rig.api, "T4", items: [line])

        try await rig.api.dispatch(table: "T4")
        #expect(try await rig.api.testKitchenTickets().count == 1)
        #expect(try await rig.api.activeOrder(table: "T4")?.lines.allSatisfy(\.isDispatched) == true)
    }

    // MARK: Rapports et clôtures

    @Test func theXReportAndTheZClosureReturnTheRealPrintQueued() async throws {
        let rig = try await makeRig()
        _ = try await counterSale(rig.api, method: .creditCard)
        rig.clock.advance(1) // jobs relus par date de création
        #expect(try await rig.api.printXReport(terminalId: "T01"))
        rig.clock.advance(1)
        let z = try await rig.api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Marie")
        #expect(z.printQueued == true)
        let kinds = try await rig.api.allTestJobs().map(\.kind)
        #expect(kinds == [.receipt, .report, .report])

        try await rig.api.setTestPrinterActive(rig.till.id, false)
        #expect(try await rig.api.printXReport(terminalId: "T01") == false)
    }

    @Test func aZClosureWithoutPrinterIsStillSealed() async throws {
        let rig = try await makeRig(receiptPrinter: false)
        let z = try await rig.api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Marie")
        #expect(z.printQueued == false && z.closureSequence == 1)
    }

    @Test func aPeriodClosureIsQueued() async throws {
        let rig = try await makeRig(LocalTestClock("2026-09-10T12:00:00Z"))
        _ = try await counterSale(rig.api, method: .creditCard)
        rig.clock.set("2026-09-10T21:00:00Z")
        _ = try await rig.api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
        rig.clock.set("2026-10-02T09:00:00Z")
        let closure = try await rig.api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
        #expect(closure.printQueued == true)
        #expect(try await rig.api.allTestJobs().last?.kind == .report)
    }

    // MARK: Duplicata

    @Test func reprintsQueueTheirDuplicateAndAreJournaledOnce() async throws {
        let rig = try await makeRig()
        let sale = try await counterSale(rig.api, method: .creditCard)
        let number = try #require(sale.receiptNumber)
        rig.clock.advance(1)
        #expect(try await rig.api.reprintReceipt(receiptIdentifier: number) == ReprintResult(printQueued: true, duplicateNumber: 1))
        rig.clock.advance(1)
        #expect(try await rig.api.reprintReceipt(receiptIdentifier: number) == ReprintResult(printQueued: true, duplicateNumber: 2))
        #expect(try await rig.api.journalRows(LocalJournalEvents.duplicatePrinted).count == 2)
        await #expect(throws: APIError.notFound("Reçu introuvable.")) { try await rig.api.reprintReceipt(receiptIdentifier: "inconnu") }

        rig.clock.advance(1)
        _ = try await rig.api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Marie")
        rig.clock.advance(1)
        #expect(try await rig.api.reprintLatestClosure(terminalId: "T01") == ReprintResult(printQueued: true, duplicateNumber: 1))
        #expect(try await rig.api.journalRows(LocalJournalEvents.duplicatePrinted).count == 3)

        let jobs = try await rig.api.allTestJobs()
        #expect(jobs.map(\.duplicateNumber) == [nil, 1, 2, nil, 1])
        await rig.api.runPrintCycle()
        #expect(rig.fake.sent.count == 5 && payload(rig.fake.sent[2].payload, contains: "DUPLICATA"))
    }

    // MARK: Pannes

    @Test func anOfflinePrinterKeepsTheSaleAndPrintsWhenBackOnline() async throws {
        let rig = try await makeRig()
        rig.fake.setOnline(false, for: rig.till.id)
        let result = try await counterSale(rig.api)
        #expect(result.receiptNumber == "T01-000001" && result.printQueued == true)

        await rig.api.runPrintCycle()
        var job = try #require(try await rig.api.allTestJobs().first)
        #expect(job.status == .pending && job.attempts == 1 && job.lastError == "imprimante hors ligne" && rig.fake.sent.isEmpty)

        rig.fake.setOnline(true, for: rig.till.id)
        rig.clock.advance(5)
        await rig.api.runPrintCycle()
        job = try #require(try await rig.api.allTestJobs().first)
        #expect(job.status == .sent && rig.fake.sent.count == 1 && payload(rig.fake.sent[0].payload, contains: "T01-000001"))
    }

    @Test func aSQLFailureWhileQueuingKeepsTheSale() async throws {
        let rig = try await makeRig()
        try await rig.api.testExec("CREATE TRIGGER fail_print BEFORE INSERT ON PrintJobs BEGIN SELECT RAISE(ABORT, 'disque plein'); END;")
        let result = try await counterSale(rig.api)
        #expect(result.receiptNumber == "T01-000001" && result.printQueued == false)
        #expect(try await rig.api.testReceipt("T01-000001") != nil)
        let table = try await tablePayment(rig.api, requestReceipt: true)
        #expect(table.receiptNumber == "T01-000002" && table.printQueued == false)
    }

    @Test func twoSalesInARowArePrintedInOrder() async throws {
        let rig = try await makeRig()
        rig.fake.setOnline(false, for: rig.till.id)
        _ = try await counterSale(rig.api)
        rig.clock.advance(1)
        _ = try await counterSale(rig.api)
        await rig.api.runPrintCycle()
        rig.fake.setOnline(true, for: rig.till.id)
        rig.clock.advance(5)
        await rig.api.runPrintCycle()
        try #require(rig.fake.sent.count == 2)
        #expect(payload(rig.fake.sent[0].payload, contains: "T01-000001") && payload(rig.fake.sent[1].payload, contains: "T01-000002"))
    }
}
