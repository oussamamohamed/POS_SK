import XCTest

/// Vérifie que l'app respecte la langue système (String Catalog) et garde le pavé PIN en LTR même en arabe.
final class LocalizationUITests: PosUITestCase {
    func testArabicShowsArabicLabelsAndKeepsKeypadLeftToRight() {
        let app = launch(pin: nil, language: "ar")
        XCTAssertTrue(app.staticTexts["الصندوق مقفل"].waitForExistence(timeout: 5))
        let one = app.buttons["pin.1"].frame
        let three = app.buttons["pin.3"].frame
        XCTAssertLessThan(one.minX, three.minX)
    }

    func testEnglish() {
        let app = launch(pin: nil, language: "en")
        XCTAssertTrue(app.staticTexts["Terminal locked"].waitForExistence(timeout: 5))
    }
}
