import POSKit
import SwiftUI

/// Écran cuisine (KDS) : trois colonnes, un toucher fait avancer le bon.
struct KitchenView: View {
    @Environment(AppModel.self) private var app

    private let columns: [TicketStatus] = [.pending, .inPreparation, .ready]

    var body: some View {
        VStack(alignment: .leading, spacing: 16) {
            HStack {
                Text("Cuisine").font(.largeTitle.weight(.bold))
                Spacer()
                Button {
                    Task { await app.kitchen.load() }
                } label: {
                    Label("Actualiser", systemImage: "arrow.clockwise")
                }
                .buttonStyle(.bordered)
                .accessibilityIdentifier("kds.refresh")
            }

            ScrollView(.horizontal, showsIndicators: false) {
                HStack(spacing: 10) {
                    stationChip(nil, title: "Tous les postes")
                    ForEach(app.kitchen.stations, id: \.self) { station in
                        stationChip(station, title: KitchenTicket.stationLabel(for: station))
                    }
                }
            }

            HStack(alignment: .top, spacing: 16) {
                ForEach(columns) { status in
                    column(status)
                }
            }
        }
        .padding(24)
        .task {
            while !Task.isCancelled {
                await app.kitchen.load()
                try? await Task.sleep(for: .seconds(10))
            }
        }
    }

    private func stationChip(_ station: String?, title: String) -> some View {
        let selected = app.kitchen.stationFilter == station
        return Button {
            app.kitchen.stationFilter = station
        } label: {
            Text(title)
                .font(.subheadline.weight(.semibold))
                .padding(.horizontal, 16)
                .frame(minHeight: 40)
                .foregroundStyle(selected ? Color.white : Color.primary)
                .background(Capsule().fill(selected ? Color.accentColor : Color.posSurface))
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("kds.station.\(station ?? "all")")
    }

    private func column(_ status: TicketStatus) -> some View {
        let tickets = app.kitchen.tickets(in: status)
        return VStack(alignment: .leading, spacing: 12) {
            HStack {
                Circle().fill(status.color).frame(width: 12, height: 12)
                Text(status.label).font(.headline)
                Spacer()
                Text("\(tickets.count)")
                    .font(.headline.monospacedDigit())
                    .padding(.horizontal, 10)
                    .padding(.vertical, 2)
                    .background(Capsule().fill(status.color.opacity(0.15)))
                    .accessibilityIdentifier("kds.count.\(status.apiName)")
            }
            ScrollView {
                LazyVStack(spacing: 12) {
                    ForEach(tickets) { ticket in
                        TicketCard(ticket: ticket, isLate: app.kitchen.isLate(ticket)) {
                            Task { await app.kitchen.bump(ticket) }
                        }
                    }
                }
                .padding(.bottom, 16)
            }
        }
        .padding(14)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(RoundedRectangle(cornerRadius: 20, style: .continuous).fill(Color.posSurface.opacity(0.6)))
    }
}

struct TicketCard: View {
    var ticket: KitchenTicket
    var isLate: Bool
    var onBump: () -> Void

    var body: some View {
        Button(action: onBump) {
            VStack(alignment: .leading, spacing: 10) {
                HStack(alignment: .firstTextBaseline) {
                    Text(ticket.tableNumber).font(.title3.weight(.bold))
                    Spacer()
                    TimelineView(.periodic(from: .now, by: 30)) { context in
                        Label("\(ticket.elapsedMinutes(now: context.date)) min", systemImage: "clock")
                            .font(.subheadline.weight(.semibold).monospacedDigit())
                            .foregroundStyle(isLate ? Color.red : Color.secondary)
                    }
                }
                HStack {
                    Text(ticket.stationLabel)
                    if !ticket.serverName.isEmpty {
                        Text("· \(ticket.serverName.components(separatedBy: " (").first ?? ticket.serverName)")
                    }
                    Spacer()
                    Text("\(ticket.coversCount) couv.")
                }
                .font(.caption)
                .foregroundStyle(.secondary)

                Divider()

                ForEach(ticket.items) { item in
                    VStack(alignment: .leading, spacing: 2) {
                        Text("\(item.quantity)× \(item.productName)").font(.body.weight(.semibold))
                        if let modifiers = item.modifiersSummary, !modifiers.isEmpty {
                            Text(modifiers).font(.caption).foregroundStyle(.secondary)
                        }
                        if let comment = item.kitchenComment, !comment.isEmpty {
                            Text(comment).font(.caption.italic()).foregroundStyle(.orange)
                        }
                    }
                }

                Text(actionLabel)
                    .font(.caption.weight(.bold))
                    .foregroundStyle(ticket.status.next.color)
            }
            .padding(14)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Color.posElevated))
            .overlay(
                RoundedRectangle(cornerRadius: 16, style: .continuous)
                    .stroke(isLate ? Color.red : ticket.status.color.opacity(0.5), lineWidth: isLate ? 3 : 1)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("kds.ticket.\(ticket.status.apiName)")
        .accessibilityHint(actionLabel)
    }

    private var actionLabel: String {
        switch ticket.status {
        case .pending: "Toucher pour lancer la préparation"
        case .inPreparation: "Toucher quand c'est prêt"
        case .ready: "Toucher quand c'est servi"
        case .served: "Servi"
        }
    }
}
