import Foundation

/// Nom de fichier à écrire dans un dossier local : dernier composant seulement, caractères hors `[A-Za-z0-9_.-]` remplacés par `_`,
/// `fallback` si le résultat est vide ou fait de points. Un nom venu du serveur ou dérivé d'une saisie ne sort jamais du dossier.
enum SafeFileName {
    static let maxLength = 120

    static func sanitized(_ name: String, fallback: String) -> String {
        let last = name.split(whereSeparator: { $0 == "/" || $0 == "\\" }).last.map(String.init) ?? ""
        let safe = last.replacingOccurrences(of: "[^A-Za-z0-9_.-]", with: "_", options: .regularExpression)
        guard !safe.isEmpty, !safe.allSatisfy({ $0 == "." }) else { return fallback }
        // Longueur bornée (255 octets max par composant sur APFS) : extension conservée, base rognée.
        guard safe.count > maxLength else { return safe }
        let ext = (safe as NSString).pathExtension
        let suffix = ext.isEmpty || ext.count > 16 ? "" : "." + ext
        return String(safe.prefix(maxLength - suffix.count)) + suffix
    }
}
