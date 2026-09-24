import POSKit
import SwiftUI

/// Pavé numérique à l'écran : évite le clavier système qui masquerait la note.
struct NumericKeypad: View {
    enum Key: Hashable {
        case digit(Int)
        case doubleZero
        case clear
        case delete
    }

    /// Préfixe des identifiants d'accessibilité (`pin.key.1`, `amount.key.00`…).
    var identifierPrefix: String
    var bottomLeft: Key = .clear
    var keyHeight: CGFloat = 72
    var onKey: (Key) -> Void

    private static let rows: [[Int]] = [[1, 2, 3], [4, 5, 6], [7, 8, 9]]

    var body: some View {
        Grid(horizontalSpacing: 12, verticalSpacing: 12) {
            ForEach(Self.rows, id: \.self) { row in
                GridRow {
                    ForEach(row, id: \.self) { digit in
                        key(.digit(digit))
                    }
                }
            }
            GridRow {
                key(bottomLeft)
                key(.digit(0))
                key(.delete)
            }
        }
    }

    private func key(_ key: Key) -> some View {
        Button {
            onKey(key)
        } label: {
            label(for: key)
                .frame(maxWidth: .infinity, minHeight: keyHeight)
                .background(
                    RoundedRectangle(cornerRadius: 16, style: .continuous)
                        .fill(Color.posElevated)
                )
                .contentShape(Rectangle())
        }
        .buttonStyle(.plain)
        .accessibilityIdentifier("\(identifierPrefix).key.\(identifier(for: key))")
        .accessibilityLabel(accessibilityLabel(for: key))
    }

    @ViewBuilder
    private func label(for key: Key) -> some View {
        switch key {
        case .digit(let digit):
            Text("\(digit)").font(.system(size: 30, weight: .semibold, design: .rounded))
        case .doubleZero:
            Text("00").font(.system(size: 26, weight: .semibold, design: .rounded))
        case .clear:
            Image(systemName: "xmark").font(.title2.weight(.semibold)).foregroundStyle(.secondary)
        case .delete:
            Image(systemName: "delete.left").font(.title2.weight(.semibold)).foregroundStyle(.secondary)
        }
    }

    private func identifier(for key: Key) -> String {
        switch key {
        case .digit(let digit): "\(digit)"
        case .doubleZero: "00"
        case .clear: "clear"
        case .delete: "delete"
        }
    }

    private func accessibilityLabel(for key: Key) -> String {
        switch key {
        case .digit(let digit): "\(digit)"
        case .doubleZero: "Double zéro"
        case .clear: "Effacer tout"
        case .delete: "Effacer"
        }
    }
}

/// Points de saisie du code PIN.
struct PinDots: View {
    var count: Int
    var length: Int

    var body: some View {
        HStack(spacing: 18) {
            ForEach(0..<max(length, count), id: \.self) { index in
                Circle()
                    .fill(index < count ? Color.accentColor : Color.clear)
                    .overlay(Circle().stroke(Color.accentColor, lineWidth: 2))
                    .frame(width: 18, height: 18)
            }
        }
        .accessibilityElement(children: .ignore)
        .accessibilityIdentifier("pin.dots")
        .accessibilityValue("\(count)")
    }
}

/// Pastille de statut colorée.
struct StatusPill: View {
    var text: String
    var color: Color
    var systemImage: String?

    var body: some View {
        HStack(spacing: 4) {
            if let systemImage {
                Image(systemName: systemImage)
            }
            Text(text)
        }
        .font(.caption.weight(.semibold))
        .padding(.horizontal, 10)
        .padding(.vertical, 4)
        .foregroundStyle(color)
        .background(Capsule().fill(color.opacity(0.15)))
    }
}

/// Indicateur chiffré de l'en-tête (tables libres, couverts…).
struct StatTile: View {
    var title: String
    var value: String
    var systemImage: String
    var tint: Color

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: systemImage)
                .font(.title3)
                .foregroundStyle(tint)
                .frame(width: 40, height: 40)
                .background(Circle().fill(tint.opacity(0.15)))
            VStack(alignment: .leading, spacing: 2) {
                Text(value).font(.title3.weight(.bold)).monospacedDigit()
                Text(title).font(.caption).foregroundStyle(.secondary)
            }
        }
        .padding(12)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(RoundedRectangle(cornerRadius: 16, style: .continuous).fill(Color.posSurface))
        .accessibilityElement(children: .combine)
    }
}

/// Bannière de notification éphémère (succès, erreurs réseau…).
struct NoticeBanner: View {
    @Environment(AppModel.self) private var app

    var body: some View {
        if let notice = app.context.notice {
            HStack(spacing: 10) {
                Image(systemName: notice.style.systemImage)
                    .foregroundStyle(notice.style.color)
                Text(notice.message)
                    .font(.callout.weight(.medium))
                    .multilineTextAlignment(.leading)
            }
            .padding(.horizontal, 18)
            .padding(.vertical, 12)
            .background(.regularMaterial, in: Capsule())
            .overlay(Capsule().stroke(notice.style.color.opacity(0.4), lineWidth: 1))
            .shadow(color: .black.opacity(0.12), radius: 12, y: 4)
            .padding(.top, 12)
            .padding(.horizontal, 24)
            .transition(.move(edge: .top).combined(with: .opacity))
            // Ne bloque jamais les touches de l'écran en dessous (disparaît seule).
            .allowsHitTesting(false)
            .accessibilityElement(children: .combine)
            .accessibilityIdentifier("notice.banner")
            .task(id: notice.id) {
                try? await Task.sleep(for: .seconds(notice.style == .error ? 4 : 2.5))
                if app.context.notice?.id == notice.id {
                    app.context.notice = nil
                }
            }
        }
    }
}

extension View {
    /// Affiche les notifications au-dessus de cette vue. À appliquer à la racine et à chaque
    /// modale : sinon un message (paiement refusé, PIN invalide…) resterait caché derrière.
    func noticeOverlay() -> some View {
        overlay(alignment: .top) {
            NoticeBanner()
        }
    }
}

/// État vide centré.
struct EmptyStateView<Actions: View>: View {
    var title: String
    var message: String
    var systemImage: String
    @ViewBuilder var actions: () -> Actions

    var body: some View {
        VStack(spacing: 16) {
            Image(systemName: systemImage)
                .font(.system(size: 56))
                .foregroundStyle(.tertiary)
            Text(title).font(.title2.weight(.semibold))
            Text(message)
                .font(.body)
                .foregroundStyle(.secondary)
                .multilineTextAlignment(.center)
                .frame(maxWidth: 420)
            actions()
                .frame(maxWidth: 360)
        }
        .padding(40)
        .frame(maxWidth: .infinity, maxHeight: .infinity)
    }
}
