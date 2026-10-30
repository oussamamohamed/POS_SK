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
                ChipButton(title: String(localized: "kitchen.filter_all_stations"), isSelected: kitchen.stationFilter == nil) { kitchen.stationFilter = nil }
                    .accessibilityIdentifier("kds.filter.all")
                ForEach(kitchen.stations, id: \.self) { station in
                    ChipButton(title: station.replacingOccurrences(of: "_", with: " "), isSelected: kitchen.stationFilter == station) { kitchen.stationFilter = station }
                        .accessibilityIdentifier("kds.filter.\(station)")
                }
                Spacer()
                if let refresh = kitchen.lastRefresh {
                    Text("kitchen.updated_at \(refresh.formatted(date: .omitted, time: .standard))").font(.posLabel).foregroundStyle(Theme.inkSubtle)
                }
                Button { Task { await kitchen.load() } } label: {
                    Image(systemName: "arrow.clockwise")
                        .font(Font.pos(17, .semibold))
                        .frame(width: Theme.touchMin, height: Theme.touchMin)
                }
                .buttonStyle(ActionButtonStyle(kind: .neutral))
                .accessibilityLabel("common.refresh_label")
                    .accessibilityIdentifier("kds.refresh")
            }
            .padding(Theme.Space.xl)

            kitchenStats(kitchen)

            if !canBump {
                Label("kitchen.readonly_notice", systemImage: "eye")
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

            kitchenBottom(kitchen)
                .padding(.horizontal, Theme.Space.xl)
                .padding(.vertical, Theme.Space.l)
        }
        .task {
            // Rafraîchissement périodique en complément du temps réel SignalR.
            while !Task.isCancelled {
                await kitchen.load()
                try? await Task.sleep(for: .seconds(15))
            }
        }
    }

    /// Tuiles : bons en cours, en retard, durée moyenne, sortis la dernière heure (sur les derniers bons du serveur).
    private func kitchenStats(_ kitchen: KitchenStore) -> some View {
        let stats = KitchenStats.compute(tickets: kitchen.tickets)
        let average = stats.averageSeconds.map { String(format: "%d:%02d", Int($0) / 60, Int($0) % 60) } ?? "—"
        return HStack(spacing: Theme.Space.m) {
            KPIView(title: String(localized: "kitchen.stat_open"), value: "\(stats.open)", systemImage: "tray.full")
            KPIView(title: String(localized: "kitchen.stat_late"), value: "\(stats.late)", systemImage: "clock.badge.exclamationmark", tint: Theme.danger)
            KPIView(title: String(localized: "kitchen.stat_avg"), value: average, systemImage: "timer")
            KPIView(title: String(localized: "kitchen.stat_out"), value: "\(stats.servedLastHour)", systemImage: "checkmark.circle")
        }
        .padding(.horizontal, Theme.Space.xl)
        .padding(.bottom, Theme.Space.m)
        .fixedSize(horizontal: false, vertical: true)
        .accessibilityIdentifier("kds.stats")
    }

    /// « À cuire » (portions en attente, toutes salles) et « Récemment sortis ».
    private func kitchenBottom(_ kitchen: KitchenStore) -> some View {
        let allDay = KitchenStats.allDay(tickets: kitchen.tickets)
        let served = KitchenStats.recentlyServed(tickets: kitchen.tickets)
        return HStack(alignment: .top, spacing: Theme.Space.l) {
            Card {
                VStack(alignment: .leading, spacing: Theme.Space.xs) {
                    Text("kitchen.allday_title").font(.posHeadline).foregroundStyle(Theme.ink)
                    if allDay.isEmpty { Text("kitchen.allday_empty").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                    ForEach(allDay.prefix(4), id: \.name) { row in
                        HStack { Text(row.name).font(.posBody); Spacer(); Text("×\(row.quantity)").font(Font.posMono(15, .semibold)) }.foregroundStyle(Theme.ink)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .accessibilityIdentifier("kds.allday")
            Card {
                VStack(alignment: .leading, spacing: Theme.Space.xs) {
                    Text("kitchen.bumped_title").font(.posHeadline).foregroundStyle(Theme.ink)
                    if served.isEmpty { Text("kitchen.bumped_empty").font(.posBody).foregroundStyle(Theme.inkSubtle) }
                    ForEach(served) { t in
                        let took = (t.completedAtUtc ?? Date()).timeIntervalSince(t.dispatchedAtUtc ?? Date())
                        HStack { Text(t.tableNumber).font(.posBody.weight(.semibold)); Spacer(); Text(String(format: "%d:%02d", Int(max(0, took)) / 60, Int(max(0, took)) % 60)).font(Font.posMono(15, .semibold)) }.foregroundStyle(Theme.ink)
                    }
                }
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .accessibilityIdentifier("kds.bumped")
        }
        .fixedSize(horizontal: false, vertical: true)
    }

    private func column(_ status: TicketStatus, canBump: Bool) -> some View {
        let tickets = model.kitchen.tickets(in: status)
        return VStack(alignment: .leading, spacing: 12) {
            HStack {
                Circle().fill(Theme.color(for: status)).frame(width: 10, height: 10)
                Text(status.label).font(.posHeadline).foregroundStyle(Theme.ink)
                Spacer()
                Text("\(tickets.count)").font(Font.posMono(15, .semibold)).foregroundStyle(Theme.inkMuted)
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
            let isOpen = ticket.status == .pending || ticket.status == .inPreparation
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                HStack(spacing: Theme.Space.s) {
                    Text(ticket.tableNumber).font(Font.pos(22, .heavy)).foregroundStyle(Theme.ink)
                    Spacer()
                    if isOpen && minutes > KitchenTarget.minutes { Badge(text: String(localized: "kitchen.late_tag"), color: Theme.danger) }
                    Label("\(minutes) min", systemImage: "timer")
                        .font(Font.posMono(15, .semibold))
                        .foregroundStyle(minutes >= KitchenTarget.minutes ? Theme.danger : minutes >= 10 ? Theme.warning : Theme.inkMuted)
                }
                if isOpen {
                    ProgressView(value: min(1, Double(minutes) / Double(KitchenTarget.minutes)))
                        .tint(minutes >= KitchenTarget.minutes ? Theme.danger : Theme.success)
                        .accessibilityHidden(true)
                }
                ForEach(ticket.items) { item in
                    VStack(alignment: .leading, spacing: 2) {
                        HStack(alignment: .firstTextBaseline, spacing: Theme.Space.s) {
                            Text("\(item.quantity)×")
                                .font(Font.posMono(15, .semibold))
                                .frame(minWidth: 28, alignment: .leading)
                            Text(item.productName).font(Font.pos(15, .semibold))
                        }
                        .foregroundStyle(Theme.ink)
                        if let mods = item.modifiersSummary, !mods.isEmpty {
                            Text(mods).font(Font.pos(13, .medium)).foregroundStyle(Theme.warning).padding(.leading, 36)
                        }
                        if let comment = item.kitchenComment, !comment.isEmpty {
                            Text("kitchen.quoted_comment \(comment)").font(Font.pos(13, .medium)).italic().foregroundStyle(Theme.inkMuted).padding(.leading, 36)
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
