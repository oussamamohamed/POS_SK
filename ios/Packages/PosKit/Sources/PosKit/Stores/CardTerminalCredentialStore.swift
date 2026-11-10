import Foundation
import Security

/// Lecteur Stripe Terminal de ce poste : clé **restreinte** (`rk_…`) et identifiant du lecteur (`tmr_…`).
public struct StripeReaderSettings: Codable, Equatable, Sendable {
    public var secretKey: String
    public var readerId: String

    public init(secretKey: String, readerId: String) {
        self.secretKey = secretKey
        self.readerId = readerId
    }

    /// Une clé secrète complète (`sk_…`) donnerait tous les droits Stripe à quiconque ouvre l'iPad : seules les clés restreintes sont acceptées.
    public var isValid: Bool {
        secretKey.hasPrefix("rk_") && secretKey.count > 12 && readerId.hasPrefix("tmr_") && readerId.count > 5
    }

    public var terminal: StripeCardTerminal { StripeCardTerminal(secretKey: secretKey, readerId: readerId) }
}

@MainActor
public protocol CardTerminalCredentialStore: AnyObject {
    func load() -> StripeReaderSettings?
    @discardableResult func save(_ settings: StripeReaderSettings) -> Bool
    func clear()
}

/// Tests unitaires, tests UI et aperçus.
@MainActor
public final class InMemoryCardTerminalCredentialStore: CardTerminalCredentialStore {
    private var value: StripeReaderSettings?
    public init(_ value: StripeReaderSettings? = nil) { self.value = value }
    public func load() -> StripeReaderSettings? { value }
    @discardableResult public func save(_ settings: StripeReaderSettings) -> Bool { value = settings; return true }
    public func clear() { value = nil }
}

/// Trousseau iOS, propre à cet appareil : la clé ne part ni dans une sauvegarde de l'appareil ni vers iCloud.
@MainActor
public final class KeychainCardTerminalCredentialStore: CardTerminalCredentialStore {
    private let service = "com.restaurantpos.cardterminal"
    private let account = "stripe-reader"

    public init() {}

    private var baseQuery: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }

    public func load() -> StripeReaderSettings? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return try? JSONDecoder().decode(StripeReaderSettings.self, from: data)
    }

    @discardableResult
    public func save(_ settings: StripeReaderSettings) -> Bool {
        guard let data = try? JSONEncoder().encode(settings) else { return false }
        SecItemDelete(baseQuery as CFDictionary)
        var query = baseQuery
        query[kSecValueData as String] = data
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        return SecItemAdd(query as CFDictionary, nil) == errSecSuccess
    }

    public func clear() { SecItemDelete(baseQuery as CFDictionary) }
}
