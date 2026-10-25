import XCTest

/// Mode autonome : tout vit sur l'iPad (`LocalPosAPI` sur SQLite en mémoire pendant les tests).
final class StandaloneUITests: PosUITestCase {

    /// Saisit un champ du formulaire de première configuration.
    private func fill(_ id: String, _ text: String) {
        let field = element(id)
        XCTAssertTrue(field.waitForExistence(timeout: 10), "« \(id) » introuvable")
        field.tap()
        field.typeText(text)
    }

    private func fillFirstRun(pin: String = "4321", confirmation: String = "4321") {
        fill("setup.name", "Marie Curie")
        fill("setup.pin", pin)
        fill("setup.pinConfirm", confirmation)
        fill("setup.company", "Chez Marie")
        fill("setup.siret", "12345678901234")
    }

    private func openTable(_ number: String, covers: Int = 2) {
        tap("nav.floor")
        tap("table.\(number)")
        tap("covers.\(covers)")
        tap("covers.confirm")
        waitLabel("ticket.title", contains: "Table \(number)")
    }

    func testModeChoiceThenFirstRunThenUnlock() {
        launchStandalone(modeChoice: true, pin: nil)
        assertExists("mode.server", timeout: 10)
        tap("mode.standalone")
        assertExists("setup.name", timeout: 10)
        assertNotExists("pin.1")

        fillFirstRun()
        tap("setup.submit")
        assertExists("pin.1", timeout: 10)
        assertExists("lock.standalone")
        typePin("4321")
        assertExists("nav.order", timeout: 10)
        assertNotExists("standalone.banner")
        XCTAssertTrue(label(of: "header.operator").contains("Marie Curie"))
    }

    func testBackupReminderAndSettingsAppearWhenStandaloneIsChosenAtRuntime() {
        launchStandalone(modeChoice: true, pin: nil)
        tap("mode.standalone", timeout: 10)
        assertExists("setup.name", timeout: 10)
        fillFirstRun()
        tap("setup.submit")
        assertExists("pin.1", timeout: 10)
        typePin("4321")
        assertExists("nav.order", timeout: 10)
        assertExists("backup.banner", timeout: 10)
        tap("nav.admin")
        tap("admin.network")
        assertExists("backup.passphrase.set")
    }

    func testChoosingServerModeLeadsToPairing() {
        launchStandalone(modeChoice: true, pin: nil)
        tap("mode.server", timeout: 10)
        assertExists("pairing.code", timeout: 10)
        assertNotExists("setup.name")
    }

    func testBlankInstallationRequiresFirstRunBeforeAnyPin() {
        launchStandalone(blank: true, pin: nil)
        assertExists("setup.name", timeout: 10)
        assertNotExists("pin.1")
        assertNotExists("nav.order")
    }

    func testFirstRunRejectsMismatchedPins() {
        launchStandalone(blank: true, pin: nil)
        fillFirstRun(pin: "4321", confirmation: "1234")
        tap("setup.submit")
        waitLabel("setup.error", contains: "ne correspondent pas")
        assertNotExists("pin.1")
    }

    func testFullTableJourneyWorksOnTheLocalBackend() {
        launchStandalone()
        assertNotExists("standalone.banner")
        openTable("T1", covers: 3)
        addProduct("Pizza Margherita AOP")
        addProduct("Pizza Margherita AOP")
        addProduct("Tiramisu Maison")
        waitLabel("line.qty.Pizza Margherita AOP", contains: "2")
        waitLabel("ticket.total", contains: "32,50")

        tap("ticket.send")
        waitToast(containing: "envoyée en cuisine")
        assertExists("table.T1")
        XCTAssertTrue(label(of: "table.T1").contains("32,50"))

        tap("table.T1")
        waitLabel("ticket.total", contains: "32,50")
        tap("ticket.pay")
        tap("payment.method.cash")
        tap("payment.cash.50")
        waitLabel("payment.change", contains: "17,50")
        tap("payment.validate")
        waitLabel("result.change", contains: "17,50")
        tap("result.done")
        assertExists("table.T1")
        XCTAssertTrue(label(of: "table.T1").contains("Libre"))
    }

    func testFiscalSectionIsVisibleForManagerAndHiddenForWaiter() {
        launchStandalone()
        assertExists("nav.admin")
        assertExists("nav.fiscal")
        tap("nav.fiscal")
        assertExists("fiscal.previewX")
        app.terminate()

        launchStandalone(pin: "2468")
        assertExists("nav.floor")
        assertNotExists("nav.fiscal")
        assertNotExists("nav.admin")
        assertNotExists("standalone.banner")
    }

    func testNoNonFiscalBannerAndABackupReminderOnAFreshInstallation() {
        launchStandalone()
        assertNotExists("standalone.banner")
        assertExists("backup.banner", timeout: 10)
    }

    func testBackupSettingsExportClearsTheReminder() throws {
        let folder = NSTemporaryDirectory() + "ui-backup-\(UUID().uuidString)"
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        launchStandalone(backupFolder: folder)
        assertExists("backup.banner", timeout: 10)

        tap("nav.admin")
        tap("admin.network")
        tap("backup.passphrase.set")
        fill("backup.passphrase.field", "phrase de test 1")
        fill("backup.passphrase.confirm", "phrase de test 1")
        tap("backup.passphrase.save")

        tap("backup.export")
        fill("backup.export.passphrase", "phrase de test 1")
        fill("backup.export.confirmPassphrase", "phrase de test 1")
        tap("backup.export.confirm")

        assertNotExists("backup.banner")
        XCTAssertFalse(label(of: "backup.lastSuccess").isEmpty)
    }

    func testExportWithDifferentPassphrasesIsRefused() throws {
        let folder = NSTemporaryDirectory() + "ui-backup-\(UUID().uuidString)"
        try FileManager.default.createDirectory(atPath: folder, withIntermediateDirectories: true)
        launchStandalone(backupFolder: folder)
        tap("nav.admin")
        tap("admin.network")
        tap("backup.export")
        fill("backup.export.passphrase", "phrase de test 1")
        fill("backup.export.confirmPassphrase", "phrase de test 2")
        tap("backup.export.confirm")
        assertExists("backup.export.mismatch")
        assertExists("backup.export.passphrase")
        XCTAssertTrue(try FileManager.default.contentsOfDirectory(atPath: folder).isEmpty)
    }

    func testAPassphraseMismatchIsRefused() {
        launchStandalone()
        tap("nav.admin")
        tap("admin.network")
        tap("backup.passphrase.set")
        fill("backup.passphrase.field", "phrase de test 1")
        fill("backup.passphrase.confirm", "phrase de test 2")
        tap("backup.passphrase.save")
        assertExists("backup.passphrase.field")
    }

    /// Mois précédent dans le fuseau du simulateur, comme la clé proposée par l'écran Fiscal.
    /// ponytail: lancé à minuit pile un 1er du mois, l'app et le test pourraient voir deux mois différents ; relancer suffit.
    private func previousMonthKey() -> String {
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = .current
        let previous = calendar.date(byAdding: .month, value: -1, to: Date())!
        return String(format: "%04d-%02d", calendar.component(.year, from: previous), calendar.component(.month, from: previous))
    }

    /// Mode autonome (base de démonstration, horloge réelle) : le mois précédent n'a ni reçu ni Z, sa clôture est donc permise.
    func testStandaloneClosureArchiveFecAndGreenVerification() {
        launchStandalone()
        tap("nav.fiscal")
        let key = previousMonthKey()

        tap("fiscal.executePeriodClosure")
        waitToast(containing: "Clôture de période scellée")
        tap("fiscal.executePeriodClosure")
        waitToast(containing: "déjà clôturée") // refus métier lisible

        tap("fiscal.archivePeriodClosure_\(key)")
        waitToast(containing: "Archive générée")
        assertExists("fiscal.archived_\(key)")
        tap("fiscal.shareArchive_\(key)")
        assertExists("fiscal.archiveShare")

        tap("fec.generate")
        waitLabel("fec.status", contains: "généré")
        assertExists("fec.share")

        tap("fiscal.verifyChains")
        waitLabel("fiscal.verificationStatus", contains: "intègres")
        assertExists("fiscal.chain.period_closures")
        assertExists("fiscal.chain.archives")
        screenshot("Fiscal autonome")
    }

    /// Éditeur d'imprimante autonome : transport Epson, recherche simulée, résultat → identifiant et IP remplis.
    func testPrinterSearchFillsEditor() {
        launchStandalone()
        tap("nav.admin")
        tap("admin.printers")
        tap("printer.add")
        assertExists("printer.transport")
        element("printer.transport").tap()
        app.buttons["Epson"].firstMatch.tap()
        assertExists("printer.deviceId")
        tap("printer.search")
        assertExists("printer.search.result.0", timeout: 10)
        assertExists("printer.search.result.1")
        element("printer.search.result.0").tap()
        let id = element("printer.deviceId")
        XCTAssertTrue((id.value as? String)?.contains("TM_T88;TCP:192.168.1.60") == true, "identifiant : \(String(describing: id.value))")
        XCTAssertEqual(element("printer.name").value as? String, "TM-T88VI")
    }
}
