import SwiftUI
import PosKit

/// Réglage du TPE du mode autonome : clé restreinte Stripe et lecteur, mémorisés dans le Trousseau de l'iPad.
struct CardTerminalSection: View {
    @Environment(AppEnvironment.self) private var environment
    @State private var secretKey = ""
    @State private var readerId = ""
    @State private var showsInvalid = false

    var body: some View {
        if let controller = environment.cardTerminal {
            Section {
                LabeledContent(
                    "card_terminal.status_label",
                    value: controller.isConfigured ? String(localized: "card_terminal.status_set") : String(localized: "card_terminal.status_missing")
                )
                .accessibilityIdentifier("cardTerminal.status")
                SecureField("card_terminal.key_field", text: $secretKey)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .accessibilityIdentifier("cardTerminal.key")
                TextField("card_terminal.reader_field", text: $readerId)
                    .textInputAutocapitalization(.never)
                    .autocorrectionDisabled()
                    .accessibilityIdentifier("cardTerminal.reader")
                if showsInvalid { Text("card_terminal.key_invalid").font(.footnote).foregroundStyle(Theme.warning) }
                Button("card_terminal.save") {
                    showsInvalid = !controller.save(secretKey: secretKey, readerId: readerId)
                    if !showsInvalid { secretKey = ""; readerId = "" }
                }
                .disabled(secretKey.isEmpty || readerId.isEmpty)
                .accessibilityIdentifier("cardTerminal.save")
                if controller.isConfigured {
                    Button("card_terminal.clear", role: .destructive) { controller.clear() }
                        .accessibilityIdentifier("cardTerminal.clear")
                }
            } header: {
                Text("card_terminal.section")
            } footer: {
                Text("card_terminal.footer")
            }
        }
    }
}
