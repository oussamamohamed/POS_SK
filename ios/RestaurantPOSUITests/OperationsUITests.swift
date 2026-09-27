import XCTest

/// Cuisine, Happy Hour, clôture fiscale et back-office.
final class OperationsUITests: PosUITestCase {

    func testKitchenTicketLifecycle() {
        launch(pin: "5678") // chef de cuisine : peut faire avancer les bons
        tap("nav.floor")
        tap("table.T1")
        tap("covers.confirm")
        addProduct("Entrecôte 300g")
        tap("option.Bleu")
        tap("modifiers.confirm")
        tap("ticket.send")
        tap("nav.kitchen")
        waitLabel("kds.count.pending", contains: "1")
        tap("kds.ticket.T1")
        waitLabel("kds.count.inPreparation", contains: "1")
        tap("kds.ticket.T1")
        waitLabel("kds.count.ready", contains: "1")
        screenshot("Écran cuisine")
    }

    func testHappyHourPricesAndOverride() {
        launch(happyHour: true)
        assertExists("happyhour.banner")
        tap("nav.floor")
        tap("table.T2")
        tap("covers.confirm")
        addProduct("Bière Artisanale IPA 33cl")
        waitLabel("ticket.total", contains: "5,00")
        tap("happyhour.override")
        typePin("1234", prefix: "hh.pin")
        tap("hh.stop")
        assertNotExists("happyhour.banner", timeout: 5)
    }

    func testFiscalXAndZReports() {
        launch()
        addProduct("Pizza 4 Fromages")
        tap("fastcash.exact")
        tap("change.done")
        tap("nav.fiscal")
        waitLabel("slip.title", contains: "RAPPORT X")
        waitLabel("slip.totalTtc", contains: "14,50")
        tap("fiscal.executeZ")
        let confirm = app.buttons["Clôturer et sceller la journée"].firstMatch
        XCTAssertTrue(confirm.waitForExistence(timeout: 5))
        confirm.tap()
        waitLabel("slip.title", contains: "RAPPORT Z")
        waitLabel("slip.status", contains: "scellée")
        screenshot("Clôture Z")
    }

    func testFecExport() {
        launch(section: "fiscal")
        tap("fec.generate")
        waitLabel("fec.status", contains: "généré")
        assertExists("fec.share")
    }

    func testAdminCreatesProductVisibleInSalesGrid() {
        launch(section: "admin")
        tap("admin.catalog")
        tap("catalog.addProduct")
        let name = element("product.name")
        name.tap()
        name.typeText("Mojito Maison")
        let price = element("product.price")
        price.tap()
        price.typeText("9,50")
        tap("product.save")
        assertExists("catalog.product.Mojito Maison")
        tap("nav.order")
        tap("category.CAT_STARTERS")
        assertExists("product.Mojito Maison")
    }

    func testAdminCreatesStaffWhoCanLogIn() {
        launch(section: "admin")
        tap("admin.staff")
        tap("staff.add")
        let name = element("staff.name")
        name.tap()
        name.typeText("Léa Petit")
        let pin = element("staff.pin")
        pin.tap()
        pin.typeText("4321")
        tap("staff.save")
        assertExists("staff.Léa Petit")
        tap("nav.lock")
        typePin("4321")
        assertExists("nav.order", timeout: 10)
        XCTAssertTrue(label(of: "header.operator").contains("Léa Petit"))
    }

    func testGridEditorAssignsSlot() {
        launch(section: "admin")
        tap("admin.grid")
        tap("grid.slot.3.3")
        let picker = element("slot.product")
        XCTAssertTrue(picker.waitForExistence(timeout: 5))
        picker.tap()
        app.buttons.matching(NSPredicate(format: "label BEGINSWITH 'Tiramisu Maison'")).firstMatch.tap()
        tap("slot.save")
        waitLabel("grid.slot.3.3", contains: "Tiramisu")
    }

    func testPrinterTest() {
        launch(section: "admin")
        tap("admin.printers")
        tap("printer.test.Imprimante Cuisine Chaude")
        waitToast(containing: "Ticket de test envoyé")
    }

    func testDashboardShowsSales() {
        launch()
        addProduct("Café Gourmand")
        tap("fastcash.exact")
        tap("change.done")
        tap("nav.admin")
        tap("admin.dashboard")
        waitLabel("kpi.sales", contains: "8,50")
        screenshot("Tableau de bord")
    }
}
