import XCTest

/// Mode autonome : tout vit sur l'iPad (`LocalPosAPI` sur SQLite en mémoire pendant les tests).
final class StandaloneUITests: PosUITestCase {

    /// Saisit un champ du formulaire de première configuration.
    private func fill(_ id: String, _ text: String) {
        let field = element(id)
        XCTAssertTrue(field.waitForExistence(timeout: 10), "« \(id) » introuvable")
        field.tap()
        field.typeText(text)
    }

    private func fillFirstRun(pin: String = "4321", confirmation: String = "4321") {
        fill("setup.name", "Marie Curie")
        fill("setup.pin", pin)
        fill("setup.pinConfirm", confirmation)
        fill("setup.company", "Chez Marie")
        fill("setup.siret", "12345678901234")
    }

    private func openTable(_ number: String, covers: Int = 2) {
        tap("nav.floor")
        tap("table.\(number)")
        tap("covers.\(covers)")
        tap("covers.confirm")
        waitLabel("ticket.title", contains: "Table \(number)")
    }

    func testModeChoiceThenFirstRunThenUnlock() {
        launchStandalone(modeChoice: true, pin: nil)
        assertExists("mode.server", timeout: 10)
        tap("mode.standalone")
        assertExists("setup.name", timeout: 10)
        assertNotExists("pin.1")

        fillFirstRun()
        tap("setup.submit")
        assertExists("pin.1", timeout: 10)
        assertExists("lock.standalone")
        typePin("4321")
        assertExists("nav.order", timeout: 10)
        assertExists("standalone.banner")
        XCTAssertTrue(label(of: "header.operator").contains("Marie Curie"))
    }

    func testChoosingServerModeLeadsToPairing() {
        launchStandalone(modeChoice: true, pin: nil)
        tap("mode.server", timeout: 10)
        assertExists("pairing.code", timeout: 10)
        assertNotExists("setup.name")
    }

    func testBlankInstallationRequiresFirstRunBeforeAnyPin() {
        launchStandalone(blank: true, pin: nil)
        assertExists("setup.name", timeout: 10)
        assertNotExists("pin.1")
        assertNotExists("nav.order")
    }

    func testFirstRunRejectsMismatchedPins() {
        launchStandalone(blank: true, pin: nil)
        fillFirstRun(pin: "4321", confirmation: "1234")
        tap("setup.submit")
        waitLabel("setup.error", contains: "ne correspondent pas")
        assertNotExists("pin.1")
    }

    func testFullTableJourneyWorksOnTheLocalBackend() {
        launchStandalone()
        assertExists("standalone.banner")
        openTable("T1", covers: 3)
        addProduct("Pizza Margherita AOP")
        addProduct("Pizza Margherita AOP")
        addProduct("Tiramisu Maison")
        waitLabel("line.qty.Pizza Margherita AOP", contains: "2")
        waitLabel("ticket.total", contains: "32,50")

        tap("ticket.send")
        waitToast(containing: "envoyée en cuisine")
        assertExists("table.T1")
        XCTAssertTrue(label(of: "table.T1").contains("32,50"))

        tap("table.T1")
        waitLabel("ticket.total", contains: "32,50")
        tap("ticket.pay")
        tap("payment.method.cash")
        tap("payment.cash.50")
        waitLabel("payment.change", contains: "17,50")
        tap("payment.validate")
        waitLabel("result.change", contains: "17,50")
        tap("result.done")
        assertExists("table.T1")
        XCTAssertTrue(label(of: "table.T1").contains("Libre"))
    }

    func testFiscalSectionIsHiddenAndWaiterKeepsHisRights() {
        launchStandalone()
        assertExists("nav.admin")
        assertNotExists("nav.fiscal")
        app.terminate()

        launchStandalone(pin: "2468")
        assertExists("nav.floor")
        assertNotExists("nav.fiscal")
        assertNotExists("nav.admin")
        assertExists("standalone.banner")
    }
}
