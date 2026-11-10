import Foundation
import Observation

/// Sauvegarde de la base du serveur .NET : état et sauvegarde immédiate (responsable).
@MainActor @Observable
public final class ServerBackupStore {
    public private(set) var status: ServerBackupStatus?
    public private(set) var isRunning = false
    private let api: PosAPI
    private let notifier: Notifier

    public init(api: PosAPI, notifier: Notifier) {
        self.api = api
        self.notifier = notifier
    }

    public func load() async {
        do { status = try await api.serverBackupStatus() } catch { notifier.error(error) }
    }

    public func runNow() async {
        guard !isRunning else { return }
        isRunning = true
        defer { isRunning = false }
        do {
            let result = try await api.runServerBackup()
            notifier.success(String(format: L10n.string("backup.server_done"), result.fileName))
        } catch {
            notifier.error(error)
        }
        await load()
    }
}
