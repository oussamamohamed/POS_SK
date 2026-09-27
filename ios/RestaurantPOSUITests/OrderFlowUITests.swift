import XCTest

/// Parcours service en salle : table → commande → cuisine → encaissement.
final class OrderFlowUITests: PosUITestCase {

    private func openTable(_ number: String, covers: Int = 2) {
        tap("nav.floor")
        tap("table.\(number)")
        tap("covers.\(covers)")
        tap("covers.confirm")
        waitLabel("ticket.title", contains: "Table \(number)")
    }

    func testTableOrderSendToKitchenAndPay() {
        launch()
        openTable("T1", covers: 3)
        addProduct("Pizza Margherita AOP")
        addProduct("Pizza Margherita AOP")
        addProduct("Tiramisu Maison")
        waitLabel("line.qty.Pizza Margherita AOP", contains: "2")
        waitLabel("ticket.total", contains: "32,50")
        screenshot("Ticket table T1")

        tap("ticket.send")
        waitToast(containing: "envoyée en cuisine")
        // Retour automatique au plan de salle, table occupée avec son total.
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

    func testModifiersAreMandatoryAndPriced() {
        launch()
        openTable("T2")
        addProduct("Entrecôte 300g")
        tap("option.Saignant")
        tap("modifiers.confirm")
        assertExists("line.Entrecôte 300g")

        addProduct("Burger Gourmet Rossini")
        tap("option.Double Cheddar Fondu")
        tap("option.Bacon Croustillant")
        tap("option.Œuf au Plat") // maximum 2 → refusé
        waitLabel("modifiers.error", contains: "Maximum 2")
        waitLabel("modifiers.price", contains: "24,00")
        let comment = element("modifiers.comment")
        comment.tap()
        comment.typeText("Sans oignons")
        tap("modifiers.confirm")
        waitLabel("ticket.total", contains: "50,00")
        screenshot("Options et commentaire cuisine")
    }

    func testDraftLineEditing() {
        launch()
        openTable("T3")
        addProduct("Café Gourmand")
        tap("line.plus.Café Gourmand")
        waitLabel("line.qty.Café Gourmand", contains: "2")
        tap("line.course.Café Gourmand")
        waitLabel("line.course.Café Gourmand", contains: "Suite")
        tap("line.minus.Café Gourmand")
        tap("line.minus.Café Gourmand")
        assertNotExists("line.Café Gourmand")
    }

    func testClearKeepsItemsAlreadyInKitchen() {
        launch()
        openTable("T4")
        addProduct("Tartare de Saumon")
        tap("ticket.send")
        tap("table.T4")
        addProduct("Tiramisu Maison")
        tap("ticket.clear")
        assertNotExists("line.Tiramisu Maison")
        assertExists("line.Tartare de Saumon")
    }

    func testGlobalDiscount() {
        launch()
        openTable("T5")
        addProduct("Entrecôte 300g")
        tap("option.À Point")
        tap("modifiers.confirm")
        tap("ticket.discount")
        let value = element("discount.value")
        XCTAssertTrue(value.waitForExistence(timeout: 5))
        tap("discount.apply") // 10 % par défaut
        waitLabel("ticket.total", contains: "23,40")
        waitToast(containing: "Remise appliquée")
    }

    func testSplitBillInThreeParts() {
        launch()
        openTable("T6")
        addProduct("Entrecôte 300g")
        tap("option.À Point")
        tap("modifiers.confirm")
        addProduct("Tiramisu Maison")
        waitLabel("ticket.total", contains: "33,50")
        tap("ticket.pay")
        app.buttons["Partager à parts égales"].tap()
        tap("payment.guests.plus")
        waitLabel("payment.guests", contains: "3")
        waitLabel("payment.amount", contains: "11,17")
        tap("payment.validate")
        waitLabel("payment.amount", contains: "11,17")
        tap("payment.validate")
        waitLabel("payment.amount", contains: "11,16")
        tap("payment.validate")
        assertExists("result.done")
        tap("result.done")
        XCTAssertTrue(label(of: "table.T6").contains("Libre"))
    }

    func testTransferToAnotherTable() {
        launch()
        openTable("T7")
        addProduct("Salade César Poulet")
        tap("ticket.transfer")
        tap("transfer.table.T8")
        tap("transfer.confirm")
        waitLabel("ticket.title", contains: "Table T8")
        assertExists("line.Salade César Poulet")
    }

    func testCategoryFilteringAndQuickKeys() {
        launch()
        tap("category.CAT_PIZZAS")
        assertExists("product.Pizza 4 Fromages")
        assertNotExists("product.Tiramisu Maison")
        tap("quickkey.Café Gourmand")
        assertExists("line.Café Gourmand")
    }

    func testAddTable() {
        launch(section: "floor")
        tap("floor.add")
        let field = element("addTable.number")
        field.tap()
        field.typeText("Terrasse 1")
        tap("addTable.confirm")
        assertExists("table.Terrasse 1")
    }
}
