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

    /// `ComputePeriodClosureHash` : hexadécimal minuscule, `periodType` entier (`Monthly = 1`, `Annual = 2`), fin de période au format « O ».
    static func periodClosureHash(
        previousHash: String, terminalId: String, periodType: Int, periodKey: String, totalTtcCents: Int, totalHtCents: Int,
        taxesJson: String, tendersJson: String, perpetualGrandTotalCents: Int, periodEnd: Date
    ) -> String {
        sha256(
            "\(previousHash)|\(terminalId)|\(periodType)|\(periodKey)|\(totalTtcCents)|\(totalHtCents)|\(taxesJson)|\(tendersJson)|\(perpetualGrandTotalCents)|\(timestampText(periodEnd))",
            lowercase: true
        )
    }

    /// `ComputeArchiveHash` : hexadécimal minuscule, `periodType` entier, date de création au format « O ». Chaîne globale (toutes archives).
    static func archiveHash(previousHash: String, periodType: Int, periodKey: String, fileSha256: String, createdAt: Date) -> String {
        sha256("\(previousHash)|\(periodType)|\(periodKey)|\(fileSha256)|\(timestampText(createdAt))", lowercase: true)
    }

    /// SHA-256 d'octets bruts en hexadécimal minuscule (`Convert.ToHexStringLower(SHA256.HashData(…))`).
    static func sha256Hex(_ data: Data) -> String {
        SHA256.hash(data: data).map { String(format: "%02x", $0) }.joined()
    }

    /// `Guid.ToString()` : minuscules.
    static func guidText(_ id: UUID) -> String { id.uuidString.lowercased() }

    /// Texte à écrire en base pour une date fiscale (`CreatedAtUtc`, `OccurredAtUtc`) : milliseconde entière, jamais via `SQLDate.format`
    /// qui tronque et ferait perdre 1 ms à la relecture. Le texte signé (`timestampText`) fait foi.
    static func storedText(_ date: Date) -> String {
        String(timestampText(date).prefix(23)) + "Z" // 2026-10-10T09:00:00.123Z
    }

    /// Date signée : arrondie à la milliseconde, sous la forme exacte que `SQLDate.parse` rend pour `storedText` (idempotent).
    static func roundedToMilliseconds(_ date: Date) -> Date {
        SQLDate.parse(storedText(date)) ?? date
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
    /// Lit un objet `{"clé":entier,…}` en conservant l'ordre des clés (le .NET fusionne dans l'ordre d'insertion). `nil` si le texte n'est pas de cette forme.
    /// ponytail: clés sans guillemet, virgule ni deux-points échappés (taux de TVA et noms de moyens de paiement) ; passer à un vrai parseur si les clés deviennent libres.
    static func parseIntObject(_ json: String) -> [(String, Int)]? {
        let text = json.trimmingCharacters(in: .whitespacesAndNewlines)
        guard text.hasPrefix("{"), text.hasSuffix("}") else { return nil }
        let inner = text.dropFirst().dropLast().trimmingCharacters(in: .whitespacesAndNewlines)
        if inner.isEmpty { return [] }
        var entries: [(String, Int)] = []
        for part in inner.split(separator: ",") {
            guard let colon = part.lastIndex(of: ":") else { return nil }
            let key = part[..<colon].trimmingCharacters(in: .whitespaces)
            let value = part[part.index(after: colon)...].trimmingCharacters(in: .whitespaces)
            guard key.count >= 2, key.hasPrefix("\""), key.hasSuffix("\""), let number = Int(value) else { return nil }
            entries.append((String(key.dropFirst().dropLast()), number))
        }
        return entries
    }

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
