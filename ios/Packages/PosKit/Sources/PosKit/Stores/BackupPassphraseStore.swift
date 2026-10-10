import Foundation
import Security

/// Phrase de sauvegarde mémorisée pour la sauvegarde automatique.
@MainActor
public protocol BackupPassphraseStore: AnyObject {
    func load() -> String?
    @discardableResult func save(_ passphrase: String) -> Bool
    func clear()
}

/// Tests unitaires, tests UI et aperçus.
@MainActor
public final class InMemoryBackupPassphraseStore: BackupPassphraseStore {
    private var value: String?
    public init(_ value: String? = nil) { self.value = value }
    public func load() -> String? { value }
    @discardableResult public func save(_ passphrase: String) -> Bool { value = passphrase; return true }
    public func clear() { value = nil }
}

/// Trousseau iOS, propre à cet appareil : la phrase ne part ni dans une sauvegarde de l'appareil ni vers iCloud.
@MainActor
public final class KeychainBackupPassphraseStore: BackupPassphraseStore {
    private let service = "com.restaurantpos.backup"
    private let account = "passphrase"

    public init() {}

    private var baseQuery: [String: Any] {
        [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: service, kSecAttrAccount as String: account]
    }

    public func load() -> String? {
        var query = baseQuery
        query[kSecReturnData as String] = true
        query[kSecMatchLimit as String] = kSecMatchLimitOne
        var item: CFTypeRef?
        guard SecItemCopyMatching(query as CFDictionary, &item) == errSecSuccess, let data = item as? Data else { return nil }
        return String(data: data, encoding: .utf8)
    }

    @discardableResult
    public func save(_ passphrase: String) -> Bool {
        SecItemDelete(baseQuery as CFDictionary)
        var query = baseQuery
        query[kSecValueData as String] = Data(passphrase.utf8)
        query[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
        return SecItemAdd(query as CFDictionary, nil) == errSecSuccess
    }

    public func clear() { SecItemDelete(baseQuery as CFDictionary) }
}
