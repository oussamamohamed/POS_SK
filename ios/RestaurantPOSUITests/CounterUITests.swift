import XCTest

/// Vente directe au comptoir / à emporter.
final class CounterUITests: PosUITestCase {
    func testFastCashShowsChangeAndPickupNumber() {
        launch()
        waitLabel("ticket.title", contains: "Comptoir")
        addProduct("Pizza Margherita AOP")
        waitLabel("ticket.total", contains: "12,50")
        tap("fastcash.20")
        waitLabel("change.amount", contains: "7,50")
        waitLabel("change.pickup", contains: "#A-01")
        screenshot("Rendu monnaie comptoir")
        tap("change.done")
        waitLabel("ticket.total", contains: "0,00")
    }

    func testCardCheckoutWithBuzzer() {
        launch()
        addProduct("Café Gourmand")
        tap("ticket.pay")
        tap("payment.method.creditCard")
        let buzzer = element("payment.buzzer")
        buzzer.tap()
        buzzer.typeText("7")
        tap("payment.validate")
        waitLabel("change.pickup", contains: "#A-01")
        tap("change.done")
    }

    func testHoldRecallAndSupervisorVoid() {
        launch()
        addProduct("Café Gourmand")
        tap("ticket.hold")
        let field = element("hold.label")
        field.tap()
        field.typeText("Marie")
        tap("hold.confirm")
        waitToast(containing: "mise en attente")
        waitLabel("ticket.heldQueue", contains: "1")

        tap("ticket.heldQueue")
        tap("held.recall.Marie")
        assertExists("line.Café Gourmand")

        tap("ticket.hold")
        tap("hold.confirm")
        tap("ticket.heldQueue")
        let voidButton = app.buttons.matching(NSPredicate(format: "identifier BEGINSWITH 'held.void.'")).firstMatch
        XCTAssertTrue(voidButton.waitForExistence(timeout: 5))
        voidButton.tap()
        typePin("2468", prefix: "void.pin") // serveuse : refusé
        tap("void.confirm")
        waitToast(containing: "Autorisation insuffisante")
        typePin("1234", prefix: "void.pin")
        tap("void.confirm")
        waitToast(containing: "annulée")
    }

    func testTakeawayVatSwitch() {
        launch()
        addProduct("Pizza Margherita AOP")
        XCTAssertTrue(app.staticTexts["TVA 5,5 %"].waitForExistence(timeout: 5))
        app.buttons["Sur place"].tap()
        XCTAssertTrue(app.staticTexts["TVA 10 %"].waitForExistence(timeout: 5))
    }
}
