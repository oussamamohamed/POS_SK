import Foundation

extension LocalOrderStatus {
    /// Une commande ouverte, envoyée en cuisine ou en attente d'addition peut encore être modifiée ; réglée ou annulée, non.
    var isModifiable: Bool { self == .open || self == .sentToKitchen || self == .billRequested }
}

extension APIError {
    /// Opération refusée parce que la commande est déjà réglée ou annulée.
    static let localOrderClosed = APIError.server(status: 409, message: "Commande déjà réglée ou annulée.")
    /// Encaissement refusé tant que la commande est en attente (elle doit d'abord être rappelée).
    static let localOrderHeld = APIError.server(status: 409, message: "Commande en attente : rappelez-la avant de l'encaisser.")
    /// La note est figée dès le premier règlement : plus de remise, de retrait de remise ni de gratuité.
    static let localOrderPartiallyPaid = APIError.server(status: 409, message: "Remise ou gratuité impossible : la commande est déjà partiellement réglée.")
}

extension LocalOrderRepository {
    func status(of id: UUID) throws -> LocalOrderStatus? {
        try db.query("SELECT Status FROM Orders WHERE Id = ?", [.uuid(id)]).first?.int("Status").flatMap { LocalOrderStatus(rawValue: $0) }
    }

    func tipCents(of id: UUID) throws -> Int {
        try db.query("SELECT TipAmount FROM Orders WHERE Id = ?", [.uuid(id)]).first?.int("TipAmount") ?? 0
    }

    func setTip(orderId: UUID, cents: Int) throws {
        try db.run("UPDATE Orders SET TipAmount = ? WHERE Id = ?", [.integer(cents), .uuid(orderId)])
    }

    /// Numéro de retrait, bip et destination d'une vente au comptoir.
    func setPickup(orderId: UUID, number: String, buzzer: String?, destination: OrderDestination) throws {
        try db.run(
            "UPDATE Orders SET PickupNumber = ?, PickupBuzzer = ?, Destination = ? WHERE Id = ?",
            [.text(number), .string(buzzer), .integer(destination.rawValue), .uuid(orderId)]
        )
    }

    /// Lignes payables en titres-restaurant (`OrderItems.IsFoodVoucherEligible`).
    func foodVoucherEligibleLineIds(orderId: UUID) throws -> Set<UUID> {
        Set(try db.query("SELECT Id FROM OrderItems WHERE OrderId = ? AND IsFoodVoucherEligible = 1", [.uuid(orderId)]).compactMap { $0.uuid("Id") })
    }
}
