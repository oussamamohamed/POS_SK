import Foundation
import Testing
@testable import PosKit

@Suite("SQLiteDatabase")
struct SQLiteDatabaseTests {
    private func makeDB() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try db.exec("CREATE TABLE T (Id TEXT PRIMARY KEY, N INTEGER, R REAL, S TEXT, B BLOB)")
        return db
    }

    @Test func roundTripsEveryValueType() throws {
        let db = try makeDB()
        let id = UUID()
        let date = Date(timeIntervalSince1970: 1_790_000_000.123)
        try db.run("INSERT INTO T (Id, N, R, S, B) VALUES (?, ?, ?, ?, ?)",
                   [.uuid(id), .integer(42), .real(1.5), .string("à l'emporter"), .blob(Data([1, 2, 3]))])
        let row = try #require(try db.query("SELECT * FROM T").first)
        #expect(row.uuid("Id") == id)
        #expect(row.int("N") == 42)
        #expect(row.double("R") == 1.5)
        #expect(row.string("S") == "à l'emporter")
        #expect(row["B"] == .blob(Data([1, 2, 3])))
        #expect(SQLDate.parse(SQLDate.format(date)).map { abs($0.timeIntervalSince(date)) < 0.002 } == true)
    }

    @Test func nullsAndDecimals() throws {
        let db = try makeDB()
        try db.run("INSERT INTO T (Id, S) VALUES (?, ?)", [.text("a"), .decimal(5.5)])
        try db.run("INSERT INTO T (Id, S) VALUES (?, ?)", [.text("b"), .string(nil)])
        let rows = try db.query("SELECT * FROM T ORDER BY Id")
        #expect(rows[0].decimal("S") == 5.5)
        #expect(rows[1].string("S") == nil)
        #expect(rows[1].int("N") == nil)
    }

    @Test func rollsBackFailedTransaction() throws {
        let db = try makeDB()
        struct Boom: Error {}
        #expect(throws: Boom.self) {
            try db.transaction {
                try db.run("INSERT INTO T (Id) VALUES ('x')")
                throw Boom()
            }
        }
        #expect(try db.query("SELECT * FROM T").isEmpty)
    }

    @Test func commitsSuccessfulTransaction() throws {
        let db = try makeDB()
        try db.transaction { try db.run("INSERT INTO T (Id) VALUES ('x')") }
        #expect(try db.query("SELECT * FROM T").count == 1)
    }

    @Test func surfacesSQLErrors() throws {
        let db = try makeDB()
        #expect(throws: SQLiteError.self) { try db.run("INSERT INTO Missing VALUES (1)") }
        try db.run("INSERT INTO T (Id) VALUES ('dup')")
        #expect(throws: SQLiteError.self) { try db.run("INSERT INTO T (Id) VALUES ('dup')") }
    }

    @Test func tracksUserVersion() throws {
        let db = try makeDB()
        #expect(try db.userVersion() == 0)
        try db.exec("PRAGMA user_version = 3")
        #expect(try db.userVersion() == 3)
    }
}
