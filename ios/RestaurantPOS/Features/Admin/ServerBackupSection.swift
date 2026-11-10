import SwiftUI
import PosKit

/// Sauvegarde de la base du serveur (mode serveur) : état, dernier fichier et sauvegarde immédiate. Le mode autonome a `BackupSettingsSection`.
struct ServerBackupSection: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let backup = model.serverBackup
        Section("backup.section") {
            if let status = backup.status {
                if status.configured {
                    LabeledContent("backup.server_state", value: status.overdue ? String(localized: "backup.server_overdue") : String(localized: "backup.server_up_to_date"))
                        .accessibilityIdentifier("backup.server.state")
                } else {
                    Text("backup.server_not_configured").font(.footnote).foregroundStyle(Theme.warning)
                        .accessibilityIdentifier("backup.server.notConfigured")
                }
                LabeledContent("backup.last_success_label", value: status.lastSuccessUtc.map { $0.formatted(date: .abbreviated, time: .shortened) } ?? String(localized: "backup.never"))
                    .accessibilityIdentifier("backup.server.lastSuccess")
                if let file = status.lastFileName {
                    LabeledContent("backup.server_last_file", value: "\(file) (\(Self.size(status.lastSizeBytes)))")
                }
                LabeledContent("backup.server_copies", value: String(status.copies))
                LabeledContent("backup.folder_label", value: status.folder)
                if let error = status.lastError { Text(error).font(.footnote).foregroundStyle(Theme.warning) }
                Button("backup.export_now") { Task { await backup.runNow() } }
                    .disabled(!status.configured || backup.isRunning)
                    .accessibilityIdentifier("backup.server.run")
            }
        }
        .task { await backup.load() }
    }

    private static func size(_ bytes: Int64?) -> String {
        ByteCountFormatter.string(fromByteCount: bytes ?? 0, countStyle: .file)
    }
}
