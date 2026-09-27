import XCTest

final class PairingUITests: PosUITestCase {
    func testPairingWithCodeLeadsToPinScreen() {
        launch(pin: nil, paired: false)
        let code = element("pairing.code")
        XCTAssertTrue(code.waitForExistence(timeout: 10), "L'écran d'appairage doit s'afficher sur un iPad neuf")
        code.tap()
        code.typeText("testcode")
        tap("pairing.submit")
        assertExists("pin.1", timeout: 10)
    }

    func testInvalidCodeShowsError() {
        launch(pin: nil, paired: false)
        let code = element("pairing.code")
        XCTAssertTrue(code.waitForExistence(timeout: 10))
        code.tap()
        code.typeText("WRONG123")
        tap("pairing.submit")
        waitLabel("pairing.error", contains: "invalide")
        assertNotExists("pin.1")
    }

    func testUnpairFromServerSettingsReturnsToPairing() {
        launch(pin: nil)
        tap("lock.server")
        tap("settings.unpair")
        assertExists("pairing.code", timeout: 10)
    }
}
