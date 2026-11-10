import Foundation

/// Méthodes de `PosAPI` pas encore portées sur la base locale. Chaque plan suivant retire ses lignes d'ici.
extension LocalPosAPI {
    // MARK: Appairage : sans objet en mode autonome (le poste est son propre terminal)
    public func pair(code: String) async throws -> PairResponse { throw unsupported() }

    // MARK: Sauvegarde du serveur : sans objet (le mode autonome sauvegarde sa propre base, `LocalBackupService`)
    public func serverBackupStatus() async throws -> ServerBackupStatus { throw unsupported() }
    public func runServerBackup() async throws -> ServerBackupResult { throw unsupported() }
}
