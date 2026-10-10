import SwiftUI
import PosKit

/// Réglages de sauvegarde du mode autonome : phrase, dossier, état et export manuel.
struct BackupSettingsSection: View {
    @Environment(AppEnvironment.self) private var environment
    @State private var showsPassphrase = false
    @State private var showsExport = false
    @State private var showsFolderPicker = false

    var body: some View {
        if let backup = environment.backup {
            Section("backup.section") {
                LabeledContent("backup.passphrase_label", value: backup.hasPassphrase ? String(localized: "backup.passphrase_set") : String(localized: "backup.passphrase_missing"))
                Button("backup.passphrase_change") { showsPassphrase = true }
                    .accessibilityIdentifier("backup.passphrase.set")
                    .sheet(isPresented: $showsPassphrase) { PassphraseSheet(backup: backup) }
                LabeledContent("backup.folder_label", value: backup.folderName ?? String(localized: "backup.folder_none"))
                    .accessibilityIdentifier("backup.folder")
                Button("backup.folder_choose") { showsFolderPicker = true }
                    .accessibilityIdentifier("backup.folder.choose")
                    .fileImporter(isPresented: $showsFolderPicker, allowedContentTypes: [.folder]) { result in
                        if case .success(let url) = result { backup.setFolder(url); Task { await backup.refresh() } }
                    }
                lastSuccessRow(backup)
                if let error = backup.lastError { Text(error).font(.footnote).foregroundStyle(Theme.warning) }
                Button("backup.export_now") { showsExport = true }
                    .disabled(backup.isRunning)
                    .accessibilityIdentifier("backup.export")
                    .sheet(isPresented: $showsExport) { ExportSheet(backup: backup) }
            }
            .task { await backup.refresh() }
        }
    }

    @ViewBuilder
    private func lastSuccessRow(_ backup: BackupController) -> some View {
        if let last = backup.status?.lastSuccess {
            LabeledContent("backup.last_success_label", value: last.formatted(date: .abbreviated, time: .shortened))
                .accessibilityIdentifier("backup.lastSuccess")
        } else {
            LabeledContent("backup.last_success_label", value: String(localized: "backup.never"))
                .accessibilityIdentifier("backup.lastSuccess")
        }
    }
}

private struct PassphraseSheet: View {
    let backup: BackupController
    @Environment(\.dismiss) private var dismiss
    @State private var phrase = ""
    @State private var confirmation = ""
    @State private var message: LocalizedStringKey?

    var body: some View {
        NavigationStack {
            Form {
                Section { Text("backup.passphrase_warning").font(.footnote).foregroundStyle(Theme.warning) }
                Section {
                    SecureField("backup.passphrase_field", text: $phrase).accessibilityIdentifier("backup.passphrase.field")
                    SecureField("backup.passphrase_confirm", text: $confirmation).accessibilityIdentifier("backup.passphrase.confirm")
                    if let message { Text(message).font(.footnote).foregroundStyle(Theme.warning) }
                }
            }
            .navigationTitle("backup.passphrase_title")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("backup.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("backup.save") {
                        if phrase != confirmation { message = "backup.passphrase_mismatch" }
                        else if !backup.setPassphrase(phrase) { message = "backup.passphrase_too_short" }
                        else { dismiss() }
                    }
                    .accessibilityIdentifier("backup.passphrase.save")
                }
            }
        }
    }
}

private struct ExportSheet: View {
    let backup: BackupController
    @Environment(\.dismiss) private var dismiss
    @State private var phrase = ""
    @State private var confirmation = ""
    @State private var mismatch = false

    var body: some View {
        NavigationStack {
            Form {
                SecureField("backup.export_passphrase", text: $phrase).accessibilityIdentifier("backup.export.passphrase")
                SecureField("backup.passphrase_confirm", text: $confirmation).accessibilityIdentifier("backup.export.confirmPassphrase")
                if mismatch { Text("backup.passphrase_mismatch").font(.footnote).foregroundStyle(Theme.warning).accessibilityIdentifier("backup.export.mismatch") }
                if let error = backup.lastError { Text(error).font(.footnote).foregroundStyle(Theme.warning) }
            }
            .navigationTitle("backup.export_title")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("backup.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("backup.export_confirm") {
                        mismatch = phrase != confirmation
                        guard !mismatch else { return }
                        Task { if await backup.exportNow(passphrase: phrase) { dismiss() } }
                    }
                    .disabled(backup.isRunning)
                    .accessibilityIdentifier("backup.export.confirm")
                }
            }
        }
    }
}
