import SwiftUI
import PosKit

// MARK: - Boutons

/// Bouton d'action principal, grande cible tactile.
struct ActionButton: View {
    let title: String
    var systemImage: String?
    var tint: Color = .accentColor
    var prominent = true
    var isLoading = false
    let action: () -> Void

    var body: some View {
        Button(action: { Haptics.tap(); action() }) {
            HStack(spacing: 8) {
                if isLoading {
                    ProgressView().tint(prominent ? .white : tint)
                } else if let systemImage {
                    Image(systemName: systemImage)
                }
                Text(title).lineLimit(1).minimumScaleFactor(0.7)
            }
            .font(.headline)
            .frame(maxWidth: .infinity, minHeight: Theme.touchTarget)
        }
        .buttonStyle(ActionButtonStyle(tint: tint, prominent: prominent))
        .disabled(isLoading)
    }
}

struct ActionButtonStyle: ButtonStyle {
    let tint: Color
    let prominent: Bool
    @Environment(\.isEnabled) private var isEnabled

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(prominent ? Color.white : tint)
            .background(
                RoundedRectangle(cornerRadius: Theme.smallRadius, style: .continuous)
                    .fill(prominent ? tint : tint.opacity(0.14))
            )
            .opacity(isEnabled ? 1 : 0.45)
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .animation(.snappy(duration: 0.15), value: configuration.isPressed)
    }
}

/// Puce sélectionnable (familles, filtres, pourboires…).
struct ChipButton: View {
    let title: String
    var systemImage: String?
    var isSelected: Bool
    var tint: Color = .accentColor
    let action: () -> Void

    var body: some View {
        Button(action: { Haptics.tap(); action() }) {
            HStack(spacing: 6) {
                if let systemImage { Image(systemName: systemImage) }
                Text(title).lineLimit(1)
            }
            .font(.subheadline.weight(.semibold))
            .padding(.horizontal, 16)
            .frame(minHeight: 44)
            .foregroundStyle(isSelected ? Color.white : Color.primary)
            .background(Capsule().fill(isSelected ? tint : Color(.secondarySystemFill)))
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}

// MARK: - Badges & cartes

struct Badge: View {
    let text: String
    var color: Color = .secondary
    var systemImage: String?

    var body: some View {
        HStack(spacing: 3) {
            if let systemImage { Image(systemName: systemImage) }
            Text(text)
        }
        .font(.caption2.weight(.bold))
        .padding(.horizontal, 7)
        .padding(.vertical, 3)
        .foregroundStyle(color)
        .background(Capsule().fill(color.opacity(0.15)))
    }
}

struct Card<Content: View>: View {
    var padding: CGFloat = 16
    @ViewBuilder let content: Content

    var body: some View {
        content
            .padding(padding)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous).fill(Color(.secondarySystemGroupedBackground)))
    }
}

struct KPIView: View {
    let title: String
    let value: String
    var subtitle: String?
    var systemImage: String
    var tint: Color = .accentColor

    var body: some View {
        Card {
            VStack(alignment: .leading, spacing: 6) {
                Label(title, systemImage: systemImage).font(.subheadline).foregroundStyle(tint)
                Text(value).font(.title.weight(.bold)).monospacedDigit().contentTransition(.numericText())
                if let subtitle { Text(subtitle).font(.caption).foregroundStyle(.secondary) }
            }
        }
        .accessibilityElement(children: .combine)
    }
}

// MARK: - Pavé numérique

/// Pavé numérique tactile (évite l'apparition du clavier système qui masquerait le ticket).
struct NumericKeypad: View {
    var allowsDecimal = false
    var keySize: CGFloat = 72
    var identifierPrefix = "keypad"
    let onKey: (String) -> Void
    let onDelete: () -> Void
    var onClear: (() -> Void)?

    private var rows: [[String]] {
        [["1", "2", "3"], ["4", "5", "6"], ["7", "8", "9"], [allowsDecimal ? "," : "C", "0", "⌫"]]
    }

    var body: some View {
        Grid(horizontalSpacing: 12, verticalSpacing: 12) {
            ForEach(rows, id: \.self) { row in
                GridRow {
                    ForEach(row, id: \.self) { key in
                        Button { press(key) } label: {
                            Group {
                                if key == "⌫" {
                                    Image(systemName: "delete.left")
                                } else {
                                    Text(key)
                                }
                            }
                            .font(.title.weight(.medium))
                            .frame(width: keySize, height: keySize)
                            .background(Circle().fill(Color(.tertiarySystemFill)))
                            .foregroundStyle(.primary)
                        }
                        .buttonStyle(KeyPressStyle())
                        .accessibilityIdentifier("\(identifierPrefix).\(accessibilityKey(key))")
                        .accessibilityLabel(accessibilityLabel(key))
                    }
                }
            }
        }
    }

    private func press(_ key: String) {
        Haptics.tap()
        switch key {
        case "⌫": onDelete()
        case "C": onClear?()
        default: onKey(key)
        }
    }

    private func accessibilityKey(_ key: String) -> String {
        switch key {
        case "⌫": "delete"
        case "C": "clear"
        case ",": "decimal"
        default: key
        }
    }

    private func accessibilityLabel(_ key: String) -> String {
        switch key {
        case "⌫": "Effacer"
        case "C": "Tout effacer"
        case ",": "Virgule"
        default: key
        }
    }
}

struct KeyPressStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.9 : 1)
            .opacity(configuration.isPressed ? 0.7 : 1)
            .animation(.snappy(duration: 0.1), value: configuration.isPressed)
    }
}

/// Points de saisie du PIN.
struct PinDots: View {
    let count: Int
    let filled: Int
    var isError = false

    var body: some View {
        HStack(spacing: 18) {
            ForEach(0..<count, id: \.self) { index in
                Circle()
                    .fill(index < filled ? (isError ? Color.red : Color.primary) : Color.clear)
                    .overlay(Circle().stroke(isError ? Color.red : Color.secondary, lineWidth: 2))
                    .frame(width: 18, height: 18)
            }
        }
        .accessibilityElement()
        .accessibilityIdentifier("pin.dots")
        .accessibilityValue("\(filled) sur \(count)")
    }
}

/// Saisie d'un code PIN superviseur via pavé numérique.
struct SupervisorPinPad: View {
    @Binding var pin: String
    var identifierPrefix = "supervisor"

    var body: some View {
        VStack(spacing: 16) {
            PinDots(count: SessionStore.pinLength, filled: pin.count)
            NumericKeypad(keySize: 60, identifierPrefix: identifierPrefix) { digit in
                if pin.count < SessionStore.pinLength { pin += digit }
            } onDelete: {
                if !pin.isEmpty { pin.removeLast() }
            } onClear: {
                pin = ""
            }
        }
    }
}

// MARK: - États vides & toasts

struct EmptyStateView: View {
    let title: String
    let systemImage: String
    var message: String?

    var body: some View {
        ContentUnavailableView {
            Label(title, systemImage: systemImage)
        } description: {
            if let message { Text(message) }
        }
    }
}

struct ToastOverlay: View {
    let notifier: Notifier

    var body: some View {
        VStack(spacing: 8) {
            ForEach(notifier.toasts) { toast in
                HStack(spacing: 10) {
                    Image(systemName: Theme.icon(for: toast.kind)).foregroundStyle(Theme.color(for: toast.kind))
                    Text(toast.message).font(.subheadline.weight(.medium)).multilineTextAlignment(.leading)
                }
                .padding(.horizontal, 16)
                .padding(.vertical, 12)
                .background(.regularMaterial, in: Capsule())
                .shadow(color: .black.opacity(0.12), radius: 10, y: 4)
                .onTapGesture { notifier.dismiss(toast.id) }
                .transition(.move(edge: .top).combined(with: .opacity))
                .accessibilityIdentifier("toast")
                .accessibilityLabel(toast.message)
            }
        }
        .padding(.top, 8)
        .animation(.spring(duration: 0.3), value: notifier.toasts)
        .frame(maxWidth: 560)
    }
}

// MARK: - En-têtes de section

struct SheetHeader: View {
    let title: String
    var subtitle: String?
    var onClose: () -> Void

    var body: some View {
        HStack(alignment: .firstTextBaseline) {
            VStack(alignment: .leading, spacing: 2) {
                Text(title).font(.title2.weight(.bold))
                if let subtitle { Text(subtitle).font(.subheadline).foregroundStyle(.secondary) }
            }
            Spacer()
            Button(action: onClose) {
                Image(systemName: "xmark.circle.fill").font(.title).symbolRenderingMode(.hierarchical).foregroundStyle(.secondary)
            }
            .accessibilityIdentifier("sheet.close")
            .accessibilityLabel("Fermer")
        }
    }
}
