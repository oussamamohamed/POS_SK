import Foundation

/// Heures d'un planning : saisie `HH:mm` ou `HH:mm:ss`, stockage `HH:mm:ss` (comme `TimeOnly` d'EF Core), affichage `HH:mm`.
enum LocalTimeOfDay {
    /// Secondes depuis minuit ; `nil` si le texte n'est pas une heure valide (0 à 23 h, 0 à 59 min, 0 à 59 s).
    static func seconds(from text: String) -> Int? {
        let parts = text.split(separator: ":", omittingEmptySubsequences: false)
        guard parts.count == 2 || parts.count == 3 else { return nil }
        let numbers = parts.compactMap { part -> Int? in
            guard (1...2).contains(part.count), part.allSatisfy(\.isASCII), part.allSatisfy(\.isNumber) else { return nil }
            return Int(part)
        }
        guard numbers.count == parts.count, numbers[0] < 24, numbers[1] < 60 else { return nil }
        let seconds = numbers.count == 3 ? numbers[2] : 0
        guard seconds < 60 else { return nil }
        return numbers[0] * 3600 + numbers[1] * 60 + seconds
    }

    static func stored(_ seconds: Int) -> String {
        String(format: "%02d:%02d:%02d", seconds / 3600, seconds % 3600 / 60, seconds % 60)
    }

    static func display(_ seconds: Int) -> String {
        String(format: "%02d:%02d", seconds / 3600, seconds % 3600 / 60)
    }
}
