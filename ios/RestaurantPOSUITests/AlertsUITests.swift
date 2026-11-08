import XCTest

/// Spec 024, lot B : seuils d'alerte et clôture d'une alerte avec note (`-UITestMode`, alerte de démonstration).
final class AlertsUITests: PosUITestCase {
    func testThresholdsAndResolvingAnAlertWithANote() {
        launch()
        tap("nav.admin")
        tap("admin.alerts")
        assertExists("alerts.toggle.voids_count")
        tap("alerts.toggle.voids_count")

        assertExists("alerts.row.voids_count")
        tap("alerts.resolve.voids_count")
        tap("alerts.note")
        element("alerts.note").typeText("Erreurs de saisie vérifiées")
        tap("alerts.resolveConfirm")
        assertNotExists("alerts.row.voids_count", timeout: 5)

        tap("alerts.state")
        app.buttons["Traitées"].firstMatch.tap()
        assertExists("alerts.row.voids_count")
    }

    func testTheBellListsTheOpenAlert() {
        launch()
        tap("needs.bell")
        XCTAssertTrue(app.staticTexts["Annulations"].firstMatch.waitForExistence(timeout: 5), "l'alerte d'annulations doit figurer dans la cloche")
    }
}
