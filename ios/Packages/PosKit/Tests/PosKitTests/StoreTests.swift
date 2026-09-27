import Foundation
import Testing
@testable import PosKit

/// Fabrique un `AppModel` branché sur le backend en mémoire, déverrouillé avec le PIN donné.
@MainActor
func makeModel(pin: String = "1234") async -> (AppModel, InMemoryPosAPI) {
    let api = InMemoryPosAPI()
    let defaults = UserDefaults(suiteName: "PosKitTests-\(UUID().uuidString)")!
    let model = AppModel(api: api, settings: TerminalSettings(defaults: defaults))
    model.notifier.autoDismissAfter = nil
    for digit in pin { await model.session.appendDigit(String(digit)) }
    await model.catalog.load()
    return (model, api)
}

@MainActor
@Suite("Session")
struct SessionTests {
    @Test func unlocksWithValidPin() async {
        let (model, _) = await makeModel(pin: "2468")
        #expect(model.session.isUnlocked)
        #expect(model.session.currentOperator?.role == .waiter)
        #expect(!model.session.isManager)
    }

    @Test func rejectsInvalidPinAndClearsEntry() async {
        let (model, _) = await makeModel(pin: "0000")
        #expect(!model.session.isUnlocked)
        #expect(model.session.pinError == "Code PIN ou identifiants incorrects")
        #expect(model.session.pinEntry.isEmpty)
    }

    @Test func keypadEditing() async {
        let (model, _) = await makeModel(pin: "")
        await model.session.appendDigit("1")
        await model.session.appendDigit("2")
        model.session.deleteDigit()
        #expect(model.session.pinEntry == "1")
        model.session.clearPin()
        #expect(model.session.pinEntry.isEmpty)
    }

    @Test func lockClearsOperator() async {
        let (model, _) = await makeModel()
        model.session.lock()
        #expect(!model.session.isUnlocked)
    }
}

@MainActor
@Suite("Catalogue et grille")
struct CatalogStoreTests {
    @Test func loadsCatalogAndAutoGrid() async {
        let (model, _) = await makeModel()
        #expect(model.catalog.categories.count == 5)
        #expect(model.catalog.cells.count == 16)
        #expect(model.catalog.cells.first?.product != nil)
        #expect(!model.catalog.quickKeys.isEmpty)
    }

    @Test func filtersByCategory() async {
        let (model, _) = await makeModel()
        await model.catalog.selectCategory("CAT_PIZZAS")
        let names = model.catalog.cells.compactMap(\.product?.name)
        #expect(names == ["Pizza Margherita AOP", "Pizza 4 Fromages"])
    }

    @Test func pagingIsClamped() async {
        let (model, _) = await makeModel()
        await model.catalog.nextPage()
        #expect(model.catalog.pageIndex == 0)
    }
}

@MainActor
@Suite("Ticket — prise de commande")
struct TicketStoreTests {
    func product(_ model: AppModel, _ name: String) -> Product {
        model.catalog.products.first { $0.name == name }!
    }

    @Test func addingSameProductMergesDraftLines() async {
        let (model, _) = await makeModel()
        await model.ticket.load(table: "T1")
        let pizza = product(model, "Pizza Margherita AOP")
        model.ticket.add(pizza)
        model.ticket.add(pizza)
        #expect(model.ticket.lines.count == 1)
        #expect(model.ticket.lines[0].quantity == 2)
        #expect(model.ticket.totals.totalTtc == Money(cents: 2500))
    }

    @Test func modifiersCreateDistinctLines() async throws {
        let (model, _) = await makeModel()
        await model.ticket.load(table: "T1")
        let burger = product(model, "Burger Gourmet Rossini")
        var selection = ModifierSelection(product: burger)
        let extras = burger.modifierGroups[1]
        try selection.toggle(extras.options[0], in: extras)
        selection.kitchenComment = "Sans oignons"
        model.ticket.add(burger, selection: selection)
        model.ticket.add(burger, selection: ModifierSelection(product: burger))
        #expect(model.ticket.lines.count == 2)
        #expect(model.ticket.totals.totalTtc == Money(cents: 1950 * 2 + 250))
        #expect(model.ticket.lines[0].asInput.modifiers.last == "💬 Sans oignons")
    }

    @Test func draftEditing() async {
        let (model, _) = await makeModel()
        await model.ticket.load(table: "T2")
        model.ticket.add(product(model, "Tiramisu Maison"))
        let id = model.ticket.lines[0].id
        model.ticket.increment(id)
        model.ticket.cycleCourse(id)
        #expect(model.ticket.lines[0].quantity == 2)
        #expect(model.ticket.lines[0].course == .suite)
        model.ticket.decrement(id)
        model.ticket.decrement(id)
        #expect(model.ticket.lines.isEmpty)
    }

    @Test func sendToKitchenPersistsDispatchesAndSetsEatIn() async {
        let (model, api) = await makeModel()
        await model.ticket.load(table: "T3")
        model.ticket.add(product(model, "Entrecôte 300g"))
        model.ticket.add(product(model, "Bière Artisanale IPA 33cl"))
        let ok = await model.ticket.sendToKitchen()
        #expect(ok)
        #expect(model.ticket.lines.allSatisfy { $0.isDispatched && !$0.isDraft })
        #expect(model.ticket.destination == .eatIn)
        let tickets = try! await api.kitchenTickets()
        #expect(Set(tickets.map(\.stationId)) == ["GRILL", "BAR"])
        #expect(tickets.allSatisfy { !$0.tableNumber.contains("EMPORTER") })
    }

    @Test func savedLinesCannotBeDecrementedButCanBeIncremented() async {
        let (model, _) = await makeModel()
        await model.ticket.load(table: "T4")
        model.ticket.add(product(model, "Café Gourmand"))
        await model.ticket.sendToKitchen()
        let saved = model.ticket.lines[0]
        model.ticket.decrement(saved.id)
        #expect(model.ticket.lines.count == 1)
        #expect(model.notifier.lastMessage?.contains("Offrir") == true)
        model.ticket.increment(saved.id)
        #expect(model.ticket.lines.count == 2)
        #expect(model.ticket.lines[1].isDraft)
        model.ticket.clearDrafts()
        #expect(model.ticket.lines.count == 1)
    }

    @Test func switchingTablesCommitsDrafts() async {
        let (model, _) = await makeModel()
        await model.ticket.load(table: "T5")
        model.ticket.add(product(model, "Tartare de Saumon"))
        await model.ticket.load(table: "T6")
        #expect(model.ticket.lines.isEmpty)
        await model.ticket.load(table: "T5")
        #expect(model.ticket.lines.count == 1)
        #expect(!model.ticket.lines[0].isDraft)
    }

    @Test func globalDiscountAndComp() async {
        let (model, _) = await makeModel()
        await model.ticket.load(table: "T1")
        model.ticket.add(product(model, "Entrecôte 300g"))
        model.ticket.add(product(model, "Tiramisu Maison"))
        #expect(await model.ticket.applyGlobalDiscount(type: .percentage, value: 10, reason: "Fidélité"))
        #expect(model.ticket.totals.totalTtc == Money(cents: 3015))
        let tiramisu = model.ticket.compEligibleLines.first { $0.name == "Tiramisu Maison" }!
        #expect(await model.ticket.comp(lineId: tiramisu.id, reason: "Anniversaire"))
        #expect(model.ticket.totals.totalTtc == Money(cents: 2340))
        await model.ticket.removeDiscount()
        #expect(model.ticket.totals.totalTtc == Money(cents: 2600))
    }

    @Test func fullPaymentFreesTheTable() async {
        let (model, api) = await makeModel()
        await model.ticket.load(table: "T1")
        model.ticket.add(product(model, "Pizza 4 Fromages"))
        let outcome = await model.ticket.pay(method: .cash, amount: Money(cents: 1450), tendered: Money(cents: 2000))
        #expect(outcome?.change == Money(cents: 550))
        #expect(outcome?.isComplete == true)
        #expect(model.ticket.lines.isEmpty)
        let t1 = try! await api.tables().first { $0.tableNumber == "T1" }!
        #expect(t1.status == .free)
    }

    @Test func splitPaymentTracksRemainingBalance() async {
        let (model, _) = await makeModel()
        await model.ticket.load(table: "T2")
        model.ticket.add(product(model, "Entrecôte 300g"))
        model.ticket.add(product(model, "Pizza Margherita AOP"))
        var plan = PaymentPlan(due: model.ticket.amountDue)
        plan.setGuests(3)
        for _ in 0..<2 {
            let outcome = await model.ticket.pay(method: .creditCard, amount: plan.amountToCollect, tendered: plan.amountToCollect)
            #expect(outcome?.isComplete == false)
            plan.markPartPaid()
        }
        #expect(model.ticket.amountDue == plan.amountToCollect)
        let last = await model.ticket.pay(method: .creditCard, amount: plan.amountToCollect, tendered: plan.amountToCollect)
        #expect(last?.isComplete == true)
    }

    @Test func cashPaymentRequiresEnoughMoney() async {
        let (model, api) = await makeModel()
        await model.ticket.load(table: "T1")
        model.ticket.add(product(model, "Pizza 4 Fromages"))
        let outcome = await model.ticket.pay(method: .cash, amount: Money(cents: 1450), tendered: Money(cents: 1000))
        #expect(outcome == nil)
        #expect(await api.calls.contains("pay") == false)
    }

    @Test func transferMovesTheOrder() async {
        let (model, _) = await makeModel()
        await model.ticket.load(table: "T7")
        model.ticket.add(product(model, "Salade César Poulet"))
        #expect(await model.ticket.transfer(to: "T8", merge: false))
        #expect(model.ticket.tableNumber == "T8")
        #expect(model.ticket.lines.count == 1)
        await model.floor.load()
        #expect(model.floor.tables.first { $0.tableNumber == "T7" }?.status == .free)
    }

    @Test func networkErrorsAreSurfacedAndDraftsKept() async {
        let (model, api) = await makeModel()
        await model.ticket.load(table: "T1")
        model.ticket.add(product(model, "Tiramisu Maison"))
        await api.setFailure(.transport("hors ligne"))
        let ok = await model.ticket.sendToKitchen()
        #expect(!ok)
        #expect(model.ticket.lines.first?.isDraft == true)
        #expect(model.notifier.toasts.last?.kind == .error)
    }

    @Test func unauthorizedLocksTheTerminal() async {
        let (model, api) = await makeModel()
        await model.ticket.load(table: "T1")
        model.ticket.add(product(model, "Tiramisu Maison"))
        await api.setFailure(.unauthorized)
        _ = await model.ticket.sendToKitchen()
        #expect(!model.session.isUnlocked)
    }
}

@MainActor
@Suite("Comptoir — vente à emporter")
struct CounterTests {
    func product(_ model: AppModel, _ name: String) -> Product { model.catalog.products.first { $0.name == name }! }

    @Test func counterCheckoutReturnsPickupNumberAndChange() async {
        let (model, _) = await makeModel()
        await model.ticket.openCounter()
        #expect(model.ticket.isCounter)
        #expect(model.ticket.destination == .takeaway)
        model.ticket.add(product(model, "Pizza Margherita AOP"))
        // À emporter : TVA 5,5 %
        #expect(model.ticket.totals.vatLines.first?.ratePercent == 5.5)
        let outcome = await model.ticket.counterCheckout(method: .cash, amount: Money(cents: 1250), tendered: Money(cents: 2000), tip: .zero, buzzer: "12", printReceipt: false, policy: .capAtBalance)
        #expect(outcome?.pickupNumber == "#A-01")
        #expect(outcome?.change == Money(cents: 750))
        #expect(outcome?.buzzer == "12")
        #expect(model.ticket.lines.isEmpty)
    }

    @Test func mealVoucherCreditPolicyIssuesVoucher() async {
        let (model, _) = await makeModel()
        await model.ticket.openCounter()
        model.ticket.add(product(model, "Tiramisu Maison"))
        let outcome = await model.ticket.counterCheckout(method: .mealVoucher, amount: Money(cents: 750), tendered: Money(cents: 1000), tip: .zero, buzzer: nil, printReceipt: false, policy: .customerCreditVoucher)
        #expect(outcome?.creditVoucher?.amount == Money(cents: 250))
    }

    @Test func holdRecallAndVoid() async {
        let (model, _) = await makeModel()
        await model.ticket.openCounter()
        model.ticket.add(product(model, "Café Gourmand"))
        #expect(await model.ticket.hold(label: "Client pressé"))
        #expect(model.ticket.lines.isEmpty)
        #expect(model.ticket.heldOrders.count == 1)
        #expect(await model.ticket.recall(model.ticket.heldOrders[0]))
        #expect(model.ticket.lines.count == 1)
        #expect(model.ticket.heldOrders.isEmpty)

        #expect(await model.ticket.hold(label: "À annuler"))
        let held = model.ticket.heldOrders[0]
        #expect(await model.ticket.voidHeld(held, supervisorPin: "2468") == false) // serveuse : refusé
        #expect(await model.ticket.voidHeld(held, supervisorPin: "1234"))
        #expect(model.ticket.heldOrders.isEmpty)
    }

    @Test func switchingDestinationChangesVat() async {
        let (model, _) = await makeModel()
        await model.ticket.openCounter()
        model.ticket.add(product(model, "Pizza Margherita AOP"))
        await model.ticket.switchDestination(.eatIn)
        #expect(model.ticket.totals.vatLines.first?.ratePercent == 10)
    }
}

@MainActor
@Suite("Happy Hour")
struct HappyHourTests {
    @Test func overrideAppliesReducedPricesOnlyOnSite() async {
        let (model, _) = await makeModel()
        #expect(await model.happyHour.activateOverride(pin: "1234", minutes: 30, reason: "Match"))
        #expect(model.happyHour.isActive)
        #expect(model.happyHour.remainingSeconds > 0)
        let beer = model.catalog.products.first { $0.name.hasPrefix("Bière") }!
        await model.ticket.load(table: "T1")
        model.ticket.add(beer)
        #expect(model.ticket.lines[0].unitPrice == Money(cents: 500))
        #expect(model.ticket.lines[0].isHappyHourApplied)
        #expect(model.ticket.lines[0].originalUnitPrice == Money(cents: 600))

        await model.ticket.openCounter(destination: .takeaway)
        model.ticket.add(beer)
        #expect(model.ticket.lines.last?.unitPrice == Money(cents: 600))
    }

    @Test func overrideNeedsSupervisor() async {
        let (model, _) = await makeModel()
        #expect(await model.happyHour.activateOverride(pin: "2468", minutes: 30, reason: "") == false)
        #expect(!model.happyHour.isActive)
    }

    @Test func countdownTicks() async {
        let (model, _) = await makeModel()
        model.happyHour.apply(HappyHourStatus(isActive: true, currentWindow: HappyHourWindow(startTime: nil, endTime: nil, remainingMinutes: 1)))
        #expect(model.happyHour.countdownText == "01:00")
        model.happyHour.tick()
        #expect(model.happyHour.countdownText == "00:59")
    }
}

@MainActor
@Suite("Cuisine, salle et fiscal")
struct OperationsTests {
    @Test func kitchenBumpRequiresKitchenRole() async {
        let (model, _) = await makeModel(pin: "2468")
        await model.ticket.load(table: "T1")
        model.ticket.add(model.catalog.products[0])
        await model.ticket.sendToKitchen()
        await model.kitchen.load()
        let ticket = model.kitchen.tickets(in: .pending)[0]
        await model.kitchen.bump(ticket)
        #expect(model.notifier.toasts.last?.kind == .error)
    }

    @Test func kitchenBumpAdvancesStatus() async {
        let (model, _) = await makeModel(pin: "5678")
        await model.ticket.load(table: "T1")
        model.ticket.add(model.catalog.products[0])
        await model.ticket.sendToKitchen()
        await model.kitchen.load()
        await model.kitchen.bump(model.kitchen.tickets(in: .pending)[0])
        #expect(model.kitchen.tickets(in: .inPreparation).count == 1)
    }

    @Test func floorAddAndOpenTable() async {
        let (model, _) = await makeModel()
        await model.floor.load()
        #expect(await model.floor.addTable(number: "Terrasse 1", capacity: 4))
        #expect(await model.floor.addTable(number: "T1", capacity: 2) == false)
        let table = model.floor.tables.first { $0.tableNumber == "Terrasse 1" }!
        #expect(await model.floor.open(table, covers: 3))
        #expect(model.floor.tables.first { $0.tableNumber == "Terrasse 1" }?.coversCount == 3)
    }

    @Test func zClosureAfterSales() async {
        let (model, _) = await makeModel()
        await model.ticket.load(table: "T1")
        model.ticket.add(model.catalog.products.first { $0.name == "Pizza 4 Fromages" }!)
        _ = await model.ticket.pay(method: .creditCard, amount: Money(cents: 1450), tendered: Money(cents: 1450))
        await model.fiscal.previewX()
        #expect(model.fiscal.report?.totalSalesTtc == Money(cents: 1450))
        #expect(model.fiscal.isSealed == false)
        #expect(await model.fiscal.executeZ())
        #expect(model.fiscal.isSealed)
        #expect(model.fiscal.report?.closureSequence == 1)
    }

    @Test func zClosureRefusedForWaiter() async {
        let (model, _) = await makeModel(pin: "2468")
        #expect(await model.fiscal.executeZ() == false)
    }
}

@MainActor
@Suite("Back-office")
struct AdminTests {
    @Test func productCrud() async {
        let (model, _) = await makeModel()
        let draft = ProductDraft(name: "Mojito", categoryId: "CAT_DRINKS", price: Money(cents: 900), taxRatePercent: 20, stationId: "BAR")
        #expect(await model.catalogAdmin.saveProduct(id: nil, draft: draft))
        let mojito = model.catalog.products.first { $0.name == "Mojito" }!
        var edited = ProductDraft(product: mojito)
        edited.price = Money(cents: 950)
        #expect(await model.catalogAdmin.saveProduct(id: mojito.id, draft: edited))
        #expect(model.catalog.products.first { $0.id == mojito.id }?.price == Money(cents: 950))
        await model.catalogAdmin.archive(model.catalog.products.first { $0.id == mojito.id }!)
        #expect(!model.catalog.products.contains { $0.id == mojito.id })
    }

    @Test func invalidProductRejected() async {
        let (model, _) = await makeModel()
        #expect(await model.catalogAdmin.saveProduct(id: nil, draft: ProductDraft(name: "", categoryId: "CAT_DRINKS", price: Money(cents: 100))) == false)
    }

    @Test func staffPinValidation() async {
        let (model, _) = await makeModel()
        #expect(await model.staff.save(id: nil, name: "Léa", role: .waiter, pin: "12") == false)
        #expect(await model.staff.save(id: nil, name: "Léa", role: .waiter, pin: "4321"))
        await model.staff.load()
        #expect(model.staff.members.contains { $0.name == "Léa" })
    }

    @Test func printerValidation() {
        #expect(PrinterStore.isValidIPv4("192.168.1.200"))
        #expect(!PrinterStore.isValidIPv4("192.168.1"))
        #expect(!PrinterStore.isValidIPv4("300.1.1.1"))
    }

    @Test func gridEditorAssignSwapAndResize() async {
        let (model, _) = await makeModel()
        await model.gridEditor.select(category: "CAT_DRINKS")
        let water = model.catalog.products.first { $0.name.hasPrefix("Eau") }!
        await model.gridEditor.assign(product: water, at: GridPosition(row: 3, column: 3), label: "Eau", colorHex: "#00AAFF")
        #expect(model.gridEditor.layout?.slot(at: GridPosition(row: 3, column: 3))?.productId == water.id)
        await model.gridEditor.move(from: GridPosition(row: 3, column: 3), to: GridPosition(row: 0, column: 0))
        #expect(model.gridEditor.layout?.slot(at: GridPosition(row: 0, column: 0))?.productId == water.id)
        await model.gridEditor.applyDimensions(columns: 5, rows: 3, applyToAll: false)
        #expect(model.gridEditor.layout?.columnsCount == 5)
        await model.gridEditor.addPage()
        #expect(model.gridEditor.pageIndex == 1)
        #expect(model.gridEditor.layout?.totalPages == 2)
    }

    @Test func happyHourAdminBatchRules() async {
        let (model, _) = await makeModel()
        await model.happyHourAdmin.load()
        let pizzaIds = Set(model.catalog.products.filter { $0.categoryId == "CAT_PIZZAS" }.map(\.id))
        #expect(await model.happyHourAdmin.applyToProducts(pizzaIds, mode: .fixedPrice, value: 9))
        #expect(await model.happyHourAdmin.applyToCategories(["CAT_DESSERTS"], percent: 150) == false)
        #expect(model.happyHourAdmin.selectedSchedule?.priceRules.count == 4)
        let ruleIds = Set(model.happyHourAdmin.selectedSchedule!.priceRules.compactMap(\.id).prefix(2))
        await model.happyHourAdmin.deleteRules(ruleIds)
        #expect(model.happyHourAdmin.selectedSchedule?.priceRules.count == 2)
    }

    @Test func happyHourScheduleValidation() async {
        let (model, _) = await makeModel()
        await model.happyHourAdmin.load()
        #expect(await model.happyHourAdmin.create(HappyHourSchedule(name: "Brunch", daysOfWeek: [], startTime: "11:00", endTime: "14:00")) == false)
        #expect(await model.happyHourAdmin.create(HappyHourSchedule(name: "Brunch", daysOfWeek: [0, 6], startTime: "11:00", endTime: "14:00")))
        #expect(model.happyHourAdmin.selectedSchedule?.name == "Brunch")
    }
}
