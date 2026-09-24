import SwiftUI
import PosKit

/// Écran cuisine (KDS) : trois colonnes, un tap fait avancer le bon.
struct KitchenScreen: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let kitchen = model.kitchen
        let canBump = model.session.currentOperator?.role.canBumpKitchen ?? false
        VStack(spacing: 0) {
            HStack(spacing: 8) {
                ChipButton(title: "Tous les postes", isSelected: kitchen.stationFilter == nil) { kitchen.stationFilter = nil }
                    .accessibilityIdentifier("kds.filter.all")
                ForEach(kitchen.stations, id: \.self) { station in
                    ChipButton(title: station.replacingOccurrences(of: "_", with: " "), isSelected: kitchen.stationFilter == station) { kitchen.stationFilter = station }
                        .accessibilityIdentifier("kds.filter.\(station)")
                }
                Spacer()
                if let refresh = kitchen.lastRefresh {
                    Text("Mis à jour \(refresh.formatted(date: .omitted, time: .standard))").font(.caption).foregroundStyle(.secondary)
                }
                Button { Task { await kitchen.load() } } label: { Image(systemName: "arrow.clockwise") }
                    .buttonStyle(.bordered)
                    .accessibilityIdentifier("kds.refresh")
            }
            .padding(20)

            if !canBump {
                Label("Lecture seule : seuls la cuisine et les responsables font avancer les bons.", systemImage: "eye")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
                    .padding(.bottom, 8)
            }

            HStack(alignment: .top, spacing: 16) {
                column(.pending, canBump: canBump)
                column(.inPreparation, canBump: canBump)
                column(.ready, canBump: canBump)
            }
            .padding(.horizontal, 20)
            .padding(.bottom, 20)
        }
        .task {
            // Rafraîchissement périodique en complément du temps réel SignalR.
            while !Task.isCancelled {
                await kitchen.load()
                try? await Task.sleep(for: .seconds(15))
            }
        }
    }

    private func column(_ status: TicketStatus, canBump: Bool) -> some View {
        let tickets = model.kitchen.tickets(in: status)
        return VStack(alignment: .leading, spacing: 12) {
            HStack {
                Circle().fill(Theme.color(for: status)).frame(width: 10, height: 10)
                Text(status.label).font(.headline)
                Spacer()
                Text("\(tickets.count)").font(.headline.monospacedDigit()).foregroundStyle(.secondary)
                    .accessibilityIdentifier("kds.count.\(status)")
            }
            ScrollView {
                LazyVStack(spacing: 12) {
                    ForEach(tickets) { ticket in
                        KitchenTicketCard(ticket: ticket)
                            .onTapGesture {
                                guard canBump else { return }
                                Haptics.tap()
                                Task { await model.kitchen.bump(ticket) }
                            }
                    }
                }
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(RoundedRectangle(cornerRadius: Theme.cornerRadius).fill(Theme.color(for: status).opacity(0.06)))
    }
}

struct KitchenTicketCard: View {
    let ticket: KitchenTicket

    var body: some View {
        TimelineView(.periodic(from: .now, by: 30)) { context in
            let minutes = ticket.dispatchedAtUtc.map { Int(context.date.timeIntervalSince($0) / 60) } ?? 0
            VStack(alignment: .leading, spacing: 8) {
                HStack {
                    Text(ticket.tableNumber).font(.title3.weight(.bold))
                    Spacer()
                    Badge(text: ticket.stationId.replacingOccurrences(of: "_", with: " "), color: .secondary)
                }
                ForEach(ticket.items) { item in
                    VStack(alignment: .leading, spacing: 2) {
                        Text("\(item.quantity)× \(item.productName)").font(.body.weight(.semibold))
                        if let mods = item.modifiersSummary, !mods.isEmpty {
                            Text(mods).font(.caption).foregroundStyle(.orange)
                        }
                        if let comment = item.kitchenComment, !comment.isEmpty {
                            Label(comment, systemImage: "text.bubble").font(.caption).foregroundStyle(.secondary)
                        }
                    }
                }
                HStack {
                    if let server = ticket.serverName, !server.isEmpty { Label(server, systemImage: "person").font(.caption) }
                    Spacer()
                    Label("\(minutes) min", systemImage: "timer")
                        .font(.caption.weight(.bold))
                        .foregroundStyle(minutes >= 20 ? .red : minutes >= 10 ? .orange : .secondary)
                }
                .foregroundStyle(.secondary)
            }
            .padding(14)
            .background(RoundedRectangle(cornerRadius: Theme.smallRadius).fill(Color(.secondarySystemGroupedBackground)))
            .overlay(alignment: .leading) {
                UnevenRoundedRectangle(topLeadingRadius: Theme.smallRadius, bottomLeadingRadius: Theme.smallRadius)
                    .fill(Theme.color(for: ticket.status))
                    .frame(width: 5)
            }
        }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isButton)
        .accessibilityIdentifier("kds.ticket.\(ticket.tableNumber)")
    }
}
