import SwiftUI
import PosKit

/// Jetons du design system AGY POS (thème « Service » sombre / « Jour » clair).
/// Référence : design system « AGY POS » (tokens.json) — garder les noms alignés.
enum Theme {
    // MARK: Surfaces

    /// Fond de l'app derrière les panneaux.
    static let canvas = dynamic(dark: 0x0B1120, light: 0xF3F5F9)
    /// Panneaux et cartes : ticket, table, tuile, feuilles.
    static let surface = dynamic(dark: 0x111A2E, light: 0xFFFFFF)
    /// Éléments posés sur une surface : touches, champs, bouton neutre.
    static let raised = dynamic(dark: 0x1A2540, light: 0xEEF2F7)
    /// Rail de navigation, barre d'actions du ticket, colonnes KDS.
    static let sunken = dynamic(dark: 0x070B16, light: 0xE6EBF2)
    /// Filets décoratifs.
    static let line = dynamic(dark: 0x1F2B44, light: 0xE2E8F0)
    /// Bords de contrôles et cellules vides (≥ 3:1).
    static let lineStrong = dynamic(dark: 0x5B6B86, light: 0x8391A7)

    // MARK: Texte

    static let ink = dynamic(dark: 0xF1F5F9, light: 0x0F172A)
    static let inkMuted = dynamic(dark: 0x9AA8BD, light: 0x475569)
    static let inkSubtle = dynamic(dark: 0x8190A8, light: 0x5B6B82)

    // MARK: Marque et action

    /// Aplat de l'action principale ; texte `onPrimary` dessus.
    static let primary = dynamic(dark: 0x2563EB, light: 0x2563EB)
    static let onPrimary = Color.white
    /// Bleu utilisé comme texte ou icône sur une surface.
    static let primaryInk = dynamic(dark: 0x7CB0FF, light: 0x1D4ED8)
    /// Fond du bouton tonal et de l'onglet actif.
    static let primarySoft = dynamic(dark: 0x3B82F6, light: 0x2563EB, darkAlpha: 0.18, lightAlpha: 0.10)
    static let brandCyan = dynamic(dark: 0x22D3EE, light: 0x0891B2)
    static let brandViolet = dynamic(dark: 0x8B5CF6, light: 0x7C3AED)
    static let focus = brandCyan

    // MARK: Sémantique

    static let success = dynamic(dark: 0x34D399, light: 0x047857)
    static let warning = dynamic(dark: 0xFBBF24, light: 0xA14A08)
    static let danger = dynamic(dark: 0xF87171, light: 0xC81E1E)
    static let dangerSoft = dynamic(dark: 0xF87171, light: 0xC81E1E, darkAlpha: 0.16, lightAlpha: 0.08)

    static let statusFree = dynamic(dark: 0x10B981, light: 0x059669)
    static let statusOccupied = dynamic(dark: 0xF97316, light: 0xEA580C)
    static let statusBill = dynamic(dark: 0xA78BFA, light: 0x7C3AED)
    static let statusPaid = dynamic(dark: 0x38BDF8, light: 0x0284C7)

    /// Aplat Happy Hour ; texte `onHappy` dessus, jamais du blanc.
    static let happyHour = dynamic(dark: 0xFBBF24, light: 0xFBBF24)
    static let onHappy = dynamic(dark: 0x1C1300, light: 0x1C1300)
    /// Prix Happy Hour en texte sur une surface.
    static let happyInk = dynamic(dark: 0xFBBF24, light: 0xA14A08)

    static let courseSuite = dynamic(dark: 0xA78BFA, light: 0x6D28D9)
    static let courseDessert = dynamic(dark: 0xF472B6, light: 0xBE185D)
    static let courseOnDemand = dynamic(dark: 0x2DD4BF, light: 0x0F766E)

    static let scrim = dynamic(dark: 0x020610, light: 0x0F172A, darkAlpha: 0.64, lightAlpha: 0.32)

    // MARK: Métriques

    enum Space {
        static let xs: CGFloat = 4
        static let s: CGFloat = 8
        static let m: CGFloat = 12
        static let l: CGFloat = 16
        static let xl: CGFloat = 20
        static let xxl: CGFloat = 24
        static let xxxl: CGFloat = 32
    }

    enum Radius {
        static let sm: CGFloat = 10
        static let md: CGFloat = 14
        static let lg: CGFloat = 20
        static let xl: CGFloat = 28
    }

    /// Rayon des conteneurs (cartes, ticket, colonnes).
    static let cornerRadius: CGFloat = Radius.lg
    /// Rayon de ce qu'on touche (boutons, tuiles, touches).
    static let smallRadius: CGFloat = Radius.md
    /// Cible tactile minimale absolue (Apple HIG).
    static let touchMin: CGFloat = 44
    /// Hauteur des boutons d'action et des lignes.
    static let touchTarget: CGFloat = 56
    /// Bouton Encaisser, touches du pavé.
    static let touchLarge: CGFloat = 72
    static let sidebarWidth: CGFloat = 88
    static let headerHeight: CGFloat = 64
    static let ticketWidth: CGFloat = 400

    // MARK: Correspondances métier

    static func color(for status: TableStatus) -> Color {
        switch status {
        case .free: statusFree
        case .occupied: statusOccupied
        case .billRequested: statusBill
        case .paid: statusPaid
        }
    }

    static func color(for status: TicketStatus) -> Color {
        switch status {
        case .pending: warning
        case .inPreparation: primaryInk
        case .ready: success
        case .served: inkSubtle
        }
    }

    static func color(for course: CourseType) -> Color {
        switch course {
        case .direct: primaryInk
        case .suite: courseSuite
        case .dessert: courseDessert
        case .onDemand: courseOnDemand
        }
    }

    static func color(for kind: Toast.Kind) -> Color {
        switch kind {
        case .success: success
        case .info: primaryInk
        case .warning: warning
        case .error: danger
        }
    }

    /// Couleur qui suit le thème clair / sombre de l'iPad.
    private static func dynamic(dark: UInt32, light: UInt32, darkAlpha: CGFloat = 1, lightAlpha: CGFloat = 1) -> Color {
        Color(uiColor: UIColor { traits in
            traits.userInterfaceStyle == .dark
                ? UIColor(rgb: dark, alpha: darkAlpha)
                : UIColor(rgb: light, alpha: lightAlpha)
        })
    }

    static func icon(for kind: Toast.Kind) -> String {
        switch kind {
        case .success: "checkmark.circle.fill"
        case .info: "info.circle.fill"
        case .warning: "exclamationmark.triangle.fill"
        case .error: "xmark.octagon.fill"
        }
    }

    static func icon(for method: PaymentMethod) -> String {
        switch method {
        case .cash: "banknote"
        case .creditCard: "creditcard"
        case .mealVoucher: "ticket"
        case .giftCard: "giftcard"
        case .roomCharge: "bed.double"
        }
    }
}

extension UIColor {
    convenience init(rgb: UInt32, alpha: CGFloat = 1) {
        self.init(
            red: CGFloat((rgb >> 16) & 0xFF) / 255,
            green: CGFloat((rgb >> 8) & 0xFF) / 255,
            blue: CGFloat(rgb & 0xFF) / 255,
            alpha: alpha
        )
    }
}

/// Typographie AGY POS : texte en SF Pro, montants et chronomètres en chiffres à chasse fixe.
extension Font {
    static let posDisplay = Font.system(size: 40, weight: .heavy)
    static let posTitle = Font.system(size: 26, weight: .bold)
    static let posHeadline = Font.system(size: 17, weight: .bold)
    static let posBody = Font.system(size: 15, weight: .medium)
    static let posLabel = Font.system(size: 13, weight: .semibold)
    static let posCaption = Font.system(size: 12, weight: .bold)
    static let posAmountXL = Font.system(size: 44, weight: .semibold, design: .monospaced)
    static let posAmount = Font.system(size: 20, weight: .semibold, design: .monospaced)
    static let posAmountSmall = Font.system(size: 15, weight: .medium, design: .monospaced)
}

/// Apparence choisie dans Gestion (sombre par défaut, pour le service en salle).
enum AppearancePreference: String, CaseIterable, Identifiable {
    case dark, light, system
    static let storageKey = "appearance"
    var id: String { rawValue }
    var label: String {
        switch self {
        case .dark: String(localized: "admin.appearance_dark")
        case .light: String(localized: "admin.appearance_light")
        case .system: String(localized: "admin.appearance_system")
        }
    }
    var colorScheme: ColorScheme? {
        switch self {
        case .dark: .dark
        case .light: .light
        case .system: nil
        }
    }
}

extension Color {
    /// `#RRGGBB` ou `RRGGBB` ; renvoie `nil` si le format est invalide.
    init?(hex: String?) {
        guard var value = hex?.trimmingCharacters(in: .whitespacesAndNewlines), !value.isEmpty else { return nil }
        if value.hasPrefix("#") { value.removeFirst() }
        guard value.count == 6, let rgb = UInt32(value, radix: 16) else { return nil }
        self.init(
            red: Double((rgb >> 16) & 0xFF) / 255,
            green: Double((rgb >> 8) & 0xFF) / 255,
            blue: Double(rgb & 0xFF) / 255
        )
    }

    /// Représentation `#RRGGBB` (pour l'API).
    var hexString: String {
        let resolved = UIColor(self).resolvedColor(with: UITraitCollection(userInterfaceStyle: .light))
        var r: CGFloat = 0, g: CGFloat = 0, b: CGFloat = 0, a: CGFloat = 0
        resolved.getRed(&r, green: &g, blue: &b, alpha: &a)
        return String(format: "#%02X%02X%02X", Int(round(r * 255)), Int(round(g * 255)), Int(round(b * 255)))
    }
}

enum Haptics {
    @MainActor static func tap() { UIImpactFeedbackGenerator(style: .light).impactOccurred() }
    @MainActor static func success() { UINotificationFeedbackGenerator().notificationOccurred(.success) }
    @MainActor static func error() { UINotificationFeedbackGenerator().notificationOccurred(.error) }
}

extension Date {
    /// « 12 min » depuis une date passée.
    func elapsedDescription(now: Date = Date()) -> String {
        let minutes = max(0, Int(now.timeIntervalSince(self) / 60))
        if minutes < 60 { return "\(minutes) min" }
        return "\(minutes / 60) h \(String(format: "%02d", minutes % 60))"
    }
}
