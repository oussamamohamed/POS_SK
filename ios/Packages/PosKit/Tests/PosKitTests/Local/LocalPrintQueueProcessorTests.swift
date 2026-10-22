import Foundation
import Testing
@testable import PosKit

@Suite("Moteur de la file d'impression")
struct LocalPrintQueueProcessorTests {
    @Test func aJobIsSentOnce() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Caisse", drawer: true)
        try await api.addTestPrinter(printer)
        let job = try await api.enqueueTestJob(printer: printer.id, marker: "TICKET-42", drawer: true)
        clock.advance(2)

        await api.runPrintCycle()

        #expect(fake.sent.count == 1)
        let sent = try #require(fake.sent.first)
        #expect(sent.printerId == printer.id && payload(sent.payload, contains: "TICKET-42") && payload(sent.payload, contains: "\u{1B}p"))
        let stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .sent && stored.attempts == 1 && stored.lastError == nil && stored.sentAtUtc == clock.now)

        await api.runPrintCycle()
        #expect(fake.sent.count == 1)
    }

    @Test func failureThenSuccessAfterTheRetryDelay() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Caisse")
        try await api.addTestPrinter(printer)
        let job = try await api.enqueueTestJob(printer: printer.id)
        fake.failNext(1, error: PrinterTransportError.connection("refusée"))

        await api.runPrintCycle()
        var stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .pending && stored.attempts == 1 && stored.lastError == "refusée")
        #expect(stored.nextAttemptAtUtc == clock.now.addingTimeInterval(5) && stored.sentAtUtc == nil)

        clock.advance(4)
        await api.runPrintCycle()
        #expect(fake.sent.isEmpty)
        #expect(try await api.testJob(job.id)?.attempts == 1)

        clock.advance(1)
        await api.runPrintCycle()
        stored = try #require(try await api.testJob(job.id))
        #expect(fake.sent.count == 1 && stored.status == .sent && stored.attempts == 2 && stored.lastError == nil && stored.sentAtUtc == clock.now)
    }

    @Test func retryDelaysFollowTheServerSchedule() async throws {
        #expect([0, 1, 2, 3, 4, 5, 12].map(LocalPosAPI.retryDelay(attempts:)) == [5, 5, 15, 30, 60, 60, 60])

        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Caisse")
        try await api.addTestPrinter(printer)
        let job = try await api.enqueueTestJob(printer: printer.id)
        fake.failAlways(for: printer.id)

        var gaps: [TimeInterval] = []
        for _ in 0..<5 {
            await api.runPrintCycle()
            let stored = try #require(try await api.testJob(job.id))
            let gap = stored.nextAttemptAtUtc.timeIntervalSince(clock.now)
            gaps.append(gap)
            clock.advance(gap)
        }
        #expect(gaps == [5, 15, 30, 60, 60])
        #expect(try await api.testJob(job.id)?.attempts == 5)
    }

    @Test func pastTheDeadlineAJobFailsAndUnblocksItsPrinter() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Caisse")
        try await api.addTestPrinter(printer)
        let first = try await api.enqueueTestJob(printer: printer.id, marker: "PREMIER")
        fake.setOnline(false, for: printer.id)
        await api.runPrintCycle()
        clock.advance(25 * 60)
        let second = try await api.enqueueTestJob(printer: printer.id, marker: "SECOND")
        clock.advance(5 * 60)
        fake.setOnline(true, for: printer.id)

        await api.runPrintCycle()

        let failed = try #require(try await api.testJob(first.id))
        #expect(failed.status == .failed && failed.lastError == "imprimante hors ligne" && failed.attempts == 1)
        #expect(try await api.testJob(second.id)?.status == .sent)
        #expect(fake.sent.count == 1 && payload(fake.sent[0].payload, contains: "SECOND"))
    }

    @Test func aJobNeverAttemptedFailsWithDeadlineExceeded() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Caisse")
        try await api.addTestPrinter(printer)
        let job = try await api.enqueueTestJob(printer: printer.id)
        clock.advance(30 * 60)

        await api.runPrintCycle()

        let stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .failed && stored.lastError == "Deadline exceeded" && stored.attempts == 0 && fake.sent.isEmpty)
    }

    @Test func fifoPerPrinterAndAFailureOnlyBlocksItsOwnPrinter() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let one = textPrinter("Bar"), two = textPrinter("Cuisine", ip: "10.0.0.6")
        try await api.addTestPrinter(one)
        try await api.addTestPrinter(two)
        let a = try await api.enqueueTestJob(printer: one.id, marker: "UN-A")
        clock.advance(0.001)
        let b = try await api.enqueueTestJob(printer: one.id, marker: "UN-B")
        let c = try await api.enqueueTestJob(printer: two.id, marker: "DEUX-C")
        fake.failAlways(for: one.id)

        await api.runPrintCycle()

        #expect(try await api.testJob(a.id)?.attempts == 1)
        #expect(try await api.testJob(b.id)?.attempts == 0)
        #expect(try await api.testJob(c.id)?.status == .sent)
        #expect(fake.sent.map(\.printerId) == [two.id])

        fake.setOnline(true, for: one.id)
        clock.advance(5)
        await api.runPrintCycle()
        let onOne = fake.sent.filter { $0.printerId == one.id }
        #expect(onOne.count == 2 && payload(onOne[0].payload, contains: "UN-A") && payload(onOne[1].payload, contains: "UN-B"))
        #expect(try await api.testJob(b.id)?.status == .sent)
    }

    @Test func aJobWaitingForItsRetryBlocksTheNextOnes() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Bar")
        try await api.addTestPrinter(printer)
        let a = try await api.enqueueTestJob(printer: printer.id, marker: "A")
        fake.failNext(1)
        await api.runPrintCycle()
        clock.advance(1)
        let b = try await api.enqueueTestJob(printer: printer.id, marker: "B")

        await api.runPrintCycle()

        #expect(fake.sent.isEmpty)
        #expect(try await api.testJob(b.id)?.attempts == 0)
        #expect(try await api.testJob(a.id)?.attempts == 1)
    }

    @Test func disabledOrDeletedPrintersCancelTheirPendingJobs() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let disabled = textPrinter("Bar")
        try await api.addTestPrinter(disabled)
        let onDisabled = try await api.enqueueTestJob(printer: disabled.id)
        try await api.setTestPrinterActive(disabled.id, false)
        let orphan = try await api.enqueueTestJob(printer: UUID())

        await api.runPrintCycle()

        let a = try #require(try await api.testJob(onDisabled.id))
        let b = try #require(try await api.testJob(orphan.id))
        #expect(a.status == .cancelled && a.lastError == "Printer disabled" && a.attempts == 0)
        #expect(b.status == .cancelled && b.lastError == "Printer deleted" && b.attempts == 0)
        #expect(fake.sent.isEmpty)
    }

    @Test func anOverlappingCycleNeverSendsTheSameJobTwice() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Caisse")
        try await api.addTestPrinter(printer)
        let job = try await api.enqueueTestJob(printer: printer.id)
        let latch = SendLatch()
        fake.gate = { await latch.wait() }

        let first = Task { await api.runPrintCycle() }
        await latch.waitUntilEntered()
        #expect(try await api.testJob(job.id)?.attempts == 1) // marqué avant l'attente réseau
        let secondDone = Flag()
        let second = Task { await api.runPrintCycle(); await secondDone.set() }
        await yieldUntil { let done = await secondDone.isSet; let entered = await latch.entered; return done || entered > 1 }
        #expect(await secondDone.isSet) // le second cycle a rendu la main pendant que le premier envoyait
        #expect(await latch.entered == 1)
        await latch.open()
        await first.value
        await second.value

        #expect(fake.sent.count == 1)
        let stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .sent && stored.attempts == 1)
        #expect(await api.testInFlightCount == 0)
    }

    @Test func aJobCancelledDuringItsSendStaysCancelled() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Caisse")
        try await api.addTestPrinter(printer)
        let job = try await api.enqueueTestJob(printer: printer.id)
        let latch = SendLatch()
        fake.gate = { await latch.wait() }

        let cycle = Task { await api.runPrintCycle() }
        await latch.waitUntilEntered()
        try await api.cancelPrintJob(id: job.id)
        await latch.open()
        await cycle.value

        let stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .cancelled && stored.sentAtUtc == nil && stored.attempts == 1)
    }

    @Test func anUnreadableDocumentIsAFailedAttemptNotACrash() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Caisse")
        try await api.addTestPrinter(printer)
        let job = try await api.enqueueTestJob(printer: printer.id, json: "{pas du json")

        await api.runPrintCycle()
        var stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .pending && stored.attempts == 1 && !(stored.lastError ?? "").isEmpty)
        #expect(stored.nextAttemptAtUtc == clock.now.addingTimeInterval(5) && fake.sent.isEmpty)

        clock.advance(30 * 60)
        await api.runPrintCycle()
        #expect(try await api.testJob(job.id)?.status == .failed)
        try await api.retryPrintJob(id: job.id)
        stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .pending && stored.attempts == 0)
    }

    @Test func pendingJobsAreResumedByTheNextLaunch() async throws {
        let clock = LocalTestClock(), path = temporaryDatabasePath()
        let printer = textPrinter("Caisse")
        let firstFake = FakePrinterTransport()
        let (retried, fresh): (PrintJobRecord, PrintJobRecord)
        do {
            let first = try await makePrintAPI(clock: clock, transport: firstFake, path: path)
            try await first.addTestPrinter(printer)
            retried = try await first.enqueueTestJob(printer: printer.id, marker: "AVANT")
            firstFake.failNext(1)
            await first.runPrintCycle()
            clock.advance(1)
            fresh = try await first.enqueueTestJob(printer: printer.id, marker: "APRES")
        }
        clock.advance(4)
        let secondFake = FakePrinterTransport()
        let second = try await makePrintAPI(clock: clock, transport: secondFake, path: path)

        await second.runPrintCycle()

        #expect(firstFake.sent.isEmpty)
        #expect(secondFake.sent.count == 2 && payload(secondFake.sent[0].payload, contains: "AVANT") && payload(secondFake.sent[1].payload, contains: "APRES"))
        #expect(try await second.testJob(retried.id)?.status == .sent)
        #expect(try await second.testJob(fresh.id)?.status == .sent)
    }

    @Test func purgeRemovesOldSentAndCancelledJobsOnly() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = UUID()
        func make(_ status: PrintJobStatus) async throws -> UUID {
            var job = try await api.enqueueTestJob(printer: printer)
            job.status = status
            try await api.saveTestJob(job)
            return job.id
        }
        let oldSent = try await make(.sent), oldCancelled = try await make(.cancelled), oldFailed = try await make(.failed)
        clock.advance(86_400)
        let recentSent = try await make(.sent), recentCancelled = try await make(.cancelled)
        clock.advance(6.5 * 86_400)

        try await api.purgePrintQueue()

        let remaining = Set(try await api.allTestJobs().map(\.id))
        #expect(remaining == [oldFailed, recentSent, recentCancelled])
        #expect(!remaining.contains(oldSent) && !remaining.contains(oldCancelled))
    }

    @Test func aSendFailingPastTheDeadlineFailsTheJobWithoutRescheduling() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Caisse")
        try await api.addTestPrinter(printer)
        let job = try await api.enqueueTestJob(printer: printer.id)
        clock.advance(30 * 60 - 1) // encore dû à la préparation
        let latch = SendLatch()
        fake.gate = { await latch.wait() }
        fake.failNext(1, error: PrinterTransportError.connection("coupée"))

        let cycle = Task { await api.runPrintCycle() }
        await latch.waitUntilEntered()
        let before = try #require(try await api.testJob(job.id))
        clock.advance(1)
        await latch.open()
        await cycle.value

        // L'instant retenu est celui d'avant l'envoi (comme le .NET) : on rejoue donc l'échéance au cycle d'après, sans relance.
        var stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .pending && stored.lastError == "coupée" && stored.nextAttemptAtUtc == before.deadlineAtUtc.addingTimeInterval(4))
        await api.runPrintCycle()
        stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .failed && stored.lastError == "coupée" && stored.attempts == 1 && fake.sent.isEmpty)
    }

    @Test func aSendFailingAtTheDeadlineInstantIsFailedImmediately() async throws {
        let clock = LocalTestClock(), fake = FakePrinterTransport()
        let api = try await makePrintAPI(clock: clock, transport: fake)
        let printer = textPrinter("Caisse")
        try await api.addTestPrinter(printer)
        let job = try await api.enqueueTestJob(printer: printer.id)
        // Branche « échéance » de processPrinter : l'échéance est ramenée à l'instant de l'envoi pendant que celui-ci est suspendu.
        let latch = SendLatch()
        fake.gate = { await latch.wait() }
        fake.failNext(1, error: PrinterTransportError.connection("coupée"))
        let cycle = Task { await api.runPrintCycle() }
        await latch.waitUntilEntered()
        var moved = try #require(try await api.testJob(job.id))
        moved.deadlineAtUtc = clock.now
        try await api.saveTestJob(moved)
        await latch.open()
        await cycle.value

        let stored = try #require(try await api.testJob(job.id))
        #expect(stored.status == .failed && stored.lastError == "coupée" && stored.attempts == 1)
        #expect(stored.nextAttemptAtUtc == clock.now) // pas de relance programmée
    }

    @Test func notifyWithoutAWorkerIsHarmless() async throws {
        let api = try await makePrintAPI(clock: LocalTestClock(), transport: FakePrinterTransport())
        await api.notifyPrintQueue()
        await api.stopPrintWorker()
        await api.notifyPrintQueue()
    }

    /// Seul test à horloge réelle : la boucle démarre une seule fois, imprime sans appel manuel, puis s'arrête.
    @Test func theWorkerLoopPrintsAndStops() async throws {
        let fake = FakePrinterTransport()
        let api = try LocalPosAPI(path: ":memory:", transport: fake)
        let printer = textPrinter("Caisse")
        try await api.addTestPrinter(printer)
        try await api.enqueueTestJob(printer: printer.id, marker: "UN")
        await api.startPrintWorker()
        await api.startPrintWorker()
        for _ in 0..<200 where fake.sent.isEmpty { try await Task.sleep(for: .milliseconds(10)) }
        #expect(fake.sent.count == 1)

        try await api.enqueueTestJob(printer: printer.id, marker: "DEUX")
        await api.notifyPrintQueue()
        for _ in 0..<200 where fake.sent.count < 2 { try await Task.sleep(for: .milliseconds(10)) }
        #expect(fake.sent.count == 2) // réveil par signal, bien avant les 5 s de scrutation

        await api.stopPrintWorker()
        try await api.enqueueTestJob(printer: printer.id, marker: "TROIS")
        await api.notifyPrintQueue()
        try await Task.sleep(for: .milliseconds(150))
        #expect(fake.sent.count == 2)
    }
}
