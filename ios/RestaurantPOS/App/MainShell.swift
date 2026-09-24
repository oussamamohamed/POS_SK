import SwiftUI
import PosKit

/// Coque principale : barre latérale compacte + en-tête + écran courant.
struct MainShell: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router

    var body: some View {
        HStack(spacing: 0) {
            SidebarRail()
            Divider().ignoresSafeArea()
            VStack(spacing: 0) {
                HeaderBar()
                if model.happyHour.isActive {
                    HappyHourBanner()
                }
                content
                    .frame(maxWidth: .infinity, maxHeight: .infinity)
            }
        }
        .background(Color(.systemGroupedBackground))
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
        case .kitchen: KitchenScreen()
        case .fiscal: FiscalScreen()
        case .admin: AdminScreen()
        }
    }
}

struct SidebarRail: View {
    @Environment(AppModel.self) private var model
    @Environment(Router.self) private var router

    var body: some View {
        VStack(spacing: 6) {
            Image(systemName: "fork.knife.circle.fill")
                .font(.system(size: 34))
                .foregroundStyle(Color.accentColor)
                .padding(.vertical, 14)

            ForEach(Router.Section.allCases) { section in
                if !section.requiresManager || model.session.isManager {
                    RailButton(section: section, isSelected: router.section == section) {
                        router.section = section
                    }
                }
            }

            Spacer()

            Button {
                model.session.lock()
            } label: {
                VStack(spacing: 4) {
                    Image(systemName: "lock.fill").font(.title3)
                    Text("Verrouiller").font(.caption2.weight(.semibold))
                }
                .frame(width: 76, height: 64)
                .foregroundStyle(.secondary)
            }
            .accessibilityIdentifier("nav.lock")
            .padding(.bottom, 12)
        }
        .frame(width: Theme.sidebarWidth)
        .background(Color(.secondarySystemBackground))
    }
}

private struct RailButton: View {
    let section: Router.Section
    let isSelected: Bool
    let action: () -> Void

    var body: some View {
        Button(action: { Haptics.tap(); action() }) {
            VStack(spacing: 4) {
                Image(systemName: section.systemImage).font(.title3).symbolVariant(isSelected ? .fill : .none)
                Text(section.title).font(.caption2.weight(.semibold))
            }
            .frame(width: 76, height: 64)
            .foregroundStyle(isSelected ? Color.accentColor : Color.secondary)
            .background(RoundedRectangle(cornerRadius: 14, style: .continuous).fill(isSelected ? Color.accentColor.opacity(0.14) : .clear))
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
        HStack(spacing: 16) {
            Text(router.section.title).font(.title2.weight(.bold))
            Spacer()
            ConnectionBadge(isOnline: model.network.isOnline || environment.launch.isUITest, isRealtime: model.isRealtimeConnected, terminal: model.settings.terminalId)
            if let op = model.session.currentOperator {
                HStack(spacing: 10) {
                    Image(systemName: "person.crop.circle.fill").font(.title2).foregroundStyle(Color.accentColor)
                    VStack(alignment: .leading, spacing: 0) {
                        Text(op.name).font(.subheadline.weight(.semibold)).lineLimit(1)
                        Text(op.role.label).font(.caption).foregroundStyle(.secondary)
                    }
                }
                .accessibilityElement(children: .combine)
                .accessibilityIdentifier("header.operator")
            }
        }
        .padding(.horizontal, 20)
        .frame(height: 60)
        .background(Color(.systemBackground))
    }
}

struct ConnectionBadge: View {
    let isOnline: Bool
    let isRealtime: Bool
    let terminal: String

    var body: some View {
        HStack(spacing: 6) {
            Circle().fill(isOnline ? Color.green : Color.red).frame(width: 8, height: 8)
            Text(isOnline ? "En ligne · \(terminal)" : "Hors ligne").font(.caption.weight(.semibold))
            if isRealtime { Image(systemName: "bolt.fill").font(.caption2).foregroundStyle(.yellow) }
        }
        .padding(.horizontal, 10)
        .padding(.vertical, 6)
        .background(Capsule().fill(Color(.secondarySystemFill)))
        .accessibilityIdentifier("header.connection")
    }
}

/// Bandeau Happy Hour avec compte à rebours et accès aux dérogations superviseur.
struct HappyHourBanner: View {
    @Environment(AppModel.self) private var model
    @State private var showsOverride = false

    var body: some View {
        HStack(spacing: 14) {
            Image(systemName: model.happyHour.status.isOverride ? "bolt.fill" : "wineglass.fill").font(.title3)
            VStack(alignment: .leading, spacing: 0) {
                Text(model.happyHour.bannerTitle).font(.subheadline.weight(.bold))
                    .accessibilityIdentifier("happyhour.banner")
                Text(model.happyHour.status.isOverride ? "Tarifs réduits forcés par un responsable" : "Tarifs préférentiels actifs")
                    .font(.caption)
            }
            Spacer()
            Text(model.happyHour.countdownText)
                .font(.title3.monospacedDigit().weight(.bold))
                .accessibilityIdentifier("happyhour.countdown")
            Button("Dérogation") { showsOverride = true }
                .buttonStyle(.bordered)
                .tint(.white)
                .accessibilityIdentifier("happyhour.override")
        }
        .foregroundStyle(.white)
        .padding(.horizontal, 20)
        .padding(.vertical, 10)
        .background(LinearGradient(colors: [Theme.happyHour, .orange], startPoint: .leading, endPoint: .trailing))
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
            SheetHeader(title: "Dérogation Happy Hour", subtitle: "Code PIN d'un responsable requis") { dismiss() }
            SupervisorPinPad(pin: $pin, identifierPrefix: "hh.pin")
            TextField("Motif (facultatif)", text: $reason)
                .textFieldStyle(.roundedBorder)
                .accessibilityIdentifier("hh.reason")
            HStack(spacing: 12) {
                ActionButton(title: "+30 min", systemImage: "plus.circle", tint: Theme.happyHour) {
                    Task { if await model.happyHour.activateOverride(pin: pin, minutes: 30, reason: reason) { dismiss() } }
                }
                .accessibilityIdentifier("hh.extend30")
                ActionButton(title: "Forcer 1 h", systemImage: "bolt", tint: .orange) {
                    Task { if await model.happyHour.activateOverride(pin: pin, minutes: 60, reason: reason) { dismiss() } }
                }
                .accessibilityIdentifier("hh.force60")
                ActionButton(title: "Arrêter", systemImage: "stop.circle", tint: .red, prominent: false) {
                    Task { if await model.happyHour.stopOverride(pin: pin, reason: reason) { dismiss() } }
                }
                .accessibilityIdentifier("hh.stop")
            }
        }
        .padding(28)
        .presentationDetents([.large])
    }
}
