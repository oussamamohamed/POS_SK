import Foundation
import Observation
import PosKit

/// Sauvegarde du mode autonome côté application : état affiché, phrase (Keychain), dossier (signet), exécution quotidienne et export manuel.
@MainActor @Observable
final class BackupController {
    private(set) var status: BackupStatus?
    private(set) var lastError: String?
    private(set) var isRunning = false
    private(set) var hasPassphrase = false
    private(set) var folderName: String?

    @ObservationIgnored private let api: LocalPosAPI
    @ObservationIgnored private let passphrases: BackupPassphraseStore
    @ObservationIgnored private let defaults: UserDefaults
    @ObservationIgnored private let folderOverride: URL?
    private static let bookmarkKey = "backup.folderBookmark"

    init(api: LocalPosAPI, passphrases: BackupPassphraseStore, defaults: UserDefaults, folderOverride: URL?) {
        self.api = api
        self.passphrases = passphrases
        self.defaults = defaults
        self.folderOverride = folderOverride
        hasPassphrase = passphrases.load() != nil
        folderName = resolveFolder()?.lastPathComponent
    }

    var needsAttention: Bool { status?.isOverdue ?? false }
    var neverBackedUp: Bool { status != nil && status?.lastSuccess == nil }

    func refresh() async {
        status = try? await api.backupStatus()
        hasPassphrase = passphrases.load() != nil
        folderName = resolveFolder()?.lastPathComponent
    }

    /// Enregistre la phrase si elle a au moins 8 caractères.
    func setPassphrase(_ passphrase: String) -> Bool {
        guard passphrase.count >= LocalPosAPI.minimumBackupPassphraseLength, passphrases.save(passphrase) else { return false }
        hasPassphrase = true
        return true
    }

    func setFolder(_ url: URL) {
        let scoped = url.startAccessingSecurityScopedResource()
        defer { if scoped { url.stopAccessingSecurityScopedResource() } }
        if let data = try? url.bookmarkData() {
            defaults.set(data, forKey: Self.bookmarkKey)
            folderName = url.lastPathComponent
        }
    }

    /// Au premier plan : sauvegarde si la dernière date de plus de 24 h (ou n'existe pas) et que phrase et dossier sont configurés.
    func runIfDue() async {
        guard !isRunning else { return }
        isRunning = true
        defer { isRunning = false }
        await refresh()
        guard status?.isOverdue == true, let phrase = passphrases.load() else { return }
        _ = await perform(passphrase: phrase, scheduled: true)
    }

    /// Export manuel : la phrase est redemandée à l'utilisateur.
    func exportNow(passphrase: String) async -> Bool {
        guard !isRunning else { return false }
        isRunning = true
        defer { isRunning = false }
        return await perform(passphrase: passphrase, scheduled: false)
    }

    private func perform(passphrase: String, scheduled: Bool) async -> Bool {
        guard let folder = resolveFolder() else {
            lastError = String(localized: "backup.error_no_folder")
            return false
        }
        let scoped = folder.startAccessingSecurityScopedResource()
        defer { if scoped { folder.stopAccessingSecurityScopedResource() } }
        do {
            if scheduled { _ = try await api.runScheduledBackup(to: folder, passphrase: passphrase) }
            else { _ = try await api.exportBackup(to: folder, passphrase: passphrase) }
            lastError = nil
            await refresh()
            return true
        } catch {
            lastError = error.localizedDescription
            await refresh()
            return false
        }
    }

    private func resolveFolder() -> URL? {
        if let folderOverride { return folderOverride }
        guard let data = defaults.data(forKey: Self.bookmarkKey) else { return nil }
        var stale = false
        return try? URL(resolvingBookmarkData: data, bookmarkDataIsStale: &stale)
    }
}
