import SwiftUI
import PosKit

/// Premier lancement : « Serveur » ou « Autonome ».
struct ModeChoiceScreen: View {
    @Environment(AppEnvironment.self) private var environment

    var body: some View {
        ZStack {
            Theme.canvas.ignoresSafeArea()
            VStack(spacing: Theme.Space.xxl) {
                Image("BrandMark")
                    .resizable()
                    .scaledToFill()
                    .frame(width: 88, height: 88)
                    .clipShape(RoundedRectangle(cornerRadius: Theme.Radius.lg, style: .continuous))
                    .accessibilityHidden(true)
                VStack(spacing: Theme.Space.s) {
                    Text("mode.choice_title").font(.posTitle).foregroundStyle(Theme.ink)
                    Text("mode.choice_subtitle").font(.posBody).foregroundStyle(Theme.inkMuted)
                }
                HStack(spacing: Theme.Space.l) {
                    ModeCard(title: "mode.server_title", detail: "mode.server_detail", systemImage: "server.rack", identifier: "mode.server") {
                        environment.choose(.server)
                    }
                    ModeCard(title: "mode.standalone_title", detail: "mode.standalone_detail", systemImage: "ipad", identifier: "mode.standalone") {
                        environment.choose(.standalone)
                    }
                }
            }
            .padding(40)
            .frame(maxWidth: 820)
        }
    }
}

private struct ModeCard: View {
    let title: LocalizedStringKey
    let detail: LocalizedStringKey
    let systemImage: String
    let identifier: String
    let action: () -> Void

    var body: some View {
        Button(action: { Haptics.tap(); action() }) {
            VStack(alignment: .leading, spacing: Theme.Space.s) {
                Image(systemName: systemImage).font(.system(size: 32, weight: .semibold)).foregroundStyle(Theme.primaryInk)
                Text(title).font(.posHeadline).foregroundStyle(Theme.ink)
                Text(detail).font(.posBody).foregroundStyle(Theme.inkMuted).multilineTextAlignment(.leading)
            }
            .padding(Theme.Space.xl)
            .frame(maxWidth: .infinity, minHeight: 190, alignment: .topLeading)
            .background(RoundedRectangle(cornerRadius: Theme.Radius.lg, style: .continuous).fill(Theme.surface))
            .contentShape(RoundedRectangle(cornerRadius: Theme.Radius.lg, style: .continuous))
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier(identifier)
    }
}

/// La base locale n'a pas pu être ouverte : on ne démarre pas (jamais de base vide de remplacement).
struct StartupErrorScreen: View {
    @Environment(AppEnvironment.self) private var environment
    let message: String

    var body: some View {
        VStack(spacing: Theme.Space.l) {
            Image(systemName: "exclamationmark.triangle.fill").font(.system(size: 44)).foregroundStyle(Theme.danger)
            Text("startup.error_title").font(.posTitle).foregroundStyle(Theme.ink)
            Text(message).font(.posBody).foregroundStyle(Theme.inkMuted).multilineTextAlignment(.center)
                .accessibilityIdentifier("startup.message")
            ActionButton(title: String(localized: "startup.retry"), systemImage: "arrow.clockwise", kind: .primary) {
                environment.retryStartup()
            }
            .frame(maxWidth: 320)
            .accessibilityIdentifier("startup.retry")
        }
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .background(Theme.canvas.ignoresSafeArea())
    }
}

/// Rappel permanent : le mode autonome n'est pas encore fiscal (sous-projet 2).
struct StandaloneBanner: View {
    var body: some View {
        HStack(spacing: Theme.Space.s) {
            Image(systemName: "exclamationmark.triangle.fill")
            Text("mode.non_fiscal_banner").font(.system(size: 13, weight: .semibold))
            Spacer()
        }
        .foregroundStyle(Theme.warning)
        .padding(.horizontal, Theme.Space.xl)
        .padding(.vertical, Theme.Space.s)
        .background(Theme.warning.opacity(0.14))
        .accessibilityElement(children: .combine)
        .accessibilityIdentifier("standalone.banner")
    }
}
