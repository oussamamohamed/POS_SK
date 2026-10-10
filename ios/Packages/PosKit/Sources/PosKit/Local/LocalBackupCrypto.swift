import CommonCrypto
import CryptoKit
import Foundation

enum LocalBackupFailure: Error, Equatable, LocalizedError {
    /// Le fichier n'est pas une sauvegarde de l'application (en-tête absent ou illisible).
    case notABackup
    /// Phrase fausse ou fichier altéré : AES-GCM ne permet pas de les distinguer.
    case wrongPassphraseOrCorrupted
    /// La base restaurée a une chaîne fiscale rompue.
    case brokenChain([String])

    var errorDescription: String? {
        switch self {
        case .notABackup: "Ce fichier n'est pas une sauvegarde de l'application."
        case .wrongPassphraseOrCorrupted: "Phrase de sauvegarde incorrecte ou fichier altéré."
        case .brokenChain(let issues): "Chaîne fiscale rompue dans la sauvegarde : \(issues.prefix(3).joined(separator: " ; "))"
        }
    }
}

/// Fichier de sauvegarde : `magic(8) | itérations PBKDF2 (UInt32 BE) | sel(16) | AES-GCM combiné (nonce 12 | texte chiffré | tag 16)`.
/// L'en-tête est authentifié (AAD) ; la clé dérive de la phrase par PBKDF2-HMAC-SHA256.
enum LocalBackupCrypto {
    static let defaultIterations = 200_000
    private static let magic = Data("POSBAK1\n".utf8)
    private static let saltLength = 16
    private static let headerLength = 8 + 4 + 16
    private static let maxIterations = 10_000_000

    static func seal(_ plain: Data, passphrase: String, iterations: Int) throws -> Data {
        var salt = Data(count: saltLength)
        let status = salt.withUnsafeMutableBytes { SecRandomCopyBytes(kSecRandomDefault, saltLength, $0.baseAddress!) }
        guard status == errSecSuccess else { throw LocalBackupFailure.wrongPassphraseOrCorrupted }
        var header = magic
        withUnsafeBytes(of: UInt32(iterations).bigEndian) { header.append(contentsOf: $0) }
        header.append(salt)
        let key = try deriveKey(passphrase: passphrase, salt: salt, iterations: iterations)
        let box = try AES.GCM.seal(plain, using: key, authenticating: header)
        guard let combined = box.combined else { throw LocalBackupFailure.wrongPassphraseOrCorrupted }
        return header + combined
    }

    static func open(_ file: Data, passphrase: String) throws -> Data {
        let bytes = Data(file) // indices à partir de 0
        guard bytes.count >= headerLength, bytes.prefix(magic.count) == magic else { throw LocalBackupFailure.notABackup }
        let iterations = Int(bytes[8..<12].reduce(UInt32(0)) { ($0 << 8) | UInt32($1) })
        guard iterations > 0, iterations <= maxIterations else { throw LocalBackupFailure.notABackup }
        let header = Data(bytes.prefix(headerLength))
        let salt = Data(bytes[12..<headerLength])
        do {
            let key = try deriveKey(passphrase: passphrase, salt: salt, iterations: iterations)
            let box = try AES.GCM.SealedBox(combined: Data(bytes.suffix(from: headerLength)))
            return try AES.GCM.open(box, using: key, authenticating: header)
        } catch {
            throw LocalBackupFailure.wrongPassphraseOrCorrupted
        }
    }

    private static func deriveKey(passphrase: String, salt: Data, iterations: Int) throws -> SymmetricKey {
        var derived = [UInt8](repeating: 0, count: 32)
        let status = salt.withUnsafeBytes { saltBytes in
            CCKeyDerivationPBKDF(
                CCPBKDFAlgorithm(kCCPBKDF2), passphrase, passphrase.utf8.count,
                saltBytes.bindMemory(to: UInt8.self).baseAddress, salt.count,
                CCPseudoRandomAlgorithm(kCCPRFHmacAlgSHA256), UInt32(iterations), &derived, derived.count
            )
        }
        guard status == kCCSuccess else { throw LocalBackupFailure.wrongPassphraseOrCorrupted }
        return SymmetricKey(data: derived)
    }
}
