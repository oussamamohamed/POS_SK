import XCTest

/// Impression : langue des bons cuisine, poste des familles, état des imprimantes, ticket à table.
final class PrintingUITests: PosUITestCase {

    func testKitchenTicketLanguagePicker() {
        launch(section: "admin")
        tap("admin.network")
        let picker = element("admin.kitchenTicketLanguage")
        for _ in 0..<3 where !picker.exists { app.swipeUp() }
        XCTAssertTrue(picker.waitForExistence(timeout: 5))
        picker.tap()
        app.buttons["العربية"].firstMatch.tap()
        waitToast(containing: "Langue du bon cuisine enregistrée")
    }

    func testPrinterStatusShownPerPrinter() {
        launch(section: "admin")
        tap("admin.printers")
        let status = app.descendants(matching: .any).matching(identifier: "printer.status").firstMatch
        XCTAssertTrue(status.waitForExistence(timeout: 5))
        XCTAssertTrue(status.label.contains("État inconnu"), "statut : \(status.label)")
    }

    func testPrinterEditorHasTextModeToggle() {
        launch(section: "admin")
        tap("admin.printers")
        let row = app.staticTexts["Imprimante Caisse Comptoir"].firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        row.tap()
        XCTAssertTrue(app.switches["printer.textMode"].waitForExistence(timeout: 5))
    }

    func testCategoryStationPicker() {
        launch(section: "admin")
        tap("admin.catalog")
        let chip = element("category.chip.CAT_STARTERS")
        XCTAssertTrue(chip.waitForExistence(timeout: 5))
        chip.press(forDuration: 1)
        tap("category.edit")
        assertExists("category.station")
    }

    func testProductStationDefaultsToCategoryStation() {
        launch(section: "admin")
        tap("admin.catalog")
        tap("catalog.addProduct")
        let picker = element("product.station")
        XCTAssertTrue(picker.waitForExistence(timeout: 5))
        XCTAssertTrue(picker.label.contains("Poste de la famille") || (picker.value as? String)?.contains("Poste de la famille") == true, "valeur : \(picker.label) / \(String(describing: picker.value))")
    }

    func testTablePaymentHasPrintReceiptToggle() {
        launch()
        tap("nav.floor")
        tap("table.T1")
        tap("covers.confirm")
        addProduct("Pizza Margherita AOP")
        tap("ticket.pay")
        assertExists("payment.printReceipt")
    }

    func testTablePaymentWithoutReceiptCheckboxShowsNoPrintWarning() {
        launch()
        tap("nav.floor")
        tap("table.T1")
        tap("covers.confirm")
        addProduct("Pizza Margherita AOP")
        tap("ticket.pay")
        tap("payment.validate")
        assertExists("result.done")
        let warning = app.descendants(matching: .any).matching(identifier: "toast").matching(NSPredicate(format: "label CONTAINS %@", "Aucune imprimante")).firstMatch
        XCTAssertFalse(warning.waitForExistence(timeout: 2), "aucun avertissement d'impression sans case cochée")
    }

    func testFailedJobShowsLocalizedKind() {
        launch(section: "admin", failedPrintJob: true)
        tap("admin.printers")
        let jobs = element("printer.jobs")
        XCTAssertTrue(jobs.waitForExistence(timeout: 5))
        jobs.buttons.firstMatch.tap()
        XCTAssertTrue(app.staticTexts["Ticket de caisse"].waitForExistence(timeout: 5))
        XCTAssertTrue(app.buttons["Relancer"].waitForExistence(timeout: 5))
    }
}
