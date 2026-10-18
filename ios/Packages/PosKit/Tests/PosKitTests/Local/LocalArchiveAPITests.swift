import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : archives fiscales")
struct LocalArchiveAPITests {
    private struct Context {
        let api: LocalPosAPI
        let db: SQLiteDatabase
        let folder: URL
        let clock: LocalTestClock
        let operatorId: UUID?
    }

    /// API sur fichier avec son propre dossier d'archives (les tests tournent en parallèle), horloge de test et calendrier UTC.
    private func makeContext(pin: String = "1234") async throws -> Context {
        let clock = LocalTestClock("2026-09-10T12:00:00Z")
        let path = temporaryDatabasePath()
        let folder = FileManager.default.temporaryDirectory.appendingPathComponent("archives-\(UUID().uuidString)", isDirectory: true)
        let api = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar, archivesDirectory: folder)
        let login = try await api.login(pin: pin)
        return Context(api: api, db: try SQLiteDatabase(path: path), folder: folder, clock: clock, operatorId: login.operatorId)
    }

    /// Septembre 2026 : une vente, son Z, puis la clôture mensuelle.
    private func closeSeptember(_ c: Context) async throws -> FiscalPeriodClosure {
        let burger = try await localProduct(c.api, "Burger Gourmet Rossini")
        try await seatTable(c.api, "T1", items: [localInput(burger)])
        let order = try #require(try await c.api.activeOrder(table: "T1"))
        _ = try await c.api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1950)]))
        c.clock.set("2026-09-10T21:00:00Z")
        _ = try await c.api.zClosure(terminalId: "T01", managerId: UUID(), managerName: "Chef")
        c.clock.set("2026-10-02T09:00:00Z")
        return try await c.api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-09")
    }

    private func folderContents(_ c: Context) -> [String] {
        ((try? FileManager.default.contentsOfDirectory(atPath: c.folder.path)) ?? []).sorted()
    }

    @Test func theArchiveIsWrittenSignedAndListed() async throws {
        let c = try await makeContext()
        let closure = try await closeSeptember(c)
        let archive = try await c.api.createArchive(periodClosureId: closure.id)

        #expect(archive.fileName == "archive-T01-M-2026-09.zip")
        #expect(archive.archiveSequence == 1)
        #expect(archive.previousSignatureHash == FiscalHashing.genesisHash)
        #expect(archive.createdByUserId == c.operatorId)
        #expect(folderContents(c) == ["archive-T01-M-2026-09.zip"]) // pas de .tmp laissé
        let data = try Data(contentsOf: c.folder.appendingPathComponent(archive.fileName))
        #expect(FiscalHashing.sha256Hex(data) == archive.fileSha256)
        #expect(data.count == archive.fileSizeBytes)
        #expect(ZipStoreReader.names(in: data) == ["receipts.json", "closures.json", "period-closures.json", "journal.json", "manifest.json"])
        #expect(try await c.api.archives().map(\.id) == [archive.id])
    }

    #if os(macOS)
    @Test(.enabled(if: FileManager.default.isExecutableFile(atPath: "/usr/bin/unzip")))
    func anIndependentUnzipReadsTheArchiveAndTheManifestHashesMatch() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        let path = c.folder.appendingPathComponent(archive.fileName).path
        #expect(try runUnzip(["-t", path]).status == 0)
        let manifestData = try runUnzip(["-p", path, "manifest.json"]).output
        let manifest = try #require(try JSONSerialization.jsonObject(with: manifestData) as? [String: Any])
        #expect(manifest["archiveId"] as? String == FiscalHashing.guidText(archive.id))
        #expect((manifest["counts"] as? [String: Int])?["receipts"] == 1)
        let files = try #require(manifest["files"] as? [String: String])
        #expect(files.count == 4)
        for (name, sha) in files {
            #expect(FiscalHashing.sha256Hex(try runUnzip(["-p", path, name]).output) == sha, Comment(rawValue: name))
        }
    }
    #endif

    @Test func archivesChainAcrossClosures() async throws {
        let c = try await makeContext()
        let september = try await closeSeptember(c)
        c.clock.set("2026-11-02T09:00:00Z")
        let october = try await c.api.executePeriodClosure(terminalId: "T01", periodType: .monthly, periodKey: "2026-10")
        let first = try await c.api.createArchive(periodClosureId: september.id)
        let second = try await c.api.createArchive(periodClosureId: october.id)
        #expect(second.archiveSequence == 2)
        #expect(second.previousSignatureHash == first.signatureHash)
        #expect(try await c.api.archives().map(\.archiveSequence) == [2, 1])
    }

    @Test func aSecondArchiveIsRefusedAndWritesNothing() async throws {
        let c = try await makeContext()
        let closure = try await closeSeptember(c)
        _ = try await c.api.createArchive(periodClosureId: closure.id)
        await #expect(throws: APIError.server(status: 409, message: L10n.string("fiscal.archive_error_exists"))) {
            _ = try await c.api.createArchive(periodClosureId: closure.id)
        }
        #expect(try c.db.query("SELECT 1 AS x FROM FiscalArchives").count == 1)
        #expect(try c.db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'ARCHIVE_CREATED'").count == 1)
        #expect(folderContents(c) == ["archive-T01-M-2026-09.zip"])
    }

    @Test func anUnknownClosureIsNotFoundAndWritesNothing() async throws {
        let c = try await makeContext()
        await #expect(throws: APIError.notFound(L10n.string("fiscal.archive_error_closure_missing"))) {
            _ = try await c.api.createArchive(periodClosureId: UUID())
        }
        #expect(try c.db.query("SELECT 1 AS x FROM FiscalArchives").isEmpty)
        #expect(folderContents(c).isEmpty)
    }

    @Test func verificationUsesTheFileContent() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        let data = try Data(contentsOf: c.folder.appendingPathComponent(archive.fileName))
        #expect(try await c.api.verifyArchive(data: data, fileName: archive.fileName).isValid)
        var tampered = data
        tampered[43] ^= 0xFF // 1er octet des données de receipts.json
        let result = try await c.api.verifyArchive(data: tampered, fileName: archive.fileName)
        #expect(result == ArchiveVerificationResult(isValid: false, archiveId: archive.id, reason: "hash_mismatch"))
        await #expect(throws: APIError.server(status: 400, message: L10n.string("fiscal.archive_error_empty_file"))) {
            _ = try await c.api.verifyArchive(data: Data(), fileName: archive.fileName)
        }
    }

    @Test func theFileIsServedAndTheExportJournaledWithoutTerminalOrOperator() async throws {
        let c = try await makeContext()
        let closure = try await closeSeptember(c)
        let archive = try await c.api.createArchive(periodClosureId: closure.id)
        let url = try await c.api.archiveFile(id: archive.id)
        #expect(url.standardizedFileURL == c.folder.appendingPathComponent(archive.fileName).standardizedFileURL)
        let row = try #require(try c.db.query("SELECT * FROM JournalEntries WHERE EventType = 'ARCHIVE_EXPORTED'").first)
        #expect(row.string("PayloadJson") ==
            "{\"archiveId\":\"\(FiscalHashing.guidText(archive.id))\",\"periodClosureId\":\"\(FiscalHashing.guidText(closure.id))\","
            + "\"fileName\":\"archive-T01-M-2026-09.zip\",\"fileSha256\":\"\(archive.fileSha256)\"}")
        #expect(row.string("TerminalId") == nil && row.string("OperatorId") == nil)

        await #expect(throws: APIError.notFound(L10n.string("fiscal.archive_error_file_missing"))) { _ = try await c.api.archiveFile(id: UUID()) }
    }

    @Test func aMissingFileIsRebuiltIdenticallyAndServed() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        try FileManager.default.removeItem(at: c.folder.appendingPathComponent(archive.fileName))
        let url = try await c.api.archiveFile(id: archive.id)
        let data = try Data(contentsOf: url)
        #expect(FiscalHashing.sha256Hex(data) == archive.fileSha256)
        #expect(folderContents(c) == [archive.fileName])
        #expect(try await c.api.verifyArchive(data: data, fileName: archive.fileName).isValid)
        #expect(try c.db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'ARCHIVE_EXPORTED'").count == 1)
    }

    @Test func aTemporaryFileLeftByAFailedMoveIsRenamed() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        let temporary = c.folder.appendingPathComponent("\(FiscalHashing.guidText(archive.id)).tmp")
        try FileManager.default.moveItem(at: c.folder.appendingPathComponent(archive.fileName), to: temporary)
        let url = try await c.api.archiveFile(id: archive.id)
        #expect(folderContents(c) == [archive.fileName]) // renommé, pas recopié à côté
        #expect(FiscalHashing.sha256Hex(try Data(contentsOf: url)) == archive.fileSha256)
    }

    @Test func aTruncatedTemporaryFileIsIgnoredAndTheZipRebuilt() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        let temporary = c.folder.appendingPathComponent("\(FiscalHashing.guidText(archive.id)).tmp")
        try FileManager.default.removeItem(at: c.folder.appendingPathComponent(archive.fileName))
        try Data("PK tronqué".utf8).write(to: temporary)
        let url = try await c.api.archiveFile(id: archive.id)
        #expect(FiscalHashing.sha256Hex(try Data(contentsOf: url)) == archive.fileSha256)
        #expect(Set(folderContents(c)) == [archive.fileName, temporary.lastPathComponent])
    }

    @Test func aFileThatCannotBeRebuiltIdenticallyIsRefusedAndNothingIsWritten() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        try FileManager.default.removeItem(at: c.folder.appendingPathComponent(archive.fileName))
        try c.db.exec("DROP TRIGGER TR_FiscalReceipts_NoUpdate")
        try c.db.run("UPDATE FiscalReceipts SET ReceiptNumber = 'ALTERE'")
        await #expect(throws: APIError.notFound(L10n.string("fiscal.archive_error_unrecoverable"))) {
            _ = try await c.api.archiveFile(id: archive.id)
        }
        #expect(folderContents(c).isEmpty)
        #expect(try c.db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'ARCHIVE_EXPORTED'").isEmpty)
    }

    @Test func aFailingExportJournalDoesNotPreventServingTheFile() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        try c.db.exec("CREATE TRIGGER failExport BEFORE INSERT ON JournalEntries WHEN NEW.EventType = 'ARCHIVE_EXPORTED' BEGIN SELECT RAISE(ABORT, 'boom'); END")
        let url = try await c.api.archiveFile(id: archive.id)
        #expect(url.lastPathComponent == archive.fileName)
        #expect(try c.db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'ARCHIVE_EXPORTED'").isEmpty)
    }

    @Test func aRefusedCreationLeavesNoRowJournalOrFile() async throws {
        let c = try await makeContext()
        let closure = try await closeSeptember(c)
        try c.db.exec("CREATE TRIGGER failCreate BEFORE INSERT ON JournalEntries WHEN NEW.EventType = 'ARCHIVE_CREATED' BEGIN SELECT RAISE(ABORT, 'boom'); END")
        await #expect(throws: (any Error).self) { _ = try await c.api.createArchive(periodClosureId: closure.id) }
        #expect(try c.db.query("SELECT 1 AS x FROM FiscalArchives").isEmpty)
        #expect(folderContents(c).isEmpty)
    }

    @Test func aWaiterCannotUseArchives() async throws {
        let c = try await makeContext(pin: "2468")
        await #expect(throws: APIError.self) { _ = try await c.api.archives() }
        await #expect(throws: APIError.self) { _ = try await c.api.createArchive(periodClosureId: UUID()) }
        await #expect(throws: APIError.self) { _ = try await c.api.verifyArchive(data: Data("x".utf8), fileName: "a.zip") }
        await #expect(throws: APIError.self) { _ = try await c.api.archiveFile(id: UUID()) }
    }

    @Test func theDefaultArchivesFolderSitsNextToTheDatabase() {
        #expect(LocalPosAPI.defaultArchivesDirectory(databasePath: "/x/RestaurantPOS/pos-local.sqlite").path == "/x/RestaurantPOS/pos-local-archives")
        let a = LocalPosAPI.defaultArchivesDirectory(databasePath: ":memory:")
        let b = LocalPosAPI.defaultArchivesDirectory(databasePath: ":memory:")
        #expect(a != b)
    }

    @Test func anUnreadableArchiveRowGivesAReadableErrorEverywhere() async throws {
        let c = try await makeContext()
        let archive = try await c.api.createArchive(periodClosureId: try await closeSeptember(c).id)
        let zip = try Data(contentsOf: c.folder.appendingPathComponent(archive.fileName))
        try c.db.exec("DROP TRIGGER TR_FiscalArchives_NoUpdate")
        try c.db.run("UPDATE FiscalArchives SET Id = 'pas-un-uuid'")
        let unreadable = APIError.server(status: 500, message: L10n.string("fiscal.archive_error_chain_unreadable"))
        await #expect(throws: unreadable) { _ = try await c.api.archives() }
        await #expect(throws: unreadable) { _ = try await c.api.verifyArchive(data: zip, fileName: archive.fileName) }
        await #expect(throws: unreadable) { _ = try await c.api.createArchive(periodClosureId: archive.periodClosureId) }
    }
}
