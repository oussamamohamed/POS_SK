import Foundation
import Testing
@testable import PosKit

/// Accès de test au répartiteur (la base reste sur l'acteur).
extension LocalPosAPI {
    func testReceipt(_ number: String) throws -> FiscalReceiptRecord? { try fiscalRepository.receipt(identifier: number) }
    func testLatestClosure() throws -> DailyFiscalClosureRecord? { try fiscalRepository.latestClosure(terminalId: Self.standaloneTerminalId) }
    func testSummary(now: Date) throws -> LocalFiscalRepository.FiscalSummary { try fiscalRepository.summary(terminalId: Self.standaloneTerminalId, now: now) }
    func testReportData(_ start: Date?, _ end: Date) throws -> ReportPrintData {
        try fiscalRepository.reportPrintData(terminalId: Self.standaloneTerminalId, periodStart: start, periodEnd: end)
    }
    func testReceiptCount(_ c: DailyFiscalClosureRecord) throws -> Int {
        try fiscalRepository.receiptCountForClosure(terminalId: c.terminalId, sequence: c.sequence, periodStartUtc: c.periodStartUtc, periodEndUtc: c.periodEndUtc)
    }
    func testKitchenTickets() throws -> [(id: UUID, station: String, dispatchedAt: Date)] {
        try db.query("SELECT Id, StationId, DispatchedAtUtc FROM KitchenTickets ORDER BY rowid").map { ($0.uuid("Id")!, $0.string("StationId")!, $0.date("DispatchedAtUtc")!) }
    }
    func testExec(_ sql: String) throws { try db.exec(sql) }
    var testContext: TicketContext { ticketContext }
}

@Suite("Répartiteur d'impression")
struct LocalPrintDispatchTests {
    private static let burger = "Burger Gourmet Rossini"

    /// Base démo sans ses imprimantes (désactivées) : chaque test pose les siennes.
    private func makeAPI(_ clock: LocalTestClock = LocalTestClock()) async throws -> LocalPosAPI {
        let api = try await makePrintAPI(clock: clock, transport: FakePrinterTransport())
        for seeded in try await api.printers() { try await api.setTestPrinterActive(seeded.id, false) }
        return api
    }

    private func receiptPrinter(_ name: String = "Caisse", drawer: Bool = true, active: Bool = true) -> Printer {
        Printer(name: name, ipAddress: "10.0.0.9", openCashDrawerOnReceipt: drawer, assignedStationIds: ["RECEIPT"], isActive: active, textMode: true)
    }

    /// Vente comptoir réglée (1 burger à 19,50 €) : commande, n° de reçu.
    private func counterSale(_ api: LocalPosAPI, method: PaymentMethod = .cash, buzzer: String? = "7") async throws -> (order: ActiveOrder, receiptNumber: String, product: Product) {
        let burger = try await localProduct(api, Self.burger)
        _ = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        let order = try await api.addItems(table: "Comptoir", items: [localInput(burger)])
        let result = try await api.counterCheckout(CounterCheckoutRequest(
            orderId: order.orderId, terminalId: "T01", destination: .takeaway, pickupBuzzer: buzzer, tipAmount: .zero,
            requestFiscalReceiptPrint: false, mealVoucherPolicy: .capAtBalance,
            tenders: [CounterTender(method: method, amount: Money(cents: 1950), tendered: Money(cents: 1950))]
        ))
        return (order, try #require(result.receiptNumber), burger)
    }

    /// Commande attendue d'une vente comptoir, écrite à la main (pas par la fonction de conversion testée).
    private func expectedCounterOrder(_ order: ActiveOrder, _ product: Product, buzzer: String? = "7") -> TicketOrder {
        TicketOrder(destination: .takeaway, tableNumber: order.tableNumber, pickupNumber: "#A-01", pickupBuzzer: buzzer, items: [
            TicketOrderItem(quantity: 1, productName: product.name, productId: product.id, totalTtcCents: 1950, selectedModifiers: [], course: .direct),
        ])
    }

    private func expectedReceipt(_ r: FiscalReceiptRecord) -> TicketReceipt {
        TicketReceipt(receiptNumber: r.receiptNumber, createdAtUtc: r.createdAtUtc, terminalId: r.terminalId, totalTtcCents: r.ttcCents,
                      totalHtCents: r.htCents, taxBreakdownJson: r.taxBreakdownJson, signatureHash: r.signature)
    }

    private func document(_ job: PrintJobRecord) throws -> TicketDocument { try TicketDocumentJSON.deserialize(job.documentJson) }

    // MARK: 1. Comptoir sans ticket

    @Test func counterSaleWithoutReceiptQueuesAPickupVoucherAndOpensTheDrawerOnlyForCashOnADrawerPrinter() async throws {
        let clock = LocalTestClock()
        let api = try await makeAPI(clock)
        let printer = receiptPrinter()
        try await api.addTestPrinter(printer)
        let sale = try await counterSale(api)

        #expect(await api.queueCounterSale(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, withFiscalReceipt: false, hasCash: true))
        let job = try #require(try await api.allTestJobs().first)
        #expect(job.kind == .pickupVoucher && job.printerId == printer.id && job.openCashDrawer && job.duplicateOfDocumentId == nil && job.duplicateNumber == nil)
        #expect(try document(job) == TicketBuilder.pickupCoupon(order: expectedCounterOrder(sale.order, sale.product), pickupNumber: "#A-01", buzzer: "7",
                                                                 language: "fr", now: clock.now))

        clock.advance(1)
        #expect(await api.queueCounterSale(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, withFiscalReceipt: false, hasCash: false))
        #expect(try await api.allTestJobs().map(\.openCashDrawer) == [true, false])
    }

    @Test func aPrinterWithoutDrawerNeverOpensIt() async throws {
        let api = try await makeAPI()
        try await api.addTestPrinter(receiptPrinter(drawer: false))
        let sale = try await counterSale(api)
        #expect(await api.queueCounterSale(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, withFiscalReceipt: false, hasCash: true))
        #expect(try await api.allTestJobs().map(\.openCashDrawer) == [false])
    }

    // MARK: 2. Comptoir avec ticket

    @Test func counterSaleWithReceiptQueuesOneReceiptJobWithTheVoucherAfterACut() async throws {
        let clock = LocalTestClock()
        let api = try await makeAPI(clock)
        let printer = receiptPrinter()
        try await api.addTestPrinter(printer)
        let sale = try await counterSale(api, method: .creditCard)
        let receipt = try #require(try await api.testReceipt(sale.receiptNumber))

        #expect(await api.queueCounterSale(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, withFiscalReceipt: true, hasCash: false))
        let jobs = try await api.allTestJobs()
        #expect(jobs.count == 1)
        let job = try #require(jobs.first)
        #expect(job.kind == .receipt && !job.openCashDrawer && job.duplicateOfDocumentId == receipt.id && job.duplicateNumber == nil)
        let expected = TicketBuilder.fiscalReceipt(
            expectedReceipt(receipt), order: expectedCounterOrder(sale.order, sale.product), pickupNumber: "#A-01", buzzer: "7", language: "fr",
            settings: LocalTicketMapping.settings(try await api.settings()), duplicateNumber: nil, context: await api.testContext
        )
        let stored = try document(job)
        #expect(stored == expected)
        #expect(stored.lines.contains(.separator(cut: true)))
        #expect(stored.lines.contains(.text("Logiciel: RestaurantPOS v1.0.0", align: .center)))
    }

    // MARK: 3. Paiement à table

    @Test func tableReceiptMatchesTheBuilder() async throws {
        let api = try await makeAPI()
        let printer = receiptPrinter()
        try await api.addTestPrinter(printer)
        let burger = try await localProduct(api, Self.burger)
        try await seatTable(api, "T1", items: [localInput(burger, quantity: 2)])
        let order = try #require(try await api.activeOrder(table: "T1"))
        let result = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 3900)]))
        let number = try #require(result.receiptNumber)
        let receipt = try #require(try await api.testReceipt(number))

        #expect(await api.queueTableReceipt(orderId: order.orderId, receiptNumber: receipt.receiptNumber, hasCash: true))
        let job = try #require(try await api.allTestJobs().first)
        #expect(job.kind == .receipt && job.openCashDrawer && job.duplicateOfDocumentId == receipt.id)
        let expectedOrder = TicketOrder(destination: .eatIn, tableNumber: "T1", pickupNumber: nil, pickupBuzzer: nil, items: [
            TicketOrderItem(quantity: 2, productName: burger.name, productId: burger.id, totalTtcCents: 3900, selectedModifiers: [], course: .direct),
        ])
        #expect(try document(job) == TicketBuilder.receipt(expectedReceipt(receipt), order: expectedOrder, language: "fr",
                                                            settings: LocalTicketMapping.settings(try await api.settings()), duplicateNumber: nil,
                                                            context: await api.testContext))
    }

    // MARK: 4. Bons cuisine

    @Test func kitchenTicketsGoToEveryActivePrinterOfTheirStationWithoutDrawer() async throws {
        let api = try await makeAPI()
        let hotA = Printer(name: "Chaud A", ipAddress: "10.0.0.1", openCashDrawerOnReceipt: true, assignedStationIds: ["HOT_KITCHEN"], textMode: true)
        let hotB = Printer(name: "Chaud B", ipAddress: "10.0.0.2", assignedStationIds: ["HOT_KITCHEN", "BAR"], textMode: true)
        let cold = Printer(name: "Froid", ipAddress: "10.0.0.3", assignedStationIds: ["COLD"], isActive: false, textMode: true)
        for p in [hotA, hotB, cold] { try await api.addTestPrinter(p) }
        let burger = try await localProduct(api, Self.burger)
        func line(_ station: String, _ comment: [String] = []) -> OrderItemInput {
            var input = localInput(burger, modifiers: comment)
            input.preparationStationId = station
            return input
        }
        try await seatTable(api, "T2", covers: 3, items: [line("HOT_KITCHEN", ["Saignant"]), line("COLD", ["Froid"]), line("BAR", ["Glace"])])
        try await api.dispatch(table: "T2")
        let tickets = try await api.testKitchenTickets()
        #expect(tickets.map(\.station) == ["HOT_KITCHEN", "COLD", "BAR"])

        await api.queueKitchenTickets(ticketIds: tickets.map(\.id))
        let jobs = try await api.allTestJobs()
        #expect(jobs.allSatisfy { $0.kind == .kitchenTicket && !$0.openCashDrawer && $0.duplicateOfDocumentId == nil })
        #expect(Set(jobs.map(\.printerId)) == [hotA.id, hotB.id] && jobs.count == 3)
        #expect(jobs.filter { $0.printerId == hotB.id }.count == 2)

        let order = TicketOrder(destination: .eatIn, tableNumber: "T2", pickupNumber: nil, pickupBuzzer: nil, items: [
            TicketOrderItem(quantity: 1, productName: burger.name, productId: burger.id, totalTtcCents: 1950, selectedModifiers: ["Saignant"], course: .direct),
            TicketOrderItem(quantity: 1, productName: burger.name, productId: burger.id, totalTtcCents: 1950, selectedModifiers: ["Froid"], course: .direct),
            TicketOrderItem(quantity: 1, productName: burger.name, productId: burger.id, totalTtcCents: 1950, selectedModifiers: ["Glace"], course: .direct),
        ])
        let hot = TicketKitchen(serverName: "Sophie", coversCount: 3, dispatchedAtUtc: tickets[0].dispatchedAt, items: [
            TicketKitchenItem(quantity: 1, productName: burger.name, productId: burger.id, modifiersSummary: "Saignant", kitchenComment: nil),
        ])
        let expectedHot = TicketBuilder.kitchenTicket(hot, order: order, language: try await api.settings().kitchenTicketLanguage, context: await api.testContext)
        let hotJob = try #require(jobs.first { $0.printerId == hotA.id })
        #expect(try document(hotJob) == expectedHot)
    }

    // MARK: 5. Choix de l'imprimante de ticket

    @Test func noActiveReceiptPrinterQueuesNothing() async throws {
        let api = try await makeAPI()
        try await api.addTestPrinter(receiptPrinter(active: false))
        try await api.addTestPrinter(Printer(name: "Cuisine", ipAddress: "10.0.0.4", assignedStationIds: ["HOT_KITCHEN"], textMode: true))
        let sale = try await counterSale(api)
        #expect(await api.queueCounterSale(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, withFiscalReceipt: true, hasCash: true) == false)
        #expect(await api.queueTableReceipt(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, hasCash: true) == false)
        #expect(await api.queueXReport(summary: try await api.testSummary(now: Date()), terminalId: "T01") == false)
        #expect(try await api.allTestJobs().isEmpty)
    }

    @Test func theFirstActiveReceiptPrinterByOrdinalNameWins() async throws {
        let api = try await makeAPI()
        let beta = receiptPrinter("Beta")
        for p in [receiptPrinter("alpha"), receiptPrinter("Zeta"), beta, receiptPrinter("Aaa", active: false)] { try await api.addTestPrinter(p) }
        let sale = try await counterSale(api)
        #expect(await api.queueCounterSale(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, withFiscalReceipt: false, hasCash: false))
        #expect(try await api.allTestJobs().map(\.printerId) == [beta.id])
    }

    // MARK: 6. Rapports

    @Test func xZAndPeriodReportsAreReportJobsWithoutDrawer() async throws {
        let clock = LocalTestClock()
        let api = try await makeAPI(clock)
        let printer = receiptPrinter()
        try await api.addTestPrinter(printer)
        _ = try await counterSale(api)
        clock.advance(60)
        let settings = LocalTicketMapping.settings(try await api.settings())
        let context = await api.testContext

        let summary = try await api.testSummary(now: clock.now)
        #expect(await api.queueXReport(summary: summary, terminalId: "T01"))
        var job = try #require(try await api.allTestJobs().last)
        #expect(job.kind == .report && !job.openCashDrawer && job.duplicateOfDocumentId == nil)
        #expect(try document(job) == TicketBuilder.xReport(
            LocalTicketMapping.reportSummary(summary, terminalId: "T01"), data: try await api.testReportData(summary.periodStartUtc, summary.periodEndUtc),
            language: "fr", now: clock.now, settings: settings, context: context))

        _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Marie")
        let closure = try #require(try await api.testLatestClosure())
        clock.advance(1) // jobs relus par date de création
        #expect(await api.queueZClosure(closure))
        job = try #require(try await api.allTestJobs().last)
        #expect(job.kind == .report && !job.openCashDrawer && job.duplicateOfDocumentId == closure.id && job.duplicateNumber == nil)
        let daily = TicketDailyClosure(
            terminalId: "T01", closureSequence: 1, totalSalesTtcCents: 1950, totalSalesHtCents: closure.totalSalesHtCents, receiptCount: 1,
            vatBreakdownCents: LocalTicketMapping.rateAmounts(closure.taxesSummaryJson), paymentTotalsCents: [TicketMethodAmount(method: 0, cents: 1950)],
            perpetualGrandTotalCents: 1950, signatureHash: closure.signatureHash, closedAtUtc: closure.periodEndUtc, periodStartUtc: closure.periodStartUtc,
            sealedByUserName: closure.sealedByUserName
        )
        #expect(try await api.testReceiptCount(closure) == 1)
        let zData = try await api.testReportData(closure.periodStartUtc, closure.periodEndUtc)
        #expect(zData.categories.first?.items.first?.productName == Self.burger)
        #expect(try document(job) == TicketBuilder.zClosure(daily, data: zData, language: "fr", settings: settings, duplicateNumber: nil, context: context))

        let start = try #require(SQLDate.parse("2026-09-01T00:00:00Z")), end = try #require(SQLDate.parse("2026-10-01T00:00:00Z"))
        let period = PeriodClosureRecord(
            id: UUID(), terminalId: "T01", periodType: .monthly, periodKey: "2026-09", sequence: 3, periodStartUtc: start, periodEndUtc: end,
            totalTtcCents: 5000, totalHtCents: 4545, taxesSummaryJson: "{\"10\":455}", tenderTotalsJson: "{\"Cash\":5000}", perpetualGrandTotalCents: 9000,
            dailyClosureCount: 4, previousSignatureHash: "p", signatureHash: "s", sealedByUserId: UUID(), sealedByUserName: "Marie", createdAtUtc: end
        )
        clock.advance(1)
        #expect(await api.queuePeriodClosure(period))
        job = try #require(try await api.allTestJobs().last)
        #expect(job.kind == .report && !job.openCashDrawer && job.duplicateOfDocumentId == nil)
        let expectedPeriod = TicketPeriodClosure(
            terminalId: "T01", isMonthly: true, periodKey: "2026-09", closureSequence: 3, periodStartUtc: start, periodEndUtc: end, totalTtcCents: 5000,
            totalHtCents: 4545, taxesSummaryJson: "{\"10\":455}", tenderTotalsJson: "{\"Cash\":5000}", perpetualGrandTotalCents: 9000, dailyClosureCount: 4,
            signatureHash: "s", sealedByUserName: "Marie"
        )
        #expect(try document(job) == TicketBuilder.periodClosure(expectedPeriod, data: try await api.testReportData(start, end), language: "fr",
                                                                  settings: settings, context: context))
    }

    // MARK: 7. Duplicata

    @Test func receiptReprintsAreNumberedJournaledAndNeverOpenTheDrawer() async throws {
        let clock = LocalTestClock()
        let api = try await makeAPI(clock)
        try await api.addTestPrinter(receiptPrinter())
        let sale = try await counterSale(api)
        let receipt = try #require(try await api.testReceipt(sale.receiptNumber))
        let manager = UUID()

        let first = await api.queueReceiptReprint(receipt, operatorId: manager, now: clock.now)
        clock.advance(1)
        let second = await api.queueReceiptReprint(receipt, operatorId: manager, now: clock.now)
        #expect(first.printQueued && first.duplicateNumber == 1 && second.printQueued && second.duplicateNumber == 2)

        let jobs = try await api.allTestJobs()
        #expect(jobs.map(\.duplicateNumber) == [1, 2] && jobs.allSatisfy { $0.kind == .receipt && !$0.openCashDrawer && $0.duplicateOfDocumentId == receipt.id })
        let expected = TicketBuilder.receipt(expectedReceipt(receipt), order: expectedCounterOrder(sale.order, sale.product), language: "fr",
                                             settings: LocalTicketMapping.settings(try await api.settings()), duplicateNumber: 2, context: await api.testContext)
        let stored = try document(try #require(jobs.last))
        #expect(stored == expected && stored.lines.first == .text("DUPLICATA n\u{00B0}2", align: .center, bold: true, large: true))

        let journal = try await api.journalRows(LocalJournalEvents.duplicatePrinted)
        #expect(journal.count == 2 && journal.allSatisfy { $0.operatorId == manager && $0.terminalId == "T01" })
        #expect(journal[1].payload == "{\"documentId\":\"\(FiscalHashing.guidText(receipt.id))\",\"documentType\":\"receipt\",\"receiptNumber\":\"T01-000001\",\"duplicateNumber\":2,\"printQueued\":true,\"terminalId\":\"T01\"}")
    }

    @Test func aReprintWithoutPrinterStillConsumesItsNumberAndIsJournaled() async throws {
        let clock = LocalTestClock()
        let api = try await makeAPI(clock)
        let sale = try await counterSale(api)
        let receipt = try #require(try await api.testReceipt(sale.receiptNumber))
        let result = await api.queueReceiptReprint(receipt, operatorId: nil, now: clock.now)
        #expect(!result.printQueued && result.duplicateNumber == 1)
        #expect(try await api.allTestJobs().isEmpty)
        let journal = try await api.journalRows(LocalJournalEvents.duplicatePrinted)
        #expect(journal.count == 1 && journal[0].payload.contains("\"duplicateNumber\":1,\"printQueued\":false"))
    }

    @Test func aReceiptWhoseOrderIsGoneIsReprintedFromTheFallback() async throws {
        let clock = LocalTestClock()
        let api = try await makeAPI(clock)
        try await api.addTestPrinter(receiptPrinter())
        let sale = try await counterSale(api)
        let r = try #require(try await api.testReceipt(sale.receiptNumber))
        let orphan = FiscalReceiptRecord(id: r.id, orderId: UUID(), terminalId: r.terminalId, receiptNumber: r.receiptNumber, sequence: r.sequence,
                                         ttcCents: r.ttcCents, htCents: r.htCents, taxBreakdownJson: r.taxBreakdownJson, htBreakdownJson: r.htBreakdownJson,
                                         signature: r.signature, createdAtUtc: r.createdAtUtc)
        #expect(await api.queueReceiptReprint(orphan, operatorId: nil, now: clock.now) == (true, 1))
        let job = try #require(try await api.allTestJobs().first)
        #expect(try document(job) == TicketBuilder.receiptFallback(expectedReceipt(r), language: "fr", settings: LocalTicketMapping.settings(try await api.settings()),
                                                                    duplicateNumber: 1, context: await api.testContext))
    }

    @Test func zClosureReprintIsAReportDuplicate() async throws {
        let clock = LocalTestClock()
        let api = try await makeAPI(clock)
        try await api.addTestPrinter(receiptPrinter())
        _ = try await counterSale(api)
        _ = try await api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Marie")
        let closure = try #require(try await api.testLatestClosure())

        #expect(await api.queueZClosureReprint(closure, operatorId: nil, now: clock.now) == (true, 1))
        let job = try #require(try await api.allTestJobs().first)
        #expect(job.kind == .report && !job.openCashDrawer && job.duplicateOfDocumentId == closure.id && job.duplicateNumber == 1)
        let stored = try document(job)
        #expect(stored.lines.first == .text("DUPLICATA n\u{00B0}1", align: .center, bold: true, large: true))
        let journal = try await api.journalRows(LocalJournalEvents.duplicatePrinted)
        #expect(journal.count == 1 && journal[0].payload.contains("\"documentType\":\"closure\",\"closureSequence\":1,\"duplicateNumber\":1,\"printQueued\":true"))
    }

    // MARK: 8. Échec SQL

    @Test func aSQLFailureWhileQueuingReturnsFalseAndKeepsTheSale() async throws {
        let clock = LocalTestClock()
        let api = try await makeAPI(clock)
        try await api.addTestPrinter(receiptPrinter())
        let sale = try await counterSale(api)
        let receipt = try #require(try await api.testReceipt(sale.receiptNumber))
        try await api.testExec("CREATE TRIGGER fail_print BEFORE INSERT ON PrintJobs BEGIN SELECT RAISE(ABORT, 'disque plein'); END;")

        #expect(await api.queueCounterSale(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, withFiscalReceipt: true, hasCash: true) == false)
        #expect(await api.queueTableReceipt(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, hasCash: true) == false)
        #expect(await api.queueZClosure(DailyFiscalClosureRecord.sample) == false)
        await api.queueKitchenTickets(ticketIds: [UUID()])
        // Le numéro et l'entrée JET sont annulés avec le job : rien n'est consommé.
        #expect(await api.queueReceiptReprint(receipt, operatorId: nil, now: clock.now) == (false, 0))
        #expect(try await api.journalRows(LocalJournalEvents.duplicatePrinted).isEmpty)
        #expect(try await api.testReceipt(sale.receiptNumber) == receipt)

        try await api.testExec("DROP TABLE PrintJobs")
        #expect(await api.queueCounterSale(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, withFiscalReceipt: false, hasCash: true) == false)
        #expect(await api.queueReceiptReprint(receipt, operatorId: nil, now: clock.now) == (false, 0))
    }

    // MARK: 9. Conversion des données

    @Test func lineTotalsFollowTheServerFormula() {
        let product = UUID()
        func line(_ name: String, qty: Int, price: Int, comp: Bool = false, discount: Decimal = 0, extra: Int = 0, happy: Int? = nil) -> OrderLine {
            OrderLine(productId: product, productName: name, quantity: qty, unitPrice: Money(cents: price), taxRatePercent: 10,
                      modifiersSummary: extra > 0 ? ["Bacon"] : [], course: .suite, isComp: comp, discountPercent: discount,
                      modifiersPriceExtra: Money(cents: extra), isHappyHourApplied: happy != nil, originalUnitPrice: happy.map { Money(cents: $0) })
        }
        let order = ActiveOrder(tableNumber: "T9", lines: [
            line("Offert", qty: 2, price: 1200, comp: true),
            line("Remise", qty: 3, price: 333, discount: 15),          // 999 × 0,85 = 849,15 → 849
            line("Remise ronde", qty: 1, price: 1, discount: 50),      // 0,5 → 1 (au plus loin de zéro)
            line("Bacon", qty: 2, price: 1500, extra: 250),            // (1500 + 250) × 2
            line("Happy", qty: 2, price: 400, happy: 600),             // prix Happy Hour déjà dans le prix unitaire
            line("Tout", qty: 3, price: 990, discount: 10, extra: 120), // (1110 × 3) × 0,9 = 2997
        ], globalDiscountType: .percentage, globalDiscountValue: 50, destination: .eatIn, pickupNumber: "#A-03", pickupBuzzer: "4")

        let mapped = LocalTicketMapping.order(order)
        #expect(mapped.items.map(\.totalTtcCents) == [0, 849, 1, 3500, 800, 2997])
        #expect(mapped.destination == .eatIn && mapped.tableNumber == "T9" && mapped.pickupNumber == "#A-03" && mapped.pickupBuzzer == "4")
        #expect(mapped.items[3].selectedModifiers == ["Bacon"] && mapped.items[3].course == .suite && mapped.items[3].productId == product)

        var takeaway = order
        takeaway.destination = .takeaway
        let doc = TicketBuilder.pickupCoupon(order: LocalTicketMapping.order(takeaway), pickupNumber: "#A-03", buzzer: nil, language: "fr", now: Date())
        #expect(doc.lines.contains(.columns(label: TicketTexts.get("fr", "receipt.mode", [:]), value: TicketTexts.get("fr", "receipt.mode_takeaway", [:]))))
        let arabic = TicketBuilder.pickupCoupon(order: mapped, pickupNumber: "#A-03", buzzer: nil, language: "ar", now: Date())
        #expect(arabic.rightToLeft && arabic.language == "ar")
    }

    @Test func theReceiptLanguageDrivesTheStoredDocument() async throws {
        let api = try await makeAPI()
        try await api.addTestPrinter(receiptPrinter())
        var settings = try await api.settings()
        settings.receiptLanguage = "ar"
        _ = try await api.saveSettings(settings)
        let sale = try await counterSale(api)
        #expect(await api.queueCounterSale(orderId: sale.order.orderId, receiptNumber: sale.receiptNumber, withFiscalReceipt: true, hasCash: false))
        let doc = try document(try #require(try await api.allTestJobs().first))
        #expect(doc.rightToLeft && doc.language == "ar")
    }

    @Test func settingsAndReportSummariesAreMapped() {
        let blank = LocalTicketMapping.settings(RestaurantSettings(receiptLanguage: "fr"))
        #expect(blank == TicketSettings(companyName: "", addressLines: "", siret: "", vatNumber: "", certificateNumber: nil))
        let full = LocalTicketMapping.settings(RestaurantSettings(receiptLanguage: "fr", companyName: "Chez Paul", addressLines: "1 rue A", siret: "123",
                                                                  vatNumber: "FR1", certificateNumber: "C-9"))
        #expect(full == TicketSettings(companyName: "Chez Paul", addressLines: "1 rue A", siret: "123", vatNumber: "FR1", certificateNumber: "C-9"))

        let start = Date(timeIntervalSince1970: 1_000), end = Date(timeIntervalSince1970: 2_000)
        let summary = LocalFiscalRepository.FiscalSummary(
            periodStartUtc: start, periodEndUtc: end, totalSalesTtcCents: 1500, totalSalesHtCents: 1323, receiptCount: 2,
            vatBreakdown: ["10": Money(cents: 127), "5.5": Money(cents: 50)], paymentTotals: ["Cash": Money(cents: 1000), "CreditCard": Money(cents: 500)],
            perpetualGrandTotalCents: 8000, taxesSummaryJson: "{}", tenderTotalsJson: "{}"
        )
        let mapped = LocalTicketMapping.reportSummary(summary, terminalId: "T01")
        #expect(mapped == TicketReportSummary(
            terminalId: "T01", periodStartUtc: start, periodEndUtc: end, totalSalesTtcCents: 1500, totalSalesHtCents: 1323, receiptCount: 2,
            vatBreakdownCents: [TicketRateAmount(rate: Decimal(string: "5.5")!, cents: 50), TicketRateAmount(rate: 10, cents: 127)],
            paymentTotalsCents: [TicketMethodAmount(method: 0, cents: 1000), TicketMethodAmount(method: 1, cents: 500)], perpetualGrandTotalCents: 8000
        ))
        #expect(LocalTicketMapping.rateAmounts("{\"20\":10,\"5.5\":3}") == [TicketRateAmount(rate: Decimal(string: "5.5")!, cents: 3), TicketRateAmount(rate: 20, cents: 10)])
        #expect(LocalTicketMapping.methodAmounts("{\"RoomCharge\":7,\"Cash\":2}") == [TicketMethodAmount(method: 0, cents: 2), TicketMethodAmount(method: 4, cents: 7)])
    }
}

extension DailyFiscalClosureRecord {
    static let sample = DailyFiscalClosureRecord(
        id: UUID(), terminalId: "T01", sequence: 1, periodStartUtc: Date(timeIntervalSince1970: 0), periodEndUtc: Date(timeIntervalSince1970: 60),
        totalSalesTtcCents: 0, totalSalesHtCents: 0, taxesSummaryJson: "{}", tenderTotalsJson: "{}", perpetualGrandTotalCents: 0,
        previousSignatureHash: FiscalHashing.genesisHash, signatureHash: "x", sealedByUserName: "Marie", sealedByUserId: UUID(), createdAtUtc: Date(timeIntervalSince1970: 60)
    )
}
