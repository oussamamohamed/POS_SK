import CryptoKit
import Foundation

/// Hachage des PIN compatible avec `OperatorAuthenticationService.HashPin` : SHA-256 de `salt + pin`, en hexadécimal minuscule.
enum PinHasher {
    static func hash(pin: String, salt: String) -> String {
        SHA256.hash(data: Data((salt + pin).utf8)).map { String(format: "%02x", $0) }.joined()
    }

    static func makeSalt() -> String {
        (0..<16).map { _ in String(format: "%02x", UInt8.random(in: .min ... .max)) }.joined()
    }
}
