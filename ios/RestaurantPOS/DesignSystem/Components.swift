import SwiftUI
import PosKit

// MARK: - Boutons

/// Bouton d'action principal, grande cible tactile. Une seule variante `.primary` par zone d'écran.
struct ActionButton: View {
    enum Kind {
        /// Aplat bleu : Encaisser, Ouvrir, Valider.
        case primary
        /// Fond bleu doux : action secondaire importante (Cuisine).
        case tonal
        /// Fond neutre : navigation et actions courantes.
        case neutral
        /// Fond rouge doux : Vider, Annuler, Arrêter.
        case danger
    }

    let title: String
    var systemImage: String?
    var tint: Color = Theme.primary
    var prominent = true
    var isLoading = false
    /// Variante du design system ; à défaut, `tint`/`prominent` (compatibilité).
    var kind: Kind?
    var height: CGFloat = Theme.touchTarget
    let action: () -> Void

    var body: some View {
        Button(action: { Haptics.tap(); action() }) {
            HStack(spacing: Theme.Space.s) {
                if isLoading {
                    ProgressView().tint(style.foreground)
                } else if let systemImage {
                    Image(systemName: systemImage)
                }
                Text(title).lineLimit(1).minimumScaleFactor(0.7)
            }
            .font(height >= Theme.touchLarge ? Font.system(size: 19, weight: .bold) : Font.posHeadline)
            .padding(.horizontal, Theme.Space.l)
            .frame(maxWidth: .infinity, minHeight: height)
        }
        .buttonStyle(style)
        .disabled(isLoading)
    }

    private var style: ActionButtonStyle {
        if let kind { ActionButtonStyle(kind: kind) } else { ActionButtonStyle(tint: tint, prominent: prominent) }
    }
}

struct ActionButtonStyle: ButtonStyle {
    let background: Color
    let foreground: Color
    @Environment(\.isEnabled) private var isEnabled

    init(tint: Color, prominent: Bool) {
        background = prominent ? tint : tint.opacity(0.14)
        foreground = prominent ? Theme.onPrimary : tint
    }

    init(kind: ActionButton.Kind) {
        switch kind {
        case .primary: background = Theme.primary; foreground = Theme.onPrimary
        case .tonal: background = Theme.primarySoft; foreground = Theme.primaryInk
        case .neutral: background = Theme.raised; foreground = Theme.ink
        case .danger: background = Theme.dangerSoft; foreground = Theme.danger
        }
    }

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .foregroundStyle(foreground)
            .background(RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous).fill(background))
            .contentShape(RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous))
            .opacity(isEnabled ? 1 : 0.45)
            .scaleEffect(configuration.isPressed ? 0.97 : 1)
            .animation(.snappy(duration: 0.15), value: configuration.isPressed)
    }
}

/// Puce sélectionnable (familles, filtres, pourboires…). La couleur de catégorie reste dans la pastille.
struct ChipButton: View {
    let title: String
    var systemImage: String?
    var isSelected: Bool
    /// Couleur de catégorie, affichée en pastille.
    var tint: Color?
    var count: Int?
    let action: () -> Void

    var body: some View {
        Button(action: { Haptics.tap(); action() }) {
            HStack(spacing: Theme.Space.s) {
                if let tint {
                    Circle().fill(tint).frame(width: 10, height: 10)
                        .overlay(Circle().stroke(isSelected ? Theme.onPrimary : .clear, lineWidth: 2))
                }
                if let systemImage { Image(systemName: systemImage) }
                Text(title).lineLimit(1)
                if let count {
                    Text("\(count)").font(.system(size: 13, weight: .semibold, design: .monospaced))
                        .foregroundStyle(isSelected ? Theme.onPrimary : Theme.inkMuted)
                }
            }
            .font(.posLabel)
            .padding(.horizontal, Theme.Space.l)
            .frame(minHeight: Theme.touchMin)
            .foregroundStyle(isSelected ? Theme.onPrimary : Theme.ink)
            .background(Capsule().fill(isSelected ? Theme.primary : Theme.raised))
        }
        .buttonStyle(.plain)
        .accessibilityAddTraits(isSelected ? .isSelected : [])
    }
}

// MARK: - Badges & cartes

/// Étiquette compacte : une pastille (ou icône) de couleur et un mot, jamais la couleur seule.
struct Badge: View {
    let text: String
    var color: Color = Theme.inkMuted
    var systemImage: String?
    /// `false` : pas de pastille (poste de préparation, méta-information).
    var marker = true

    var body: some View {
        HStack(spacing: 5) {
            if let systemImage {
                Image(systemName: systemImage).foregroundStyle(color)
            } else if marker {
                Circle().fill(color).frame(width: 7, height: 7)
            }
            Text(text).foregroundStyle(marker || systemImage != nil ? Theme.ink : Theme.inkMuted)
        }
        .font(.posCaption)
        .lineLimit(1)
        .padding(.horizontal, 9)
        .frame(minHeight: 24)
        .background(Capsule().fill(Theme.raised))
    }
}

/// Étiquette de suite (Direct, Suite, Dessert, À la demande), au contour.
struct CourseTag: View {
    let course: CourseType

    var body: some View {
        let color = Theme.color(for: course)
        Text(course.label.uppercased())
            .font(.system(size: 11, weight: .bold))
            .tracking(0.4)
            .foregroundStyle(color)
            .padding(.horizontal, 8)
            .frame(minHeight: 20)
            .overlay(Capsule().strokeBorder(color, lineWidth: 1.5))
    }
}

struct Card<Content: View>: View {
    var padding: CGFloat = Theme.Space.l
    @ViewBuilder let content: Content

    var body: some View {
        content
            .padding(padding)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(RoundedRectangle(cornerRadius: Theme.cornerRadius, style: .continuous).fill(Theme.surface))
            .cardShadow()
    }
}

extension View {
    /// Profondeur des cartes : ombre douce en clair, filet d'1 px en sombre.
    func cardShadow(radius: CGFloat = Theme.cornerRadius) -> some View {
        modifier(CardShadow(radius: radius))
    }
}

private struct CardShadow: ViewModifier {
    let radius: CGFloat
    @Environment(\.colorScheme) private var colorScheme

    func body(content: Content) -> some View {
        if colorScheme == .dark {
            content.overlay(RoundedRectangle(cornerRadius: radius, style: .continuous).strokeBorder(Color.white.opacity(0.05), lineWidth: 1))
        } else {
            content.shadow(color: Color(red: 15 / 255, green: 23 / 255, blue: 42 / 255).opacity(0.07), radius: 6, y: 3)
        }
    }
}

struct KPIView: View {
    let title: String
    let value: String
    var subtitle: String?
    var systemImage: String
    var tint: Color = Theme.inkMuted

    var body: some View {
        Card {
            VStack(alignment: .leading, spacing: Theme.Space.xs) {
                Label(title, systemImage: systemImage).font(.posLabel).foregroundStyle(Theme.inkMuted)
                Text(value)
                    .font(.system(size: 28, weight: .semibold, design: .monospaced))
                    .foregroundStyle(Theme.ink)
                    .lineLimit(1).minimumScaleFactor(0.6)
                    .contentTransition(.numericText())
                    .environment(\.layoutDirection, .leftToRight)
                if let subtitle { Text(subtitle).font(.posCaption).foregroundStyle(Theme.inkSubtle) }
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
        Grid(horizontalSpacing: Theme.Space.m, verticalSpacing: Theme.Space.m) {
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
                            .font(isFunctionKey(key) ? .posHeadline : .system(size: keySize * 0.38, weight: .semibold, design: .monospaced))
                            .frame(width: keySize, height: keySize)
                            .background(
                                RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous)
                                    .fill(isFunctionKey(key) ? Color.clear : Theme.raised)
                            )
                            .foregroundStyle(isFunctionKey(key) ? Theme.inkMuted : Theme.ink)
                            .contentShape(RoundedRectangle(cornerRadius: Theme.Radius.md, style: .continuous))
                        }
                        .buttonStyle(KeyPressStyle())
                        .accessibilityIdentifier("\(identifierPrefix).\(accessibilityKey(key))")
                        .accessibilityLabel(accessibilityLabel(key))
                    }
                }
            }
        }
        // Pavé numérique : toujours de gauche à droite, même en arabe (montants, PIN).
        .environment(\.layoutDirection, .leftToRight)
    }

    private func isFunctionKey(_ key: String) -> Bool { key == "C" || key == "⌫" }

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
        case "⌫": String(localized: "common.keypad_delete")
        case "C": String(localized: "common.keypad_clear")
        case ",": String(localized: "common.keypad_decimal")
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
                    .fill(index < filled ? (isError ? Theme.danger : Theme.ink) : Color.clear)
                    .overlay(Circle().strokeBorder(isError ? Theme.danger : Theme.inkMuted, lineWidth: 2))
                    .frame(width: 18, height: 18)
            }
        }
        .accessibilityElement()
        .accessibilityIdentifier("pin.dots")
        .accessibilityValue("common.pin_dots_value \(filled) \(count)")
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
                    Text(toast.message).font(.posBody.weight(.semibold)).foregroundStyle(Theme.ink).multilineTextAlignment(.leading)
                }
                .padding(.leading, Theme.Space.l)
                .padding(.trailing, Theme.Space.xl)
                .padding(.vertical, Theme.Space.m)
                .background(Capsule().fill(Theme.surface))
                .overlay(Capsule().strokeBorder(Theme.line, lineWidth: 1))
                .shadow(color: .black.opacity(0.25), radius: 20, y: 8)
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
                Text(title).font(.posTitle).foregroundStyle(Theme.ink)
                if let subtitle { Text(subtitle).font(.posBody).foregroundStyle(Theme.inkMuted) }
            }
            Spacer()
            Button(action: onClose) {
                Image(systemName: "xmark")
                    .font(.system(size: 16, weight: .bold))
                    .foregroundStyle(Theme.inkMuted)
                    .frame(width: Theme.touchMin, height: Theme.touchMin)
                    .background(Circle().fill(Theme.raised))
            }
            .buttonStyle(.plain)
            .accessibilityIdentifier("sheet.close")
            .accessibilityLabel("common.close_label")
        }
    }
}
