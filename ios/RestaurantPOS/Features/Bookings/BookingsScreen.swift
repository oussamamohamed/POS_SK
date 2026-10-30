import SwiftUI
import PosKit

/// Ce que la feuille de saisie édite : une nouvelle réservation ou une existante.
enum BookingTarget: Identifiable {
    case new
    case existing(Reservation)

    var id: String {
        switch self {
        case .new: "new"
        case .existing(let r): r.id.uuidString
        }
    }
}

/// Réservations du jour : plan du service par table, liste d'attente et réservations à placer.
struct BookingsScreen: View {
    @Environment(AppModel.self) private var model
    @State private var target: BookingTarget?

    var body: some View {
        let store = model.bookings
        VStack(alignment: .leading, spacing: Theme.Space.l) {
            HStack(spacing: Theme.Space.m) {
                KPIView(title: String(localized: "bookings.kpi_bookings"), value: "\(store.scheduled.count)", systemImage: "calendar").frame(maxWidth: 200)
                KPIView(title: String(localized: "bookings.kpi_covers"), value: "\(store.coversCount)", systemImage: "person.2").frame(maxWidth: 200)
                KPIView(title: String(localized: "bookings.kpi_late"), value: "\(store.lateCount)", systemImage: "clock.badge.exclamationmark", tint: Theme.danger).frame(maxWidth: 200)
                KPIView(title: String(localized: "bookings.kpi_waitlist"), value: "\(store.waitlist.count)", systemImage: "hourglass").frame(maxWidth: 200)
                Spacer()
            }
            .fixedSize(horizontal: false, vertical: true)

            HStack(spacing: Theme.Space.m) {
                dayNavigator(store)
                Spacer()
                Button { target = .new } label: {
                    Label("bookings.new", systemImage: "plus")
                        .font(.posHeadline)
                        .padding(.horizontal, Theme.Space.xl)
                        .frame(minHeight: Theme.touchTarget)
                }
                .buttonStyle(ActionButtonStyle(kind: .primary))
                .accessibilityIdentifier("bookings.add")
            }

            ScrollView {
                HStack(alignment: .top, spacing: Theme.Space.l) {
                    BookingsTimeline(reservations: store.scheduled, tables: model.floor.diningTables, day: store.day) { target = .existing($0) }
                        .frame(maxWidth: .infinity)
                    VStack(spacing: Theme.Space.l) {
                        BookingsList(title: String(localized: "bookings.waitlist_title"), empty: String(localized: "bookings.waitlist_empty"), items: store.waitlist, identifier: "bookings.waitlist") { r in
                            let waited = max(0, Int(Date().timeIntervalSince(r.startsAtUtc) / 60))
                            return String(localized: "bookings.waited \(waited)") + (r.quoteMinutes.map { " · " + String(localized: "bookings.quoted \($0)") } ?? "")
                        } onSelect: { target = .existing($0) }
                        BookingsList(title: String(localized: "bookings.unplaced_title"), empty: String(localized: "bookings.none_unplaced"), items: store.unplaced, identifier: "bookings.unplaced") { r in
                            r.startsAtUtc.formatted(date: .omitted, time: .shortened)
                        } onSelect: { target = .existing($0) }
                    }
                    .frame(width: 320)
                }
                .padding(.bottom, Theme.Space.xl)
            }
            .refreshable { await store.load() }
        }
        .padding([.horizontal, .top], Theme.Space.xl)
        .task {
            await model.floor.load()
            await store.load()
        }
        .sheet(item: $target) { BookingSheet(target: $0) }
    }

    private func dayNavigator(_ store: BookingsStore) -> some View {
        HStack(spacing: Theme.Space.s) {
            Button { Task { await store.shiftDay(by: -1) } } label: { Image(systemName: "chevron.left").frame(width: Theme.touchTarget, height: Theme.touchTarget) }
                .buttonStyle(ActionButtonStyle(kind: .neutral))
                .accessibilityLabel("bookings.prev_day")
                .accessibilityIdentifier("bookings.prev")
            Text(store.day.formatted(.dateTime.weekday(.wide).day().month(.wide)))
                .font(.posHeadline).foregroundStyle(Theme.ink)
                .frame(minWidth: 200)
                .accessibilityIdentifier("bookings.day")
            Button { Task { await store.shiftDay(by: 1) } } label: { Image(systemName: "chevron.right").frame(width: Theme.touchTarget, height: Theme.touchTarget) }
                .buttonStyle(ActionButtonStyle(kind: .neutral))
                .accessibilityLabel("bookings.next_day")
                .accessibilityIdentifier("bookings.next")
            if !store.isToday {
                Button { Task { await store.goToToday() } } label: { Text("bookings.today").font(.posLabel).padding(.horizontal, Theme.Space.l).frame(minHeight: Theme.touchTarget) }
                    .buttonStyle(ActionButtonStyle(kind: .tonal))
                    .accessibilityIdentifier("bookings.today")
            }
        }
    }
}

/// Liste de réservations sans table ou en attente (une ligne = une feuille de saisie à ouvrir).
private struct BookingsList: View {
    let title: String
    let empty: String
    let items: [Reservation]
    let identifier: String
    let caption: (Reservation) -> String
    let onSelect: (Reservation) -> Void

    private func isOverdue(_ r: Reservation) -> Bool {
        r.isWaitlist && (r.quoteMinutes.map { Date().timeIntervalSince(r.startsAtUtc) > TimeInterval($0 * 60) } ?? false)
    }

    var body: some View {
        Card {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                Text(title).font(.posHeadline).foregroundStyle(Theme.ink)
                if items.isEmpty {
                    Text(empty).font(.posBody).foregroundStyle(Theme.inkSubtle)
                } else {
                    ForEach(items) { r in
                        Button { onSelect(r) } label: {
                            HStack {
                                VStack(alignment: .leading, spacing: 2) {
                                    Text("\(r.guestName) · \(r.covers)").font(.posBody.weight(.semibold)).foregroundStyle(Theme.ink)
                                    Text(caption(r)).font(.posCaption).foregroundStyle(Theme.inkMuted)
                                }
                                Spacer()
                                if r.isLate || isOverdue(r) { Badge(text: String(localized: "bookings.late"), color: Theme.danger) }
                            }
                            .padding(.horizontal, Theme.Space.m)
                            .frame(maxWidth: .infinity, minHeight: Theme.touchTarget, alignment: .leading)
                            .background(RoundedRectangle(cornerRadius: Theme.Radius.sm, style: .continuous).fill(Theme.raised))
                        }
                        .buttonStyle(.plain)
                        .accessibilityIdentifier("\(identifier).\(r.id.uuidString.prefix(8))")
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier(identifier)
    }
}

/// Plan du service : une ligne par table, un bloc par réservation (position et largeur = heure et durée).
struct BookingsTimeline: View {
    let reservations: [Reservation]
    let tables: [DiningTable]
    let day: Date
    let onSelect: (Reservation) -> Void

    private static let hourWidth: CGFloat = 76
    private static let rowHeight: CGFloat = 52

    private func hours() -> (start: Int, end: Int) {
        let calendar = Calendar.autoupdatingCurrent
        let startOfDay = calendar.startOfDay(for: day)
        let starts = reservations.map { Int($0.startsAtUtc.timeIntervalSince(startOfDay) / 3600) }
        let ends = reservations.map { Int(ceil($0.endsAtUtc.timeIntervalSince(startOfDay) / 3600)) }
        return (max(0, ([11] + starts).min() ?? 11), min(26, ([23] + ends).max() ?? 23))
    }

    var body: some View {
        let range = hours()
        let startOfDay = Calendar.autoupdatingCurrent.startOfDay(for: day)
        let width = CGFloat(range.end - range.start) * Self.hourWidth
        Card {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                Text("bookings.timeline_title").font(.posHeadline).foregroundStyle(Theme.ink)
                HStack(alignment: .top, spacing: 0) {
                    VStack(alignment: .leading, spacing: 0) {
                        Color.clear.frame(width: 64, height: 24)
                        ForEach(tables) { table in
                            Text(table.tableNumber).font(.posLabel).foregroundStyle(Theme.ink)
                                .frame(width: 64, height: Self.rowHeight, alignment: .leading)
                        }
                    }
                    ScrollView(.horizontal, showsIndicators: true) {
                        VStack(alignment: .leading, spacing: 0) {
                            ZStack(alignment: .topLeading) {
                                ForEach(range.start...range.end, id: \.self) { h in
                                    Text(String(format: "%02dh", h % 24)).font(Font.posMono(12)).foregroundStyle(Theme.inkMuted)
                                        .offset(x: CGFloat(h - range.start) * Self.hourWidth - 12)
                                }
                            }
                            .frame(width: width, height: 24, alignment: .topLeading)
                            ForEach(tables) { table in
                                ZStack(alignment: .topLeading) {
                                    Rectangle().fill(Theme.line).frame(height: 1).offset(y: Self.rowHeight - 1)
                                    ForEach(reservations.filter { $0.tableNumber == table.tableNumber }) { r in
                                        block(r, startOfDay: startOfDay, rangeStart: range.start)
                                    }
                                }
                                .frame(width: width, height: Self.rowHeight, alignment: .topLeading)
                            }
                        }
                    }
                }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("bookings.timeline")
    }

    private func block(_ r: Reservation, startOfDay: Date, rangeStart: Int) -> some View {
        let minutes = r.startsAtUtc.timeIntervalSince(startOfDay) / 60 - Double(rangeStart * 60)
        let x = CGFloat(minutes / 60) * Self.hourWidth
        let w = max(40, CGFloat(r.durationMinutes) / 60 * Self.hourWidth - 4)
        let color = r.isLate ? Theme.danger : (r.status == .seated ? Theme.success : Theme.primaryInk)
        return Button { onSelect(r) } label: {
            HStack(spacing: 6) {
                RoundedRectangle(cornerRadius: 2).fill(color).frame(width: 4)
                Text("\(r.startsAtUtc.formatted(date: .omitted, time: .shortened)) \(r.guestName) · \(r.covers)")
                    .font(Font.pos(12, .semibold)).foregroundStyle(Theme.ink).lineLimit(1)
                Spacer(minLength: 0)
            }
            .padding(.vertical, 6)
            .padding(.trailing, 6)
            .frame(width: w, height: Self.rowHeight - 10, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: Theme.Radius.sm, style: .continuous).fill(Theme.raised))
        }
        .buttonStyle(.plain)
        .offset(x: max(0, x), y: 4)
        .accessibilityIdentifier("bookings.block.\(r.id.uuidString.prefix(8))")
    }
}
