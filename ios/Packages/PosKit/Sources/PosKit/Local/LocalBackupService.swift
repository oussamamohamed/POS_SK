import Foundation

/// Écriture, rotation et restauration des sauvegardes chiffrées de la base locale.
enum LocalBackupService {
    static let keepCount = 14
    private static let prefix = "pos-backup-"
    private static let fileExtension = "posbak"

    /// `pos-backup-20261010T091800Z.posbak` (UTC, triable par nom).
    static func fileName(now: Date) -> String {
        var utc = Calendar(identifier: .gregorian)
        utc.timeZone = .gmt
        let c = utc.dateComponents([.year, .month, .day, .hour, .minute, .second], from: now)
        return String(format: "\(prefix)%04d%02d%02dT%02d%02d%02dZ.\(fileExtension)", c.year ?? 0, c.month ?? 0, c.day ?? 0, c.hour ?? 0, c.minute ?? 0, c.second ?? 0)
    }

    /// Copie cohérente de la base, chiffrée, écrite de façon atomique dans `folder` (le dossier doit exister) ; garde les `keepCount` plus récentes.
    /// Un échec (dossier absent, disque plein) ne laisse aucun fichier ressemblant à une sauvegarde valide.
    static func write(db: SQLiteDatabase, to folder: URL, passphrase: String, now: Date, iterations: Int = LocalBackupCrypto.defaultIterations) throws -> URL {
        var isDirectory: ObjCBool = false
        guard FileManager.default.fileExists(atPath: folder.path, isDirectory: &isDirectory), isDirectory.boolValue else {
            throw CocoaError(.fileNoSuchFile)
        }
        let temporary = FileManager.default.temporaryDirectory.appendingPathComponent("pos-\(UUID().uuidString).sqlite")
        defer { try? FileManager.default.removeItem(at: temporary) }
        try db.backup(to: temporary.path)
        let sealed = try LocalBackupCrypto.seal(try Data(contentsOf: temporary), passphrase: passphrase, iterations: iterations)
        let destination = folder.appendingPathComponent(fileName(now: now))
        try sealed.write(to: destination, options: writeOptions)
        try prune(folder: folder)
        return destination
    }

    /// Déchiffre vers `destination` (fichier SQLite qui ne doit pas exister) puis vérifie les chaînes fiscales. Si la phrase est fausse ou la
    /// chaîne rompue, `destination` n'existe pas à la sortie.
    static func restore(from file: URL, passphrase: String, to destination: URL) throws {
        let plain = try LocalBackupCrypto.open(try Data(contentsOf: file), passphrase: passphrase)
        do {
            try plain.write(to: destination, options: [.withoutOverwriting])
            let issues = try LocalFiscalRepository(db: SQLiteDatabase(path: destination.path)).verifyChains()
            guard issues.isEmpty else { throw LocalBackupFailure.brokenChain(issues) }
        } catch {
            for suffix in ["", "-wal", "-shm"] { try? FileManager.default.removeItem(atPath: destination.path + suffix) }
            throw error
        }
    }

    private static func prune(folder: URL) throws {
        let names = try FileManager.default.contentsOfDirectory(atPath: folder.path)
            .filter { $0.hasPrefix(prefix) && $0.hasSuffix("." + fileExtension) }
            .sorted()
        for name in names.dropLast(keepCount) { try? FileManager.default.removeItem(at: folder.appendingPathComponent(name)) }
    }

    private static var writeOptions: Data.WritingOptions {
        #if os(iOS)
        [.atomic, .completeFileProtection]
        #else
        [.atomic]
        #endif
    }
}
