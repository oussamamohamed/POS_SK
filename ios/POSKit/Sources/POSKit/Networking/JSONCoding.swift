import Foundation

/// Décodage/encodage JSON aligné sur `System.Text.Json` (camelCase, dates ISO 8601 .NET).
public enum POSJSON {
    public static func decoder() -> JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let container = try decoder.singleValueContainer()
            let text = try container.decode(String.self)
            guard let date = DotNetDate.parse(text) else {
                throw DecodingError.dataCorruptedError(in: container, debugDescription: "Date invalide : \(text)")
            }
            return date
        }
        return decoder
    }

    public static func encoder() -> JSONEncoder {
        let encoder = JSONEncoder()
        encoder.dateEncodingStrategy = .custom { date, encoder in
            var container = encoder.singleValueContainer()
            try container.encode(DotNetDate.format(date))
        }
        encoder.outputFormatting = [.sortedKeys]
        return encoder
    }
}

/// Parseur des dates émises par .NET : `2026-09-18T21:27:51.1506234+00:00`,
/// `2026-09-18T21:27:51Z`, ou sans fuseau (`2026-09-18T21:27:51`, interprété en UTC).
public enum DotNetDate {
    public static func parse(_ raw: String) -> Date? {
        let text = raw.trimmingCharacters(in: .whitespaces)
        guard text.count >= 19 else { return nil }

        let base = String(text.prefix(19)) // yyyy-MM-ddTHH:mm:ss
        var rest = Substring(text.dropFirst(19))

        var fraction: Double = 0
        if rest.first == "." {
            rest = rest.dropFirst()
            let digits = rest.prefix { $0.isNumber }
            rest = rest.dropFirst(digits.count)
            fraction = Double("0." + digits) ?? 0
        }

        var offsetSeconds = 0
        if rest.first == "Z" || rest.first == "z" {
            rest = rest.dropFirst()
        } else if let sign = rest.first, sign == "+" || sign == "-" {
            // Fuseau strict : `+HH:MM` ou `+HHMM`.
            let offset = String(rest.dropFirst())
            let digits = offset.replacingOccurrences(of: ":", with: "")
            let validShape = (offset.count == 5 && offset.dropFirst(2).first == ":") || offset.count == 4
            guard validShape, digits.count == 4, digits.allSatisfy(\.isNumber),
                  let hours = Int(digits.prefix(2)), let minutes = Int(digits.suffix(2)) else { return nil }
            offsetSeconds = (hours * 3600 + minutes * 60) * (sign == "-" ? -1 : 1)
            rest = ""
        }
        guard rest.isEmpty else { return nil }

        let fields = base.split(whereSeparator: { $0 == "-" || $0 == "T" || $0 == ":" }).compactMap { Int($0) }
        guard fields.count == 6 else { return nil }

        var components = DateComponents()
        components.year = fields[0]
        components.month = fields[1]
        components.day = fields[2]
        components.hour = fields[3]
        components.minute = fields[4]
        components.second = fields[5]
        components.timeZone = TimeZone(secondsFromGMT: 0)

        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(secondsFromGMT: 0)!
        guard let utc = calendar.date(from: components) else { return nil }
        return utc.addingTimeInterval(fraction - Double(offsetSeconds))
    }

    /// Format ISO 8601 UTC avec millisecondes, accepté par `DateTimeOffset`.
    public static func format(_ date: Date) -> String {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = TimeZone(secondsFromGMT: 0)!
        let c = calendar.dateComponents([.year, .month, .day, .hour, .minute, .second, .nanosecond], from: date)
        let millis = (c.nanosecond ?? 0) / 1_000_000
        return String(
            format: "%04ld-%02ld-%02ldT%02ld:%02ld:%02ld.%03ldZ",
            c.year ?? 0, c.month ?? 0, c.day ?? 0, c.hour ?? 0, c.minute ?? 0, c.second ?? 0, millis
        )
    }
}
