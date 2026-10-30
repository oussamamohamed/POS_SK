import XCTest

/// Réservations (plan du service, liste d'attente, feuille de saisie) et analyse du menu, sur le backend en mémoire.
final class BookingsUITests: PosUITestCase {

    func testBookingsScreenShowsTimelineWaitlistAndUnplaced() {
        launch(section: "bookings")
        assertExists("bookings.timeline")
        assertExists("bookings.waitlist")
        assertExists("bookings.unplaced")
        assertExists("nav.bookings.badge")
        screenshot("Réservations")
    }

    func testCreateWaitlistEntryThenCancelIt() {
        launch(section: "bookings")
        tap("bookings.add")
        let name = element("booking.name")
        XCTAssertTrue(name.waitForExistence(timeout: 5))
        name.tap()
        name.typeText("Nguyen")
        tap("booking.waitlist")
        tap("booking.save")
        assertNotExists("booking.save", timeout: 5)
        let entry = app.staticTexts["Nguyen · 2"]
        XCTAssertTrue(entry.waitForExistence(timeout: 5))
        entry.tap()
        tap("booking.cancel")
        assertNotExists("booking.cancel", timeout: 5)
        XCTAssertFalse(app.staticTexts["Nguyen · 2"].waitForExistence(timeout: 2))
    }

    func testSeatingAnAssignedBookingOccupiesItsTable() {
        launch(section: "bookings")
        let martin = app.buttons.matching(NSPredicate(format: "label CONTAINS 'Martin'")).firstMatch
        XCTAssertTrue(martin.waitForExistence(timeout: 5))
        martin.tap()
        tap("booking.seat")
        assertNotExists("booking.seat", timeout: 5)
        tap("nav.floor")
        waitLabel("table.T1", contains: "Occupée")
    }

    func testMenuAnalysisShowsClassesAndFiltersByClass() {
        launch(section: "menu")
        assertExists("menu.chart")
        assertExists("menu.missingCost")
        tap("menu.class.0")
        XCTAssertTrue(element("menu.class.0").isSelected)
        screenshot("Analyse du menu")
    }

    /// Mode autonome (base SQLite locale) : la même saisie passe par `LocalPosAPI`.
    func testStandaloneWaitlistEntryIsStoredLocally() {
        launchStandalone()
        tap("nav.bookings")
        tap("bookings.add")
        let name = element("booking.name")
        XCTAssertTrue(name.waitForExistence(timeout: 5))
        name.tap()
        name.typeText("Okafor")
        tap("booking.waitlist")
        tap("booking.save")
        assertNotExists("booking.save", timeout: 5)
        XCTAssertTrue(app.staticTexts["Okafor · 2"].waitForExistence(timeout: 5))
    }

    func testNeedsBellListsLateBookingsAndOpensTheirScreen() {
        launch(section: "floor")
        tap("needs.bell")
        let late = app.buttons.matching(NSPredicate(format: "label CONTAINS 'Petit'")).firstMatch
        XCTAssertTrue(late.waitForExistence(timeout: 5))
        late.tap()
        assertExists("bookings.timeline")
        assertExists("nav.bookings.badge")
    }

    func testMenuAnalysisFamilyChipSortAndPick() {
        launch(section: "menu")
        assertExists("menu.chart")
        tap("menu.sort.name")
        tap("menu.cat.all")
        let row = app.descendants(matching: .any).matching(NSPredicate(format: "identifier BEGINSWITH 'menu.row.'")).firstMatch
        XCTAssertTrue(row.waitForExistence(timeout: 5))
        row.tap()
        assertExists("menu.pick")
    }

    func testTakingsScreenShowsTilesChartAndOpensFiscal() {
        launch(section: "takings")
        assertExists("takings.tiles")
        assertExists("takings.hourly")
        tap("takings.toggle")
        tap("takings.toggle")
        tap("takings.openFiscal")
        assertExists("nav.fiscal")
    }

    /// Mode autonome : les encaissements viennent des reçus fiscaux de la base locale.
    func testStandaloneTakingsScreenLoads() {
        launchStandalone()
        tap("nav.takings")
        assertExists("takings.tiles")
    }

    func testWaiterDoesNotSeeTheMenuAnalysisTab() {
        launch(pin: "2468")
        assertNotExists("nav.menu", timeout: 2)
        assertNotExists("nav.takings", timeout: 2)
        assertExists("nav.bookings")
    }
}
