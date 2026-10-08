import Foundation

extension LocalPosAPI {
    /// Sauvegarde cohérente de la base vers `url` (le fichier ne doit pas exister). Réservé aux responsables : la base contient
    /// les comptes, les ventes et, plus tard, la chaîne fiscale.
    public func backup(to url: URL) async throws {
        try requireManager()
        try db.backup(to: url.path)
    }
}
