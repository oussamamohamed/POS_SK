import SwiftUI
import PosKit
import PosPrinters

/// Feuille de recherche d'imprimantes Star / Epson : lance la recherche à l'ouverture, l'annule à la fermeture.
struct PrinterSearchSheet: View {
    @Environment(\.dismiss) private var dismiss
    @State private var store: PrinterDiscoveryStore
    /// Reçoit le brouillon construit par `PrinterDiscoveryStore.draft(from:)`.
    let onPick: (Printer) -> Void

    init(discovery: any PrinterDiscovery, onPick: @escaping (Printer) -> Void) {
        _store = State(initialValue: PrinterDiscoveryStore(discovery: discovery))
        self.onPick = onPick
    }

    var body: some View {
        NavigationStack {
            content
                .navigationTitle("admin.printer_search_title")
                .navigationBarTitleDisplayMode(.inline)
                .toolbar {
                    ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                    ToolbarItem(placement: .primaryAction) {
                        Button("admin.printer_search_retry") { Task { await store.search() } }
                            .disabled(store.phase == .searching)
                            .accessibilityIdentifier("printer.search.retry")
                    }
                }
        }
        .task { await store.search() }
        .onDisappear { store.cancel() }
    }

    @ViewBuilder private var content: some View {
        if store.phase == .searching {
            ProgressView("admin.printer_searching").frame(maxWidth: .infinity, maxHeight: .infinity)
        } else if store.phase == .done, store.results.isEmpty {
            Text("admin.printer_search_empty")
                .multilineTextAlignment(.center)
                .foregroundStyle(Theme.inkMuted)
                .padding()
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .accessibilityIdentifier("printer.search.empty")
        } else {
            List(Array(store.results.enumerated()), id: \.offset) { index, found in
                Button {
                    onPick(store.draft(from: found))
                    dismiss()
                } label: {
                    VStack(alignment: .leading, spacing: 2) {
                        Text(found.name).font(.headline)
                        Text("\(PrinterEditorSheet.label(found.transport)) · \(found.deviceIdentifier)")
                            .font(.caption).foregroundStyle(Theme.inkMuted)
                    }
                }
                .accessibilityIdentifier("printer.search.result.\(index)")
            }
        }
    }
}
