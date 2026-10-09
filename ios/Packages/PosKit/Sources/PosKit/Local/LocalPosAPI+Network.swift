import Foundation

extension LocalPosAPI {
    /// Aucun serveur : l'iPad se présente comme tel. Pas de nom d'hôte (sa résolution peut bloquer) ni d'adresse.
    public func networkInfo() async throws -> NetworkInfo {
        NetworkInfo(hostName: nil, primaryIp: nil, ipAddresses: [], port: nil, serverName: "Mode autonome (cet iPad)", status: "Standalone", version: nil)
    }

    /// Rien à synchroniser tant qu'il n'y a ni serveur ni second poste.
    public func syncStatus() async throws -> SyncStatus {
        SyncStatus(totalMessages: 0, completedMessages: 0, pendingMessages: 0, status: "Standalone", lastSyncUtc: nil)
    }

    public func forceSync() async throws {}
}
