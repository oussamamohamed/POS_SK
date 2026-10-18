import Foundation

/// Nom de fichier à écrire dans un dossier local : dernier composant seulement, caractères hors `[A-Za-z0-9_.-]` remplacés par `_`,
/// `fallback` si le résultat est vide ou fait de points. Un nom venu du serveur ou dérivé d'une saisie ne sort jamais du dossier.
enum SafeFileName {
    static func sanitized(_ name: String, fallback: String) -> String {
        let last = name.split(whereSeparator: { $0 == "/" || $0 == "\\" }).last.map(String.init) ?? ""
        let safe = last.replacingOccurrences(of: "[^A-Za-z0-9_.-]", with: "_", options: .regularExpression)
        return safe.isEmpty || safe.allSatisfy({ $0 == "." }) ? fallback : safe
    }
}
