import Foundation
import Observation

/// Contexte d'une commande en cours de saisie.
public enum OrderContext: Hashable, Sendable {
    case table(String)
    case counter

    /// Table technique utilisée par l'API pour les ventes directes au comptoir.
    public static let counterTableNumber = "Comptoir"

    public var tableNumber: String {
        switch self {
        case .table(let number): number
        case .counter: Self.counterTableNumber
        }
    }

    public var title: String {
        switch self {
        case .table(let number): "Table \(number)"
        case .counter: "Comptoir"
        }
    }

    public var isCounter: Bool { self == .counter }
}

/// Message éphémère affiché en haut de l'écran.
public struct Notice: Identifiable, Hashable, Sendable {
    public enum Style: Sendable { case success, info, warning, error }

    public let id: UUID
    public let message: String
    public let style: Style

    public init(_ message: String, style: Style = .info) {
        self.id = UUID()
        self.message = message
        self.style = style
    }
}

/// Réglages du terminal, persistés dans `UserDefaults`.
public struct TerminalSettings: Hashable, Codable, Sendable {
    public var serverAddress: String
    public var terminalId: String
    public var demoMode: Bool
    /// Validation automatique du PIN à cette longueur (4 par défaut, comme le site web).
    public var pinAutoSubmitLength: Int

    public init(serverAddress: String = "", terminalId: String = "IPAD_01", demoMode: Bool = true, pinAutoSubmitLength: Int = 4) {
        self.serverAddress = serverAddress
        self.terminalId = terminalId
        self.demoMode = demoMode
        self.pinAutoSubmitLength = pinAutoSubmitLength
    }

    public var serverURL: URL? { POSAPIClient.normalizedURL(from: serverAddress) }

    public var isValid: Bool {
        !terminalId.trimmingCharacters(in: .whitespaces).isEmpty && (demoMode || serverURL != nil)
    }
}

public protocol SettingsStore: Sendable {
    func load() -> TerminalSettings?
    func save(_ settings: TerminalSettings)
}

public struct UserDefaultsSettingsStore: SettingsStore, @unchecked Sendable {
    private let defaults: UserDefaults
    private let key = "pos.terminal.settings"

    public init(defaults: UserDefaults = .standard) {
        self.defaults = defaults
    }

    public func load() -> TerminalSettings? {
        guard let data = defaults.data(forKey: key) else { return nil }
        return try? JSONDecoder().decode(TerminalSettings.self, from: data)
    }

    public func save(_ settings: TerminalSettings) {
        if let data = try? JSONEncoder().encode(settings) {
            defaults.set(data, forKey: key)
        }
    }
}

public final class InMemorySettingsStore: SettingsStore, @unchecked Sendable {
    private let lock = NSLock()
    private var stored: TerminalSettings?

    public init(_ settings: TerminalSettings? = nil) {
        stored = settings
    }

    public func load() -> TerminalSettings? {
        lock.lock(); defer { lock.unlock() }
        return stored
    }

    public func save(_ settings: TerminalSettings) {
        lock.lock(); defer { lock.unlock() }
        stored = settings
    }
}

/// Dépendances partagées par tous les écrans : API courante, terminal, opérateur, notifications.
@MainActor
@Observable
public final class POSContext {
    public internal(set) var api: POSAPI
    public internal(set) var terminalId: String
    public internal(set) var currentOperator: Operator?
    public var notice: Notice?

    /// Appelé quand le serveur refuse le jeton (session expirée) : l'app revient à l'écran PIN.
    @ObservationIgnored var onUnauthorized: (@MainActor () -> Void)?

    public init(api: POSAPI, terminalId: String, currentOperator: Operator? = nil) {
        self.api = api
        self.terminalId = terminalId
        self.currentOperator = currentOperator
    }

    public func notify(_ message: String, style: Notice.Style = .info) {
        notice = Notice(message, style: style)
    }

    /// Transforme une erreur en message et gère l'expiration de session.
    @discardableResult
    public func report(_ error: Error) -> String {
        if case APIError.unauthorized = error {
            onUnauthorized?()
        }
        let message = (error as? LocalizedError)?.errorDescription ?? error.localizedDescription
        notify(message, style: .error)
        return message
    }
}
