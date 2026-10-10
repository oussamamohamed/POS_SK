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
        removeStaleTemporaryCopies(now: now)
        let temporary = FileManager.default.temporaryDirectory.appendingPathComponent("pos-\(UUID().uuidString).sqlite")
        defer { try? FileManager.default.removeItem(at: temporary) }
        try db.backup(to: temporary.path)
        #if os(iOS)
        // La copie en clair contient les hachages de PIN et la chaîne fiscale : illisible appareil verrouillé. Un échec abandonne la sauvegarde.
        try FileManager.default.setAttributes([.protectionKey: FileProtectionType.complete], ofItemAtPath: temporary.path)
        #endif
        let sealed = try LocalBackupCrypto.seal(try Data(contentsOf: temporary), passphrase: passphrase, iterations: iterations)
        let destination = folder.appendingPathComponent(fileName(now: now))
        try sealed.write(to: destination, options: writeOptions)
        try prune(folder: folder)
        return destination
    }

    /// Supprime les copies temporaires `pos-*.sqlite` orphelines (processus tué en cours de sauvegarde) de plus d'une heure ; rien d'autre.
    static func removeStaleTemporaryCopies(now: Date, in directory: URL = FileManager.default.temporaryDirectory) {
        let fm = FileManager.default
        guard let names = try? fm.contentsOfDirectory(atPath: directory.path) else { return }
        for name in names where name.hasPrefix("pos-") && name.hasSuffix(".sqlite") {
            let url = directory.appendingPathComponent(name)
            guard let modified = (try? fm.attributesOfItem(atPath: url.path))?[.modificationDate] as? Date, now.timeIntervalSince(modified) > 3600 else { continue }
            try? fm.removeItem(at: url)
        }
    }

    /// Déchiffre vers `destination` (fichier SQLite) puis vérifie les chaînes fiscales. Refuse (`destinationExists`) si `destination`,
    /// `-wal` ou `-shm` existe : un fichier existant n'est jamais touché. Contrat : restaurer vers un chemin temporaire, puis l'échanger
    /// avec la base active. Si la phrase est fausse ou la chaîne rompue, rien n'est laissé à `destination`.
    static func restore(from file: URL, passphrase: String, to destination: URL) throws {
        let fm = FileManager.default
        let paths = ["", "-wal", "-shm"].map { destination.path + $0 }
        guard !paths.contains(where: { fm.fileExists(atPath: $0) }) else { throw LocalBackupFailure.destinationExists }
        let plain = try LocalBackupCrypto.open(try Data(contentsOf: file), passphrase: passphrase)
        try plain.write(to: destination, options: [.withoutOverwriting])
        // Le fichier a été créé par cet appel : on peut le nettoyer en cas d'échec.
        do {
            let issues = try LocalFiscalRepository(db: SQLiteDatabase(path: destination.path)).verifyChains()
            guard issues.isEmpty else { throw LocalBackupFailure.brokenChain(issues) }
        } catch {
            for path in paths { try? fm.removeItem(atPath: path) }
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
