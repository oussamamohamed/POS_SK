import Foundation
@testable import PosKit

/// Règlement d'un moyen de paiement (`tendered` = montant remis par le client, par défaut égal au montant).
func localTender(_ method: PaymentMethod = .cash, _ amount: Int, tendered: Int? = nil) -> TenderInput {
    TenderInput(method: method, amount: Money(cents: amount), tendered: Money(cents: tendered ?? amount), changeGiven: .zero)
}

func localPayment(_ order: ActiveOrder?, table: String, tenders: [TenderInput], tip: Int = 0, terminal: String = "T01") -> PaymentRequest {
    PaymentRequest(orderId: order?.orderId, tableNumber: table, operatorId: nil, terminalId: terminal, tenders: tenders, requestReceiptPrint: false, tipAmount: Money(cents: tip))
}
