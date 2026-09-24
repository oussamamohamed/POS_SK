import Foundation
import Observation

/// Résultat d'un encaissement terminé, affiché sur l'écran de confirmation.
public struct CheckoutReceipt: Hashable, Sendable {
    public var receiptNumbers: [String]
    public var totalPaid: Money
    public var changeGiven: Money
    public var fiscalSignature: String?
    public var pickupNumber: String?
    public var creditVoucher: CreditVoucher?
}

/// Encaissement d'une note : moyens de paiement, rendu monnaie, partage à parts égales.
///
/// - En salle, chaque règlement est envoyé immédiatement (`/api/checkout/pay`) et le serveur
///   renvoie le reste à payer : on peut donc régler convive par convive.
/// - Au comptoir, les règlements sont cumulés localement puis envoyés en une seule fois
///   (`/api/orders/counter/checkout`), qui attribue le numéro de retrait.
@MainActor
@Observable
public final class CheckoutModel {
    public enum Phase: Hashable, Sendable {
        case entering
        case processing
        case completed(CheckoutReceipt)
    }

    public let context: OrderContext
    public let orderId: UUID
    public let orderTotal: Money
    public private(set) var remaining: Money
    public private(set) var phase: Phase = .entering
    public var method: PaymentMethod = .cash {
        didSet { if oldValue != method { entry.clear() } }
    }
    public var entry = AmountEntry()
    public private(set) var splitCount = 1
    private var splitParts: [Money] = []
    public private(set) var paidParts = 0
    /// Règlements comptoir en attente d'envoi.
    public private(set) var counterTenders: [CounterTender] = []
    public var pickupBuzzer = ""
    public var mealVoucherPolicy: MealVoucherPolicy = .capAtBalance
    public let destination: OrderDestination

    private var receiptNumbers: [String] = []
    private var paidTotal: Money = .zero
    private var lastChange: Money = .zero
    private var lastSignature: String?
    private let ctx: POSContext

    public init(context: OrderContext, order: ActiveOrder, destination: OrderDestination, posContext: POSContext) {
        self.context = context
        self.orderId = order.orderId
        self.orderTotal = order.totalTtcAmount
        self.remaining = order.totalTtcAmount
        self.destination = destination
        self.ctx = posContext
    }

    // MARK: Partage

    public var isSplit: Bool { splitCount > 1 }
    public var parts: [Money] { splitParts }

    /// Partage le reste à payer en `count` parts égales (au centime près).
    public func split(into count: Int) {
        let clamped = max(1, min(count, 20))
        splitCount = clamped
        paidParts = 0
        splitParts = clamped > 1 ? SplitCalculator.equalParts(of: remaining, count: clamped) : []
        entry.clear()
    }

    // MARK: Montants

    /// Montant attendu pour ce règlement : la part du convive courant ou tout le reste.
    public var amountDue: Money {
        if isSplit, paidParts < splitParts.count {
            return Money.min(splitParts[paidParts], remaining)
        }
        return remaining
    }

    /// Montant remis par le client (par défaut : exactement le montant attendu).
    public var tendered: Money { entry.isEmpty ? amountDue : entry.amount }

    /// Part du montant remis imputée sur la note.
    public var applied: Money { Money.min(tendered, amountDue) }

    /// Rendu monnaie : uniquement en espèces.
    public var change: Money {
        method.allowsOverpayment ? Money.max(.zero, tendered - amountDue) : .zero
    }

    /// Un titre-restaurant peut dépasser le dû (surplus géré par la politique choisie), pas une carte.
    public var overpaymentNotAllowed: Bool {
        tendered > amountDue && !method.allowsOverpayment && method != .mealVoucher
    }

    public var cashSuggestions: [Money] { CashSuggestions.amounts(for: amountDue) }

    public var canSubmit: Bool {
        phase == .entering && tendered.isPositive && amountDue.isPositive && !overpaymentNotAllowed
    }

    public var counterTendersTotal: Money { counterTenders.reduce(.zero) { $0 + $1.amount } }

    // MARK: Validation

    public func submit() async {
        guard canSubmit else { return }
        switch context {
        case .table:
            await payTable()
        case .counter:
            await addCounterTender()
        }
    }

    private func payTable() async {
        phase = .processing
        let tender = TenderItem(method: method, amount: applied, tendered: tendered, changeGiven: change)
        let request = PaymentSettlementRequest(
            orderId: orderId,
            tableNumber: context.tableNumber,
            operatorId: ctx.currentOperator?.id,
            tenders: [tender],
            terminalId: ctx.terminalId
        )
        do {
            let result = try await ctx.api.pay(request)
            if let number = result.receiptNumber { receiptNumbers.append(number) }
            paidTotal += result.totalPaid
            lastChange = result.changeGiven
            lastSignature = result.fiscalSignature ?? lastSignature
            remaining = Money.max(.zero, result.remainingBalance)
            advanceSplit()
            entry.clear()
            if remaining.isPositive {
                phase = .entering
                let changeText = result.changeGiven.isPositive ? " — Rendu \(result.changeGiven.formatted)" : ""
                ctx.notify("Règlement de \(result.totalPaid.formatted) enregistré\(changeText). Reste \(remaining.formatted).", style: .success)
            } else {
                phase = .completed(CheckoutReceipt(
                    receiptNumbers: receiptNumbers,
                    totalPaid: paidTotal,
                    changeGiven: lastChange,
                    fiscalSignature: lastSignature,
                    pickupNumber: nil,
                    creditVoucher: nil
                ))
            }
        } catch {
            phase = .entering
            ctx.report(error)
        }
    }

    private func addCounterTender() async {
        let facial: Money? = method == .mealVoucher ? tendered : nil
        let tender = CounterTender(method: method, amount: applied, tendered: tendered, facialValue: facial)
        counterTenders.append(tender)
        lastChange = change
        remaining = Money.max(.zero, remaining - applied)
        advanceSplit()
        entry.clear()
        if remaining.isPositive {
            ctx.notify("\(method.label) : \(applied.formatted) ajouté. Reste \(remaining.formatted).", style: .info)
        } else {
            await submitCounter()
        }
    }

    /// Envoie les règlements comptoir cumulés (peut être relancé après une erreur réseau).
    public func submitCounter() async {
        guard context == .counter, !counterTenders.isEmpty, !remaining.isPositive else { return }
        phase = .processing
        let buzzer = pickupBuzzer.trimmingCharacters(in: .whitespaces)
        let request = CounterCheckoutRequest(
            orderId: orderId,
            terminalId: ctx.terminalId,
            destination: destination,
            pickupBuzzer: buzzer.isEmpty ? nil : buzzer,
            mealVoucherPolicy: mealVoucherPolicy,
            tenders: counterTenders
        )
        do {
            let result = try await ctx.api.counterCheckout(request)
            phase = .completed(CheckoutReceipt(
                receiptNumbers: result.receiptNumber.map { [$0] } ?? [],
                totalPaid: result.totalPaid,
                changeGiven: result.changeGiven,
                fiscalSignature: result.fiscalSignature,
                pickupNumber: result.pickupNumber,
                creditVoucher: result.issuedCreditVoucher
            ))
        } catch {
            // Rien n'a été encaissé : on rend la main avec les règlements annulés.
            counterTenders.removeAll()
            remaining = orderTotal
            if isSplit { split(into: splitCount) }
            phase = .entering
            ctx.report(error)
        }
    }

    /// Annule les règlements comptoir saisis (non encore envoyés).
    public func resetCounterTenders() {
        guard context == .counter, phase == .entering else { return }
        counterTenders.removeAll()
        remaining = orderTotal
        if isSplit { split(into: splitCount) }
    }

    private func advanceSplit() {
        guard isSplit else { return }
        paidParts = min(paidParts + 1, splitParts.count)
        if paidParts >= splitParts.count, remaining.isPositive {
            // Paiement partiel inférieur à la part : le reliquat devient une part supplémentaire.
            splitParts.append(remaining)
        }
    }

    public var isCompleted: Bool {
        if case .completed = phase { return true }
        return false
    }
}

/// Permet de présenter l'encaissement avec `.fullScreenCover(item:)`.
extension CheckoutModel: Identifiable {}
