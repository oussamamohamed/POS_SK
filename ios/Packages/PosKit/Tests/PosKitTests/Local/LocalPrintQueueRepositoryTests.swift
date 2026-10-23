import Foundation
import Testing
@testable import PosKit

@Suite("Dépôt de la file d'impression")
struct LocalPrintQueueRepositoryTests {
    private let t0 = Date(timeIntervalSince1970: 1_760_000_000.125)
    private let printer = UUID()

    private func makeRepo() throws -> LocalPrintQueueRepository {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        return LocalPrintQueueRepository(db: db)
    }

    @discardableResult
    private func add(_ repo: LocalPrintQueueRepository, printer: UUID? = nil, kind: PrintJobKind = .receipt, at: Date? = nil) throws -> PrintJobRecord {
        try repo.enqueue(printerId: printer ?? self.printer, kind: kind, documentJson: "{}", openCashDrawer: false,
                         duplicateOfDocumentId: nil, duplicateNumber: nil, now: at ?? t0)
    }

    @Test func enqueueSetsTheInitialState() throws {
        let repo = try makeRepo()
        let docId = UUID()
        let job = try repo.enqueue(printerId: printer, kind: .kitchenTicket, documentJson: "{\"a\":1}", openCashDrawer: true,
                                   duplicateOfDocumentId: docId, duplicateNumber: 2, now: t0)
        #expect(job.status == .pending && job.attempts == 0 && job.lastError == nil && job.sentAtUtc == nil)
        #expect(job.nextAttemptAtUtc == t0 && job.deadlineAtUtc == t0.addingTimeInterval(30 * 60) && job.createdAtUtc == t0)
        let read = try #require(try repo.job(id: job.id))
        #expect(read == job)
        #expect(read.kind == .kitchenTicket && read.openCashDrawer && read.documentJson == "{\"a\":1}")
        #expect(read.duplicateOfDocumentId == docId && read.duplicateNumber == 2)
        #expect(try repo.job(id: UUID()) == nil)
    }

    @Test func datesRoundTripToTheMillisecond() throws {
        let repo = try makeRepo()
        let job = try add(repo, at: Date(timeIntervalSince1970: 1_760_000_000.9871))
        let read = try #require(try repo.job(id: job.id))
        #expect(read == job)
        #expect(Int64((read.createdAtUtc.timeIntervalSince1970 * 1000).rounded()) == 1_760_000_000_987)
    }

    @Test func pendingJobsAreOrderedByCreationThenInsertion() throws {
        let repo = try makeRepo()
        let late = try add(repo, at: t0.addingTimeInterval(5))
        let a = try add(repo)
        let b = try add(repo)
        let sent = try add(repo, at: t0.addingTimeInterval(-10))
        var done = sent
        done.status = .sent
        try repo.save(done)
        #expect(try repo.pendingJobs().map(\.id) == [a.id, b.id, late.id])
    }

    @Test func jobsCreatedAtTheSameInstantKeepInsertionOrder() throws {
        let repo = try makeRepo()
        let jobs = try (0..<10).map { _ in try add(repo) }
        #expect(try repo.pendingJobs().map(\.id) == jobs.map(\.id))
        #expect(try repo.jobs(printerId: printer, statuses: [.pending], limit: 20).map(\.id) == jobs.reversed().map(\.id))
    }

    @Test func jobsFilterByPrinterAndStatusNewestFirst() throws {
        let repo = try makeRepo()
        let other = UUID()
        let old = try add(repo, at: t0)
        let mid = try add(repo, at: t0.addingTimeInterval(1))
        let new = try add(repo, at: t0.addingTimeInterval(2))
        try add(repo, printer: other, at: t0.addingTimeInterval(3))
        var failed = mid
        failed.status = .failed
        try repo.save(failed)
        #expect(try repo.jobs(printerId: printer, statuses: [.pending, .failed], limit: 10).map(\.id) == [new.id, mid.id, old.id])
        #expect(try repo.jobs(printerId: printer, statuses: [.failed], limit: 10).map(\.id) == [mid.id])
        #expect(try repo.jobs(printerId: printer, statuses: [.pending, .failed], limit: 2).map(\.id) == [new.id, mid.id])
        #expect(try repo.jobs(printerId: printer, statuses: [], limit: 10).isEmpty)
    }

    @Test func saveUpdatesStateAndCountsReflectIt() throws {
        let repo = try makeRepo()
        var job = try add(repo)
        try add(repo)
        let other = UUID()
        var failedElsewhere = try add(repo, printer: other)
        failedElsewhere.status = .failed
        try repo.save(failedElsewhere)
        job.status = .failed
        job.attempts = 3
        job.lastError = "Connexion refusée"
        job.nextAttemptAtUtc = t0.addingTimeInterval(30)
        job.sentAtUtc = t0.addingTimeInterval(31)
        try repo.save(job)
        #expect(try repo.job(id: job.id) == job)
        let counts = try repo.counts()
        #expect(counts[printer]?.pending == 1 && counts[printer]?.failed == 1)
        #expect(counts[other]?.pending == 0 && counts[other]?.failed == 1)
    }

    @Test func purgeRemovesOnlyOldSentAndCancelledJobs() throws {
        let repo = try makeRepo()
        let limit = t0.addingTimeInterval(100)
        var ids: [PrintJobStatus: UUID] = [:]
        for status in [PrintJobStatus.sent, .cancelled, .failed, .pending] {
            var j = try add(repo, at: t0)
            j.status = status
            try repo.save(j)
            ids[status] = j.id
        }
        var recent = try add(repo, at: limit.addingTimeInterval(1))
        recent.status = .sent
        try repo.save(recent)
        #expect(try repo.purge(olderThan: limit) == 2)
        #expect(try repo.job(id: ids[.sent]!) == nil && repo.job(id: ids[.cancelled]!) == nil)
        #expect(try repo.job(id: ids[.failed]!) != nil && repo.job(id: ids[.pending]!) != nil && repo.job(id: recent.id) != nil)
    }
}
