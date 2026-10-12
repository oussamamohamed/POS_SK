import CommonCrypto
import CryptoKit
import Foundation

/// Hachage des PIN compatible avec `OperatorAuthenticationService.HashPin` :
/// PBKDF2-HMAC-SHA256 (100 000 itérations, sortie 32 octets), hexadécimal minuscule.
/// Supporte aussi la vérification de l'ancien format SHA-256 (rétrocompatibilité).
enum PinHasher {
    static func hash(pin: String, salt: String) -> String {
        var derived = [UInt8](repeating: 0, count: 32)
        let saltData = Data(salt.utf8)
        let status = saltData.withUnsafeBytes { saltBytes in
            CCKeyDerivationPBKDF(
                CCPBKDFAlgorithm(kCCPBKDF2),
                pin, pin.utf8.count,
                saltBytes.bindMemory(to: UInt8.self).baseAddress, saltData.count,
                CCPseudoRandomAlgorithm(kCCPRFHmacAlgSHA256),
                100_000,
                &derived,
                32
            )
        }
        guard status == kCCSuccess else {
            return legacyHash(pin: pin, salt: salt)
        }
        return derived.map { String(format: "%02x", $0) }.joined()
    }

    static func verify(pin: String, salt: String, storedHash: String) -> Bool {
        if hash(pin: pin, salt: salt).lowercased() == storedHash.lowercased() {
            return true
        }
        return legacyHash(pin: pin, salt: salt).lowercased() == storedHash.lowercased()
    }

    static func legacyHash(pin: String, salt: String) -> String {
        SHA256.hash(data: Data((salt + pin).utf8)).map { String(format: "%02x", $0) }.joined()
    }

    static func makeSalt() -> String {
        (0..<16).map { _ in String(format: "%02x", UInt8.random(in: .min ... .max)) }.joined()
    }
}
