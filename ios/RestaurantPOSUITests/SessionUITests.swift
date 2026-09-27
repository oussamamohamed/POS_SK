import XCTest

final class SessionUITests: PosUITestCase {
    func testValidPinUnlocksTerminal() {
        launch(pin: nil)
        screenshot("Écran de verrouillage")
        typePin("1234")
        assertExists("nav.order", timeout: 10)
        XCTAssertTrue(label(of: "header.operator").contains("Alexandre Dupont"))
    }

    func testInvalidPinShowsError() {
        launch(pin: nil)
        typePin("0000")
        waitLabel("pin.error", contains: "incorrects")
        assertNotExists("nav.order")
    }

    func testDeleteKeyCorrectsEntry() {
        launch(pin: nil)
        tap("pin.9")
        tap("pin.delete")
        typePin("1234")
        assertExists("nav.order", timeout: 10)
    }

    func testLockReturnsToPinScreen() {
        launch()
        tap("nav.lock")
        assertExists("pin.1")
        assertNotExists("nav.order")
    }

    func testWaiterDoesNotSeeManagerSections() {
        launch(pin: "2468")
        assertExists("nav.floor")
        assertNotExists("nav.fiscal")
        assertNotExists("nav.admin")
    }
}
