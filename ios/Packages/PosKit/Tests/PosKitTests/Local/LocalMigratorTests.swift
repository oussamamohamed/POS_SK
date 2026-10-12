import Foundation
import Testing
@testable import PosKit

@Suite("LocalMigrator et PinHasher")
struct LocalMigratorTests {
    @Test func createsSchemaAndRecordsVersion() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        #expect(try db.userVersion() == LocalMigrator.steps.count)
        let tables = try db.query("SELECT name FROM sqlite_master WHERE type = 'table'").compactMap { $0.string("name") }
        for expected in ["Users", "Categories", "Products", "ModifierGroups", "ModifierOptions", "DiningTables", "RestaurantSettings", "GridLayouts", "GridSlots"] {
            #expect(tables.contains(expected))
        }
    }

    @Test func isIdempotent() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try LocalMigrator.migrate(db)
        #expect(try db.userVersion() == LocalMigrator.steps.count)
    }

    @Test func rejectsDatabaseNewerThanTheApp() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try db.exec("PRAGMA user_version = \(LocalMigrator.steps.count + 1)")
        #expect(throws: SQLiteError.self) { try LocalMigrator.migrate(db) }
    }

    @Test func pinHashMatchesDotNetAlgorithm() {
        // PBKDF2-HMAC-SHA256 (100k itérations, 32 octets) — même calcul que OperatorAuthenticationService.HashPin.
        let pbkdf2Expected = "f514b714a2476681856adaa9698f3dda21a7e05d8900f404a26d12538c78b29f"
        let legacySha256Expected = "60dfb59aeea14604cda73d3d6d1d95f4fbf93cb8dad2d5d4b4ae93ae28c4e330"

        #expect(PinHasher.hash(pin: "1234", salt: "00ff") == pbkdf2Expected)
        #expect(PinHasher.verify(pin: "1234", salt: "00ff", storedHash: pbkdf2Expected))
        #expect(PinHasher.verify(pin: "1234", salt: "00ff", storedHash: legacySha256Expected))
        #expect(!PinHasher.verify(pin: "wrong", salt: "00ff", storedHash: pbkdf2Expected))
    }

    @Test func saltsAre32LowercaseHexCharsAndDiffer() {
        let a = PinHasher.makeSalt(), b = PinHasher.makeSalt()
        #expect(a.count == 32 && a == a.lowercased() && a.allSatisfy(\.isHexDigit))
        #expect(a != b)
    }
}
