import Foundation

/// Règle des titres-restaurant (`MealVoucherPolicyService`) : plafond légal journalier et surpaiement.
enum LocalMealVoucher {
    static let dailyCap = Money(cents: 2500)

    struct Outcome {
        let error: String?
        let surplus: Money
    }

    /// `eligibleSubtotal` : total TTC des lignes payables en titres-restaurant ; `orderTotal` : total TTC de la commande.
    static func validate(eligibleSubtotal: Money, orderTotal: Money, amount: Money, facialValue: Money?, policy: MealVoucherPolicy) -> Outcome {
        let legalMax = min(eligibleSubtotal, dailyCap)
        if amount > legalMax {
            return Outcome(error: "Le montant par Titre-Restaurant (\(format(amount)) €) dépasse le plafond légal éligible (\(format(legalMax)) €).", surplus: .zero)
        }
        if let face = facialValue, face > orderTotal {
            let surplus = face - orderTotal
            switch policy {
            case .strictRejection:
                return Outcome(error: "Surpaiement par Titre-Restaurant refusé : la valeur faciale (\(format(face)) €) dépasse le solde dû (\(format(orderTotal)) €).", surplus: surplus)
            case .customerCreditVoucher:
                return Outcome(error: nil, surplus: surplus)
            case .capAtBalance:
                return Outcome(error: nil, surplus: .zero)
            }
        }
        return Outcome(error: nil, surplus: .zero)
    }

    /// `19.50` : deux décimales, point décimal (comme le serveur).
    static func format(_ amount: Money) -> String {
        String(format: "%d.%02d", amount.cents / 100, amount.cents % 100)
    }
}

extension LocalPosAPI {
    /// Encaisse une vente au comptoir ou à emporter. Tous les refus (panier vide, titre-restaurant hors règle) précèdent la première
    /// écriture : un refus ne consomme ni numéro de retrait ni avoir (le .NET attribue d'abord le numéro).
    public func counterCheckout(_ request: CounterCheckoutRequest) async throws -> CounterCheckoutResult {
        try requireAuth()
        guard request.tipAmount >= .zero else { throw APIError.server(status: 400, message: "Montant de pourboire invalide.") }
        let orders = orderRepository, payments = paymentRepository, holds = holdRepository
        return try db.transaction { () throws -> CounterCheckoutResult in
            guard let order = try orders.order(id: request.orderId), !order.lines.isEmpty,
                  try orders.status(of: request.orderId)?.isModifiable == true
            else { throw APIError.server(status: 400, message: "Commande introuvable ou panier vide.") }
            guard try !holds.hasActiveHold(orderId: order.orderId) else { throw APIError.localOrderHeld }
            let terminal = normalizedTerminal(request.terminalId)

            var surplus = Money.zero
            if let voucher = request.tenders.first(where: { $0.method == .mealVoucher }) {
                let eligibleIds = try orders.foodVoucherEligibleLineIds(orderId: order.orderId)
                let eligible = order.lines.filter { eligibleIds.contains($0.lineId) }
                    .reduce(Money.zero) { $0 + OrderMath.lineTotal(CartLine(serverLine: $1)) }
                let outcome = LocalMealVoucher.validate(
                    eligibleSubtotal: eligible, orderTotal: decorate(order).totalTtcAmount ?? .zero, amount: voucher.amount,
                    facialValue: voucher.facialValue, policy: request.mealVoucherPolicy
                )
                if let message = outcome.error { throw APIError.server(status: 400, message: message) }
                if request.mealVoucherPolicy == .customerCreditVoucher { surplus = outcome.surplus }
            }

            let pickup = try payments.nextPickupNumber(terminalId: terminal, now: Date())
            try orders.setPickup(orderId: order.orderId, number: pickup, buzzer: request.pickupBuzzer, destination: request.destination)
            if request.tipAmount > .zero { try orders.setTip(orderId: order.orderId, cents: request.tipAmount.cents) }

            var issued: CreditVoucher?
            if surplus > .zero {
                let code = "CR-" + String(UUID().uuidString.replacingOccurrences(of: "-", with: "").prefix(8)).uppercased()
                let expiry = Date().addingTimeInterval(90 * 86_400)
                try payments.insertCreditVoucher(code: code, orderId: order.orderId, terminalId: terminal, amount: surplus, expiresAt: expiry)
                issued = CreditVoucher(voucherCode: code, amount: surplus, expiresAtUtc: expiry)
            }

            guard let current = try orders.order(id: order.orderId) else { throw SQLiteError(code: -1, message: "Commande introuvable après mise à jour") }
            let tenders = request.tenders.map { (method: $0.method, amount: $0.amount, tendered: $0.tendered) }
            let settlement = try settle(order: current, tenders: tenders, terminalId: terminal)
            return CounterCheckoutResult(
                orderId: order.orderId, pickupNumber: pickup, totalPaid: settlement.totalPaid, changeGiven: settlement.changeGiven,
                remainingBalance: settlement.remaining, receiptNumber: settlement.receiptNumber, fiscalSignature: nil, issuedCreditVoucher: issued,
                openCashDrawer: request.tenders.contains { $0.method == .cash }, printQueued: false
            )
        }
    }
}
