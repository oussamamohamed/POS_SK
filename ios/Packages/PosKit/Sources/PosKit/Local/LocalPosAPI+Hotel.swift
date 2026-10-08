import Foundation

extension LocalPosAPI {
    /// Chambres occupées, par numéro (`GET /api/hotel/rooms`).
    public func hotelRooms() async throws -> [HotelRoom] {
        try requireAuth()
        return try hotelRepository.occupiedRooms()
    }

    /// Facture une commande sur une chambre. Refusée si la chambre est inconnue ou libre, si le plafond de crédit serait dépassé, ou si le
    /// montant n'est pas exactement le solde de la commande (écart assumé : le .NET ne contrôle pas le montant). La commande est clôturée
    /// et sa table libérée (le .NET laisse la commande ouverte).
    public func chargeRoom(_ request: RoomChargeRequest) async throws -> OperationResult {
        try requireAuth()
        let failed = APIError.server(status: 400, message: "Facturation chambre échouée.")
        guard request.amount > .zero, request.tipAmount >= .zero else { throw failed }
        let floor = floorRepository, orders = orderRepository, hotel = hotelRepository, holds = holdRepository
        return try db.transaction { () throws -> OperationResult in
            // Repli sur la commande de la table seulement sans `orderId` : un identifiant inconnu est refusé.
            guard let orderId = try request.orderId ?? floor.table(request.tableNumber)?.activeOrderId,
                  let order = try orders.order(id: orderId), try orders.status(of: orderId)?.isModifiable == true
            else { throw failed }
            guard try !holds.hasActiveHold(orderId: orderId) else { throw APIError.localOrderHeld }
            let (total, _, paid) = try balance(of: order)
            guard request.amount == total - paid else { throw failed }

            guard let room = try hotel.occupiedRoom(request.roomNumber) else {
                throw APIError.server(status: 400, message: "Chambre \(request.roomNumber) introuvable ou non occupée.")
            }
            let charge = request.amount + request.tipAmount
            guard room.currentBalance + charge <= room.maxCreditLimit else {
                throw APIError.server(status: 400, message: "Plafond de crédit chambre dépassé.")
            }
            let guest = request.guestName.trimmingCharacters(in: .whitespacesAndNewlines)
            try hotel.addToBalance(roomNumber: request.roomNumber, charge)
            try hotel.insertCharge(
                orderId: orderId, roomNumber: room.roomNumber, guestName: guest, amount: request.amount, tip: request.tipAmount,
                signature: request.signatureDataUrl, notes: request.notes
            )
            try closeOrder(order, as: .paid)
            return OperationResult(success: true, message: "Facturation de \(LocalMealVoucher.format(charge)) € enregistrée sur la chambre \(room.roomNumber) (\(guest))")
        }
    }
}
