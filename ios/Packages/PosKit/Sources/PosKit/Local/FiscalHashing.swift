import CryptoKit
import Foundation

/// Miroir de `FiscalHashing.cs` : SHA-256 d'une chaîne UTF-8 séparée par `|`. Reçus en hexadécimal majuscule, journal (JET) en minuscule.
enum FiscalHashing {
    static let genesisHash = "GENESIS_" + String(repeating: "0", count: 64)

    static func sha256(_ raw: String, lowercase: Bool) -> String {
        SHA256.hash(data: Data(raw.utf8)).map { String(format: lowercase ? "%02x" : "%02X", $0) }.joined()
    }

    static func receiptHash(previousHash: String, terminalId: String, sequence: Int, amountCents: Int, timestamp: String, taxBreakdownJson: String) -> String {
        sha256("\(previousHash)|\(terminalId)|\(sequence)|\(amountCents)|\(timestamp)|\(taxBreakdownJson)", lowercase: false)
    }

    static func jetHash(previousHash: String, chainSequence: Int, eventType: String, timestamp: String, terminalId: String?, operatorId: UUID?, payloadJson: String) -> String {
        let operatorText = operatorId.map(guidText) ?? ""
        return sha256("\(previousHash)|\(chainSequence)|\(eventType)|\(timestamp)|\(terminalId ?? "")|\(operatorText)|\(payloadJson)", lowercase: true)
    }

    /// `Guid.ToString()` : minuscules.
    static func guidText(_ id: UUID) -> String { id.uuidString.lowercased() }

    /// Date signée : arrondie à la milliseconde, sous la forme exacte que `SQLDate.parse` rend pour ce texte (idempotent).
    /// Attention : `SQLDate.format` tronque au lieu d'arrondir ; la valeur relue peut donc perdre 1 ms. Le texte signé fait foi.
    static func roundedToMilliseconds(_ date: Date) -> Date {
        let text = timestampText(date) // 2026-10-10T09:00:00.1230000+00:00
        return SQLDate.parse(String(text.prefix(23)) + "Z") ?? date
    }

    /// `DateTimeOffset.ToString("O")` d'une date UTC : `2026-10-10T09:00:00.1230000+00:00`.
    static func timestampText(_ date: Date) -> String {
        let milliseconds = Int64((date.timeIntervalSince1970 * 1000).rounded())
        let seconds = Int64((Double(milliseconds) / 1000).rounded(.down))
        let fraction = Int(milliseconds - seconds * 1000)
        let base = Date(timeIntervalSince1970: Double(seconds)).formatted(Date.ISO8601FormatStyle()) // 2026-10-10T09:00:00Z
        return String(base.dropLast()) + String(format: ".%03d0000+00:00", fraction)
    }
}

/// JSON écrit comme `System.Text.Json` par défaut : le texte signé doit être identique octet pour octet à celui du .NET.
enum FiscalJSON {
    /// Chaîne : ASCII imprimable seul, le reste (et `" < > & ' + \``) en `\uXXXX`.
    static func string(_ value: String) -> String {
        var out = "\""
        for unit in value.utf16 {
            switch unit {
            case 0x22: out += "\\u0022"
            case 0x5C: out += "\\\\"
            case 0x08: out += "\\b"
            case 0x09: out += "\\t"
            case 0x0A: out += "\\n"
            case 0x0C: out += "\\f"
            case 0x0D: out += "\\r"
            case 0x26, 0x27, 0x2B, 0x3C, 0x3E, 0x60: out += String(format: "\\u%04X", UInt32(unit))
            case 0x20...0x7E: out.unicodeScalars.append(UnicodeScalar(UInt8(unit)))
            default: out += String(format: "\\u%04X", UInt32(unit))
            }
        }
        return out + "\""
    }

    static func guid(_ id: UUID) -> String { "\"" + FiscalHashing.guidText(id) + "\"" }

    /// `DateTimeOffset` UTC : décimales sans zéros de fin, `+00:00` (ex. `"2026-10-10T09:00:00.12+00:00"`).
    static func dateTime(_ date: Date) -> String {
        let text = FiscalHashing.timestampText(date)
        guard let dot = text.firstIndex(of: ".") else { return "\"\(text)\"" }
        let head = text[..<dot]
        let digits = text[text.index(after: dot)...].prefix(7)
        let trimmed = String(digits.reversed().drop { $0 == "0" }.reversed())
        return "\"" + head + (trimmed.isEmpty ? "" : "." + trimmed) + "+00:00\""
    }

    /// Objet compact, dans l'ordre donné ; chaque valeur est déjà du JSON.
    static func object(_ fields: [(String, String)]) -> String {
        "{" + fields.map { "\"\($0.0)\":\($0.1)" }.joined(separator: ",") + "}"
    }
}
