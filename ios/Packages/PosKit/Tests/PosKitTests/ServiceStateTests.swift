import Foundation
import Testing
@testable import PosKit

@Suite("États de service, plan et statistiques cuisine")
struct ServiceStateTests {
    private let now = Date(timeIntervalSince1970: 1_800_000_000)
    private func table(_ number: String = "T1", status: TableStatus = .occupied, capacity: Int = 4, orderId: UUID? = nil) -> DiningTable {
        DiningTable(tableNumber: number, capacity: capacity, status: status, activeOrderId: orderId)
    }
    private func ticket(_ number: String = "T1", status: TicketStatus = .pending, minutesAgo: Double, orderId: UUID? = nil, doneMinutesAgo: Double? = nil) -> KitchenTicket {
        KitchenTicket(orderId: orderId, tableNumber: number, stationId: "HOT_KITCHEN", status: status, dispatchedAtUtc: now.addingTimeInterval(-minutesAgo * 60),
                      completedAtUtc: doneMinutesAgo.map { now.addingTimeInterval(-$0 * 60) }, items: [KitchenTicketItem(productName: "Burger", quantity: 2)])
    }

    @Test func derivesTheSixStates() {
        let order = UUID()
        #expect(TableServiceState.of(table(status: .free), tickets: [], now: now) == .free)
        #expect(TableServiceState.of(table(status: .billRequested), tickets: [], now: now) == .bill)
        #expect(TableServiceState.of(table(), tickets: [], now: now) == .ordering)
        #expect(TableServiceState.of(table(), tickets: [ticket(minutesAgo: 5)], now: now) == .cooking)
        #expect(TableServiceState.of(table(), tickets: [ticket(minutesAgo: 13)], now: now) == .late)
        #expect(TableServiceState.of(table(orderId: order), tickets: [ticket(status: .served, minutesAgo: 30, orderId: order)], now: now) == .eating)
        // Le bon d'une autre table ne compte pas.
        #expect(TableServiceState.of(table("T2"), tickets: [ticket("T1", minutesAgo: 30)], now: now) == .ordering)
    }

    @Test func shapesFollowCapacityAndRowsWrap() {
        #expect(FloorLayout.shape(capacity: 2, number: "T1").shape == .round)
        #expect(FloorLayout.shape(capacity: 4, number: "T2").shape == .square)
        #expect(FloorLayout.shape(capacity: 6, number: "T3").shape == .rect)
        #expect(FloorLayout.shape(capacity: 10, number: "T4").shape == .long)
        #expect(FloorLayout.shape(capacity: 6, number: "Bar").shape == .bar)

        let tables = (1...12).map { table("T\($0)", status: .free, capacity: 4) }
        let (boxes, height) = FloorLayout.boxes(for: tables)
        #expect(boxes.count == 12)
        #expect(boxes.values.allSatisfy { $0.x + $0.width <= 900 + 0.001 })
        #expect(Set(boxes.values.map(\.y)).count > 1)
        #expect(height > boxes.values.map(\.y).max()!)
        #expect(FloorLayout.chairs(of: boxes["T1"]!).count == 4)
    }

    @Test func kitchenStatsAverageAndLateAndLastHour() {
        let calendar = Calendar(identifier: .gregorian)
        let tickets = [
            ticket("T1", minutesAgo: 20),
            ticket("T2", status: .served, minutesAgo: 30, doneMinutesAgo: 20),
            ticket("T3", status: .served, minutesAgo: 50, doneMinutesAgo: 20),
            ticket("T4", minutesAgo: 3)
        ]
        let stats = KitchenStats.compute(tickets: tickets, now: now, calendar: calendar)
        #expect(stats.open == 2 && stats.late == 1)
        #expect(stats.servedLastHour == 2)
        #expect(abs((stats.averageSeconds ?? 0) - 20 * 60) < 0.01)
        let all = KitchenStats.allDay(tickets: tickets)
        #expect(all.first?.name == "Burger" && all.first?.quantity == 4)
        #expect(KitchenStats.recentlyServed(tickets: tickets).count == 2)
    }
}
