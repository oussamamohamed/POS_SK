import Foundation
import Observation

public struct Toast: Identifiable, Equatable, Sendable {
    public enum Kind: Sendable { case success, info, warning, error }
    public let id = UUID()
    public let kind: Kind
    public let message: String
}

/// File de notifications affichées en surimpression (équivalent des « toasts » du client web).
@MainActor @Observable
public final class Notifier {
    public private(set) var toasts: [Toast] = []
    /// Dernier message, conservé pour les tests et l'accessibilité.
    public private(set) var lastMessage: String?
    public var autoDismissAfter: Duration? = .seconds(3)

    public init() {}

    public func post(_ message: String, _ kind: Toast.Kind = .info) {
        let toast = Toast(kind: kind, message: message)
        toasts.append(toast)
        lastMessage = message
        if toasts.count > 4 { toasts.removeFirst(toasts.count - 4) }
        if let delay = autoDismissAfter {
            Task { [weak self] in
                try? await Task.sleep(for: delay)
                self?.dismiss(toast.id)
            }
        }
    }

    public func success(_ message: String) { post(message, .success) }
    public func info(_ message: String) { post(message, .info) }
    public func warning(_ message: String) { post(message, .warning) }
    public func error(_ message: String) { post(message, .error) }

    public func error(_ error: Error) {
        post((error as? LocalizedError)?.errorDescription ?? error.localizedDescription, .error)
    }

    public func dismiss(_ id: UUID) { toasts.removeAll { $0.id == id } }
}

/// Paramètres du terminal : adresse et préférences dans `UserDefaults`, identité du poste dans `credentialStore`.
@MainActor @Observable
public final class TerminalSettings {
    private let defaults: UserDefaults
    @ObservationIgnored private let credentialStore: DeviceCredentialStore

    public var serverURL: String { didSet { defaults.set(serverURL, forKey: Keys.server) } }
    public var siren: String { didSet { defaults.set(siren, forKey: Keys.siren) } }
    public var mealVoucherPolicy: MealVoucherPolicy { didSet { defaults.set(mealVoucherPolicy.rawValue, forKey: Keys.voucher) } }

    /// Identité du poste attribuée par le serveur à l'appairage.
    public private(set) var credentials: DeviceCredentials?
    public var isPaired: Bool { credentials != nil }
    /// Identifiant fiscal attribué par le serveur (`T01`…). Vide tant que le poste n'est pas appairé.
    public var terminalId: String { credentials?.terminalId ?? "" }

    enum Keys {
        static let server = "pos.serverURL"
        static let terminal = "pos.terminalId"
        static let siren = "pos.siren"
        static let voucher = "pos.mealVoucherPolicy"
    }

    public init(defaults: UserDefaults = .standard, credentialStore: DeviceCredentialStore = InMemoryCredentialStore()) {
        self.defaults = defaults
        serverURL = defaults.string(forKey: Keys.server) ?? "http://localhost:5080"
        siren = defaults.string(forKey: Keys.siren) ?? "123456789"
        mealVoucherPolicy = MealVoucherPolicy(rawValue: defaults.integer(forKey: Keys.voucher)) ?? .capAtBalance
        self.credentialStore = credentialStore
        credentials = credentialStore.load()
        // Ancien identifiant saisi à la main : remplacé par celui du serveur.
        defaults.removeObject(forKey: Keys.terminal)
    }

    public var url: URL? {
        let trimmed = serverURL.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let url = URL(string: trimmed), url.scheme == "http" || url.scheme == "https", url.host != nil else { return nil }
        return url
    }

    /// Renvoie `false` sans marquer le poste comme appairé si l'identité n'a pas pu être stockée
    /// (ex. échec Keychain) : le code d'appairage à usage unique est alors déjà consommé côté serveur.
    @discardableResult
    public func pair(serverURL: String, credentials: DeviceCredentials) -> Bool {
        self.serverURL = serverURL
        guard credentialStore.save(credentials) else { return false }
        self.credentials = credentials
        return true
    }

    /// Poste révoqué ou dissocié : retour à l'écran d'appairage. L'adresse du serveur est conservée.
    public func unpair() {
        credentialStore.clear()
        credentials = nil
    }
}

/// Session opérateur (déverrouillage par PIN).
@MainActor @Observable
public final class SessionStore {
    public private(set) var currentOperator: Operator?
    public private(set) var token: String?
    public private(set) var isAuthenticating = false
    public var pinEntry = ""
    public private(set) var pinError: String?
    /// Les codes PIN opérateur comptent 4 chiffres ; la saisie est validée automatiquement.
    /// (Chaque échec compte pour le verrouillage anti-force-brute du serveur.)
    public static let pinLength = 4

    private let api: PosAPI

    public init(api: PosAPI) { self.api = api }

    public var isUnlocked: Bool { currentOperator != nil }
    public var isManager: Bool { currentOperator?.role.isManager ?? false }

    public func appendDigit(_ digit: String) async {
        guard pinEntry.count < Self.pinLength, !isAuthenticating else { return }
        pinError = nil
        pinEntry += digit
        if pinEntry.count == Self.pinLength { await submit() }
    }

    public func deleteDigit() {
        guard !pinEntry.isEmpty else { return }
        pinEntry.removeLast()
        pinError = nil
    }

    public func clearPin() { pinEntry = ""; pinError = nil }

    public func submit() async {
        guard pinEntry.count == Self.pinLength, !isAuthenticating else { return }
        isAuthenticating = true
        defer { isAuthenticating = false }
        do {
            let result = try await api.login(pin: pinEntry)
            if result.success, let id = result.operatorId {
                currentOperator = Operator(id: id, name: result.operatorName ?? "Opérateur", role: result.role ?? .waiter)
                token = result.token
                await api.setToken(result.token)
                pinEntry = ""
                pinError = nil
            } else {
                pinError = result.errorMessage ?? "Code PIN invalide"
                pinEntry = ""
            }
        } catch {
            pinError = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
            pinEntry = ""
        }
    }

    public func lock() {
        currentOperator = nil
        pinEntry = ""
        pinError = nil
    }

    /// Appelé quand l'API répond 401 : on verrouille pour forcer une nouvelle saisie du PIN.
    public func handleUnauthorized() {
        token = nil
        Task { await api.setToken(nil) }
        lock()
    }
}
