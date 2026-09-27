import Foundation
import Testing
@testable import POSKit

private let products = InMemoryPOSBackend.defaultProducts
private func product(_ prefix: String) -> Product { products.first { $0.name.hasPrefix(prefix) }! }

@MainActor
@Suite("Session opérateur")
struct SessionTests {
    @Test func validPinUnlocksAndRoutesByRole() async {
        let app = await loggedInApp(pin: "1234")
        #expect(app.isUnlocked)
        #expect(app.currentOperator?.role == .floorManager)
        #expect(app.section == .floor)
        #expect(app.context.notice?.message == "Bonjour Alexandre D.")

        let chef = await loggedInApp(pin: "5678")
        #expect(chef.section == .kitchen)
    }

    @Test func invalidPinShowsErrorAndClears() async {
        let app = await loggedInApp(pin: "0000")
        #expect(!app.isUnlocked)
        #expect(app.pinError == "Code PIN ou identifiants incorrects")
        #expect(app.pin.code.isEmpty)
        #expect(app.pinFailures == 1)
    }

    @Test func lockSavesPendingLinesThenLogsOut() async throws {
        let backend = InMemoryPOSBackend()
        let app = await loggedInApp(backend: backend)
        await app.openTable("T1")
        app.order.add(product("Pizza Margherita"))
        await app.lock()
        #expect(!app.isUnlocked)
        await #expect(throws: APIError.unauthorized) {
            try await backend.activeOrder(tableNumber: "T1")
        }
        _ = try await backend.login(pin: "1234")
        #expect(try await backend.activeOrder(tableNumber: "T1")?.lines.count == 1)
    }

    @Test func unauthorizedResponseReturnsToPinScreen() async {
        let backend = InMemoryPOSBackend()
        let app = await loggedInApp(backend: backend)
        await backend.logout() // le serveur a invalidé le jeton
        await app.openTable("T1")
        #expect(!app.isUnlocked)
        #expect(app.pinError?.contains("Session expirée") == true)
    }

    @Test func applyingSettingsPersistsAndSwitchesServer() async {
        let store = InMemorySettingsStore(TerminalSettings(terminalId: "IPAD_A", demoMode: true))
        var built: [TerminalSettings] = []
        let app = AppModel(settingsStore: store, makeAPI: { settings in
            built.append(settings)
            return InMemoryPOSBackend()
        })
        var next = app.settings
        next.terminalId = "IPAD_B"
        await app.apply(next)
        #expect(built.count == 1) // même serveur : pas de reconnexion
        #expect(app.context.terminalId == "IPAD_B")
        #expect(store.load()?.terminalId == "IPAD_B")

        next.demoMode = false
        next.serverAddress = "192.168.1.20:5000"
        await app.apply(next)
        #expect(built.count == 2)
        #expect(built.last?.serverURL?.absoluteString == "http://192.168.1.20:5000")
    }

    @Test func settingsValidation() {
        #expect(TerminalSettings(serverAddress: "", terminalId: "A", demoMode: true).isValid)
        #expect(!TerminalSettings(serverAddress: "", terminalId: "A", demoMode: false).isValid)
        #expect(TerminalSettings(serverAddress: "10.0.0.2:5000", terminalId: "A", demoMode: false).isValid)
        #expect(!TerminalSettings(serverAddress: "10.0.0.2", terminalId: " ", demoMode: false).isValid)
    }
}

@MainActor
@Suite("Carte et plan de salle")
struct CatalogAndFloorTests {
    @Test func catalogStartsOnFavoritesAndSearchesWholeMenu() async {
        let app = await loggedInApp()
        await app.catalog.load()
        #expect(app.catalog.categories.count == 5)
        #expect(app.catalog.selectedCategoryID == CatalogModel.favoritesID)
        #expect(app.catalog.visibleProducts.allSatisfy { $0.isQuickKey })

        app.catalog.selectedCategoryID = "CAT_BOISSONS"
        #expect(app.catalog.visibleProducts.map(\.name) == ["Bière Artisanale IPA 33cl", "Verre Bordeaux AOP 12cl", "Expresso Pur Arabica"])

        app.catalog.searchText = "creme"
        #expect(app.catalog.visibleProducts.isEmpty)
        app.catalog.searchText = "TIRAMISU speculos"
        #expect(app.catalog.visibleProducts.map(\.name) == ["Tiramisu Spéculos Maison"])
    }

    @Test func floorPlanHidesCounterAndSortsNaturally() async {
        let backend = InMemoryPOSBackend()
        let app = await loggedInApp(backend: backend)
        _ = try? await backend.openCounterOrder(terminalId: "X", destination: .takeaway)
        _ = try? await backend.addItems(tableNumber: "T10", items: [])
        await app.floor.load()
        #expect(app.floor.tables.map(\.tableNumber) == ["T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8", "T10"])
        #expect(app.floor.occupiedCount == 2)
        #expect(app.floor.coversInRoom == 4)
        app.floor.filter = .free
        #expect(!app.floor.visibleTables.contains { $0.tableNumber == "T3" })
    }

    @Test func openingATableAssignsWaiterAndCovers() async {
        let app = await loggedInApp(pin: "2468")
        await app.floor.load()
        let table = app.floor.tables.first { $0.tableNumber == "T1" }!
        #expect(await app.floor.open(table, covers: 3))
        let opened = app.floor.tables.first { $0.tableNumber == "T1" }!
        #expect(opened.status == .occupied)
        #expect(opened.coversCount == 3)
        #expect(opened.assignedWaiterName == "Sophie Martin (Serveuse)")
    }
}

@MainActor
@Suite("Prise de commande")
struct OrderTests {
    @Test func identicalTapsAreMergedLocally() async {
        let app = await loggedInApp()
        await app.openTable("T1")
        let pizza = product("Pizza Margherita")
        app.order.add(pizza)
        app.order.add(pizza)
        app.order.currentCourse = .suite
        app.order.add(pizza)
        #expect(app.order.pending.map(\.quantity) == [2, 1])
        #expect(app.order.totalDue.cents == 3750)
        #expect(app.order.itemCount == 3)
        #expect(app.order.addCounter == 3)
    }

    @Test func quantityControls() async {
        let app = await loggedInApp()
        await app.openTable("T1")
        app.order.add(product("Expresso"))
        let id = app.order.pending[0].id
        app.order.increment(id)
        #expect(app.order.pending[0].quantity == 2)
        app.order.decrement(id)
        app.order.decrement(id)
        #expect(app.order.pending.isEmpty)
    }

    @Test func addingWithoutContextWarns() async {
        let app = await loggedInApp()
        app.order.add(product("Expresso"))
        #expect(app.order.pending.isEmpty)
        #expect(app.context.notice?.style == .warning)
    }

    @Test func sendToKitchenSavesDispatchesAndCreatesTickets() async throws {
        let app = await loggedInApp()
        await app.openTable("T1")
        let burger = product("Burger")
        app.order.add(burger, modifiers: [burger.modifierGroups[0].options[1]], note: "Sans oignons")
        app.order.add(product("Bière"))
        await app.order.sendToKitchen()

        #expect(app.order.pending.isEmpty)
        #expect(app.order.serverLines.count == 2)
        #expect(app.order.serverLines.allSatisfy { $0.isDispatched })
        #expect(app.order.totalDue.cents == 2550)
        #expect(app.context.notice?.message == "Commande envoyée en cuisine")

        await app.kitchen.load()
        let pending = app.kitchen.tickets(in: .pending)
        #expect(pending.map(\.stationId).sorted() == ["BAR", "HOT_KITCHEN"])
        #expect(pending.allSatisfy { $0.tableNumber == "T1" })
        let burgerTicket = try #require(pending.first { $0.stationId == "HOT_KITCHEN" })
        #expect(burgerTicket.items.first?.modifiersSummary == "Saignant, Note : Sans oignons")
    }

    @Test func nothingToSendIsReported() async {
        let app = await loggedInApp()
        await app.openTable("T1")
        await app.order.sendToKitchen()
        #expect(app.context.notice?.message == "Rien de nouveau à envoyer en cuisine.")
    }

    @Test func switchingTablesKeepsPendingLinesOnTheServer() async {
        let app = await loggedInApp()
        await app.openTable("T1")
        app.order.add(product("Pizza Reine"))
        await app.openTable("T2")
        #expect(app.order.context == .table("T2"))
        #expect(app.order.isEmpty)
        await app.openTable("T1")
        #expect(app.order.serverLines.map(\.productName) == ["Pizza Reine Royale"])
        #expect(!app.order.serverLines[0].isDispatched)
    }

    @Test func existingOrderIsLoadedWithTotals() async {
        let app = await loggedInApp()
        await app.openTable("T3")
        #expect(app.order.serverLines.count == 2)
        #expect(app.order.sentLines.count == 2)
        #expect(app.order.totals.ttc.cents == 3600)
        #expect(app.order.totals.ht.cents == 3190) // 2273 (pizzas 10 %) + 917 (vin 20 %)
    }

    @Test func counterDestinationHoldAndRecall() async throws {
        let app = await loggedInApp()
        await app.openCounter()
        #expect(app.order.context == .counter)
        #expect(app.order.destination == .takeaway)

        await app.order.setDestination(.eatIn)
        #expect(app.order.order?.destination == .eatIn)

        #expect(await app.order.hold(label: "Vide") == false)

        app.order.add(product("Salade"))
        #expect(await app.order.hold(label: "Marc"))
        #expect(app.order.isEmpty)
        await app.held.load()
        let held = try #require(app.held.orders.first)
        #expect(held.customerLabel == "Marc")
        #expect(held.totalTtc.cents == 950)

        #expect(await app.order.recall(held))
        #expect(app.order.serverLines.map(\.productName) == ["Salade César Poulet"])
        await app.held.load()
        #expect(app.held.orders.isEmpty)
    }

    @Test func recallIsRefusedWhileAnotherSaleIsOpen() async throws {
        let app = await loggedInApp()
        await app.openCounter()
        app.order.add(product("Salade"))
        #expect(await app.order.hold(label: "A"))
        app.order.add(product("Expresso"))
        await app.held.load()
        #expect(await app.order.recall(app.held.orders[0]) == false)
        #expect(app.context.notice?.style == .warning)
    }

    @Test func voidingHeldOrderNeedsAManagerPin() async throws {
        let app = await loggedInApp(pin: "2468")
        await app.openCounter()
        app.order.add(product("Salade"))
        _ = await app.order.hold(label: "A")
        await app.held.load()
        let held = try #require(app.held.orders.first)
        #expect(await app.held.void(held, supervisorPin: "2468") == false)
        #expect(app.context.notice?.message == "Autorisation insuffisante : code PIN superviseur ou gérant requis.")
        #expect(await app.held.void(held, supervisorPin: "1234"))
        #expect(app.held.orders.isEmpty)
    }
}

@MainActor
@Suite("Encaissement")
struct CheckoutTests {
    private func tableCheckout(_ app: AppModel, items: [Product]) async throws -> CheckoutModel {
        await app.openTable("T1")
        items.forEach { app.order.add($0) }
        #expect(await app.order.prepareCheckout())
        let order = try #require(app.order.order)
        return CheckoutModel(context: .table("T1"), order: order, destination: .eatIn, posContext: app.context)
    }

    @Test func cashPaymentGivesChangeAndFreesTable() async throws {
        let app = await loggedInApp()
        let checkout = try await tableCheckout(app, items: [product("Burger"), product("Bière")])
        #expect(checkout.amountDue.cents == 2550)
        #expect(checkout.cashSuggestions.map(\.cents) == [2550, 3000, 4000, 5000])
        checkout.entry.set(Money(cents: 3000))
        #expect(checkout.change.cents == 450)
        await checkout.submit()

        guard case .completed(let receipt) = checkout.phase else {
            Issue.record("encaissement non terminé : \(checkout.phase)")
            return
        }
        #expect(receipt.totalPaid.cents == 2550)
        #expect(receipt.changeGiven.cents == 450)
        #expect(receipt.receiptNumbers == ["IPAD_TEST-000001"])

        await app.order.paymentCompleted()
        #expect(app.order.context == nil)
        await app.floor.load()
        #expect(app.floor.tables.first { $0.tableNumber == "T1" }?.status == .free)
    }

    @Test func cardCannotExceedAmountDue() async throws {
        let app = await loggedInApp()
        let checkout = try await tableCheckout(app, items: [product("Expresso")])
        checkout.method = .creditCard
        checkout.entry.set(Money(cents: 1000))
        #expect(checkout.overpaymentNotAllowed)
        #expect(!checkout.canSubmit)
        #expect(checkout.change == .zero)
    }

    @Test func changingMethodResetsTypedAmount() async throws {
        let app = await loggedInApp()
        let checkout = try await tableCheckout(app, items: [product("Expresso")])
        checkout.entry.set(Money(cents: 1000))
        checkout.method = .creditCard
        #expect(checkout.entry.isEmpty)
        #expect(checkout.tendered.cents == 250)
    }

    @Test func equalSplitPaysGuestByGuest() async throws {
        let app = await loggedInApp()
        let checkout = try await tableCheckout(app, items: [product("Burger"), product("Pizza Margherita"), product("Expresso")])
        #expect(checkout.remaining.cents == 3450)
        checkout.split(into: 3)
        #expect(checkout.parts.map(\.cents) == [1150, 1150, 1150])

        checkout.method = .creditCard
        await checkout.submit()
        #expect(checkout.remaining.cents == 2300)
        #expect(checkout.paidParts == 1)
        #expect(checkout.phase == .entering)

        checkout.method = .cash
        checkout.entry.set(Money(cents: 2000))
        await checkout.submit()
        #expect(checkout.remaining.cents == 1150)

        checkout.method = .mealVoucher
        await checkout.submit()
        guard case .completed(let receipt) = checkout.phase else {
            Issue.record("encaissement non terminé")
            return
        }
        #expect(receipt.receiptNumbers.count == 3)
        #expect(receipt.totalPaid.cents == 3450)
    }

    @Test func partialPaymentBelowPartKeepsBalanceExact() async throws {
        let app = await loggedInApp()
        let checkout = try await tableCheckout(app, items: [product("Pizza Margherita")])
        checkout.split(into: 2)
        checkout.method = .creditCard
        checkout.entry.set(Money(cents: 500))
        await checkout.submit()
        #expect(checkout.remaining.cents == 750)
        #expect(checkout.amountDue.cents == 625)
        await checkout.submit()
        #expect(checkout.remaining.cents == 125)
        #expect(checkout.amountDue.cents == 125)
        await checkout.submit()
        #expect(checkout.isCompleted)
    }

    @Test func counterCombinesTendersIntoOneCheckout() async throws {
        let app = await loggedInApp()
        await app.openCounter()
        app.order.add(product("Burger"))
        #expect(await app.order.prepareCheckout())
        let order = try #require(app.order.order)
        let checkout = CheckoutModel(context: .counter, order: order, destination: .takeaway, posContext: app.context)
        checkout.pickupBuzzer = "7"

        checkout.method = .mealVoucher
        checkout.entry.set(Money(cents: 1000))
        await checkout.submit()
        #expect(checkout.counterTenders.count == 1)
        #expect(checkout.remaining.cents == 950)
        #expect(checkout.phase == .entering)

        checkout.method = .cash
        checkout.entry.set(Money(cents: 2000))
        await checkout.submit()
        guard case .completed(let receipt) = checkout.phase else {
            Issue.record("vente comptoir non terminée")
            return
        }
        #expect(receipt.pickupNumber == "#A-01")
        #expect(receipt.changeGiven.cents == 1050)
        #expect(receipt.totalPaid.cents == 1950)

        await app.order.paymentCompleted()
        #expect(app.order.context == .counter)
        #expect(app.order.isEmpty)
    }

    @Test func counterResetRestoresBalance() async throws {
        let app = await loggedInApp()
        await app.openCounter()
        app.order.add(product("Burger"))
        _ = await app.order.prepareCheckout()
        let checkout = CheckoutModel(context: .counter, order: app.order.order!, destination: .takeaway, posContext: app.context)
        checkout.method = .giftCard
        checkout.entry.set(Money(cents: 500))
        await checkout.submit()
        checkout.resetCounterTenders()
        #expect(checkout.remaining.cents == 1950)
        #expect(checkout.counterTenders.isEmpty)
    }

    @Test func emptyNoteCannotBeCheckedOut() async {
        let app = await loggedInApp()
        await app.openTable("T1")
        #expect(await app.order.prepareCheckout() == false)
        #expect(app.context.notice?.message == "La note est vide.")
    }
}

@MainActor
@Suite("Cuisine et fiscal")
struct KitchenAndFiscalTests {
    @Test func bumpMovesTicketThroughColumns() async throws {
        let app = await loggedInApp(pin: "5678")
        await app.kitchen.load()
        let ticket = try #require(app.kitchen.tickets(in: .inPreparation).first)
        await app.kitchen.bump(ticket)
        #expect(app.kitchen.tickets(in: .inPreparation).isEmpty)
        #expect(app.kitchen.tickets(in: .ready).count == 2)
        #expect(app.kitchen.stations == ["BAR", "HOT_KITCHEN"])
        app.kitchen.stationFilter = "BAR"
        #expect(app.kitchen.tickets(in: .ready).count == 1)
    }

    @Test func lateTicketsAreFlagged() {
        let model = KitchenModel(context: POSContext(api: InMemoryPOSBackend(), terminalId: "X"))
        let old = KitchenTicket(orderId: UUID(), tableNumber: "T1", stationId: "BAR", status: .pending, dispatchedAtUtc: Date().addingTimeInterval(-20 * 60), items: [])
        let fresh = KitchenTicket(orderId: UUID(), tableNumber: "T1", stationId: "BAR", status: .pending, dispatchedAtUtc: Date(), items: [])
        let ready = KitchenTicket(orderId: UUID(), tableNumber: "T1", stationId: "BAR", status: .ready, dispatchedAtUtc: Date().addingTimeInterval(-40 * 60), items: [])
        #expect(model.isLate(old))
        #expect(!model.isLate(fresh))
        #expect(!model.isLate(ready))
        #expect(old.elapsedMinutes() == 20)
        #expect(KitchenTicket.stationLabel(for: "HOT_KITCHEN") == "Cuisine chaude")
    }

    @Test func fiscalReportsAreForManagersOnly() async {
        let waiter = await loggedInApp(pin: "2468")
        #expect(!waiter.fiscal.isAllowed)
        await waiter.fiscal.load()
        #expect(waiter.fiscal.xReport == nil)
        #expect(await waiter.fiscal.closeDay() == false)
    }

    @Test func xReportThenZClosure() async throws {
        let app = await loggedInApp()
        await app.openTable("T1")
        app.order.add(product("Burger"))
        app.order.add(product("Bière"))
        _ = await app.order.prepareCheckout()
        let checkout = CheckoutModel(context: .table("T1"), order: app.order.order!, destination: .eatIn, posContext: app.context)
        checkout.method = .creditCard
        await checkout.submit()

        await app.fiscal.load()
        let x = try #require(app.fiscal.xReport)
        #expect(x.totalSalesTtc.cents == 2550)
        #expect(x.receiptCount == 1)
        #expect(x.sortedPayments.first?.label == "Carte bancaire")
        #expect(x.sortedVat.map(\.rate) == ["10.0", "20.0"])
        #expect(app.fiscal.lastClosure == nil)

        #expect(await app.fiscal.closeDay())
        #expect(app.fiscal.lastClosure?.closureSequence == 1)
        #expect(app.fiscal.lastClosure?.isSealedClosure == true)
        #expect(app.fiscal.xReport?.totalSalesTtc == .zero)
        #expect(app.fiscal.xReport?.perpetualGrandTotal.cents == 2550)
    }
}
