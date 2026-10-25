import XCTest

/// Écran Gestion → Imprimantes (lot 4d) en mode autonome : transport, recherche simulée
/// (`FakePrinterDiscovery`), enregistrement, page de test (`FakePrinterTransport`).
final class PrinterAdminUITests: PosUITestCase {
    private func openEditor(language: String = "fr") {
        launchStandalone(language: language)
        tap("nav.admin")
        tap("admin.printers")
        tap("printer.add")
    }

    private func choose(transport: String) {
        tap("printer.transport")
        let option = app.buttons[transport].firstMatch
        XCTAssertTrue(option.waitForExistence(timeout: 5), "Transport « \(transport) » introuvable")
        option.tap()
        assertExists("printer.deviceId")
    }

    /// Epson : transport → recherche → résultat 0 (TM-T88VI) → enregistrement.
    private func addEpsonFromSearch(saveLabel: String? = nil) {
        choose(transport: "Epson")
        tap("printer.search")
        assertExists("printer.search.result.0", timeout: 10)
        element("printer.search.result.0").tap()
        let id = element("printer.deviceId")
        XCTAssertTrue((id.value as? String)?.contains("TM_T88;TCP:192.168.1.60") == true, "identifiant : \(String(describing: id.value))")
        tap("printer.save")
    }

    private func fill(_ id: String, _ text: String) {
        tap(id)
        element(id).typeText(text)
    }

    func testAddEpsonPrinterAppearsInList() {
        openEditor()
        addEpsonFromSearch()
        XCTAssertTrue(app.staticTexts["TM-T88VI"].firstMatch.waitForExistence(timeout: 5), "la ligne TM-T88VI doit apparaître")
        assertNotExists("printer.save")
    }

    func testTestButtonShowsSuccessToast() {
        openEditor()
        addEpsonFromSearch()
        tap("printer.test.TM-T88VI")
        waitToast(containing: "Test d'impression réussi")
    }

    func testStarBluetoothWithoutIpCanBeSaved() {
        openEditor()
        fill("printer.name", "Bar BT")
        choose(transport: "Star")
        tap("printer.search")
        assertExists("printer.search.result.1", timeout: 10)
        element("printer.search.result.1").tap()
        XCTAssertTrue((element("printer.deviceId").value as? String)?.hasPrefix("bluetooth:") == true)
        tap("printer.save")
        XCTAssertTrue(app.staticTexts["Bar BT"].firstMatch.waitForExistence(timeout: 5), "la ligne Bar BT doit apparaître")
        assertNotExists("printer.save")
    }

    func testInvalidIdentifierWarnsAndKeepsSheetOpen() {
        openEditor()
        fill("printer.name", "Cassée")
        choose(transport: "Epson")
        fill("printer.deviceId", "n-importe-quoi")
        tap("printer.save")
        waitToast(containing: "Identifiant d'imprimante invalide")
        assertExists("printer.save")
        assertExists("printer.deviceId")
    }

    func testEpsonJourneyInEnglish() {
        openEditor(language: "en")
        XCTAssertTrue(element("printer.transport").label.hasPrefix("Transport"))
        choose(transport: "Epson")
        XCTAssertEqual(element("printer.search").label, "Search")
        addEpsonFromSearch()
        XCTAssertTrue(app.staticTexts["TM-T88VI"].firstMatch.waitForExistence(timeout: 5))
    }

    func testEpsonJourneyInArabic() {
        openEditor(language: "ar")
        XCTAssertTrue(element("printer.transport").label.hasPrefix("وسيلة الاتصال"))
        choose(transport: "Epson")
        XCTAssertEqual(element("printer.search").label, "بحث")
        addEpsonFromSearch()
        XCTAssertTrue(app.staticTexts["TM-T88VI"].firstMatch.waitForExistence(timeout: 5))
    }

    func testServerModeHasNoTransportPicker() {
        launch(section: "admin")
        tap("admin.printers")
        tap("printer.add")
        assertExists("printer.ip")
        assertNotExists("printer.transport")
        assertNotExists("printer.search")
    }
}
