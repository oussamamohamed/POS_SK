import POSKit
import SwiftUI

struct RootView: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        Group {
            if app.isUnlocked {
                MainView()
                    .transition(.opacity)
            } else {
                PinLockView()
                    .transition(.opacity)
            }
        }
        .animation(.easeInOut(duration: 0.25), value: app.isUnlocked)
        .noticeOverlay()
        .animation(.spring(duration: 0.35), value: app.context.notice)
        .task {
            await app.catalog.load()
        }
    }
}

/// Barre latérale fixe façon terminal de caisse : toujours visible, une touche par écran.
struct MainView: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        HStack(spacing: 0) {
            NavigationRail()
            Divider()
            content
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(Color.posBackground)
        }
        .ignoresSafeArea(.keyboard)
    }

    @ViewBuilder
    private var content: some View {
        switch app.section {
        case .floor:
            FloorPlanView()
        case .order, .counter:
            OrderEntryView()
        case .kitchen:
            KitchenView()
        case .reports:
            FiscalView()
        case .settings:
            NavigationStack {
                SettingsView(isModal: false)
            }
        }
    }
}

struct NavigationRail: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        VStack(spacing: 6) {
            Image(systemName: "fork.knife.circle.fill")
                .font(.system(size: 34))
                .foregroundStyle(Color.accentColor)
                .padding(.vertical, 14)
                .accessibilityHidden(true)

            ForEach(AppSection.allCases) { section in
                if section == .settings {
                    Spacer(minLength: 12)
                }
                railButton(section)
            }

            operatorBadge
        }
        .padding(.vertical, 8)
        .padding(.horizontal, 8)
        .frame(width: 96)
        .background(Color.posSurface)
    }

    private func railButton(_ section: AppSection) -> some View {
        let selected = app.section == section
        return Button {
            Task { await select(section) }
        } label: {
            VStack(spacing: 6) {
                Image(systemName: section.systemImage)
                    .font(.system(size: 22, weight: .semibold))
                    .frame(height: 26)
                Text(shortTitle(section))
                    .font(.caption2.weight(.semibold))
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
            }
            .frame(maxWidth: .infinity, minHeight: 64)
            .foregroundStyle(selected ? Color.white : Color.primary)
            .background(
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .fill(selected ? Color.accentColor : Color.clear)
            )
            .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("nav.\(section.rawValue)")
        .accessibilityLabel(section.title)
        .accessibilityAddTraits(selected ? .isSelected : [])
    }

    private var operatorBadge: some View {
        VStack(spacing: 6) {
            Text(initials)
                .font(.headline)
                .foregroundStyle(.white)
                .frame(width: 44, height: 44)
                .background(Circle().fill(Color.accentColor.gradient))
                .accessibilityIdentifier("session.operator")
                .accessibilityLabel(app.currentOperator?.name ?? "")
            Button {
                Task { await app.lock() }
            } label: {
                Label("Verrouiller", systemImage: "lock.fill")
                    .labelStyle(.iconOnly)
                    .font(.title3)
                    .frame(width: 56, height: 44)
            }
            .buttonStyle(.bordered)
            .accessibilityIdentifier("session.lock")
            .accessibilityLabel("Verrouiller la caisse")
        }
        .padding(.top, 8)
    }

    private var initials: String {
        let name = app.currentOperator?.name.components(separatedBy: " (").first ?? ""
        return name.split(separator: " ").prefix(2).compactMap(\.first).map(String.init).joined()
    }

    private func shortTitle(_ section: AppSection) -> String {
        switch section {
        case .floor: "Salle"
        case .order: "Commande"
        case .counter: "Comptoir"
        case .kitchen: "Cuisine"
        case .reports: "Rapports"
        case .settings: "Réglages"
        }
    }

    private func select(_ section: AppSection) async {
        if section == .counter {
            await app.openCounter()
        } else {
            app.section = section
        }
    }
}
