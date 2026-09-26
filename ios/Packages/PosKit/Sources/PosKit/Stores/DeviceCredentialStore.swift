import Foundation
import Security

/// Stockage de l'identité du poste.
@MainActor
public protocol DeviceCredentialStore: AnyObject {
    func load() -> DeviceCredentials?
    func save(_ credentials: DeviceCredentials)
    func clear()
}

/// Tests unitaires, tests UI et aperçus.
@MainActor
public final class InMemoryCredentialStore: DeviceCredentialStore {
    private var value: DeviceCredentials?
    public init(_ value: DeviceCredentials? = nil) { self.value = value }
    public func load() -> DeviceCredentials? { value }
    public func save(_ credentials: DeviceCredentials) { value = credentials }
    public func clear() { value = nil }
}

/// Trousseau iOS : le jeton d'appareil ne doit jamais finir dans `UserDefaults`.
@MainActor
public final class KeychainCredentialStore: DeviceCredentialStore {
    private let service = "com.restaurantpos.device"
    private let account = "credentials"

    public init() {}

    private var baseQuery: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword,
         kSecAttrService as String: service,
         kSecAttrAccount as String: account]
    }

    public func load() -> DeviceCredentials? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return try? JSONDecoder().decode(DeviceCredentials.self, from: data)
    }

    public func save(_ credentials: DeviceCredentials) {
        guard let data = try? JSONEncoder().encode(credentials) else { return }
        SecItemDelete(baseQuery as CFDictionary)
        var query = baseQuery
        query[kSecValueData as String] = data
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        SecItemAdd(query as CFDictionary, nil)
    }

    public func clear() { SecItemDelete(baseQuery as CFDictionary) }
}
