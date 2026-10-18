import Foundation

/// SIREN saisi pour le FEC : 9 chiffres, ou vide (le nom du fichier porte alors `000000000`, comme le .NET).
public enum Siren {
    /// Chiffres de toute écriture (clavier arabe : ٠-٩) ramenés à l'ASCII, espaces retirés ; le reste est gardé tel quel pour être refusé.
    public static func normalized(_ raw: String) -> String {
        String(raw.compactMap { character -> Character? in
            if character.isWhitespace { return nil }
            if character.unicodeScalars.count == 1, character.unicodeScalars.first?.properties.numericType == .decimal,
               let digit = character.wholeNumberValue {
                return Character(String(digit))
            }
            return character
        })
    }

    /// Vide après rognage, ou exactement 9 chiffres ASCII (le nom du fichier FEC en dérive : rien d'autre n'y entre).
    public static func isValid(_ siren: String) -> Bool {
        let trimmed = siren.trimmingCharacters(in: .whitespacesAndNewlines)
        return trimmed.isEmpty || (trimmed.unicodeScalars.count == 9 && trimmed.unicodeScalars.allSatisfy { ("0"..."9").contains($0) })
    }
}
