import Foundation

public enum PosJSON {
    /// L'API renvoie des dates ISO-8601 avec ou sans fractions de seconde et avec décalage
    /// (« 2026-09-24T20:18:10.894089+00:00 », « 0001-01-01T00:00:00+00:00 »).
    public static func makeDecoder() -> JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let string = try container.decode(String.self)
            if let date = parseDate(string) { return date }
            throw DecodingError.dataCorruptedError(in: container, debugDescription: "Date invalide : \(string)")
        }
        return decoder
    }

    public static func makeEncoder() -> JSONEncoder {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .iso8601
        encoder.outputFormatting = [.sortedKeys]
        return encoder
    }

    public static func parseDate(_ string: String) -> Date? {
        let withFraction = ISO8601DateFormatter()
        withFraction.formatOptions = [.withInternetDateTime, .withFractionalSeconds]
        if let date = withFraction.date(from: string) { return date }
        let plain = ISO8601DateFormatter()
        plain.formatOptions = [.withInternetDateTime]
        if let date = plain.date(from: string) { return date }
        // Fractions > 3 chiffres (précision .NET à 7 chiffres) : tronquer à la milliseconde.
        if let dot = string.firstIndex(of: "."),
           let zone = string[dot...].firstIndex(where: { $0 == "+" || $0 == "-" || $0 == "Z" }) {
            let fraction = string[string.index(after: dot)..<zone].prefix(3)
            let trimmed = String(string[..<dot]) + "." + fraction + String(string[zone...])
            return withFraction.date(from: trimmed)
        }
        return nil
    }
}
