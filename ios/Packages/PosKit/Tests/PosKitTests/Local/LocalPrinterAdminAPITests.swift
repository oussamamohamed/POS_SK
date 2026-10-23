import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : administration de la file d'impression")
struct LocalPrinterAdminAPITests {
    private static let jobNotFound = APIError.notFound("Impression introuvable.")
    private static let notRetryable = APIError.server(status: 409, message: "Seules les impressions en échec peuvent être relancées.")
    private static let notCancellable = APIError.server(status: 409, message: "Cette impression ne peut plus être annulée.")
    private static let forbidden = APIError.forbidden("Action réservée à un responsable.")

    private func setStatus(_ api: LocalPosAPI, _ job: PrintJobRecord, _ status: PrintJobStatus) async throws {
        var copy = job
        copy.status = status
        try await api.saveTestJob(copy)
    }

    @Test func statusesCountOpenJobsAndReportTheTrackerState() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let bar = textPrinter("Bar"), idle = textPrinter("Cave", ip: "10.0.0.7")
        try await api.addTestPrinter(bar)
        try await api.addTestPrinter(idle)
        try await api.setTestPrinterActive(idle.id, false)
        try await api.enqueueTestJob(printer: bar.id)
        try await api.enqueueTestJob(printer: bar.id)
        try await setStatus(api, try await api.enqueueTestJob(printer: bar.id), .failed)
        try await setStatus(api, try await api.enqueueTestJob(printer: bar.id), .sent)
        fake.failAlways(for: bar.id)
        clock.advance(3)
        await api.runPrintCycle()

        let statuses = try await api.printerStatuses()
        let barStatus = try #require(statuses.first { $0.printerId == bar.id })
        #expect(barStatus == PrinterStatus(printerId: bar.id, name: "Bar", isActive: true, isOnline: false, sinceUtc: clock.now, pendingCount: 2, failedCount: 1))
        let idleStatus = try #require(statuses.first { $0.printerId == idle.id })
        #expect(idleStatus == PrinterStatus(printerId: idle.id, name: "Cave", isActive: false, isOnline: nil, sinceUtc: nil, pendingCount: 0, failedCount: 0))

        let anonymous = try LocalPosAPI(path: ":memory:", transport: fake)
        await #expect(throws: APIError.unauthorized) { try await anonymous.printerStatuses() }
    }

    @Test func jobListShowsOpenJobsNewestFirstForManagersOnly() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let bar = textPrinter("Bar")
        try await api.addTestPrinter(bar)
        let failed = try await api.enqueueTestJob(printer: bar.id, kind: .kitchenTicket)
        try await setStatus(api, failed, .failed)
        clock.advance(1)
        try await setStatus(api, try await api.enqueueTestJob(printer: bar.id), .sent)
        try await setStatus(api, try await api.enqueueTestJob(printer: bar.id), .cancelled)
        clock.advance(1)
        let pending = try await api.enqueueTestJob(printer: bar.id, kind: .pickupVoucher)

        let jobs = try await api.printJobs(printerId: bar.id)
        #expect(jobs.map(\.id) == [pending.id, failed.id])
        #expect(jobs.map(\.kind) == ["PickupVoucher", "KitchenTicket"] && jobs.map(\.status) == ["Pending", "Failed"])
        #expect(jobs[0] == PrintJobInfo(id: pending.id, printerId: bar.id, kind: "PickupVoucher", status: "Pending", attempts: 0,
                                        createdAtUtc: pending.createdAtUtc, sentAtUtc: nil, lastError: nil))

        await #expect(throws: APIError.notFound("Imprimante introuvable.")) { try await api.printJobs(printerId: UUID()) }
        let waiter = try await makePrintAPI(clock: clock, transport: fake, pin: "2468")
        await #expect(throws: Self.forbidden) { try await waiter.printJobs(printerId: bar.id) }
    }

    @Test func jobListIsCappedAtTheHundredNewest() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let bar = textPrinter("Bar")
        try await api.addTestPrinter(bar)
        let oldest = try await api.enqueueTestJob(printer: bar.id)
        for _ in 0..<100 {
            clock.advance(1)
            try await api.enqueueTestJob(printer: bar.id)
        }
        let jobs = try await api.printJobs(printerId: bar.id)
        #expect(jobs.count == 100 && !jobs.contains { $0.id == oldest.id })
    }

    @Test func retryingAFailedJobRequeuesItForAnotherLifetime() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let bar = textPrinter("Bar")
        try await api.addTestPrinter(bar)
        let job = try await api.enqueueTestJob(printer: bar.id)
        fake.failAlways(for: bar.id)
        await api.runPrintCycle()
        clock.advance(31 * 60)
        await api.runPrintCycle()
        #expect(try await api.testJob(job.id)?.status == .failed)

        try await api.retryPrintJob(id: job.id)

        let stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .pending && stored.attempts == 0 && stored.lastError == nil)
        #expect(stored.nextAttemptAtUtc == clock.now && stored.deadlineAtUtc == clock.now.addingTimeInterval(30 * 60))
        fake.setOnline(true, for: bar.id)
        await api.runPrintCycle()
        #expect(try await api.testJob(job.id)?.status == .sent)
    }

    @Test func retryingASentReceiptQueuesANumberedDuplicateWithoutTheDrawer() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let bar = textPrinter("Bar", drawer: true)
        try await api.addTestPrinter(bar)
        let original = try await api.enqueueTestJob(printer: bar.id, marker: "RECU-7", kind: .receipt, drawer: true)
        await api.runPrintCycle()
        #expect(try await api.testJob(original.id)?.status == .sent)

        try await api.retryPrintJob(id: original.id)
        await api.runPrintCycle()
        let first = try #require(try await api.allTestJobs().first { $0.duplicateNumber == 1 })
        try await api.retryPrintJob(id: first.id)
        let second = try #require(try await api.allTestJobs().first { $0.duplicateNumber == 2 })

        #expect(first.printerId == bar.id && first.kind == .receipt && !first.openCashDrawer && first.duplicateOfDocumentId == original.id)
        #expect(second.duplicateOfDocumentId == original.id && second.kind == .receipt && !second.openCashDrawer && second.status == .pending)
        let doc = try TicketDocumentJSON.deserialize(first.documentJson)
        #expect(doc.lines.first == .text("DUPLICATA n\u{00B0}1", align: .center, bold: true, large: true))
        #expect(doc.lines.contains(.text("RECU-7")))

        #expect(fake.sent.count == 2)
        #expect(payload(fake.sent[0].payload, contains: "RECU-7") && payload(fake.sent[0].payload, contains: "\u{1B}p"))
        #expect(payload(fake.sent[1].payload, contains: "DUPLICATA") && !payload(fake.sent[1].payload, contains: "\u{1B}p"))

        let manager = try #require(try await api.journalRows("DUPLICATE_PRINTED").first?.operatorId)
        let rows = try await api.journalRows("DUPLICATE_PRINTED")
        let doc0 = FiscalHashing.guidText(original.id), printer = FiscalHashing.guidText(bar.id)
        #expect(rows.map(\.payload) == [
            "{\"documentId\":\"\(doc0)\",\"documentType\":\"print_job\",\"originalJobId\":\"\(doc0)\",\"duplicateNumber\":1,\"printerId\":\"\(printer)\"}",
            "{\"documentId\":\"\(doc0)\",\"documentType\":\"print_job\",\"originalJobId\":\"\(FiscalHashing.guidText(first.id))\",\"duplicateNumber\":2,\"printerId\":\"\(printer)\"}",
        ])
        #expect(rows.allSatisfy { $0.terminalId == nil && $0.operatorId == manager })
        #expect(try await api.testJob(original.id)?.status == .sent)
    }

    @Test func aSentReportCanBeDuplicated() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let bar = textPrinter("Bar")
        try await api.addTestPrinter(bar)
        let report = try await api.enqueueTestJob(printer: bar.id, kind: .report)
        try await setStatus(api, report, .sent)
        try await api.retryPrintJob(id: report.id)
        let duplicate = try #require(try await api.allTestJobs().first { $0.duplicateOfDocumentId == report.id })
        #expect(duplicate.kind == .report && duplicate.duplicateNumber == 1)
    }

    @Test func nonRetryableJobsAreRefused() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let bar = textPrinter("Bar")
        try await api.addTestPrinter(bar)
        let voucher = try await api.enqueueTestJob(printer: bar.id, kind: .pickupVoucher)
        let kitchen = try await api.enqueueTestJob(printer: bar.id, kind: .kitchenTicket)
        let cancelled = try await api.enqueueTestJob(printer: bar.id)
        let unreadable = try await api.enqueueTestJob(printer: bar.id, json: "pas du json")
        let pending = try await api.enqueueTestJob(printer: bar.id)
        for job in [voucher, kitchen, unreadable] { try await setStatus(api, job, .sent) }
        try await setStatus(api, cancelled, .cancelled)

        for job in [voucher, kitchen, cancelled, unreadable, pending] {
            await #expect(throws: Self.notRetryable) { try await api.retryPrintJob(id: job.id) }
        }
        await #expect(throws: Self.jobNotFound) { try await api.retryPrintJob(id: UUID()) }
        #expect(try await api.allTestJobs().count == 5)
        #expect(try await api.journalRows("DUPLICATE_PRINTED").isEmpty)
        let waiter = try await makePrintAPI(clock: clock, transport: fake, pin: "2468")
        await #expect(throws: Self.forbidden) { try await waiter.retryPrintJob(id: pending.id) }
    }

    @Test func cancelOnlyOpenJobs() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let bar = textPrinter("Bar")
        try await api.addTestPrinter(bar)
        let pending = try await api.enqueueTestJob(printer: bar.id)
        let failed = try await api.enqueueTestJob(printer: bar.id)
        let sent = try await api.enqueueTestJob(printer: bar.id)
        try await setStatus(api, failed, .failed)
        try await setStatus(api, sent, .sent)

        try await api.cancelPrintJob(id: pending.id)
        try await api.cancelPrintJob(id: failed.id)

        #expect(try await api.testJob(pending.id)?.status == .cancelled)
        #expect(try await api.testJob(failed.id)?.status == .cancelled)
        await #expect(throws: Self.notCancellable) { try await api.cancelPrintJob(id: sent.id) }
        await #expect(throws: Self.notCancellable) { try await api.cancelPrintJob(id: pending.id) }
        await #expect(throws: Self.jobNotFound) { try await api.cancelPrintJob(id: UUID()) }
        #expect(try await api.testJob(sent.id)?.status == .sent)
        let waiter = try await makePrintAPI(clock: clock, transport: fake, pin: "2468")
        await #expect(throws: Self.forbidden) { try await waiter.cancelPrintJob(id: sent.id) }
    }

    @Test func testPrintSendsImmediatelyAndReportsTheOutcome() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let bar = Printer(name: "Bar", ipAddress: "10.0.0.5", port: 9101, openCashDrawerOnReceipt: true, textMode: true)
        try await api.addTestPrinter(bar)

        let ok = try await api.testPrinter(id: bar.id)
        #expect(ok == TestPrintResult(success: true, message: "Test d'impression réussi (Bar @ 10.0.0.5:9101)"))
        #expect(fake.sent.count == 1 && fake.sent[0].printerId == bar.id)
        #expect(payload(fake.sent[0].payload, contains: "10.0.0.5:9101") && payload(fake.sent[0].payload, contains: "\u{1B}p"))

        fake.failNext(1, error: PrinterTransportError.connection("refusée"))
        let ko = try await api.testPrinter(id: bar.id)
        #expect(ko == TestPrintResult(success: false, message: "Échec de connexion imprimante (10.0.0.5:9101) : refusée"))
        fake.failNext(1, error: PrinterTransportError.connectTimeout)
        #expect(try await api.testPrinter(id: bar.id).message == "Échec de connexion imprimante (10.0.0.5:9101) : Délai de connexion dépassé")

        #expect(try await api.allTestJobs().isEmpty) // hors file
        await #expect(throws: APIError.notFound("Imprimante introuvable.")) { try await api.testPrinter(id: UUID()) }
        let waiter = try await makePrintAPI(clock: clock, transport: fake, pin: "2468")
        await #expect(throws: Self.forbidden) { try await waiter.testPrinter(id: bar.id) }
    }
}
