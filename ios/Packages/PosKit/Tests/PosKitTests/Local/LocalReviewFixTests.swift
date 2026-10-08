import Foundation
import Testing
@testable import PosKit

/// Scénarios inter-tâches relevés par la revue finale du plan 1c (fusion, attente, remise et rendu après un règlement).
@Suite("LocalPosAPI : règlements partiels, attente et clôture")
struct LocalReviewFixTests {
    private static let held = APIError.server(status: 409, message: "Commande en attente : rappelez-la avant de l'encaisser.")
    private static let partiallyPaidHold = APIError.server(status: 409, message: "Impossible de mettre en attente une commande déjà partiellement réglée.")
    private static let partiallyPaidEdit = APIError.server(status: 409, message: "Remise ou gratuité impossible : la commande est déjà partiellement réglée.")

    private func seat(_ api: LocalPosAPI, _ table: String, _ name: String = "Burger Gourmet Rossini", quantity: Int = 1) async throws -> ActiveOrder {
        let product = try await localProduct(api, name)
        try await seatTable(api, table, items: [localInput(product, quantity: quantity)])
        return try #require(try await api.activeOrder(table: table))
    }

    private func counterCart(_ api: LocalPosAPI, quantity: Int = 1) async throws -> ActiveOrder {
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        _ = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        return try await api.addItems(table: "Comptoir", items: [localInput(burger, quantity: quantity)])
    }

    private func checkout(_ id: UUID, _ tenders: [CounterTender], policy: MealVoucherPolicy = .capAtBalance) -> CounterCheckoutRequest {
        CounterCheckoutRequest(
            orderId: id, terminalId: "T01", destination: .takeaway, pickupBuzzer: nil, tipAmount: .zero,
            requestFiscalReceiptPrint: false, mealVoucherPolicy: policy, tenders: tenders
        )
    }

    private func roomCharge(_ id: UUID?, table: String, amount: Int) -> RoomChargeRequest {
        RoomChargeRequest(
            orderId: id, tableNumber: table, roomNumber: "101", guestName: "Jean Dujardin", amount: Money(cents: amount),
            tipAmount: .zero, signatureDataUrl: nil, notes: nil
        )
    }

    // C1
    @Test func aPartiallyPaidTableCannotBeMergedAway() async throws {
        let api = try await makeLocalAPI()
        let source = try await seat(api, "T1")                     // 19,50 €
        let target = try await seat(api, "T2", "Café Gourmand")
        _ = try await api.pay(localPayment(source, table: "T1", tenders: [localTender(.creditCard, 1000)]))

        let result = try await api.transfer(from: "T1", to: "T2", merge: true)
        #expect(result.success == false && result.message == "Échec de la fusion de tables.")
        #expect(try await api.activeOrder(table: "T1")?.orderId == source.orderId)
        #expect(try await api.activeOrder(table: "T2")?.lines.map(\.productName) == ["Café Gourmand"])
        #expect(try await api.activeOrder(table: "T2")?.orderId == target.orderId)
        // Le règlement de 10,00 € est resté sur la commande source : il ne reste que 9,50 € à régler.
        let rest = try await api.pay(localPayment(source, table: "T1", tenders: [localTender(.cash, 950)]))
        #expect(rest.remainingBalance == .zero)
    }

    // C1 (contrôle) : le transfert simple vers une table libre reste possible et garde les règlements.
    @Test func aPartiallyPaidOrderCanStillMoveToAFreeTable() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api, "T1")
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1000)]))
        #expect(try await api.transfer(from: "T1", to: "T3", merge: false).success == true)
        await #expect(throws: APIError.server(status: 400, message: "Échec de l'encaissement.")) {
            try await api.pay(localPayment(nil, table: "T3", tenders: [localTender(.cash, 1950)]))
        }
        let rest = try await api.pay(localPayment(nil, table: "T3", tenders: [localTender(.cash, 950)]))
        #expect(rest.remainingBalance == .zero)
        #expect(try await api.tables().first { $0.tableNumber == "T3" }?.status == .free)
    }

    // I1
    @Test func aHeldOrderCannotBeSettledUntilRecalled() async throws {
        let api = try await makeLocalAPI()
        let cart = try await counterCart(api)                     // 19,50 €
        try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "Dupont")

        await #expect(throws: Self.held) { try await api.counterCheckout(checkout(cart.orderId, [CounterTender(method: .cash, amount: Money(cents: 1950), tendered: Money(cents: 1950))])) }
        await #expect(throws: Self.held) { try await api.pay(localPayment(cart, table: "Comptoir", tenders: [localTender(.cash, 1950)])) }
        await #expect(throws: Self.held) { try await api.chargeRoom(roomCharge(cart.orderId, table: "Comptoir", amount: 1950)) }
        #expect(try await api.hotelRooms().allSatisfy { $0.currentBalance == .zero })

        // Rien n'a été écrit : la mise en attente se rappelle puis s'encaisse avec le premier numéro de retrait et de reçu.
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)
        _ = try await api.recallHeldOrder(holdId: hold.holdId)
        let result = try await api.counterCheckout(checkout(cart.orderId, [CounterTender(method: .cash, amount: Money(cents: 1950), tendered: Money(cents: 1950))]))
        #expect(result.pickupNumber == "#A-01" && result.receiptNumber == "NF-T01-000001" && result.remainingBalance == .zero)
    }

    // I1 : une mise en attente dont la commande n'est plus modifiable (données antérieures au correctif) n'est pas annulée.
    @Test func voidingAHoldOfAClosedOrderIsRefused() async throws {
        let path = temporaryDatabasePath()
        let api = try LocalPosAPI(path: path)
        _ = try await api.login(pin: "1234")
        let cart = try await counterCart(api)
        try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "Dupont")
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)
        let side = try SQLiteDatabase(path: path)
        try side.run("UPDATE Orders SET Status = ? WHERE Id = ?", [.integer(LocalOrderStatus.paid.rawValue), .uuid(cart.orderId)])

        await #expect(throws: APIError.localOrderClosed) {
            try await api.voidHeldOrder(holdId: hold.holdId, supervisorPin: "1234", reason: "Client parti", terminalId: "T01")
        }
        #expect(try await api.heldOrders(terminalId: "T01").count == 1)
        #expect(try side.query("SELECT Status FROM Orders WHERE Id = ?", [.uuid(cart.orderId)]).first?.int("Status") == LocalOrderStatus.paid.rawValue)
    }

    // I2
    @Test func aPartiallyPaidOrderCannotBeHeld() async throws {
        let api = try await makeLocalAPI()
        let cart = try await counterCart(api, quantity: 2)         // 39,00 €
        let partial = try await api.counterCheckout(checkout(cart.orderId, [CounterTender(method: .creditCard, amount: Money(cents: 500), tendered: Money(cents: 500))]))
        #expect(partial.remainingBalance == Money(cents: 3400))
        await #expect(throws: Self.partiallyPaidHold) { try await api.holdOrder(orderId: cart.orderId, terminalId: "T01", label: "Dupont") }
        #expect(try await api.heldOrders(terminalId: "T01").isEmpty)
        #expect(try await api.activeOrder(table: "Comptoir")?.orderId == cart.orderId)
    }

    // I3 (partie retenue)
    @Test func discountsAndCompsAreRefusedAfterAPartialPayment() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api, "T1", quantity: 2)          // 39,00 €
        try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 10, reason: "Fidélité", operatorId: nil)  // 35,10 €
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.creditCard, 1000)]))
        let line = try #require(order.lines.first)

        await #expect(throws: Self.partiallyPaidEdit) { try await api.applyDiscount(orderId: order.orderId, type: .percentage, value: 50, reason: "Geste", operatorId: nil) }
        await #expect(throws: Self.partiallyPaidEdit) { try await api.removeDiscount(orderId: order.orderId) }
        await #expect(throws: Self.partiallyPaidEdit) { try await api.compItem(orderId: order.orderId, lineId: line.lineId, reason: "Geste", operatorId: nil) }
        // La note n'a pas bougé : il reste exactement 25,10 € à régler.
        let rest = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 2510)]))
        #expect(rest.remainingBalance == .zero)
    }

    // I4
    @Test func recallingADiningOrderMovesItToTheCounterAndFreesItOnCheckout() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api, "T4")
        try await api.holdOrder(orderId: order.orderId, terminalId: "T01", label: "Salle")
        let hold = try #require(try await api.heldOrders(terminalId: "T01").first)
        let recalled = try await api.recallHeldOrder(holdId: hold.holdId)
        #expect(recalled.orderId == order.orderId && recalled.tableNumber == "Comptoir")

        _ = try await api.counterCheckout(checkout(order.orderId, [CounterTender(method: .cash, amount: Money(cents: 1950), tendered: Money(cents: 1950))]))
        #expect(try await api.tables().first { $0.isCounter }?.status == .free)
        // Le comptoir n'est pas bloqué : un nouveau panier s'ouvre et se rappelle normalement.
        let next = try await api.openCounterOrder(terminalId: "T01", destination: .takeaway)
        #expect(next.orderId != order.orderId && next.lines.isEmpty)
    }

    // I4 : la clôture libère la table qui pointe sur la commande, quel que soit son TableNumber.
    @Test func closingAnOrderFreesTheTableThatPointsToIt() async throws {
        let path = temporaryDatabasePath()
        let api = try LocalPosAPI(path: path)
        _ = try await api.login(pin: "1234")
        let order = try await seat(api, "T1")
        let side = try SQLiteDatabase(path: path)
        try side.run("UPDATE Orders SET TableNumber = 'T9' WHERE Id = ?", [.uuid(order.orderId)])

        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, 1950)]))
        let table = try #require(try await api.tables().first { $0.tableNumber == "T1" })
        #expect(table.status == .free && table.activeOrderId == nil)
    }

    // I5
    @Test func changeIsOnlyGivenBackOnCash() async throws {
        let api = try await makeLocalAPI()
        // Carte saisie au-delà du montant : aucun rendu.
        let card = try await seat(api, "T1")
        let byCard = try await api.pay(localPayment(card, table: "T1", tenders: [localTender(.creditCard, 1950, tendered: 2000)]))
        #expect(byCard.changeGiven == .zero)
        // Espèces partielles : 15,00 € remis pour 10,00 € → 5,00 € rendus (comme la ligne stockée).
        let split = try await seat(api, "T2")
        let partial = try await api.pay(localPayment(split, table: "T2", tenders: [localTender(.cash, 1000, tendered: 1500)]))
        #expect(partial.changeGiven == Money(cents: 500) && partial.remainingBalance == Money(cents: 950))
        // Mélange carte (sur-saisie) + espèces : seul le rendu espèces compte.
        let mixed = try await seat(api, "T3")
        let both = try await api.pay(localPayment(mixed, table: "T3", tenders: [localTender(.creditCard, 1000, tendered: 1100), localTender(.cash, 950, tendered: 1000)]))
        #expect(both.changeGiven == Money(cents: 50) && both.remainingBalance == .zero)

        // Titre-restaurant de 30,00 € sur une note de 19,50 € : refusé ou avoir selon la politique, jamais de rendu.
        let strictCart = try await counterCart(api)
        let voucher = CounterTender(method: .mealVoucher, amount: Money(cents: 1950), tendered: Money(cents: 3000), facialValue: Money(cents: 3000))
        await #expect(throws: APIError.server(status: 400, message: "Surpaiement par Titre-Restaurant refusé : la valeur faciale (30.00 €) dépasse le solde dû (19.50 €).")) {
            try await api.counterCheckout(checkout(strictCart.orderId, [voucher], policy: .strictRejection))
        }
        let credited = try await api.counterCheckout(checkout(strictCart.orderId, [voucher], policy: .customerCreditVoucher))
        #expect(credited.issuedCreditVoucher?.amount == Money(cents: 1050) && credited.changeGiven == .zero)
        let capped = try await api.counterCheckout(checkout(try await counterCart(api).orderId, [voucher], policy: .capAtBalance))
        #expect(capped.issuedCreditVoucher == nil && capped.changeGiven == .zero)
    }

    // M4
    @Test func anUnknownOrderIdIsRefusedEvenIfTheTableHasAnOrder() async throws {
        let api = try await makeLocalAPI()
        let order = try await seat(api, "T1")
        await #expect(throws: APIError.server(status: 400, message: "Commande introuvable pour ce règlement.")) {
            try await api.pay(PaymentRequest(orderId: UUID(), tableNumber: "T1", operatorId: nil, terminalId: "T01", tenders: [localTender(.cash, 1950)], requestReceiptPrint: false, tipAmount: .zero))
        }
        await #expect(throws: APIError.server(status: 400, message: "Facturation chambre échouée.")) {
            try await api.chargeRoom(roomCharge(UUID(), table: "T1", amount: 1950))
        }
        #expect(try await api.activeOrder(table: "T1")?.orderId == order.orderId)
        #expect(try await api.hotelRooms().allSatisfy { $0.currentBalance == .zero })
        // Sans orderId, le repli par numéro de table reste en vigueur.
        #expect(try await api.chargeRoom(roomCharge(nil, table: "T1", amount: 1950)).success == true)
    }
}
