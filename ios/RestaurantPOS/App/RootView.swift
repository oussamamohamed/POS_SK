import SwiftUI
import PosKit

struct RootView: View {
    @Environment(AppModel.self) private var model
    @Environment(AppEnvironment.self) private var environment

    var body: some View {
        ZStack(alignment: .top) {
            if let error = environment.startupError {
                StartupErrorScreen(message: error)
            } else if environment.mode == nil {
                ModeChoiceScreen()
                    .transition(.opacity)
            } else if environment.isStandalone && environment.setupState == .checking {
                ProgressView().controlSize(.large)
            } else if environment.isStandalone && environment.setupState == .needed {
                FirstRunScreen()
                    .transition(.opacity)
            } else if !model.settings.isPaired {
                PairingScreen()
                    .transition(.opacity)
            } else if model.session.isUnlocked {
                MainShell()
                    .transition(.opacity)
            } else {
                LockScreen()
                    .transition(.opacity)
            }
            ToastOverlay(notifier: model.notifier)
        }
        // Alerte imprimante (temps réel) : affichée en toast puis effacée, pour qu'une alerte identique se redéclenche.
        .onChange(of: model.printerAlert) { _, alert in
            guard let alert else { return }
            model.notifier.warning(alert)
            model.printerAlert = nil
        }
        .animation(.easeInOut(duration: 0.25), value: model.session.isUnlocked)
        .animation(.easeInOut(duration: 0.25), value: model.settings.isPaired)
        .task(id: environment.generation) { await environment.refreshSetup() }
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
                    Text(verbatim: "AGY POS").font(.system(size: 34, weight: .heavy)).foregroundStyle(Theme.ink)
                    Text("login.locked_title").font(.system(size: 17, weight: .semibold)).foregroundStyle(Theme.ink)
                    Text("login.enter_pin_subtitle").font(.system(size: 15, weight: .medium)).foregroundStyle(Theme.inkMuted)
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
                    if environment.isStandalone {
                        Label("mode.standalone_status", systemImage: "ipad")
                            .font(.footnote)
                            .foregroundStyle(Theme.inkMuted)
                            .accessibilityIdentifier("lock.standalone")
                    } else {
                        Label(environment.launch.isUITest ? String(localized: "login.demo_mode_label") : model.settings.serverURL, systemImage: "server.rack")
                            .font(.footnote)
                            .foregroundStyle(Theme.inkMuted)
                        Button("login.change_server") { showsServerSettings = true }
                            .font(.footnote.weight(.semibold))
                            .accessibilityIdentifier("lock.server")
                    }
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
        .task { await environment.rediscoverServerIfUnreachable() }
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
    @State private var testResult: String?
    @State private var isTesting = false

    var body: some View {
        NavigationStack {
            Form {
                Section("common.master_server_section") {
                    TextField("http://192.168.1.10:5080", text: $url)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .accessibilityIdentifier("settings.serverURL")
                    Button {
                        Task { await test() }
                    } label: {
                        HStack {
                            Text("common.test_connection")
                            if isTesting { Spacer(); ProgressView() }
                        }
                    }
                    if let testResult { Text(testResult).font(.footnote).foregroundStyle(.secondary) }
                }
                if let device = environment.settings.credentials {
                    Section("common.this_ipad_section") {
                        LabeledContent("common.field_name", value: device.name)
                        LabeledContent("common.field_terminal", value: device.terminalId)
                        LabeledContent("common.field_server", value: device.serverName)
                        Button("common.unpair_ipad", role: .destructive) {
                            environment.settings.unpair()
                            dismiss()
                        }
                        .accessibilityIdentifier("settings.unpair")
                    }
                }
            }
            .navigationTitle("common.nav_connection")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) { Button("common.cancel") { dismiss() } }
                ToolbarItem(placement: .confirmationAction) {
                    Button("common.save") {
                        environment.settings.serverURL = url
                        environment.reconnect()
                        dismiss()
                    }
                    .disabled(URL(string: url)?.host == nil)
                }
            }
            .onAppear {
                url = environment.settings.serverURL
            }
        }
    }

    private func test() async {
        guard let parsed = URL(string: url), parsed.host != nil else { testResult = String(localized: "common.invalid_address"); return }
        isTesting = true
        defer { isTesting = false }
        do {
            let latency = try await HTTPPosAPI(baseURL: parsed, deviceToken: environment.settings.credentials?.token).health()
            let ms = Int(latency * 1000)
            testResult = String(localized: "common.server_reachable \(ms)")
        } catch {
            let message = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
            testResult = String(localized: "common.server_unreachable \(message)")
        }
    }
}
