import Foundation
import Observation

/// Une chose qui attend un humain maintenant (cloche « à traiter » et pastilles de navigation).
public struct NeedItem: Identifiable, Hashable, Sendable {
    public enum Kind: Hashable, Sendable { case bill, lateFood, lateBooking, waitlist }
    public let kind: Kind
    public let id: String
    public let title: String
    public let subtitle: String
    public let since: Date
    /// Numéro de table ou identifiant de réservation visé.
    public let target: String
}

/// Additions demandées, plats en retard, clients attendus en retard et attentes dépassées ; miroir de `refreshNeeds` du client web.
@MainActor @Observable
public final class NeedsStore {
    public private(set) var items: [NeedItem] = []

    private let api: PosAPI
    private let calendar: Calendar
    private let now: () -> Date

    public init(api: PosAPI, calendar: Calendar = .autoupdatingCurrent, now: @escaping () -> Date = { Date() }) {
        self.api = api
        self.calendar = calendar
        self.now = now
    }

    public func count(_ kinds: NeedItem.Kind...) -> Int { items.filter { kinds.contains($0.kind) }.count }
    public var billCount: Int { count(.bill) }
    public var lateFoodCount: Int { count(.lateFood) }
    public var bookingCount: Int { count(.lateBooking, .waitlist) }

    public func refresh() async {
        let current = now()
        let start = calendar.startOfDay(for: current)
        let end = calendar.date(byAdding: .day, value: 1, to: start) ?? start.addingTimeInterval(86_400)
        async let tables = try? api.tables()
        async let tickets = try? api.kitchenTickets()
        async let reservations = try? api.reservations(from: start, to: end)
        var found: [NeedItem] = []
        let time: (Date) -> String = { $0.formatted(date: .omitted, time: .shortened) }

        for table in await tables ?? [] where table.status == .billRequested {
            found.append(NeedItem(
                kind: .bill, id: "bill-\(table.tableNumber)",
                title: String(format: L10n.string("needs.bill"), table.tableNumber),
                subtitle: String(format: L10n.string("needs.bill_sub"), table.coversCount, table.assignedWaiterName ?? "—"),
                since: current, target: table.tableNumber))
        }
        let limit = TimeInterval(KitchenTarget.minutes * 60)
        for ticket in await tickets ?? [] where (ticket.status == .pending || ticket.status == .inPreparation) {
            guard let sent = ticket.dispatchedAtUtc, current.timeIntervalSince(sent) > limit else { continue }
            found.append(NeedItem(
                kind: .lateFood, id: "food-\(ticket.id)",
                title: String(format: L10n.string("needs.late_food"), ticket.tableNumber),
                subtitle: String(format: L10n.string("needs.late_food_sub"), time(sent), ticket.serverName ?? "—"),
                since: sent, target: ticket.tableNumber))
        }
        for r in await reservations ?? [] {
            if r.isLate {
                found.append(NeedItem(
                    kind: .lateBooking, id: "late-\(r.id)",
                    title: String(format: L10n.string("needs.booking_late"), r.guestName),
                    subtitle: String(format: L10n.string("needs.booking_sub"), r.covers, time(r.startsAtUtc), r.tableNumber ?? "—"),
                    since: r.startsAtUtc, target: r.id.uuidString))
            } else if r.isWaitlist, r.status == .booked, let quote = r.quoteMinutes, current.timeIntervalSince(r.startsAtUtc) > TimeInterval(quote * 60) {
                found.append(NeedItem(
                    kind: .waitlist, id: "wait-\(r.id)",
                    title: String(format: L10n.string("needs.waitlist_over"), r.guestName),
                    subtitle: String(format: L10n.string("needs.waitlist_sub"), r.covers, quote),
                    since: r.startsAtUtc, target: r.id.uuidString))
            }
        }
        items = found.sorted { $0.since < $1.since }
    }
}
