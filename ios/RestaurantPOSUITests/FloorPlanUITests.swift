import XCTest

/// Plan de salle dessiné (volet, ouverture d'une table) et tuiles de l'écran cuisine.
final class FloorPlanUITests: PosUITestCase {

    func testPlanShowsTablesAndPaneThenOpensAFreeTable() {
        launch(section: "floor", floorMode: "plan")
        assertExists("floor.plan")
        assertExists("floor.pane")
        tap("plan.table.T1")
        assertExists("floor.open")
        tap("floor.open")
        tap("covers.confirm")
        assertExists("nav.order")
    }

    func testPaneCloseReturnsToTheOverview() {
        launch(section: "floor", floorMode: "plan")
        tap("plan.table.T2")
        assertExists("floor.pane.close")
        tap("floor.pane.close")
        assertNotExists("floor.open", timeout: 3)
    }

    func testKitchenShowsStatsAndBottomPanels() {
        launch(section: "kitchen")
        assertExists("kds.stats")
        assertExists("kds.allday")
        assertExists("kds.bumped")
    }
}
