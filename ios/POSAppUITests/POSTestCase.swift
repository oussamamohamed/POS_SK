import XCTest

/// Base des tests UI : lance l'app en mode test (serveur simulé déterministe, iPad paysage)
/// et fournit des raccourcis lisibles pour piloter l'interface.
class POSTestCase: XCTestCase {
    var app: XCUIApplication!

    override func setUpWithError() throws {
        continueAfterFailure = false
        XCUIDevice.shared.orientation = .landscapeLeft
        app = XCUIApplication()
        app.launchArguments = ["-uiTesting"]
        app.launch()
    }

    override func tearDownWithError() throws {
        // Capture systématique : visible dans le rapport .xcresult (CI).
        if let app {
            let attachment = XCTAttachment(screenshot: app.screenshot())
            attachment.name = "Fin — \(name)"
            attachment.lifetime = .keepAlways
            add(attachment)
        }
        app = nil
    }

    // MARK: Éléments

    func element(_ identifier: String) -> XCUIElement {
        app.descendants(matching: .any).matching(identifier: identifier).firstMatch
    }

    @discardableResult
    func waitFor(_ identifier: String, timeout: TimeInterval = 5, file: StaticString = #filePath, line: UInt = #line) -> XCUIElement {
        let target = element(identifier)
        XCTAssertTrue(target.waitForExistence(timeout: timeout), "« \(identifier) » introuvable", file: file, line: line)
        return target
    }

    func tap(_ identifier: String, file: StaticString = #filePath, line: UInt = #line) {
        waitFor(identifier, file: file, line: line).tap()
    }

    /// Attend que le libellé d'un élément contienne un texte (montants mis à jour, compteurs…).
    func expectLabel(_ identifier: String, contains text: String, timeout: TimeInterval = 5, file: StaticString = #filePath, line: UInt = #line) {
        let target = waitFor(identifier, file: file, line: line)
        let predicate = NSPredicate(format: "label CONTAINS %@", text)
        let expectation = XCTNSPredicateExpectation(predicate: predicate, object: target)
        let result = XCTWaiter().wait(for: [expectation], timeout: timeout)
        XCTAssertEqual(result, .completed, "« \(identifier) » affiche « \(target.label) » au lieu de « \(text) »", file: file, line: line)
    }

    func expectValue(_ identifier: String, equals value: String, timeout: TimeInterval = 5, file: StaticString = #filePath, line: UInt = #line) {
        let target = waitFor(identifier, file: file, line: line)
        let predicate = NSPredicate(format: "value == %@", value)
        let expectation = XCTNSPredicateExpectation(predicate: predicate, object: target)
        let result = XCTWaiter().wait(for: [expectation], timeout: timeout)
        XCTAssertEqual(result, .completed, "« \(identifier) » vaut « \(String(describing: target.value)) » au lieu de « \(value) »", file: file, line: line)
    }

    func expectNotice(_ text: String, file: StaticString = #filePath, line: UInt = #line) {
        expectLabel("notice.banner", contains: text, file: file, line: line)
    }

    func screenshot(_ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }

    // MARK: Parcours

    /// Saisie du PIN sur le pavé à l'écran (validation automatique à 4 chiffres).
    func login(_ pin: String = "1234", file: StaticString = #filePath, line: UInt = #line) {
        waitFor("pin.key.1", timeout: 10, file: file, line: line)
        for digit in pin {
            tap("pin.key.\(digit)", file: file, line: line)
        }
    }

    func loginToFloor(_ pin: String = "1234", file: StaticString = #filePath, line: UInt = #line) {
        login(pin, file: file, line: line)
        waitFor("table.T1", timeout: 10, file: file, line: line)
    }

    /// Ouvre une table libre avec un nombre de couverts.
    func openFreeTable(_ number: String, covers: Int) {
        tap("table.\(number)")
        tap("covers.\(covers)")
        tap("covers.confirm")
        expectLabel("cart.title", contains: "Table \(number)")
    }

    func addProduct(_ name: String, category: String? = nil) {
        if let category {
            tap("category.\(category)")
        }
        tap("product.\(name)")
    }
}
