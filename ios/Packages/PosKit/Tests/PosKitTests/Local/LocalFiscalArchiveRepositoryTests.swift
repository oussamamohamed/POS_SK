import Foundation
import Testing
@testable import PosKit

@Suite("Archives fiscales : dépôt (chaîne, JET, vérification)")
struct LocalFiscalArchiveRepositoryTests {
    private static let manager = UUID()

    private struct Fixture {
        let db: SQLiteDatabase
        let repo: LocalFiscalRepository
        let january: PeriodClosureRecord
        let february: PeriodClosureRecord
    }

    private static func insertClosure(_ db: SQLiteDatabase, key: String, sequence: Int, start: String, end: String) throws -> UUID {
        let id = UUID()
        try db.run(
            """
            INSERT INTO PeriodClosures (Id, TerminalId, PeriodType, PeriodKey, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalTtcAmount, TotalHtAmount,
                TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, DailyClosureCount, PreviousSignatureHash, SignatureHash, SealedByUserId,
                SealedByUserName, CreatedAtUtc)
            VALUES (?, 'T01', 1, ?, ?, ?, ?, 0, 0, '{}', '{}', 0, 0, 'GENESIS', 'SIG', ?, 'Chef', ?)
            """,
            [.uuid(id), .text(key), .integer(sequence), .text(start), .text(end), .uuid(manager), .text(end)]
        )
        return id
    }

    private static func makeFixture(path: String = ":memory:") throws -> Fixture {
        let db = try SQLiteDatabase(path: path)
        try LocalMigrator.migrate(db)
        let jan = try insertClosure(db, key: "2025-01", sequence: 1, start: "2025-01-01T00:00:00.000Z", end: "2025-02-01T00:00:00.000Z")
        let feb = try insertClosure(db, key: "2025-02", sequence: 2, start: "2025-02-01T00:00:00.000Z", end: "2025-03-01T00:00:00.000Z")
        let repo = LocalFiscalRepository(db: db)
        return Fixture(db: db, repo: repo, january: try repo.periodClosure(id: jan)!, february: try repo.periodClosure(id: feb)!)
    }

    /// Construit le ZIP et enregistre l'archive, comme `LocalPosAPI.createArchive` (sans fichier sur disque).
    @discardableResult
    private static func archive(_ f: Fixture, _ closure: PeriodClosureRecord, at iso: String) throws -> (archive: FiscalArchive, zip: Data) {
        let now = SQLDate.parse(iso)!
        let id = UUID()
        let zip = try f.repo.archiveZip(for: closure, archiveId: id, createdAt: now)
        let record = try f.db.transaction {
            try f.repo.insertArchive(
                id: id, closure: closure, fileName: LocalFiscalRepository.archiveFileName(closure), fileSha256: FiscalHashing.sha256Hex(zip),
                fileSizeBytes: zip.count, userId: manager, createdAt: now
            )
        }
        return (record, zip)
    }

    @Test func archivesFormOneGlobalChainListedNewestFirst() throws {
        let f = try Self.makeFixture()
        let first = try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z").archive
        let second = try Self.archive(f, f.february, at: "2025-03-02T10:00:00.120Z").archive
        #expect(first.archiveSequence == 1 && second.archiveSequence == 2)
        #expect(first.previousSignatureHash == FiscalHashing.genesisHash)
        #expect(second.previousSignatureHash == first.signatureHash)
        #expect(second.signatureHash == FiscalHashing.archiveHash(
            previousHash: first.signatureHash, periodType: 1, periodKey: "2025-02", fileSha256: second.fileSha256, createdAt: second.createdAtUtc))
        #expect(try f.repo.fiscalArchives().map(\.archiveSequence) == [2, 1])
        #expect(try f.repo.fiscalArchive(periodClosureId: f.january.id)?.id == first.id)
        #expect(second.fileName == "archive-T01-M-2025-02.zip")
    }

    @Test func aSecondArchiveForTheSameClosureIsRefused() throws {
        let f = try Self.makeFixture()
        try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        #expect(throws: FiscalArchiveError.exists) { try Self.archive(f, f.january, at: "2025-02-03T10:00:00.000Z") }
        #expect(try f.repo.fiscalArchives().count == 1)
        #expect(try f.db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'ARCHIVE_CREATED'").count == 1)
    }

    @Test func theCreationIsJournaledWithTheDotNetPayload() throws {
        let f = try Self.makeFixture()
        let a = try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z").archive
        let row = try #require(try f.db.query("SELECT * FROM JournalEntries WHERE EventType = 'ARCHIVE_CREATED'").first)
        #expect(row.string("PayloadJson") ==
            "{\"archiveId\":\"\(FiscalHashing.guidText(a.id))\",\"periodClosureId\":\"\(FiscalHashing.guidText(f.january.id))\",\"periodType\":1,"
            + "\"periodKey\":\"2025-01\",\"fileName\":\"archive-T01-M-2025-01.zip\",\"fileSha256\":\"\(a.fileSha256)\",\"fileSizeBytes\":\(a.fileSizeBytes),"
            + "\"archiveSequence\":1,\"signatureHash\":\"\(a.signatureHash)\"}")
        #expect(row.string("TerminalId") == "T01")
        #expect(row.uuid("OperatorId") == Self.manager)
    }

    @Test func anUntouchedArchiveIsValid() throws {
        let f = try Self.makeFixture()
        let (a, zip) = try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        #expect(try f.repo.verifyArchive(data: zip, fileName: "renommée.zip") == ArchiveVerificationResult(isValid: true, archiveId: a.id, reason: nil))
    }

    @Test func oneFlippedByteIsAHashMismatch() throws {
        let f = try Self.makeFixture()
        let (a, zip) = try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        var tampered = zip
        tampered[43] ^= 0xFF // 1er octet des données de receipts.json (en-tête local 30 + nom 13) : le manifeste reste lisible
        #expect(try f.repo.verifyArchive(data: tampered, fileName: "autre.zip") == ArchiveVerificationResult(isValid: false, archiveId: a.id, reason: "hash_mismatch"))
    }

    @Test func withoutManifestTheArchiveIsFoundByHashThenByFileName() throws {
        let f = try Self.makeFixture()
        let now = SQLDate.parse("2025-02-02T10:00:00.000Z")!
        let bare = try ZipStoreWriter.archive([("a.json", Data("[]".utf8))], modifiedAt: now)
        let a = try f.db.transaction {
            try f.repo.insertArchive(id: UUID(), closure: f.january, fileName: "archive-T01-M-2025-01.zip", fileSha256: FiscalHashing.sha256Hex(bare),
                                     fileSizeBytes: bare.count, userId: Self.manager, createdAt: now)
        }
        #expect(try f.repo.verifyArchive(data: bare, fileName: "x.zip").isValid)
        var tampered = bare
        tampered[tampered.count - 1] ^= 0x01
        #expect(try f.repo.verifyArchive(data: tampered, fileName: a.fileName) == ArchiveVerificationResult(isValid: false, archiveId: a.id, reason: "hash_mismatch"))
        #expect(try f.repo.verifyArchive(data: tampered, fileName: "x.zip") == ArchiveVerificationResult(isValid: false, archiveId: nil, reason: "unknown_archive"))
    }

    @Test func emptyOrForeignDataIsAnUnknownArchive() throws {
        let f = try Self.makeFixture()
        try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        let unknown = ArchiveVerificationResult(isValid: false, archiveId: nil, reason: "unknown_archive")
        #expect(try f.repo.verifyArchive(data: Data(), fileName: "archive-T01-M-2025-01.zip") == unknown)
        #expect(try f.repo.verifyArchive(data: Data("pas un zip".utf8), fileName: "") == unknown)
        #expect(try f.repo.verifyArchive(data: Data("pas un zip".utf8), fileName: nil) == unknown)
    }

    @Test func aTamperedSignatureOrLinkIsAChainBreak() throws {
        let f = try Self.makeFixture(path: temporaryDatabasePath())
        let (first, firstZip) = try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        let (second, secondZip) = try Self.archive(f, f.february, at: "2025-03-02T10:00:00.000Z")
        // Falsification hors application : le trigger d'immuabilité est contourné sur cette base de test.
        try f.db.exec("DROP TRIGGER TR_FiscalArchives_NoUpdate")
        try f.db.run("UPDATE FiscalArchives SET SignatureHash = ? WHERE Id = ?", [.text(String(repeating: "0", count: 64)), .uuid(first.id)])
        #expect(try f.repo.verifyArchive(data: firstZip, fileName: nil) == ArchiveVerificationResult(isValid: false, archiveId: first.id, reason: "chain_break"))
        #expect(try f.repo.verifyArchive(data: secondZip, fileName: nil) == ArchiveVerificationResult(isValid: false, archiveId: second.id, reason: "chain_break"))
    }

    @Test func anUnreadableArchiveRowStopsTheChainInsteadOfForkingIt() throws {
        let f = try Self.makeFixture()
        try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        try f.db.run(
            """
            INSERT INTO FiscalArchives (Id, PeriodClosureId, PeriodType, PeriodKey, FileName, FileSha256, FileSizeBytes, ArchiveSequence,
                PreviousSignatureHash, SignatureHash, CreatedByUserId, CreatedAtUtc)
            VALUES ('pas-un-uuid', ?, 1, 'X', 'x.zip', 'a', 1, 2, 'p', 's', ?, '2025-03-01T00:00:00.000Z')
            """,
            [.uuid(UUID()), .uuid(Self.manager)]
        )
        let journalBefore = try f.db.query("SELECT COUNT(*) AS n FROM JournalEntries").first?.int("n")
        #expect(throws: FiscalArchiveError.corruptChain) { try f.repo.fiscalArchives() }
        #expect(throws: FiscalArchiveError.corruptChain) { try Self.archive(f, f.february, at: "2025-03-02T10:00:00.000Z") }
        #expect(try f.db.query("SELECT COUNT(*) AS n FROM FiscalArchives").first?.int("n") == 2)
        #expect(try f.db.query("SELECT COUNT(*) AS n FROM JournalEntries").first?.int("n") == journalBefore)
    }

    @Test func anUnreadablePreviousArchiveIsAChainBreakNotAnError() throws {
        let f = try Self.makeFixture()
        try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        let (february, februaryZip) = try Self.archive(f, f.february, at: "2025-03-02T10:00:00.000Z")
        // Falsification hors application : le trigger d'immuabilité est contourné sur cette base de test.
        try f.db.exec("DROP TRIGGER TR_FiscalArchives_NoUpdate")
        try f.db.run("UPDATE FiscalArchives SET Id = 'pas-un-uuid' WHERE ArchiveSequence = 1")
        #expect(try f.repo.verifyArchive(data: februaryZip, fileName: nil)
            == ArchiveVerificationResult(isValid: false, archiveId: february.id, reason: "chain_break"))
    }

    @Test func aNonCorruptionFailureOnThePreviousArchiveIsNotAChainBreak() throws {
        struct Locked: Error, Equatable {}
        let f = try Self.makeFixture()
        try Self.archive(f, f.january, at: "2025-02-02T10:00:00.000Z")
        let (_, februaryZip) = try Self.archive(f, f.february, at: "2025-03-02T10:00:00.000Z")
        #expect(throws: Locked()) {
            try f.repo.verifyArchive(data: februaryZip, fileName: nil, previousArchive: { _ in throw Locked() })
        }
    }
}
