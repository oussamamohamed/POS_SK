import XCTest

/// Spec 024, lots C et D : carte « À surveiller » et résumé de fin de journée depuis Encaissements (`-UITestMode`, puis mode autonome).
final class InsightsUITests: PosUITestCase {
    func testInsightsCardAndDailySummaryAreReachableFromTakings() {
        launch()
        tap("nav.takings")
        assertExists("insights.card")
        assertExists("insights.card.avgTicket") // démonstration : ticket moyen −20 % vs semaine passée

        tap("takings.openSummary")
        assertExists("summary.sheet")
        waitLabel("summary.sales", contains: "645")
        assertExists("summary.cash")
        assertExists("summary.share")
        tap("summary.close")
        assertNotExists("summary.sheet", timeout: 3)
    }

    func testStandaloneModeShowsNotEnoughHistoryAndNoClosure() {
        launchStandalone()
        tap("nav.takings")
        assertExists("insights.insufficient")
        tap("takings.openSummary")
        assertExists("summary.none")
    }
}
