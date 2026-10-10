import Foundation
import Testing
@testable import PosKit

@Suite("Sauvegarde chiffrée")
struct LocalBackupTests {
    private static let fast = 1_000 // itérations PBKDF2 réduites pour les tests
    private let at = Date(timeIntervalSince1970: 1_760_087_880)

    private func makeFolder() throws -> URL {
        let url = FileManager.default.temporaryDirectory.appendingPathComponent("backup-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: url, withIntermediateDirectories: true)
        return url
    }

    private func makeDB() throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try db.run("INSERT INTO LocalCounters (Name, Day, LastSequence) VALUES ('marker', '', 42)")
        return db
    }

    @Test func sealAndOpenRoundTrip() throws {
        let plain = Data("base de données".utf8)
        let sealed = try LocalBackupCrypto.seal(plain, passphrase: "phrase secrète 1", iterations: Self.fast)
        #expect(sealed != plain && sealed.range(of: plain) == nil)
        #expect(try LocalBackupCrypto.open(sealed, passphrase: "phrase secrète 1") == plain)
    }

    @Test func aWrongPassphraseIsRejected() throws {
        let sealed = try LocalBackupCrypto.seal(Data("x".utf8), passphrase: "bonne phrase", iterations: Self.fast)
        #expect(throws: LocalBackupFailure.wrongPassphraseOrCorrupted) { try LocalBackupCrypto.open(sealed, passphrase: "mauvaise phrase") }
    }

    @Test func aModifiedOrTruncatedFileIsRejected() throws {
        let sealed = try LocalBackupCrypto.seal(Data("contenu".utf8), passphrase: "phrase", iterations: Self.fast)
        var flipped = sealed
        flipped[flipped.count - 1] ^= 0x01
        #expect(throws: LocalBackupFailure.wrongPassphraseOrCorrupted) { try LocalBackupCrypto.open(flipped, passphrase: "phrase") }
        var header = sealed
        header[10] ^= 0x01 // l'en-tête est authentifié
        #expect(throws: LocalBackupFailure.self) { try LocalBackupCrypto.open(header, passphrase: "phrase") }
        #expect(throws: LocalBackupFailure.wrongPassphraseOrCorrupted) { try LocalBackupCrypto.open(sealed.prefix(sealed.count - 5), passphrase: "phrase") }
        #expect(throws: LocalBackupFailure.notABackup) { try LocalBackupCrypto.open(sealed.prefix(10), passphrase: "phrase") }
    }

    @Test func aFileThatIsNotABackupIsRejected() throws {
        #expect(throws: LocalBackupFailure.notABackup) { try LocalBackupCrypto.open(Data("SQLite format 3".utf8) + Data(count: 100), passphrase: "x") }
    }

    @Test func writeThenRestoreGivesBackTheSameDatabase() throws {
        let folder = try makeFolder()
        let file = try LocalBackupService.write(db: try makeDB(), to: folder, passphrase: "phrase de test", now: at, iterations: Self.fast)
        #expect(file.lastPathComponent == LocalBackupService.fileName(now: at) && file.pathExtension == "posbak")
        #expect(try Data(contentsOf: file).range(of: Data("SQLite format 3".utf8)) == nil)

        let destination = folder.appendingPathComponent("restored.sqlite")
        try LocalBackupService.restore(from: file, passphrase: "phrase de test", to: destination)
        let restored = try SQLiteDatabase(path: destination.path)
        #expect(try restored.query("SELECT LastSequence FROM LocalCounters WHERE Name = 'marker'").first?.int("LastSequence") == 42)
        #expect(try restored.userVersion() == LocalMigrator.steps.count)
    }

    @Test func restoringWithAWrongPassphraseLeavesNoFile() throws {
        let folder = try makeFolder()
        let file = try LocalBackupService.write(db: try makeDB(), to: folder, passphrase: "phrase de test", now: at, iterations: Self.fast)
        let destination = folder.appendingPathComponent("restored.sqlite")
        #expect(throws: LocalBackupFailure.wrongPassphraseOrCorrupted) { try LocalBackupService.restore(from: file, passphrase: "autre", to: destination) }
        #expect(!FileManager.default.fileExists(atPath: destination.path))
    }

    @Test func restoringABrokenChainIsRefusedAndLeavesNoFile() throws {
        let db = try makeDB()
        let repo = LocalFiscalRepository(db: db)
        try repo.appendJournal(eventType: "X", payloadJson: "{}", terminalId: "T01", operatorId: nil, now: at)
        try db.exec("DROP TRIGGER TR_JournalEntries_NoUpdate")
        try db.run("UPDATE JournalEntries SET PayloadJson = '{\"altéré\":1}'")
        let folder = try makeFolder()
        let file = try LocalBackupService.write(db: db, to: folder, passphrase: "phrase de test", now: at, iterations: Self.fast)
        let destination = folder.appendingPathComponent("restored.sqlite")
        do {
            try LocalBackupService.restore(from: file, passphrase: "phrase de test", to: destination)
            Issue.record("la restauration d'une chaîne rompue aurait dû échouer")
        } catch let LocalBackupFailure.brokenChain(issues) {
            #expect(issues.contains { $0.contains("signature_mismatch") })
        }
        #expect(!FileManager.default.fileExists(atPath: destination.path))
    }

    @Test func rotationKeepsTheFourteenNewestBackups() throws {
        let folder = try makeFolder()
        let db = try makeDB()
        for day in 0..<16 {
            _ = try LocalBackupService.write(db: db, to: folder, passphrase: "phrase", now: at.addingTimeInterval(Double(day) * 86_400), iterations: Self.fast)
        }
        let names = try FileManager.default.contentsOfDirectory(atPath: folder.path).filter { $0.hasSuffix(".posbak") }.sorted()
        #expect(names.count == LocalBackupService.keepCount)
        #expect(names.last == LocalBackupService.fileName(now: at.addingTimeInterval(15 * 86_400)))
        #expect(!names.contains(LocalBackupService.fileName(now: at)))
    }

    @Test func rotationNeverTouchesOtherFiles() throws {
        let folder = try makeFolder()
        try Data("photo".utf8).write(to: folder.appendingPathComponent("vacances.jpg"))
        let db = try makeDB()
        for day in 0..<16 {
            _ = try LocalBackupService.write(db: db, to: folder, passphrase: "phrase", now: at.addingTimeInterval(Double(day) * 86_400), iterations: Self.fast)
        }
        #expect(FileManager.default.fileExists(atPath: folder.appendingPathComponent("vacances.jpg").path))
    }

    @Test func anUnavailableFolderFailsWithoutLeavingAnyFile() throws {
        let missing = FileManager.default.temporaryDirectory.appendingPathComponent("absent-\(UUID().uuidString)", isDirectory: true)
        let db = try makeDB()
        #expect(throws: (any Error).self) { try LocalBackupService.write(db: db, to: missing, passphrase: "phrase", now: at, iterations: Self.fast) }
        #expect(!FileManager.default.fileExists(atPath: missing.path))
    }
}
