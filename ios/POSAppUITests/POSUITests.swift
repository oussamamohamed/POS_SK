import XCTest

/// Parcours métier complets, exécutés sur simulateur iPad contre le serveur simulé.
/// Données de départ : tables T1 à T8, T3 occupée (36,00 €), bons cuisine T3 en cours.
final class AuthenticationUITests: POSTestCase {
    func testWrongPinIsRejectedAndKeepsTerminalLocked() {
        login("0000")
        expectLabel("pin.error", contains: "incorrects")
        XCTAssertFalse(element("nav.floor").exists)
        screenshot("PIN refusé")
    }

    func testManagerUnlocksThenLocksTerminal() {
        loginToFloor("1234")
        expectNotice("Bonjour Alexandre")
        screenshot("Plan de salle")
        tap("session.lock")
        waitFor("pin.key.1")
        XCTAssertFalse(element("nav.floor").exists)
    }

    func testKitchenStaffLandsOnKitchenDisplay() {
        login("5678")
        waitFor("kds.count.InPreparation", timeout: 10)
    }

    func testSettingsAreReachableFromLockScreen() {
        tap("pin.settings")
        waitFor("settings.demo")
        waitFor("settings.test")
        app.buttons["Annuler"].firstMatch.tap()
        waitFor("pin.key.1")
    }
}

final class TableServiceUITests: POSTestCase {
    func testFloorPlanShowsTableStatuses() {
        loginToFloor()
        expectValue("table.T1", equals: "Libre")
        expectValue("table.T3", equals: "Occupée")
    }

    func testOrderWithModifiersIsSentToKitchen() {
        loginToFloor()
        openFreeTable("T1", covers: 3)

        addProduct("Burger Gourmet Rossini", category: "CAT_PLATS")
        tap("modifier.Saignant")
        tap("modifier.Bacon")
        expectLabel("modifiers.add", contains: "21,00")
        tap("modifiers.add")

        addProduct("Bière Artisanale IPA 33cl", category: "CAT_BOISSONS")
        addProduct("Bière Artisanale IPA 33cl")
        expectLabel("cart.pending.qty.Bière Artisanale IPA 33cl", contains: "2")
        expectLabel("cart.total", contains: "33,00")
        screenshot("Note avant envoi")

        tap("cart.send")
        expectNotice("envoyée en cuisine")
        waitFor("cart.line.Burger Gourmet Rossini")

        tap("nav.kitchen")
        expectLabel("kds.count.Pending", contains: "2")
        screenshot("Écran cuisine")
    }

    func testPendingQuantityCanBeAdjusted() {
        loginToFloor()
        openFreeTable("T2", covers: 2)
        addProduct("Expresso Pur Arabica", category: "favorites")
        tap("cart.pending.plus.Expresso Pur Arabica")
        expectLabel("cart.pending.qty.Expresso Pur Arabica", contains: "2")
        expectLabel("cart.total", contains: "5,00")
        tap("cart.pending.minus.Expresso Pur Arabica")
        expectLabel("cart.total", contains: "2,50")
    }

    func testSearchFindsProductsAcrossCategories() {
        loginToFloor()
        openFreeTable("T4", covers: 2)
        let search = waitFor("catalog.search")
        search.tap()
        search.typeText("tiramisu")
        waitFor("product.Tiramisu Spéculos Maison")
        XCTAssertFalse(element("product.Pizza Margherita AOP").exists)
    }
}

final class CheckoutUITests: POSTestCase {
    func testCashPaymentGivesChangeAndFreesTheTable() {
        loginToFloor()
        openFreeTable("T1", covers: 2)
        addProduct("Pizza Margherita AOP", category: "CAT_PIZZAS")
        tap("cart.checkout")

        expectLabel("checkout.remaining", contains: "12,50")
        tap("method.Cash")
        tap("quick.1") // 15,00 €
        expectLabel("checkout.change", contains: "2,50")
        screenshot("Encaissement espèces")
        tap("checkout.submit")

        waitFor("checkout.success")
        expectLabel("checkout.success.change", contains: "2,50")
        screenshot("Paiement accepté")
        tap("checkout.done")

        expectValue("table.T1", equals: "Libre")
    }

    func testAmountTypedOnKeypad() {
        loginToFloor()
        openFreeTable("T5", covers: 2)
        addProduct("Pizza Reine Royale", category: "CAT_PIZZAS")
        tap("cart.checkout")
        tap("method.Cash")
        for key in ["2", "0"] { tap("amount.key.\(key)") }
        tap("amount.key.00")
        expectLabel("checkout.tendered", contains: "20,00")
        expectLabel("checkout.change", contains: "5,50")
        tap("amount.key.delete")
        expectLabel("checkout.tendered", contains: "2,00")
    }

    func testBillSplitInThreeEqualParts() {
        loginToFloor()
        tap("table.T3") // table occupée : 36,00 €
        expectLabel("cart.total", contains: "36,00")
        tap("cart.checkout")

        tap("split.plus")
        tap("split.plus")
        expectLabel("split.count", contains: "3")
        expectLabel("split.part.1", contains: "12,00")

        tap("method.CreditCard")
        tap("checkout.submit")
        expectLabel("checkout.remaining", contains: "24,00")
        tap("checkout.submit")
        expectLabel("checkout.remaining", contains: "12,00")
        tap("method.Cash")
        tap("checkout.submit")

        waitFor("checkout.success")
        tap("checkout.done")
        expectValue("table.T3", equals: "Libre")
    }

    func testCardPaymentCannotExceedAmountDue() {
        loginToFloor()
        openFreeTable("T6", covers: 2)
        addProduct("Expresso Pur Arabica", category: "favorites")
        tap("cart.checkout")
        tap("method.CreditCard")
        tap("amount.key.5")
        tap("amount.key.00")
        XCTAssertFalse(element("checkout.submit").isEnabled)
        tap("checkout.close")
        waitFor("cart.checkout")
    }
}

final class CounterUITests: POSTestCase {
    func testTakeawaySaleGivesPickupNumber() {
        loginToFloor()
        tap("nav.counter")
        expectLabel("cart.title", contains: "Comptoir")
        addProduct("Expresso Pur Arabica", category: "favorites")
        addProduct("Fondant Chocolat Valrhona")
        expectLabel("cart.total", contains: "10,50")
        tap("cart.checkout")
        tap("method.Cash")
        tap("quick.0")
        tap("checkout.submit")

        expectLabel("checkout.success.pickup", contains: "#A-01")
        screenshot("Numéro de retrait")
        tap("checkout.done")
        expectLabel("cart.total", contains: "0,00")
    }

    func testHoldAndRecallCounterOrder() {
        loginToFloor()
        tap("floor.counter")
        addProduct("Salade César Poulet", category: "CAT_ENTREES")
        tap("cart.hold")

        let field = app.alerts.textFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        field.typeText("Marc")
        app.alerts.buttons["Mettre en attente"].tap()
        expectNotice("mise en attente")
        expectLabel("cart.total", contains: "0,00")

        tap("cart.heldOrders")
        tap("held.recall.Marc")
        waitFor("cart.line.Salade César Poulet")
        expectLabel("cart.total", contains: "9,50")
    }

    func testVoidingHeldOrderRequiresManagerPin() {
        loginToFloor("2468")
        tap("nav.counter")
        addProduct("Expresso Pur Arabica", category: "favorites")
        tap("cart.hold")
        let field = app.alerts.textFields.firstMatch
        XCTAssertTrue(field.waitForExistence(timeout: 5))
        field.typeText("Julie")
        app.alerts.buttons["Mettre en attente"].tap()

        tap("cart.heldOrders")
        tap("held.void.Julie")
        for digit in "2468" { tap("supervisor.key.\(digit)") }
        expectNotice("Autorisation insuffisante")
        for digit in "1234" { tap("supervisor.key.\(digit)") }
        expectNotice("annulée")
        XCTAssertFalse(element("held.recall.Julie").exists)
    }
}

final class KitchenAndReportsUITests: POSTestCase {
    func testBumpMovesTicketToReady() {
        login("5678")
        expectLabel("kds.count.InPreparation", contains: "1", timeout: 10)
        expectLabel("kds.count.Ready", contains: "1")
        element("kds.ticket.InPreparation").tap()
        expectLabel("kds.count.Ready", contains: "2")
        expectLabel("kds.count.InPreparation", contains: "0")
    }

    func testFiscalReportsAreLockedForWaiters() {
        loginToFloor("2468")
        tap("nav.reports")
        waitFor("fiscal.locked")
    }

    func testManagerSeesXReportAndSealsZClosure() {
        loginToFloor("1234")
        openFreeTable("T7", covers: 2)
        addProduct("Pizza Margherita AOP", category: "CAT_PIZZAS")
        tap("cart.checkout")
        tap("method.CreditCard")
        tap("checkout.submit")
        tap("checkout.done")

        tap("nav.reports")
        expectLabel("fiscal.x.total", contains: "12,50")
        screenshot("Rapport X")
        tap("fiscal.closeDay")
        app.buttons["Sceller la clôture Z"].firstMatch.tap()
        expectNotice("Clôture Z n°1 scellée")
        expectLabel("fiscal.z.total", contains: "12,50")
        expectLabel("fiscal.x.total", contains: "0,00")
    }
}
