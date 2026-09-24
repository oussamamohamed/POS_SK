import POSKit
import SwiftUI

/// Réglages du terminal : serveur de caisse, identifiant, mode démonstration.
struct SettingsView: View {
    @Environment(AppModel.self) private var app
    @Environment(\.dismiss) private var dismiss
    var isModal: Bool

    @State private var draft = TerminalSettings()
    @State private var testResult: Result<String, APIError>?
    @State private var isTesting = false
    @State private var loaded = false

    var body: some View {
        Form {
            Section {
                Toggle("Mode démonstration", isOn: $draft.demoMode)
                    .accessibilityIdentifier("settings.demo")
                if !draft.demoMode {
                    TextField("Adresse du serveur (ex. 192.168.1.20:5000)", text: $draft.serverAddress)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .accessibilityIdentifier("settings.address")
                }
                Button {
                    Task { await test() }
                } label: {
                    HStack {
                        Label("Tester la connexion", systemImage: "antenna.radiowaves.left.and.right")
                        Spacer()
                        if isTesting { ProgressView() }
                    }
                }
                .disabled(isTesting || !draft.isValid)
                .accessibilityIdentifier("settings.test")
                if let testResult {
                    switch testResult {
                    case .success(let message):
                        Label(message, systemImage: "checkmark.circle.fill").foregroundStyle(.green)
                    case .failure(let error):
                        Label(error.localizedDescription, systemImage: "xmark.octagon.fill").foregroundStyle(.red)
                    }
                }
            } header: {
                Text("Serveur de caisse")
            } footer: {
                Text(draft.demoMode
                     ? "Les données de démonstration restent sur cet iPad : idéal pour la formation."
                     : "L'iPad dialogue avec l'API RestaurantPos sur le réseau local (Wi-Fi du restaurant).")
            }

            Section("Terminal") {
                LabeledContent("Identifiant") {
                    TextField("IPAD_01", text: $draft.terminalId)
                        .multilineTextAlignment(.trailing)
                        .textInputAutocapitalization(.characters)
                        .autocorrectionDisabled()
                        .accessibilityIdentifier("settings.terminal")
                }
                Stepper("Longueur du code PIN : \(draft.pinAutoSubmitLength)", value: $draft.pinAutoSubmitLength, in: PinEntry.minLength...PinEntry.maxLength)
            }

            Section("À propos") {
                LabeledContent("Version", value: Bundle.main.object(forInfoDictionaryKey: "CFBundleShortVersionString") as? String ?? "—")
                LabeledContent("Opérateur", value: app.currentOperator?.name ?? "Non connecté")
            }
        }
        .navigationTitle("Réglages")
        .toolbar {
            if isModal {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Annuler") { dismiss() }
                }
            }
            ToolbarItem(placement: .confirmationAction) {
                Button("Enregistrer") {
                    Task {
                        await app.apply(draft)
                        app.context.notify("Réglages enregistrés", style: .success)
                        if isModal { dismiss() }
                    }
                }
                .disabled(!draft.isValid || draft == app.settings)
                .accessibilityIdentifier("settings.save")
            }
        }
        .onAppear {
            if !loaded {
                draft = app.settings
                loaded = true
            }
        }
        .onChange(of: draft) {
            testResult = nil
        }
    }

    private func test() async {
        isTesting = true
        testResult = await app.testConnection(draft)
        isTesting = false
    }
}
