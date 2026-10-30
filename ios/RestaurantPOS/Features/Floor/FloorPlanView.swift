import SwiftUI
import PosKit

extension TableServiceState {
    var label: String {
        switch self {
        case .free: String(localized: "floor.state_free")
        case .ordering: String(localized: "floor.state_ordering")
        case .cooking: String(localized: "floor.state_cooking")
        case .eating: String(localized: "floor.state_eating")
        case .late: String(localized: "floor.state_late")
        case .bill: String(localized: "floor.state_bill")
        case .paid: String(localized: "floor.state_paid")
        }
    }

    var color: Color {
        switch self {
        case .free: Theme.success
        case .ordering: Theme.primaryInk
        case .cooking: Theme.warning
        case .eating: Theme.inkSubtle
        case .late, .bill: Theme.danger
        case .paid: Theme.statusPaid
        }
    }
}

/// Plan de salle dessiné : forme d'après la capacité, rangées automatiques (le serveur ne stocke pas de dessin), chaises occupées en plein.
struct FloorPlanView: View {
    @Environment(AppModel.self) private var model
    let tables: [DiningTable]
    @Binding var selected: String?

    var body: some View {
        let layout = FloorLayout.boxes(for: tables)
        Color.clear
            .aspectRatio(FloorLayout.width / layout.height, contentMode: .fit)
            .overlay(alignment: .topLeading) {
                GeometryReader { geo in
                    let scale = geo.size.width / FloorLayout.width
                    ZStack(alignment: .topLeading) {
                        ForEach(tables) { table in
                            if let box = layout.boxes[table.tableNumber] {
                                let seated = table.status == .free ? 0 : table.coversCount
                                ForEach(Array(FloorLayout.chairs(of: box).enumerated()), id: \.offset) { index, rect in
                                    RoundedRectangle(cornerRadius: 4).fill(index < seated ? Theme.ink : Theme.line)
                                        .frame(width: rect.width, height: rect.height)
                                        .offset(x: rect.minX, y: rect.minY)
                                }
                                tableButton(table, box)
                            }
                        }
                    }
                    .frame(width: FloorLayout.width, height: layout.height, alignment: .topLeading)
                    .scaleEffect(scale, anchor: .topLeading)
                }
            }
            .background(RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous).fill(Theme.surface))
            .accessibilityElement(children: .contain)
            .accessibilityIdentifier("floor.plan")
    }

    private func tableButton(_ table: DiningTable, _ box: FloorBox) -> some View {
        let state = model.floor.serviceState(of: table)
        let held = state == .free ? model.bookings.heldByTable[table.tableNumber] : nil
        let isSelected = selected == table.tableNumber
        let shape: AnyShape = box.shape == .round ? AnyShape(Circle()) : AnyShape(RoundedRectangle(cornerRadius: 14, style: .continuous))
        return Button {
            Haptics.tap()
            selected = isSelected ? nil : table.tableNumber
        } label: {
            VStack(spacing: 2) {
                Text(table.tableNumber).font(Font.pos(17, .bold)).foregroundStyle(Theme.ink)
                Text(held.map { String(localized: "bookings.held \($0.formatted(date: .omitted, time: .shortened))") } ?? state.label)
                    .font(Font.pos(11, .semibold)).foregroundStyle(Theme.inkMuted).lineLimit(2).multilineTextAlignment(.center).minimumScaleFactor(0.6)
                    .frame(width: box.width - 12)
            }
            .frame(width: box.width, height: box.height)
            .background(shape.fill(state == .free ? Theme.surface : state.color.opacity(0.14)))
            .overlay(shape.stroke(state.color, style: StrokeStyle(lineWidth: 2, dash: state == .bill ? [6, 4] : [])))
            .overlay(isSelected ? shape.stroke(Theme.ink, lineWidth: 4).padding(-4) : nil)
            .contentShape(shape)
        }
        .buttonStyle(.plain)
        .offset(x: box.x, y: box.y)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("plan.table.\(table.tableNumber)")
    }
}

/// Volet du plan : vue d'ensemble (à traiter, à venir, attente) ou détail de la table choisie.
struct FloorPane: View {
    @Environment(AppModel.self) private var model
    let selected: DiningTable?
    let onOpen: (DiningTable) -> Void
    let onClose: () -> Void

    var body: some View {
        Card {
            VStack(alignment: .leading, spacing: Theme.Space.m) {
                if let table = selected { detail(table) } else { overview }
            }
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .accessibilityElement(children: .contain)
        .accessibilityIdentifier("floor.pane")
    }

    private var overview: some View {
        VStack(alignment: .leading, spacing: Theme.Space.m) {
            section("needs.title") {
                if model.needs.items.isEmpty {
                    Text("needs.empty").font(.posBody).foregroundStyle(Theme.inkSubtle)
                } else {
                    ForEach(model.needs.items.prefix(6)) { item in
                        VStack(alignment: .leading, spacing: 0) {
                            Text(item.title).font(.posBody.weight(.semibold)).foregroundStyle(Theme.ink)
                            Text(item.subtitle).font(.posCaption).foregroundStyle(Theme.inkMuted)
                        }
                    }
                }
            }
            section("floor.coming_up") {
                let up = model.bookings.todayUpcoming.prefix(3)
                if up.isEmpty { Text("bookings.empty").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                ForEach(Array(up)) { r in
                    Text("\(r.startsAtUtc.formatted(date: .omitted, time: .shortened)) · \(r.guestName) · \(r.covers)").font(.posBody).foregroundStyle(Theme.ink)
                }
            }
            section("bookings.waitlist_title") {
                if model.bookings.todayWaitlist.isEmpty { Text("bookings.waitlist_empty").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                ForEach(model.bookings.todayWaitlist) { r in
                    Text("\(r.guestName) · \(r.covers)").font(.posBody).foregroundStyle(Theme.ink)
                }
            }
        }
    }

    private func section<Content: View>(_ title: LocalizedStringKey, @ViewBuilder content: () -> Content) -> some View {
        VStack(alignment: .leading, spacing: Theme.Space.xs) {
            Text(title).font(.posHeadline).foregroundStyle(Theme.ink)
            content()
        }
    }

    private func detail(_ table: DiningTable) -> some View {
        let state = model.floor.serviceState(of: table)
        let held = model.bookings.heldByTable[table.tableNumber]
        let fits = state == .free ? model.bookings.todayWaitlist.first { $0.covers <= table.capacity } : nil
        return VStack(alignment: .leading, spacing: Theme.Space.m) {
            HStack {
                Text(table.tableNumber).font(.posTitle).foregroundStyle(Theme.ink)
                Badge(text: state.label, color: state.color)
                Spacer()
                Button(action: onClose) { Image(systemName: "xmark").frame(width: Theme.touchMin, height: Theme.touchMin) }
                    .buttonStyle(.plain).accessibilityLabel("common.close_label").accessibilityIdentifier("floor.pane.close")
            }
            Text("floor.capacity_only \(table.capacity)").font(.posBody).foregroundStyle(Theme.inkMuted)
            if table.status != .free {
                Text("\(table.coversCount) · \(table.assignedWaiterName ?? "—")").font(.posBody).foregroundStyle(Theme.inkMuted)
                if table.activeOrderTotalTtc.cents > 0 { Text(table.activeOrderTotalTtc.formatted).font(.posAmount).foregroundStyle(Theme.ink) }
            }
            if let held {
                Label(String(localized: "bookings.held \(held.formatted(date: .omitted, time: .shortened))"), systemImage: "calendar").font(.posLabel).foregroundStyle(Theme.warning)
            }
            ActionButton(title: String(localized: state == .free ? "floor.act_open" : "floor.act_recall"), kind: .primary) { onOpen(table) }
                .accessibilityIdentifier("floor.open")
            if let fits {
                ActionButton(title: String(localized: "floor.act_seat_waiting \(fits.guestName)"), kind: .tonal) {
                    Task { _ = await model.bookings.seat(fits, table: table.tableNumber); await model.floor.load(); await model.bookings.refreshAttention() }
                }
                .accessibilityIdentifier("floor.seatWaiting")
            }
        }
    }
}
