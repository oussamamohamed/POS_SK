import Foundation

/// Formats invariants des tickets, identiques au .NET (`F2`, `0.##`, `dd/MM/yyyy HH:mm`) :
/// pas de `DateFormatter` ni de `Locale`, chiffres latins.
enum TicketFormat {
    /// Montant en centimes : « 12.50 ».
    static func amount(_ cents: Int) -> String {
        let magnitude = cents.magnitude
        return "\(cents < 0 ? "-" : "")\(magnitude / 100).\(pad(Int(magnitude % 100)))"
    }

    /// Taux « 0.## » : arrondi du milieu loin de zéro, zéros finaux retirés.
    static func rate(_ rate: Decimal) -> String {
        var value = rate
        var rounded = Decimal()
        NSDecimalRound(&rounded, &value, 2, .plain)
        // `Decimal.description` ne dépend pas de la locale.
        var text = rounded.description
        if text.contains(".") {
            while text.hasSuffix("0") { text.removeLast() }
            if text.hasSuffix(".") { text.removeLast() }
        }
        return text
    }

    /// `dd/MM/yyyy HH:mm:ss` en UTC.
    static func utcDateTime(_ date: Date) -> String {
        let c = components(date, in: .gmt)
        return "\(day(c)) \(pad(c.hour)):\(pad(c.minute)):\(pad(c.second))"
    }

    /// `dd/MM/yyyy HH:mm` dans le fuseau donné.
    static func localDateTime(_ date: Date, in zone: TimeZone) -> String {
        let c = components(date, in: zone)
        return "\(day(c)) \(pad(c.hour)):\(pad(c.minute))"
    }

    /// `HH:mm` dans le fuseau donné.
    static func localTime(_ date: Date, in zone: TimeZone) -> String {
        let c = components(date, in: zone)
        return "\(pad(c.hour)):\(pad(c.minute))"
    }

    private static func components(_ date: Date, in zone: TimeZone) -> DateComponents {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = zone
        return calendar.dateComponents([.year, .month, .day, .hour, .minute, .second], from: date)
    }

    private static func day(_ c: DateComponents) -> String {
        let year = c.year ?? 0
        return "\(pad(c.day))/\(pad(c.month))/\(String(repeating: "0", count: max(0, 4 - String(year).count)))\(year)"
    }

    private static func pad(_ value: Int?) -> String { pad(value ?? 0) }
    private static func pad(_ value: Int) -> String { value < 10 ? "0\(value)" : "\(value)" }
}
