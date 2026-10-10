import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : sauvegarde chiffrée, état et journal")
struct LocalBackupAPITests {
    private func makeFolder() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("api-backup-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func makeAPI(pin: String = "1234", clock: @escaping @Sendable () -> Date = { Date() }) async throws -> (LocalPosAPI, SQLiteDatabase) {
        let path = temporaryDatabasePath()
        let api = try LocalPosAPI(path: path, clock: clock)
        await api.setBackupIterations(1_000)
        _ = try await api.login(pin: pin)
        return (api, try SQLiteDatabase(path: path))
    }

    @Test func aNewInstallationIsOverdue() async throws {
        let (api, _) = try await makeAPI()
        let status = try await api.backupStatus()
        #expect(status.lastSuccess == nil && status.isOverdue)
    }

    @Test func aSuccessfulBackupIsJournaledAndClearsTheOverdueFlag() async throws {
        let (api, db) = try await makeAPI()
        let folder = try makeFolder()
        let file = try await api.exportBackup(to: folder, passphrase: "phrase de test")
        #expect(FileManager.default.fileExists(atPath: file.path))
        let status = try await api.backupStatus()
        #expect(status.lastSuccess != nil && !status.isOverdue)
        let entry = try #require(try db.query("SELECT * FROM JournalEntries WHERE EventType = 'BACKUP_COMPLETED'").first)
        #expect(entry.string("PayloadJson")?.contains(file.lastPathComponent) == true)
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
    }

    @Test func aBackupBecomesOverdueAfterTwentyFourHours() async throws {
        let now = LockedDate(Date(timeIntervalSince1970: 1_760_087_880))
        let (api, _) = try await makeAPI(clock: { now.value })
        _ = try await api.exportBackup(to: try makeFolder(), passphrase: "phrase de test")
        now.value = now.value.addingTimeInterval(23 * 3600)
        #expect(try await api.backupStatus().isOverdue == false)
        now.value = now.value.addingTimeInterval(2 * 3600)
        #expect(try await api.backupStatus().isOverdue)
    }

    @Test func aFailedBackupIsJournaledAndLeavesTheStatusUntouched() async throws {
        let (api, db) = try await makeAPI()
        let missing = FileManager.default.temporaryDirectory.appendingPathComponent("absent-\(UUID().uuidString)", isDirectory: true)
        await #expect(throws: (any Error).self) { try await api.exportBackup(to: missing, passphrase: "phrase de test") }
        #expect(try await api.backupStatus().lastSuccess == nil)
        let entry = try #require(try db.query("SELECT * FROM JournalEntries WHERE EventType = 'BACKUP_FAILED'").first)
        #expect(entry.string("PayloadJson")?.contains("phrase de test") == false)
    }

    @Test func exportRequiresAManagerButTheScheduledRunDoesNot() async throws {
        let (api, _) = try await makeAPI(pin: "2468") // serveur
        let folder = try makeFolder()
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await api.exportBackup(to: folder, passphrase: "phrase de test") }
        _ = try await api.runScheduledBackup(to: folder, passphrase: "phrase de test")
        #expect(try await api.backupStatus().lastSuccess != nil)
    }

    @Test func aTooShortPassphraseIsRefused() async throws {
        let (api, _) = try await makeAPI()
        let folder = try makeFolder()
        await #expect(throws: APIError.self) { try await api.exportBackup(to: folder, passphrase: "court") }
    }

    @Test func theInMemoryPassphraseStoreRoundTrips() async {
        await MainActor.run {
            let store = InMemoryBackupPassphraseStore()
            #expect(store.load() == nil)
            #expect(store.save("phrase de test") && store.load() == "phrase de test")
            store.clear()
            #expect(store.load() == nil)
        }
    }
}

/// Horloge mutable partagée entre le test et l'acteur.
final class LockedDate: @unchecked Sendable {
    private let lock = NSLock()
    private var current: Date
    init(_ date: Date) { current = date }
    var value: Date {
        get { lock.withLock { current } }
        set { lock.withLock { current = newValue } }
    }
}
