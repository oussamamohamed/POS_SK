import SwiftUI
import PosKit

/// Première configuration d'une installation autonome : premier responsable et identité de l'établissement.
/// Aucun autre écran n'est accessible tant que cette configuration n'est pas faite.
struct FirstRunScreen: View {
    @Environment(AppEnvironment.self) private var environment
    @State private var managerName = ""
    @State private var pin = ""
    @State private var pinConfirm = ""
    @State private var company = ""
    @State private var address = ""
    @State private var siret = ""
    @State private var vat = ""
    @State private var error: String?
    @State private var isSaving = false

    private var canSubmit: Bool {
        !isSaving && !isBlank(managerName) && pin.count == SessionStore.pinLength && !isBlank(company) && siret.count == 14
    }

    var body: some View {
        NavigationStack {
            Form {
                Section {
                    Text("setup.subtitle").font(.footnote).foregroundStyle(.secondary)
                }
                managerSection
                restaurantSection
                submitSection
            }
            .scrollDismissesKeyboard(.interactively)
            .navigationTitle("setup.title")
        }
    }

    private var managerSection: some View {
        Section("setup.section_manager") {
            TextField("setup.field_name", text: $managerName)
                .textContentType(.name)
                .accessibilityIdentifier("setup.name")
            SecureField("setup.field_pin", text: $pin)
                .keyboardType(.numberPad)
                .onChange(of: pin) { _, value in pin = Self.digits(value, limit: SessionStore.pinLength) }
                .accessibilityIdentifier("setup.pin")
            SecureField("setup.field_pin_confirm", text: $pinConfirm)
                .keyboardType(.numberPad)
                .onChange(of: pinConfirm) { _, value in pinConfirm = Self.digits(value, limit: SessionStore.pinLength) }
                .accessibilityIdentifier("setup.pinConfirm")
        }
    }

    private var restaurantSection: some View {
        Section("setup.section_restaurant") {
            TextField("setup.field_company", text: $company)
                .accessibilityIdentifier("setup.company")
            TextField("setup.field_address", text: $address, axis: .vertical)
                .lineLimit(1...3)
                .accessibilityIdentifier("setup.address")
            TextField("setup.field_siret", text: $siret)
                .keyboardType(.numberPad)
                .onChange(of: siret) { _, value in siret = Self.digits(value, limit: 14) }
                .accessibilityIdentifier("setup.siret")
            TextField("setup.field_vat", text: $vat)
                .textInputAutocapitalization(.characters)
                .autocorrectionDisabled()
                .accessibilityIdentifier("setup.vat")
        }
    }

    private var submitSection: some View {
        Section {
            if let error {
                Text(error).foregroundStyle(Theme.danger).accessibilityIdentifier("setup.error")
            }
            Button {
                Task { await submit() }
            } label: {
                HStack {
                    Text("setup.submit")
                    if isSaving { Spacer(); ProgressView() }
                }
            }
            .disabled(!canSubmit)
            .accessibilityIdentifier("setup.submit")
        }
    }

    private func isBlank(_ text: String) -> Bool { text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty }

    private static func digits(_ text: String, limit: Int) -> String { String(text.filter { $0.isASCII && $0.isNumber }.prefix(limit)) }

    private func submit() async {
        guard pin == pinConfirm else {
            error = String(localized: "setup.pin_mismatch")
            return
        }
        isSaving = true
        defer { isSaving = false }
        error = nil
        do {
            try await environment.completeSetup(LocalSetup(
                managerName: managerName, managerPin: pin, companyName: company, address: address, siret: siret, vatNumber: vat
            ))
        } catch {
            self.error = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
        }
    }
}
