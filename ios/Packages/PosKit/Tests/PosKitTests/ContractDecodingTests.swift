import Foundation
import Testing
@testable import PosKit

/// Tests de contrat : les fixtures sont de vraies réponses capturées sur `RestaurantPos.Api`.
/// Si le backend change un format, ces tests cassent avant l'iPad.
@Suite("Contrats JSON de l'API")
struct ContractDecodingTests {
    static func fixture(_ name: String) throws -> Data {
        let url = try #require(Bundle.module.url(forResource: name, withExtension: "json", subdirectory: "Fixtures"))
        return try Data(contentsOf: url)
    }

    func decode<T: Decodable>(_ type: T.Type, _ name: String) throws -> T {
        try PosJSON.makeDecoder().decode(T.self, from: Self.fixture(name))
    }

    @Test func login() throws {
        let login = try decode(LoginResponse.self, "login")
        #expect(login.success)
        #expect(login.role == .floorManager)
        #expect(login.operatorName == "Alexandre Dupont (Manager)")
    }

    @Test func catalog() throws {
        let categories = try decode([MenuCategory].self, "categories")
        let products = try decode([Product].self, "products")
        #expect(categories.count == 5)
        #expect(categories.first?.symbolName == "leaf")
        let burger = try #require(products.first { $0.name == "Burger Gourmet Rossini" })
        #expect(burger.price == Money(cents: 1950))
        #expect(burger.modifierGroups.count == 2)
        #expect(burger.modifierGroups[0].isMandatory)
        #expect(burger.modifierGroups[0].options.contains { $0.isDefault })
        #expect(products.contains { $0.isQuickKey })
    }

    @Test func gridLayout() throws {
        let layout = try decode(TouchGridLayout.self, "grid_layout")
        #expect(layout.columnsCount * layout.rowsCount == layout.positions.count)
        #expect(layout.slots.contains { $0.productId != nil })
    }

    @Test func tablesAndOrders() throws {
        let tables = try decode([DiningTable].self, "tables")
        #expect(tables.count >= 8)
        let order = try decode(ActiveOrder.self, "table_order")
        #expect(order.lines.first?.course == .suite)
        #expect(order.lines.first?.modifiersPriceExtra == Money(cents: 250))
        #expect(order.destination == .takeaway)
        let discounted = try decode(ActiveOrder.self, "table_order_discounted")
        #expect(discounted.globalDiscount == GlobalDiscount(type: .fixedAmount, value: 2, reason: "Geste"))
        // Même calcul que le serveur : 22,00 € − 2,00 € = 20,00 €
        let totals = OrderMath.totals(lines: discounted.lines.map(CartLine.init(serverLine:)), destination: discounted.destination, discount: discounted.globalDiscount)
        #expect(totals.totalTtc == discounted.totalTtcAmount)
    }

    @Test func payment() throws {
        let result = try decode(PaymentResult.self, "pay_partial")
        #expect(result.totalPaid == Money(cents: 1000))
        #expect(result.remainingBalance == Money(cents: 1000))
    }

    @Test func kitchen() throws {
        let tickets = try decode([KitchenTicket].self, "kds_tickets")
        let ticket = try #require(tickets.first)
        #expect(ticket.stationId == "HOT_KITCHEN")
        #expect(ticket.items.first?.modifiersSummary == "À Point")
        #expect(ticket.dispatchedAtUtc != nil)
    }

    @Test func fiscal() throws {
        let x = try decode(FiscalReport.self, "x_report")
        #expect(x.receiptCount == 3)
        #expect(x.sortedVat.map(\.rate) == ["10.0", "20.0"])
        #expect(x.sortedPayments.contains { $0.method == "Carte bancaire" })
        let z = try decode(FiscalReport.self, "z_closure")
        #expect(z.closureSequence == 1)
        #expect(z.signatureHash?.count == 64)
        #expect(z.closedAtUtc != nil)
    }

    @Test func dashboard() throws {
        let dashboard = try decode(FinancialDashboard.self, "dashboard")
        #expect(dashboard.kpis.totalOrdersCount == 3)
        #expect(!dashboard.topProducts.isEmpty)
    }

    @Test func staffPrintersRooms() throws {
        let staff = try decode([StaffMember].self, "staff")
        #expect(staff.contains { $0.role == .kitchenStaff })
        let printers = try decode([Printer].self, "printers")
        #expect(printers.contains { $0.openCashDrawerOnReceipt })
        let rooms = try decode([HotelRoom].self, "rooms")
        #expect(rooms.first?.availableCredit == Money(cents: 30000))
    }

    @Test func happyHour() throws {
        let status = try decode(HappyHourStatus.self, "hh_status_active")
        #expect(status.isActive && status.isOverride)
        #expect(status.currentWindow?.remainingMinutes == 30)
        let pricing = try decode(HappyHourPricingTable.self, "hh_pricing")
        #expect(pricing.items.allSatisfy { $0.happyHourPrice < $0.standardPrice })
        let schedules = try decode([HappyHourSchedule].self, "hh_schedules")
        #expect(schedules.first?.daysLabel == "Lun, Mar, Mer, Jeu, Ven")
        #expect(schedules.first?.priceRules.contains { $0.fixedPrice == Money(cents: 500) } == true)
    }

    @Test func networkAndSync() throws {
        let info = try decode(NetworkInfo.self, "network_info")
        #expect(info.discoveryPort == 45454)
        let sync = try decode(SyncStatus.self, "sync_status")
        #expect(sync.pendingMessages == 0)
        #expect(try decode([HeldOrder].self, "held_empty").isEmpty)
    }

    @Test func heldOrderMoneyObject() throws {
        let json = #"[{"holdId":"01a0d512-eff4-7686-bb06-f2f24d8f144a","terminalId":"POS_A","orderId":"01a0d512-efbe-7616-9e70-f1bd90dd8f51","customerLabel":"Client 1","destination":0,"itemCount":1,"totalTtc":{"amountInCents":600,"currency":"EUR"},"heldAtUtc":"2026-09-24T20:19:43.220348+00:00","heldByStaffId":"01a0d511-8720-7f5d-81ce-0844b9372ef1"}]"#
        let held = try PosJSON.makeDecoder().decode([HeldOrder].self, from: Data(json.utf8))
        #expect(held.first?.totalTtc.money == Money(cents: 600))
    }

    @Test(arguments: [
        "2026-09-24T20:18:10.894089+00:00",
        "2026-09-24T00:00:00+02:00",
        "0001-01-01T00:00:00+00:00",
        "2026-09-24T20:19:43.3598547Z",
    ])
    func parsesDotNetDates(_ value: String) {
        #expect(PosJSON.parseDate(value) != nil)
    }

    @Test func unknownEnumValuesDoNotBreakDecoding() throws {
        let statuses = try JSONDecoder().decode([TableStatus].self, from: Data("[0, 3, 42]".utf8))
        #expect(statuses == [.free, .paid, .free])
        #expect(try JSONDecoder().decode(UserRole.self, from: Data(#""admin""#.utf8)) == .admin)
    }
}
