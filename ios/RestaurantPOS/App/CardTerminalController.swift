import Foundation
import Observation
import PosKit

/// TPE du mode autonome : lit le lecteur Stripe mémorisé et le branche sur `LocalPosAPI` (aucun lecteur = carte saisie à la main).
@MainActor @Observable
final class CardTerminalController {
    private(set) var settings: StripeReaderSettings?
    @ObservationIgnored private let api: LocalPosAPI
    @ObservationIgnored private let store: CardTerminalCredentialStore

    init(api: LocalPosAPI, store: CardTerminalCredentialStore) {
        self.api = api
        self.store = store
        settings = store.load().flatMap { $0.isValid ? $0 : nil }
        apply()
    }

    var isConfigured: Bool { settings != nil }

    /// `false` si la clé n'est pas une clé restreinte ou si le Trousseau refuse l'écriture : rien n'est alors branché.
    @discardableResult
    func save(secretKey: String, readerId: String) -> Bool {
        let candidate = StripeReaderSettings(
            secretKey: secretKey.trimmingCharacters(in: .whitespacesAndNewlines), readerId: readerId.trimmingCharacters(in: .whitespacesAndNewlines)
        )
        guard candidate.isValid, store.save(candidate) else { return false }
        settings = candidate
        apply()
        return true
    }

    func clear() {
        store.clear()
        settings = nil
        apply()
    }

    private func apply() {
        let terminal: (any CardTerminal)? = settings?.terminal
        Task { [api] in await api.setCardTerminal(terminal) }
    }
}
