import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : encaissements")
struct LocalTakingsTests {
    private func today() -> (from: Date, to: Date, offset: Int) {
        let calendar = Calendar.current
        let start = calendar.startOfDay(for: Date())
        return (start, calendar.date(byAdding: .day, value: 1, to: start)!, calendar.timeZone.secondsFromGMT(for: Date()) / 60)
    }

    @Test func reportsTheDaysReceiptsByLocalHourWithPaymentsAndServers() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", covers: 3, items: [localInput(burger, quantity: 2)])
        let order = try #require(try await api.activeOrder(table: "T1"))
        let totalCents = order.totalTtcAmount?.cents ?? 0
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, totalCents)]))

        let day = today()
        let report = try await api.takings(from: day.from, to: day.to, tzOffsetMinutes: day.offset, compare: nil)

        #expect(report.totalTtc == Money(cents: totalCents))
        #expect(report.tablesTurned == 1 && report.covers >= 1)
        #expect(report.isHourly && report.compare == "lastWeek")
        #expect(report.series.count == 24 && report.series.reduce(0) { $0 + $1.current.cents } == totalCents)
        let hourNow = Calendar.current.component(.hour, from: Date())
        #expect(report.series[hourNow].current.cents == totalCents)
        #expect(report.paymentMethods.count == 1 && report.paymentMethods[0].percentageOfTotal == 100)
        #expect(report.staffPerformance.count == 1 && report.staffPerformance[0].totalSalesTtc == Money(cents: totalCents))
        #expect(report.compareTotalTtc == .zero && report.deltaPercent == nil)
        #expect(report.categories.reduce(0) { $0 + $1.quantity } == 2)
        #expect(report.topProducts.first?.productName == "Burger Gourmet Rossini")
        #expect(report.voidsCount == 0 && report.discountsTtc == .zero && report.compsTtc == .zero)
        #expect(!report.vat.isEmpty && report.vat.reduce(0) { $0 + $1.baseHt.cents + $1.vat.cents } == totalCents)
    }

    @Test func aWeekIsReportedByDayAgainstThePreviousWeek() async throws {
        let api = try await makeLocalAPI()
        let day = today()
        let from = Calendar.current.date(byAdding: .day, value: -6, to: day.from)!
        let report = try await api.takings(from: from, to: day.to, tzOffsetMinutes: day.offset, compare: nil)
        #expect(!report.isHourly && report.compare == "previous" && report.series.count == 7)
    }

    @Test func listsTheDaysReceiptsAndTodaysJournal() async throws {
        let api = try await makeLocalAPI()
        let burger = try await localProduct(api, "Burger Gourmet Rossini")
        try await seatTable(api, "T1", covers: 2, items: [localInput(burger, quantity: 1)])
        let order = try #require(try await api.activeOrder(table: "T1"))
        let totalCents = order.totalTtcAmount?.cents ?? 0
        _ = try await api.pay(localPayment(order, table: "T1", tenders: [localTender(.cash, totalCents)]))

        let day = today()
        let receipts = try await api.takingsReceipts(from: day.from, to: day.to)
        #expect(receipts.count == 1 && receipts[0].totalTtc == Money(cents: totalCents) && !receipts[0].isCredit && !receipts[0].isCancelled)
        #expect(receipts[0].tableNumber == "T1" && !receipts[0].methods.isEmpty)
        let journal = try await api.journalToday(tzOffsetMinutes: day.offset)
        _ = journal  // le .NET ne journalise pas les ventes : seulement les événements fiscaux et de session
    }

    @Test func aWaiterCannotReadTakings() async throws {
        let api = try await makeLocalAPI(pin: "2468")
        let day = today()
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await api.takings(from: day.from, to: day.to, tzOffsetMinutes: day.offset, compare: nil) }
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await api.takingsReceipts(from: day.from, to: day.to) }
        await #expect(throws: APIError.forbidden("Action réservée à un responsable.")) { try await api.journalToday(tzOffsetMinutes: day.offset) }
    }

    @Test func deltaIsRelativeToTheReferenceByNow() {
        let report = TakingsReport(fromUtc: Date(), toUtc: Date(), totalTtc: Money(cents: 12_000), compareTotalTtc: Money(cents: 30_000), compareByNowTtc: Money(cents: 10_000), covers: 4, tablesTurned: 2, spendPerCover: Money(cents: 3_000), series: [])
        #expect(report.deltaPercent == 20)
    }
}
