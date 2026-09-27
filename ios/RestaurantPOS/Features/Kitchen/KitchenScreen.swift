import SwiftUI
import PosKit

/// Écran cuisine (KDS) : trois colonnes, un tap fait avancer le bon.
struct KitchenScreen: View {
    @Environment(AppModel.self) private var model

    var body: some View {
        let kitchen = model.kitchen
        let canBump = model.session.currentOperator?.role.canBumpKitchen ?? false
        VStack(spacing: 0) {
            HStack(spacing: Theme.Space.s) {
                ChipButton(title: "Tous les postes", isSelected: kitchen.stationFilter == nil) { kitchen.stationFilter = nil }
                    .accessibilityIdentifier("kds.filter.all")
                ForEach(kitchen.stations, id: \.self) { station in
                    ChipButton(title: station.replacingOccurrences(of: "_", with: " "), isSelected: kitchen.stationFilter == station) { kitchen.stationFilter = station }
                        .accessibilityIdentifier("kds.filter.\(station)")
                }
                Spacer()
                if let refresh = kitchen.lastRefresh {
                    Text("Mis à jour \(refresh.formatted(date: .omitted, time: .standard))").font(.posLabel).foregroundStyle(Theme.inkSubtle)
                }
                Button { Task { await kitchen.load() } } label: {
                    Image(systemName: "arrow.clockwise")
                        .font(.system(size: 17, weight: .semibold))
                        .frame(width: Theme.touchMin, height: Theme.touchMin)
                }
                .buttonStyle(ActionButtonStyle(kind: .neutral))
                .accessibilityLabel("Actualiser")
                    .accessibilityIdentifier("kds.refresh")
            }
            .padding(Theme.Space.xl)

            if !canBump {
                Label("Lecture seule : seuls la cuisine et les responsables font avancer les bons.", systemImage: "eye")
                    .font(.posLabel)
                    .foregroundStyle(Theme.inkMuted)
                    .padding(.bottom, Theme.Space.s)
            }

            HStack(alignment: .top, spacing: Theme.Space.l) {
                column(.pending, canBump: canBump)
                column(.inPreparation, canBump: canBump)
                column(.ready, canBump: canBump)
            }
            .padding(.horizontal, Theme.Space.xl)
            .padding(.bottom, Theme.Space.xl)
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
                Text(status.label).font(.posHeadline).foregroundStyle(Theme.ink)
                Spacer()
                Text("\(tickets.count)").font(.system(size: 15, weight: .semibold, design: .monospaced)).foregroundStyle(Theme.inkMuted)
                    .accessibilityIdentifier("kds.count.\(status)")
            }
            ScrollView {
                LazyVStack(spacing: Theme.Space.m) {
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
        .padding(Theme.Space.m)
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .background(RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous).fill(Theme.sunken))
    }
}

/// Bon de cuisine : la colonne dit le statut, le chronomètre dit l'urgence.
struct KitchenTicketCard: View {
    let ticket: KitchenTicket

    var body: some View {
        TimelineView(.periodic(from: .now, by: 30)) { context in
            let minutes = ticket.dispatchedAtUtc.map { Int(context.date.timeIntervalSince($0) / 60) } ?? 0
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                HStack(spacing: Theme.Space.s) {
                    Text(ticket.tableNumber).font(.system(size: 22, weight: .heavy)).foregroundStyle(Theme.ink)
                    Spacer()
                    Label("\(minutes) min", systemImage: "timer")
                        .font(.system(size: 15, weight: .semibold, design: .monospaced))
                        .foregroundStyle(minutes >= 20 ? Theme.danger : minutes >= 10 ? Theme.warning : Theme.inkMuted)
                }
                ForEach(ticket.items) { item in
                    VStack(alignment: .leading, spacing: 2) {
                        HStack(alignment: .firstTextBaseline, spacing: Theme.Space.s) {
                            Text("\(item.quantity)×")
                                .font(.system(size: 15, weight: .semibold, design: .monospaced))
                                .frame(minWidth: 28, alignment: .leading)
                            Text(item.productName).font(.system(size: 15, weight: .semibold))
                        }
                        .foregroundStyle(Theme.ink)
                        if let mods = item.modifiersSummary, !mods.isEmpty {
                            Text(mods).font(.system(size: 13, weight: .medium)).foregroundStyle(Theme.warning).padding(.leading, 36)
                        }
                        if let comment = item.kitchenComment, !comment.isEmpty {
                            Text("« \(comment) »").font(.system(size: 13, weight: .medium)).italic().foregroundStyle(Theme.inkMuted).padding(.leading, 36)
                        }
                    }
                }
                Theme.line.frame(height: 1)
                HStack {
                    if let server = ticket.serverName, !server.isEmpty {
                        Label(server, systemImage: "person").font(.posLabel).foregroundStyle(Theme.inkMuted)
                    }
                    Spacer()
                    Badge(text: ticket.stationId.replacingOccurrences(of: "_", with: " "), marker: false)
                }
            }
            .padding(Theme.Space.l)
            .background(RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous).fill(Theme.surface))
            .cardShadow()
            .contentShape(RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous))
        }
        .accessibilityElement(children: .combine)
        .accessibilityAddTraits(.isButton)
        .accessibilityIdentifier("kds.ticket.\(ticket.tableNumber)")
    }
}
