import SwiftUI
import PosKit

/// Coque principale : rail de navigation + en-tête + écran courant.
struct MainShell: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router
    @Environment(AppEnvironment.self) private var environment

    var body: some View {
        HStack(spacing: 0) {
            SidebarRail()
            VStack(spacing: 0) {
                HeaderBar()
                if environment.isStandalone, let backup = environment.backup, backup.needsAttention { BackupBanner(controller: backup) }
                if model.happyHour.isActive {
                    HappyHourBanner()
                }
                content
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .background(Theme.canvas)
        .task {
            // Horloge d'une seconde : compte à rebours Happy Hour.
            while !Task.isCancelled {
                try? await Task.sleep(for: .seconds(1))
                if model.happyHour.tick() { await model.happyHour.refresh() }
            }
        }
    }

    @ViewBuilder
    private var content: some View {
        switch router.section {
        case .order: OrderScreen()
        case .floor: FloorScreen()
        case .bookings: BookingsScreen()
        case .kitchen: KitchenScreen()
        case .menu: MenuAnalysisScreen()
        case .fiscal: FiscalScreen()
        case .admin: AdminScreen()
        }
    }
}

struct SidebarRail: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router
    @Environment(AppEnvironment.self) private var environment

    private func isVisible(_ section: Router.Section) -> Bool {
        !section.requiresManager || model.session.isManager
    }

    var body: some View {
        VStack(spacing: Theme.Space.s) {
            Image("BrandMark")
                .resizable()
                .scaledToFill()
                .frame(width: 48, height: 48)
                .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous))
                .padding(.top, Theme.Space.l)
                .padding(.bottom, Theme.Space.m)
                .accessibilityHidden(true)

            ForEach(Router.Section.allCases) { section in
                if isVisible(section) {
                    RailButton(section: section, isSelected: router.section == section, badge: section == .bookings ? model.bookings.attentionCount : 0) {
                        router.section = section
                    }
                }
            }

            Spacer()

            Button {
                model.session.lock()
            } label: {
                VStack(spacing: 4) {
                    Image(systemName: "lock.fill").font(.system(size: 20, weight: .semibold))
                    Text("common.nav_lock").font(Font.pos(12, .semibold))
                }
                .frame(width: 72, height: 64)
                .foregroundStyle(Theme.inkMuted)
                .contentShape(Rectangle())
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("nav.lock")
            .padding(.bottom, Theme.Space.l)
        }
        .frame(width: Theme.sidebarWidth)
        .frame(maxHeight: .infinity)
        .background(Theme.sunken.ignoresSafeArea())
    }
}

private struct RailButton: View {
    let section: Router.Section
    let isSelected: Bool
    var badge = 0
    let action: () -> Void

    var body: some View {
        Button(action: { Haptics.tap(); action() }) {
            VStack(spacing: 4) {
                Image(systemName: section.systemImage)
                    .font(Font.pos(20, .semibold))
                    .symbolVariant(isSelected ? .fill : .none)
                Text(section.title).font(Font.pos(12, .semibold)).lineLimit(1).minimumScaleFactor(0.7)
            }
            .frame(width: 72, height: 64)
            .foregroundStyle(isSelected ? Theme.primaryInk : Theme.inkMuted)
            .background(RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous).fill(isSelected ? Theme.primarySoft : .clear))
            .contentShape(RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous))
            .overlay(alignment: .topTrailing) {
                if badge > 0 {
                    Text("\(badge)").font(Font.posMono(11, .semibold)).foregroundStyle(.white)
                        .padding(.horizontal, 5).frame(minWidth: 18, minHeight: 18)
                        .background(Capsule().fill(Theme.danger))
                        .accessibilityIdentifier("nav.bookings.badge")
                }
            }
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("nav.\(section.rawValue)")
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}

struct HeaderBar: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router
    @Environment(AppEnvironment.self) private var environment

    var body: some View {
        HStack(spacing: Theme.Space.l) {
            Text(router.section.title).font(.posTitle).foregroundStyle(Theme.ink)
            if let context {
                Text(context).font(Font.pos(15, .semibold)).foregroundStyle(Theme.inkMuted).lineLimit(1)
            }
            Spacer()
            ConnectionBadge(isOnline: model.network.isOnline || environment.launch.isUITest, isRealtime: model.isRealtimeConnected, terminal: model.settings.terminalId)
            if let op = model.session.currentOperator {
                HStack(spacing: 10) {
                    Text(initials(of: op.name))
                        .font(Font.pos(15, .bold))
                        .foregroundStyle(Theme.primaryInk)
                        .frame(width: 40, height: 40)
                        .background(Circle().fill(Theme.primarySoft))
                    VStack(alignment: .leading, spacing: 0) {
                        Text(op.name).font(Font.pos(15, .bold)).foregroundStyle(Theme.ink).lineLimit(1)
                        Text(op.role.label).font(Font.pos(12, .semibold)).foregroundStyle(Theme.inkMuted)
                    }
                }
                .accessibilityElement(children: .combine)
                .accessibilityIdentifier("header.operator")
            }
        }
        .padding(.horizontal, Theme.Space.xl)
        .frame(height: Theme.headerHeight)
        .background(Theme.surface)
        .overlay(alignment: .bottom) { Theme.line.frame(height: 1) }
    }

    /// Table en cours sur l'écran Caisse.
    private var context: String? {
        guard router.section == .order else { return nil }
        let ticket = model.ticket
        if ticket.isCounter { return ticket.title }
        return ticket.covers > 0 ? "\(ticket.title) · \(ticket.covers) couvert(s)" : ticket.title
    }

    private func initials(of name: String) -> String {
        let letters = name.split(separator: " ").prefix(2).compactMap(\.first)
        return letters.isEmpty ? "?" : String(letters).uppercased()
    }
}

struct ConnectionBadge: View {
    let isOnline: Bool
    let isRealtime: Bool
    let terminal: String

    var body: some View {
        HStack(spacing: Theme.Space.s) {
            Circle().fill(isOnline ? Theme.success : Theme.danger).frame(width: 8, height: 8)
            Text(isOnline ? "common.status_online \(terminal)" : "common.status_offline").font(.posLabel).foregroundStyle(Theme.ink)
            if isRealtime { Image(systemName: "bolt.fill").font(.caption2).foregroundStyle(Theme.brandCyan) }
        }
        .padding(.horizontal, Theme.Space.m)
        .frame(minHeight: 32)
        .background(Capsule().fill(Theme.raised))
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("header.connection")
    }
}

/// Bandeau Happy Hour avec compte à rebours et accès aux dérogations superviseur.
struct HappyHourBanner: View {
    @Environment(AppModel.self) private var model
    @State private var showsOverride = false

    var body: some View {
        HStack(spacing: Theme.Space.l) {
            Image(systemName: model.happyHour.status.isOverride ? "bolt.fill" : "wineglass.fill").font(.title3)
            VStack(alignment: .leading, spacing: 0) {
                Text(model.happyHour.bannerTitle).font(Font.pos(15, .bold))
                    .accessibilityIdentifier("happyhour.banner")
                Text(model.happyHour.status.isOverride ? "common.happy_hour_override_subtitle" : "common.happy_hour_active_subtitle")
                    .font(Font.pos(12, .semibold))
            }
            Spacer()
            Text(model.happyHour.countdownText)
                .font(Font.posMono(22, .semibold))
                .accessibilityIdentifier("happyhour.countdown")
            Button { showsOverride = true } label: {
                Text("common.happy_hour_override_button")
                    .font(Font.pos(15, .bold))
                    .padding(.horizontal, Theme.Space.l)
                    .frame(minHeight: Theme.touchMin)
                    .background(RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous).fill(Theme.onHappy.opacity(0.12)))
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("happyhour.override")
        }
        .foregroundStyle(Theme.onHappy)
        .padding(.horizontal, Theme.Space.xl)
        .padding(.vertical, 10)
        .background(Theme.happyHour)
        .sheet(isPresented: $showsOverride) { HappyHourOverrideSheet() }
    }
}

struct HappyHourOverrideSheet: View {
    @Environment(AppModel.self) private var model
    @Environment(\.dismiss) private var dismiss
    @State private var pin = ""
    @State private var reason = ""

    var body: some View {
        VStack(spacing: 20) {
            SheetHeader(title: String(localized: "common.happy_hour_override_title"), subtitle: String(localized: "common.happy_hour_override_pin_hint")) { dismiss() }
            SupervisorPinPad(pin: $pin, identifierPrefix: "hh.pin")
            TextField("common.happy_hour_reason_placeholder", text: $reason)
                .textFieldStyle(.roundedBorder)
                .accessibilityIdentifier("hh.reason")
            HStack(spacing: 12) {
                ActionButton(title: String(localized: "common.happy_hour_extend_30"), systemImage: "plus.circle", kind: .tonal) {
                    Task { if await model.happyHour.activateOverride(pin: pin, minutes: 30, reason: reason) { dismiss() } }
                }
                .accessibilityIdentifier("hh.extend30")
                ActionButton(title: String(localized: "common.happy_hour_force_60"), systemImage: "bolt", kind: .primary) {
                    Task { if await model.happyHour.activateOverride(pin: pin, minutes: 60, reason: reason) { dismiss() } }
                }
                .accessibilityIdentifier("hh.force60")
                ActionButton(title: String(localized: "common.happy_hour_stop"), systemImage: "stop.circle", kind: .danger) {
                    Task { if await model.happyHour.stopOverride(pin: pin, reason: reason) { dismiss() } }
                }
                .accessibilityIdentifier("hh.stop")
            }
        }
        .padding(Theme.Space.xxl)
        .background(Theme.surface)
        .presentationDetents([.large])
    }
}
