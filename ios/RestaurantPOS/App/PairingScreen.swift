import SwiftUI
import VisionKit
import PosKit

/// Premier lancement ou poste révoqué : relie l'iPad au serveur avec le code du back-office.
struct PairingScreen: View {
    @Environment(AppEnvironment.self) private var environment
    @Environment(AppModel.self) private var model
    @State private var browser = ServerBrowser()
    @State private var url = ""
    @State private var code = ""
    @State private var error: String?
    @State private var isPairing = false
    @State private var showsScanner = false

    var body: some View {
        NavigationStack {
            Form {
                Section("pairing.discovered_servers") {
                    if browser.servers.isEmpty {
                        Label("pairing.searching_network", systemImage: "antenna.radiowaves.left.and.right")
                            .foregroundStyle(.secondary)
                    }
                    ForEach(browser.servers) { server in
                        Button(server.name) { Task { await select(server) } }
                            .accessibilityIdentifier("pairing.server.\(server.name)")
                    }
                }
                Section {
                    Button { showsScanner = true } label: {
                        Label("pairing.scan_qr", systemImage: "qrcode.viewfinder")
                    }
                    .disabled(!DataScannerViewController.isSupported)
                    .accessibilityIdentifier("pairing.scan")
                }
                Section("pairing.manual_entry_section") {
                    TextField("http://192.168.1.10:5080", text: $url)
                        .keyboardType(.URL)
                        .textInputAutocapitalization(.never)
                        .autocorrectionDisabled()
                        .accessibilityIdentifier("pairing.url")
                    TextField("pairing.code_placeholder", text: $code)
                        .textInputAutocapitalization(.characters)
                        .autocorrectionDisabled()
                        .accessibilityIdentifier("pairing.code")
                    if let error {
                        Text(error).foregroundStyle(.red).accessibilityIdentifier("pairing.error")
                    }
                    Button {
                        Task { await pair() }
                    } label: {
                        HStack {
                            Text("pairing.submit_button")
                            if isPairing { Spacer(); ProgressView() }
                        }
                    }
                    .disabled(isPairing || code.trimmingCharacters(in: .whitespaces).isEmpty || URL(string: url)?.host == nil)
                    .accessibilityIdentifier("pairing.submit")
                }
                Section {
                    Text("pairing.hint")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                }
            }
            .navigationTitle("pairing.nav_title")
        }
        .onAppear {
            url = model.settings.serverURL
            if !environment.launch.isUITest { browser.start() }
        }
        .onDisappear { browser.stop() }
        .sheet(isPresented: $showsScanner) {
            QRScannerView(onScan: { payload in
                showsScanner = false
                guard let link = PairingLink(string: payload) else { error = String(localized: "pairing.qr_not_recognized"); return }
                url = link.serverURL.absoluteString
                code = link.code
                Task { await pair() }
            }, onFailure: {
                showsScanner = false
                error = String(localized: "pairing.camera_unavailable")
            })
            .ignoresSafeArea()
        }
    }

    private func select(_ server: DiscoveredServer) async {
        if let resolved = await ServerBrowser.resolve(name: server.name) {
            url = resolved.absoluteString
            error = nil
        } else {
            error = String(localized: "pairing.cannot_reach \(server.name)")
        }
    }

    private func pair() async {
        guard let serverURL = URL(string: url.trimmingCharacters(in: .whitespaces)), serverURL.host != nil else {
            error = String(localized: "common.invalid_address")
            return
        }
        isPairing = true
        defer { isPairing = false }
        error = nil
        let api: PosAPI = environment.launch.isUITest ? model.api : HTTPPosAPI(baseURL: serverURL)
        do {
            let response = try await api.pair(code: code.trimmingCharacters(in: .whitespaces))
            if await !environment.completePairing(serverURL: serverURL, response: response) {
                error = String(localized: "pairing.register_failed")
            }
        } catch {
            self.error = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
        }
    }
}

/// Lecteur de QR (VisionKit) : renvoie le premier code lu.
struct QRScannerView: UIViewControllerRepresentable {
    let onScan: (String) -> Void
    let onFailure: () -> Void

    func makeUIViewController(context: Context) -> DataScannerViewController {
        let scanner = DataScannerViewController(recognizedDataTypes: [.barcode(symbologies: [.qr])], isHighlightingEnabled: true)
        scanner.delegate = context.coordinator
        return scanner
    }

    func updateUIViewController(_ scanner: DataScannerViewController, context: Context) {
        if !scanner.isScanning {
            do { try scanner.startScanning() } catch { context.coordinator.fail() }
        }
    }

    func makeCoordinator() -> Coordinator { Coordinator(onScan: onScan, onFailure: onFailure) }

    @MainActor
    final class Coordinator: NSObject, DataScannerViewControllerDelegate {
        private let onScan: (String) -> Void
        private let onFailure: () -> Void
        private var done = false

        init(onScan: @escaping (String) -> Void, onFailure: @escaping () -> Void) {
            self.onScan = onScan
            self.onFailure = onFailure
        }

        func fail() {
            guard !done else { return }
            done = true
            DispatchQueue.main.async { self.onFailure() }
        }

        func dataScanner(_ dataScanner: DataScannerViewController, didAdd addedItems: [RecognizedItem], allItems: [RecognizedItem]) {
            guard !done else { return }
            for item in addedItems {
                if case let .barcode(barcode) = item, let payload = barcode.payloadStringValue {
                    done = true
                    dataScanner.stopScanning()
                    onScan(payload)
                    return
                }
            }
        }
    }
}
