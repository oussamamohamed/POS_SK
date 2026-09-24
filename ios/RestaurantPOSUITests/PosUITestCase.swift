import XCTest

/// Base des tests UI : lance l'app sur le backend en mémoire (`-UITestMode`), donc sans serveur,
/// avec des données identiques à chaque lancement.
class PosUITestCase: XCTestCase {
    var app: XCUIApplication!

    override func setUpWithError() throws {
        continueAfterFailure = false
        XCUIDevice.shared.orientation = .landscapeLeft
    }

    override func tearDownWithError() throws {
        if let app, testRun?.hasSucceeded == false {
            let attachment = XCTAttachment(screenshot: app.screenshot())
            attachment.name = "Échec — \(name)"
            attachment.lifetime = .keepAlways
            add(attachment)
        }
        app?.terminate()
    }

    /// - Parameters:
    ///   - pin: déverrouillage automatique (nil = rester sur l'écran PIN).
    ///   - section: écran initial après connexion.
    @discardableResult
    func launch(pin: String? = "1234", section: String? = nil, happyHour: Bool = false) -> XCUIApplication {
        app = XCUIApplication()
        app.launchArguments = ["-UITestMode"]
        if let pin { app.launchArguments += ["-UITestPin", pin] }
        if let section { app.launchArguments += ["-UITestSection", section] }
        if happyHour { app.launchArguments.append("-UITestHappyHour") }
        app.launch()
        if pin != nil {
            XCTAssertTrue(app.buttons["nav.order"].waitForExistence(timeout: 10), "La coque principale doit s'afficher après connexion")
        }
        return app
    }

    // MARK: Raccourcis

    func element(_ id: String) -> XCUIElement {
        app.descendants(matching: .any)[id].firstMatch
    }

    func tap(_ id: String, timeout: TimeInterval = 5, file: StaticString = #filePath, line: UInt = #line) {
        let e = element(id)
        XCTAssertTrue(e.waitForExistence(timeout: timeout), "« \(id) » introuvable", file: file, line: line)
        e.tap()
    }

    func typePin(_ pin: String, prefix: String = "pin") {
        for digit in pin { tap("\(prefix).\(digit)") }
    }

    func addProduct(_ name: String) { tap("product.\(name)") }

    func label(of id: String) -> String {
        let e = element(id)
        _ = e.waitForExistence(timeout: 5)
        return e.label
    }

    /// Attend qu'un libellé contienne une valeur (les montants s'animent).
    func waitLabel(_ id: String, contains text: String, timeout: TimeInterval = 5, file: StaticString = #filePath, line: UInt = #line) {
        let e = element(id)
        let predicate = NSPredicate(format: "label CONTAINS %@", text)
        let exp = expectation(for: predicate, evaluatedWith: e)
        let result = XCTWaiter.wait(for: [exp], timeout: timeout)
        XCTAssertEqual(result, .completed, "« \(id) » devrait contenir « \(text) » (valeur : « \(e.label) »)", file: file, line: line)
    }

    func waitToast(containing text: String, timeout: TimeInterval = 5, file: StaticString = #filePath, line: UInt = #line) {
        let toast = app.descendants(matching: .any).matching(identifier: "toast").matching(NSPredicate(format: "label CONTAINS %@", text)).firstMatch
        XCTAssertTrue(toast.waitForExistence(timeout: timeout), "Toast « \(text) » attendu", file: file, line: line)
    }

    func assertExists(_ id: String, timeout: TimeInterval = 5, file: StaticString = #filePath, line: UInt = #line) {
        XCTAssertTrue(element(id).waitForExistence(timeout: timeout), "« \(id) » devrait être visible", file: file, line: line)
    }

    func assertNotExists(_ id: String, timeout: TimeInterval = 2, file: StaticString = #filePath, line: UInt = #line) {
        let gone = NSPredicate(format: "exists == false")
        let result = XCTWaiter.wait(for: [expectation(for: gone, evaluatedWith: element(id))], timeout: timeout)
        XCTAssertEqual(result, .completed, "« \(id) » ne devrait pas être visible", file: file, line: line)
    }

    func screenshot(_ name: String) {
        let attachment = XCTAttachment(screenshot: app.screenshot())
        attachment.name = name
        attachment.lifetime = .keepAlways
        add(attachment)
    }
}
