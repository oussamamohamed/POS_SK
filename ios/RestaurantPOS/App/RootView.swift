import SwiftUI
import PosKit

struct RootView: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        ZStack(alignment: .top) {
            if model.session.isUnlocked {
                MainShell()
                    .transition(.opacity)
            } else {
                LockScreen()
                    .transition(.opacity)
            }
            ToastOverlay(notifier: model.notifier)
        }
        .animation(.easeInOut(duration: 0.25), value: model.session.isUnlocked)
        .task(id: model.session.currentOperator?.id) {
            guard model.session.isUnlocked, !model.isBootstrapped else { return }
            await model.bootstrap()
        }
    }
}

/// Écran de verrouillage : saisie du PIN opérateur (4 chiffres, validation automatique).
struct LockScreen: View {
    @Environment(AppModel.self) private var model
    @Environment(AppEnvironment.self) private var environment
    @State private var shake = 0
    @State private var showsServerSettings = false

    var body: some View {
        let session = model.session
        ZStack {
            Theme.canvas.ignoresSafeArea()

            VStack(spacing: Theme.Space.xxl) {
                VStack(spacing: Theme.Space.s) {
                    Image("BrandMark")
                        .resizable()
                        .scaledToFill()
                        .frame(width: 88, height: 88)
                        .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.lg, style: .continuous))
                        .accessibilityHidden(true)
                    Text("AGY POS").font(.system(size: 34, weight: .heavy)).foregroundStyle(Theme.ink)
                    Text("Saisissez votre code PIN").font(.system(size: 17, weight: .medium)).foregroundStyle(Theme.inkMuted)
                }

                PinDots(count: SessionStore.pinLength, filled: session.pinEntry.count, isError: session.pinError != nil)
                    .modifier(ShakeEffect(animatableData: CGFloat(shake)))

                Text(session.pinError ?? " ")
                    .font(.posLabel)
                    .foregroundStyle(Theme.danger)
                    .accessibilityIdentifier("pin.error")

                NumericKeypad(keySize: 84, identifierPrefix: "pin") { digit in
                    Task { await session.appendDigit(digit) }
                } onDelete: {
                    session.deleteDigit()
                } onClear: {
                    session.clearPin()
                }
                .disabled(session.isAuthenticating)
                .overlay { if session.isAuthenticating { ProgressView().controlSize(.large) } }
            }
            .padding(40)
            .background(RoundedRectangle(cornerRadius: Theme.Radius.xl, style: .continuous).fill(Theme.surface))
            .cardShadow(radius: Theme.Radius.xl)
            .frame(maxWidth: 460)

            VStack {
                Spacer()
                HStack {
                    Label(environment.launch.isUITest ? "Mode démo (données locales)" : model.settings.serverURL, systemImage: "server.rack")
                        .font(.footnote)
                        .foregroundStyle(Theme.inkMuted)
                    Button("Changer") { showsServerSettings = true }
                        .font(.footnote.weight(.semibold))
                        .accessibilityIdentifier("lock.server")
                }
                .padding()
            }
        }
        .onChange(of: session.pinError) { _, error in
            if error != nil {
                Haptics.error()
                withAnimation(.default) { shake += 1 }
            }
        }
        .sheet(isPresented: $showsServerSettings) {
            ServerSettingsSheet()
        }
    }
}

struct ShakeEffect: GeometryEffect {
    var animatableData: CGFloat
    func effectValue(size: CGSize) -> ProjectionTransform {
        ProjectionTransform(CGAffineTransform(translationX: 10 * sin(animatableData * .pi * 4), y: 0))
    }
}

/// Réglage de l'adresse du serveur maître (accessible avant connexion).
struct ServerSettingsSheet: View {
    @Environment(AppEnvironment.self) private var environment
    @Environment(\.dismiss) private var dismiss
    @State private var url = ""
    @State private var terminal = ""
    @State private var testResult: String?
    @State private var isTesting = false

    var body: some View {
        NavigationStack {
            Form {
                Section("Serveur maître") {
                    TextField("http://192.168.1.10:5080", text: $url)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .accessibilityIdentifier("settings.serverURL")
                    Button {
                        Task { await test() }
                    } label: {
                        HStack {
                            Text("Tester la connexion")
                            if isTesting { Spacer(); ProgressView() }
                        }
                    }
                    if let testResult { Text(testResult).font(.footnote).foregroundStyle(.secondary) }
                }
                Section("Terminal") {
                    TextField("Identifiant du terminal", text: $terminal)
                        .textInputAutocapitalization(.characters)
                        .accessibilityIdentifier("settings.terminalId")
                }
            }
            .navigationTitle("Connexion")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("Annuler") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Enregistrer") {
                        environment.settings.serverURL = url
                        environment.settings.terminalId = terminal.isEmpty ? "POS_A" : terminal
                        environment.reconnect()
                        dismiss()
                    }
                    .disabled(URL(string: url)?.host == nil)
                }
            }
            .onAppear {
                url = environment.settings.serverURL
                terminal = environment.settings.terminalId
            }
        }
    }

    private func test() async {
        guard let parsed = URL(string: url), parsed.host != nil else { testResult = "Adresse invalide"; return }
        isTesting = true
        defer { isTesting = false }
        do {
            let latency = try await HTTPPosAPI(baseURL: parsed).health()
            testResult = "✓ Serveur joignable (\(Int(latency * 1000)) ms)"
        } catch {
            testResult = "✗ \((error as? LocalizedError)?.errorDescription ?? error.localizedDescription)"
        }
    }
}
