import Foundation
import os

extension LocalPosAPI {
    /// Attente maximale de la décision du TPE (comme `CardTerminal:TimeoutSeconds` côté serveur) ; l'appel réseau de l'iPad n'a pas d'autre délai.
    static let cardTimeout: Duration = .seconds(60)
    static let cardLog = Logger(subsystem: "RestaurantPOS", category: "TPE")

    private struct CardTimeout: Error {}

    static func cardRefusal(_ message: String) -> APIError { .server(status: 402, message: message) }

    /// Envoie au TPE le montant des règlements par carte et attend la décision, avant toute écriture de reçu. Renvoie la référence du TPE,
    /// ou `nil` si rien n'est à débiter (aucun TPE configuré, pas de carte, commande introuvable : la transaction refusera alors d'elle-même).
    /// Refus, délai ou terminal indisponible : lève une erreur `402`, rien n'est écrit, l'opérateur relance. `dueTip` donne le pourboire
    /// dû une fois ce paiement enregistré (ajouté au pourboire existant à table, remplaçant au comptoir) : une carte ne dépasse jamais le dû,
    /// sinon le TPE débiterait un règlement que l'encaissement refuserait ensuite.
    func chargeCard(_ tenders: [(method: PaymentMethod, amount: Money)], orderId: UUID?, dueTip: (Money) -> Money) async throws -> String? {
        let cents = tenders.filter { $0.method == .creditCard }.reduce(0) { $0 + $1.amount.cents }
        guard let terminal = cardTerminal, cents > 0, let orderId, let order = try orderRepository.order(id: orderId),
              try orderRepository.status(of: orderId)?.isModifiable == true
        else { return nil }
        let (total, tip, paid) = try balance(of: order)
        guard Money(cents: cents) <= (total + dueTip(tip) - paid).clampedAtZero() else { throw Self.paymentFailed }
        guard !cardCharging else { throw Self.cardRefusal("Le terminal de paiement est occupé par un autre paiement.") }
        cardCharging = true
        defer { cardCharging = false }

        let reference = "\(orderId.uuidString)-\(UUID().uuidString)"
        let result: CardChargeResult
        do {
            result = try await Self.withTimeout(Self.cardTimeout) { try await terminal.charge(amount: Money(cents: cents), reference: reference) }
        } catch is CardTimeout {
            throw Self.cardRefusal("Le terminal de paiement n'a pas répondu à temps. Rien n'a été débité.")
        }
        switch result.status {
        case .approved: return result.reference ?? reference
        case .declined: throw Self.cardRefusal("Carte refusée.")
        case .unavailable: throw Self.cardRefusal("Terminal de paiement injoignable.")
        }
    }

    /// Carte débitée mais reçu non écrit (conflit résiduel) : à rembourser à la main depuis le tableau de bord du fournisseur.
    func logCardNotRecorded(_ reference: String?, orderId: UUID?) {
        guard let reference else { return }
        Self.cardLog.error("Carte débitée sans reçu : commande \(orderId?.uuidString ?? "?", privacy: .public), référence TPE \(reference, privacy: .public). Rembourser le client.")
    }

    private static func withTimeout<T: Sendable>(_ limit: Duration, _ operation: @escaping @Sendable () async throws -> T) async throws -> T {
        try await withThrowingTaskGroup(of: T?.self) { group in
            group.addTask { try await operation() }
            group.addTask {
                try await Task.sleep(for: limit)
                return nil
            }
            defer { group.cancelAll() }
            guard let first = try await group.next() else { throw CancellationError() }
            guard let value = first else { throw CardTimeout() }
            return value
        }
    }
}
