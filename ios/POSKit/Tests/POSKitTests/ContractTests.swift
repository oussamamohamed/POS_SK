import Foundation
import Testing
@testable import POSKit

/// Décodage des réponses réelles de l'API (fixtures capturées sur `RestaurantPos.Api`).
/// Si le serveur change la forme d'un DTO, ces tests échouent avant que l'iPad ne casse.
@Suite("Contrat API — réponses réelles")
struct ContractTests {
    @Test func login() throws {
        let response = try Fixture.decode(LoginResponse.self, "login")
        #expect(response.success == true)
        #expect(response.role == .floorManager)
        #expect(response.operatorName == "Alexandre Dupont (Manager)")
        #expect(response.token == "test-token")
    }

    @Test func catalog() throws {
        let categories = try Fixture.decode([Category].self, "categories")
        #expect(categories.count == 5)
        #expect(categories.first?.name == "Entrées Fraîches")
        #expect(categories.first?.colorHex == "#2ECC71")

        let products = try Fixture.decode([Product].self, "products")
        #expect(products.count == 11)
        let burger = try #require(products.first { $0.name == "Burger Gourmet Rossini" })
        #expect(burger.price.cents == 1950)
        #expect(burger.taxRatePercent == 10)
        #expect(burger.preparationStationId == "HOT_KITCHEN")
        #expect(burger.modifierGroups.count == 2)
        #expect(burger.modifierGroups[0].isMandatory)
        #expect(burger.modifierGroups[0].options.first { $0.isDefault }?.name == "À Point")
        #expect(products.first { $0.name.hasPrefix("Bière") }?.taxRatePercent == 20)
    }

    @Test func tables() throws {
        let tables = try Fixture.decode([DiningTable].self, "tables")
        #expect(tables.count == 8)
        #expect(tables[0].tableNumber == "T1")
        #expect(tables.allSatisfy { $0.status == .free && $0.activeOrderId == nil })

        let opened = try Fixture.decode(DiningTable.self, "table_open")
        #expect(opened.status == .occupied)
        #expect(opened.coversCount == 3)
        #expect(opened.activeOrderId != nil)
        #expect(opened.openedAtUtc != nil)
    }

    @Test func activeOrderAfterAddingItems() throws {
        let order = try Fixture.decode(ActiveOrder.self, "order_after_add")
        #expect(order.tableNumber == "T2")
        #expect(order.lines.count == 2)
        let burger = order.lines[0]
        #expect(burger.quantity == 2)
        #expect(burger.unitPrice.cents == 1950)
        #expect(burger.modifiersPriceExtra.cents == 250)
        #expect(burger.totalPrice.cents == 4400)
        #expect(burger.modifiersSummary == ["Saignant", "Bacon"])
        #expect(!burger.isDispatched)
        #expect(order.lines[1].course == .suite)
        #expect(order.totalTtcAmount.cents == 4850)
        #expect(order.totalHtAmount.cents == 4375)
        #expect(order.totalVatAmount.cents == 475)

        let dispatched = try Fixture.decode(ActiveOrder.self, "order_after_dispatch")
        #expect(dispatched.lines.allSatisfy { $0.isDispatched })
    }

    @Test func kitchenTicketsAndBumpUseDifferentShapes() throws {
        let tickets = try Fixture.decode([KitchenTicket].self, "kds_tickets")
        #expect(tickets.count == 2)
        #expect(tickets.map(\.stationId).sorted() == ["BAR", "HOT_KITCHEN"])
        #expect(tickets.allSatisfy { $0.status == .pending && $0.dispatchedAtUtc != nil })

        // Le bump renvoie `KitchenTicketDto` : `ticketId`/`itemId` au lieu de `id`.
        let bumped = try Fixture.decode(KitchenTicket.self, "kds_bump")
        #expect(bumped.id == tickets[0].id)
        #expect(bumped.status == .inPreparation)
        #expect(bumped.items.first?.id == tickets[0].items.first?.id)
    }

    @Test func payments() throws {
        let partial = try Fixture.decode(PaymentResult.self, "pay_partial")
        #expect(partial.totalPaid.cents == 1617)
        #expect(partial.remainingBalance.cents == 3233)
        #expect(partial.receiptNumber == "IPAD_01-000001")
        #expect(partial.fiscalSignature?.count == 64)

        let final = try Fixture.decode(PaymentResult.self, "pay_final")
        #expect(final.changeGiven.cents == 1767)
        #expect(final.remainingBalance == .zero)
    }

    @Test func counterFlow() throws {
        let opened = try Fixture.decode(ActiveOrder.self, "counter_open")
        #expect(opened.tableNumber == "Comptoir")
        #expect(opened.lines.isEmpty)
        #expect(opened.destination == .takeaway)

        let switched = try Fixture.decode(ActiveOrder.self, "counter_switch")
        #expect(switched.destination == .eatIn)

        let held = try Fixture.decode(HeldOrder.self, "held_hold")
        #expect(held.customerLabel == "Client Marc")
        #expect(held.totalTtc.cents == 1250) // `Money` .NET brut : { amountInCents, currency }
        #expect(held.itemCount == 1)
        #expect(try Fixture.decode([HeldOrder].self, "held_list").count == 1)
        #expect(try Fixture.decode(ActiveOrder.self, "held_recall").lines.count == 1)

        let checkout = try Fixture.decode(CounterCheckoutResult.self, "counter_checkout")
        #expect(checkout.pickupNumber == "#A-01")
        #expect(checkout.changeGiven.cents == 750)
        #expect(checkout.openCashDrawer)
        #expect(checkout.issuedCreditVoucher == nil)
    }

    @Test func fiscalReports() throws {
        let x = try Fixture.decode(FiscalReport.self, "fiscal_x_report")
        #expect(x.totalSalesTtc.cents == 6100)
        #expect(x.totalSalesHt.cents == 5511)
        #expect(x.receiptCount == 3)
        #expect(x.sortedVat.map(\.rate) == ["10.0", "20.0"])
        #expect(x.sortedPayments.map(\.label) == ["Espèces", "Carte bancaire"])
        #expect(!x.isSealedClosure)
        #expect(x.periodStartUtc != nil)

        let z = try Fixture.decode(FiscalReport.self, "fiscal_z_closure")
        #expect(z.isSealedClosure)
        #expect(z.closureSequence == 1)
        #expect(z.signatureHash?.count == 64)
        #expect(z.totalVat.cents == 589)
    }

    @Test func errorBodies() throws {
        #expect(APIError.message(from: try Fixture.data("login_failure")) == "Code PIN ou identifiants incorrects")
        #expect(APIError.message(from: Data(#"{"message":"Aucune clôture trouvée."}"#.utf8)) == "Aucune clôture trouvée.")
        #expect(APIError.message(from: Data("pas du json".utf8)) == nil)
    }
}
