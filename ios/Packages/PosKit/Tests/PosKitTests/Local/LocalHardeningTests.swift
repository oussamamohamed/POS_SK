import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : durcissement SQLite et sauvegarde")
struct LocalHardeningTests {
    private func cleanup(_ path: String) {
        for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) }
    }

    @Test func busyTimeoutAndFullSynchronousAreSet() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        #expect(try db.query("PRAGMA busy_timeout").first?.int("timeout") == 5000)
        #expect(try db.query("PRAGMA synchronous").first?.int("synchronous") == 2)
    }

    @Test func pragmasAreSetOnAFileDatabase() throws {
        let path = temporaryDatabasePath()
        defer { cleanup(path) }
        let db = try SQLiteDatabase(path: path)
        #expect(try db.query("PRAGMA journal_mode").first?.string("journal_mode") == "wal")
        #expect(try db.query("PRAGMA busy_timeout").first?.int("timeout") == 5000)
        #expect(try db.query("PRAGMA synchronous").first?.int("synchronous") == 2)
    }

    @Test func backupIsAConsistentOpenableCopy() async throws {
        let path = temporaryDatabasePath(), copy = temporaryDatabasePath()
        defer { cleanup(path); cleanup(copy) }
        let api = try LocalPosAPI(path: path)
        _ = try await api.login(pin: "1234")
        try await api.createTable(number: "T77", capacity: 3)
        try await api.backup(to: URL(fileURLWithPath: copy))

        let restored = try LocalPosAPI(path: copy)
        #expect(try await restored.tables().contains { $0.tableNumber == "T77" })
        #expect(try await restored.login(pin: "1234").success)
    }

    @Test func backupRefusesToOverwriteAnExistingFile() async throws {
        let path = temporaryDatabasePath(), existing = temporaryDatabasePath()
        defer { cleanup(path); cleanup(existing) }
        let api = try LocalPosAPI(path: path)
        _ = try await api.login(pin: "1234")
        let content = Data("à ne pas écraser".utf8)
        try content.write(to: URL(fileURLWithPath: existing))
        await #expect(throws: SQLiteError.self) { try await api.backup(to: URL(fileURLWithPath: existing)) }
        #expect(try Data(contentsOf: URL(fileURLWithPath: existing)) == content)
    }

    @Test func backupNeedsAManager() async throws {
        let copy = temporaryDatabasePath()
        defer { cleanup(copy) }
        let waiter = try await makeLocalAPI(pin: "2468")
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await waiter.backup(to: URL(fileURLWithPath: copy)) }
        let anonymous = try LocalPosAPI(path: ":memory:")
        await #expect(throws: APIError.unauthorized) { try await anonymous.backup(to: URL(fileURLWithPath: copy)) }
        #expect(!FileManager.default.fileExists(atPath: copy))
    }

    @Test func defaultLocationCreatesTheApplicationDirectory() throws {
        let base = FileManager.default.temporaryDirectory.appendingPathComponent("loc-\(UUID().uuidString)")
        defer { try? FileManager.default.removeItem(at: base) }
        let url = try LocalDatabaseLocation.defaultURL(base: base)
        #expect(url.lastPathComponent == "pos-local.sqlite")
        #expect(url.deletingLastPathComponent().lastPathComponent == "RestaurantPOS")
        var isDirectory: ObjCBool = false
        #expect(FileManager.default.fileExists(atPath: url.deletingLastPathComponent().path, isDirectory: &isDirectory) && isDirectory.boolValue)
        #expect(try LocalDatabaseLocation.defaultURL(base: base) == url)
    }
}
