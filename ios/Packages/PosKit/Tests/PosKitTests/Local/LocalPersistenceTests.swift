import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : persistance")
struct LocalPersistenceTests {
    @Test func dataSurvivesReopeningTheFile() async throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        do {
            let first = try LocalPosAPI(path: path)
            _ = try await first.login(pin: "1234")
            try await first.createTable(number: "T42", capacity: 5)
            try await first.createStaff(name: "Persistant", role: .cashier, pin: "7777")
        }
        let reopened = try LocalPosAPI(path: path)
        #expect(try await reopened.login(pin: "7777").success)
        #expect(try await reopened.tables().count == 9)
        #expect(try await reopened.staff().count == 5)
        #expect(try await reopened.categories().count == 5)
    }

    @Test func garbageFileIsRejectedAndLeftUntouched() throws {
        let path = temporaryDatabasePath()
        defer { for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: path + suffix) } }
        let garbage = Data("ceci n'est pas une base SQLite, juste du texte assez long pour ressembler à un en-tête invalide".utf8)
        try garbage.write(to: URL(fileURLWithPath: path))
        #expect(throws: SQLiteError.self) { try LocalPosAPI(path: path) }
        #expect(try Data(contentsOf: URL(fileURLWithPath: path)) == garbage)
    }

    @Test func foreignKeysAreEnforced() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        #expect(throws: SQLiteError.self) {
            try db.run("INSERT INTO ModifierOptions (Id, GroupId, Name, ExtraPrice, IsDefault, DisplayOrder) VALUES ('a', 'missing', 'x', 0, 0, 0)")
        }
    }
}
