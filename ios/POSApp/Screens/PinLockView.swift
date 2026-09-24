import POSKit
import SwiftUI

/// Écran de verrouillage : identification express par code PIN.
struct PinLockView: View {
    @Environment(AppModel.self) private var app
    @State private var showSettings = false

    var body: some View {
        HStack(spacing: 0) {
            brandPanel
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(
                    LinearGradient(
                        colors: [Color.accentColor, Color.accentColor.opacity(0.7)],
                        startPoint: .topLeading,
                        endPoint: .bottomTrailing
                    )
                )

            pinPanel
                .frame(maxWidth: .infinity, maxHeight: .infinity)
                .background(Color.posBackground)
        }
        .ignoresSafeArea()
        .sheet(isPresented: $showSettings) {
            NavigationStack {
                SettingsView(isModal: true)
            }
            .noticeOverlay()
        }
    }

    private var brandPanel: some View {
        VStack(alignment: .leading, spacing: 18) {
            Image(systemName: "fork.knife.circle.fill")
                .font(.system(size: 64))
            Text("Restaurant POS")
                .font(.system(size: 44, weight: .bold, design: .rounded))
            TimelineView(.periodic(from: .now, by: 30)) { context in
                VStack(alignment: .leading, spacing: 4) {
                    Text(context.date.formatted(.dateTime.hour().minute()))
                        .font(.system(size: 64, weight: .light, design: .rounded))
                        .monospacedDigit()
                    Text(context.date.formatted(.dateTime.weekday(.wide).day().month(.wide)))
                        .font(.title3)
                }
            }
            Spacer()
            VStack(alignment: .leading, spacing: 8) {
                Label("Terminal \(app.settings.terminalId)", systemImage: "ipad.landscape")
                if app.settings.demoMode {
                    Label("Mode démonstration", systemImage: "sparkles")
                        .accessibilityIdentifier("pin.demoBadge")
                } else if let url = app.settings.serverURL {
                    Label(url.absoluteString, systemImage: "server.rack")
                }
            }
            .font(.headline)
        }
        .foregroundStyle(.white)
        .padding(48)
        .frame(maxWidth: .infinity, alignment: .leading)
    }

    private var pinPanel: some View {
        VStack(spacing: 28) {
            HStack {
                Spacer()
                Button {
                    showSettings = true
                } label: {
                    Image(systemName: "gearshape.fill")
                        .font(.title2)
                        .frame(width: 52, height: 52)
                }
                .accessibilityIdentifier("pin.settings")
                .accessibilityLabel("Réglages du terminal")
            }

            Spacer(minLength: 0)

            VStack(spacing: 10) {
                Text("Saisissez votre code")
                    .font(.title.weight(.semibold))
                Text("Code PIN opérateur à \(app.settings.pinAutoSubmitLength) chiffres")
                    .foregroundStyle(.secondary)
            }

            PinDots(count: app.pin.count, length: app.settings.pinAutoSubmitLength)
                .modifier(ShakeEffect(animatableData: CGFloat(app.pinFailures)))
                .animation(.linear(duration: 0.4), value: app.pinFailures)

            Group {
                if app.isAuthenticating {
                    ProgressView()
                } else if let error = app.pinError {
                    Text(error)
                        .foregroundStyle(.red)
                        .multilineTextAlignment(.center)
                        .accessibilityIdentifier("pin.error")
                } else {
                    Text(" ")
                }
            }
            .frame(height: 44)

            NumericKeypad(identifierPrefix: "pin", keyHeight: 76) { key in
                switch key {
                case .digit(let digit):
                    Task { await app.pinDigit(digit) }
                case .delete:
                    app.pinBackspace()
                case .clear:
                    app.pin.clear()
                case .doubleZero:
                    break
                }
            }
            .frame(maxWidth: 380)
            .disabled(app.isAuthenticating)
            .sensoryFeedback(.error, trigger: app.pinFailures)

            if app.settings.demoMode {
                Text("Démo : 1234 responsable · 2468 serveur · 5678 cuisine")
                    .font(.footnote)
                    .foregroundStyle(.secondary)
            }

            Spacer(minLength: 0)
        }
        .padding(32)
    }
}
