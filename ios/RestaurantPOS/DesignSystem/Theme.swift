import SwiftUI
import PosKit

/// Jetons du design system AGY POS, habillage Handi : papier / encre / tomate (clair) et son pendant chaud (sombre).
/// Les noms restent ceux de « AGY POS » (tokens.json) ; les valeurs suivent le web (`styles.css`, `:root`).
enum Theme {
    // MARK: Surfaces

    /// Fond de l'app derrière les panneaux.
    static let canvas = dynamic(dark: 0x161411, light: 0xF1ECE1)
    /// Panneaux et cartes : ticket, table, tuile, feuilles.
    static let surface = dynamic(dark: 0x1F1C18, light: 0xFBF9F6)
    /// Éléments posés sur une surface : touches, champs, bouton neutre.
    static let raised = dynamic(dark: 0x2A2621, light: 0xF3EFE6)
    /// Rail de navigation, barre d'actions du ticket, colonnes KDS.
    static let sunken = dynamic(dark: 0x100E0C, light: 0xE7E1D4)
    /// Filets décoratifs.
    static let line = dynamic(dark: 0x332E28, light: 0xDDD6C8)
    /// Bords de contrôles et cellules vides (≥ 3:1).
    static let lineStrong = dynamic(dark: 0x7D8289, light: 0x80868E)

    // MARK: Texte

    static let ink = dynamic(dark: 0xF1ECE1, light: 0x1E1B17)
    static let inkMuted = dynamic(dark: 0xB7B0A3, light: 0x57534D)
    static let inkSubtle = dynamic(dark: 0x9C968B, light: 0x67635C)

    // MARK: Marque et action

    /// Aplat de l'action principale (l'encre en clair, le papier en sombre) ; texte `onPrimary` dessus.
    static let primary = dynamic(dark: 0xF1ECE1, light: 0x1E1B17)
    static let onPrimary = dynamic(dark: 0x1E1B17, light: 0xF1ECE1)
    /// Accent utilisé comme texte ou icône sur une surface.
    static let primaryInk = dynamic(dark: 0xF1ECE1, light: 0x1E1B17)
    /// Fond du bouton tonal et de l'onglet actif.
    static let primarySoft = dynamic(dark: 0xF1ECE1, light: 0x1E1B17, darkAlpha: 0.14, lightAlpha: 0.08)
    /// Élément sélectionné (chip, bouton actif du bandeau) : encre pleine en clair ; en sombre, crème translucide cerclée
    /// de crème, plus douce que l'aplat crème de `primary`.
    static let selectedFill = dynamic(dark: 0xF1ECE1, light: 0x1E1B17, darkAlpha: 0.20, lightAlpha: 1)
    static let selectedInk = dynamic(dark: 0xF1ECE1, light: 0xF1ECE1)
    static let selectedStroke = dynamic(dark: 0xF1ECE1, light: 0x1E1B17, darkAlpha: 0.55, lightAlpha: 0)
    static let brandCyan = dynamic(dark: 0x7FB3B8, light: 0x2F6F77)
    static let brandViolet = dynamic(dark: 0xB79BD6, light: 0x6B4E8F)
    static let focus = dynamic(dark: 0xF0735A, light: 0xD4462A)

    // MARK: Sémantique

    static let success = dynamic(dark: 0x58C08A, light: 0x2F7D4F)
    static let warning = dynamic(dark: 0xE0A33A, light: 0x9A5B0A)
    static let danger = dynamic(dark: 0xF0735A, light: 0xC03A1F)
    static let dangerSoft = dynamic(dark: 0xF0735A, light: 0xC03A1F, darkAlpha: 0.16, lightAlpha: 0.08)

    static let statusFree = dynamic(dark: 0x58C08A, light: 0x2F7D4F)
    static let statusOccupied = dynamic(dark: 0xF0735A, light: 0xD4462A)
    static let statusBill = dynamic(dark: 0xE0A33A, light: 0xB9770E)
    static let statusPaid = dynamic(dark: 0xA9B0B8, light: 0x5F6670)

    /// Aplat Happy Hour ; texte `onHappy` dessus, jamais du blanc.
    static let happyHour = dynamic(dark: 0xE0A33A, light: 0xE9B04A)
    static let onHappy = dynamic(dark: 0x1C1300, light: 0x1C1300)
    /// Prix Happy Hour en texte sur une surface.
    static let happyInk = dynamic(dark: 0xE0A33A, light: 0x9A5B0A)

    static let courseSuite = dynamic(dark: 0xA78BFA, light: 0x6D28D9)
    static let courseDessert = dynamic(dark: 0xF472B6, light: 0xBE185D)
    static let courseOnDemand = dynamic(dark: 0x2DD4BF, light: 0x0F766E)

    static let scrim = dynamic(dark: 0x0A0908, light: 0x1E1B17, darkAlpha: 0.64, lightAlpha: 0.36)

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

/// Typographie AGY POS : texte en Bricolage Grotesque, montants et chronomètres en DM Mono (polices OFL embarquées,
/// `Resources/Fonts`). Hors des glyphes de ces polices (arabe), le système prend le relais.
extension Font {
    /// Texte courant ; `weight` retombe sur le gras le plus proche embarqué.
    static func pos(_ size: CGFloat, _ weight: Font.Weight = .medium) -> Font {
        let face: String
        switch weight {
        case .heavy, .black: face = "ExtraBold"
        case .bold: face = "Bold"
        case .semibold: face = "SemiBold"
        default: face = "Medium"
        }
        return .custom("BricolageGrotesque-\(face)", size: size)
    }

    /// Montants, chronomètres, numéros : chiffres à chasse fixe.
    static func posMono(_ size: CGFloat, _ weight: Font.Weight = .medium) -> Font {
        .custom(weight == .semibold || weight == .bold || weight == .heavy ? "DMMono-Medium" : "DMMono-Regular", size: size)
    }

    static let posDisplay = Font.pos(40, .heavy)
    static let posTitle = Font.pos(26, .bold)
    static let posHeadline = Font.pos(17, .bold)
    static let posBody = Font.pos(15, .medium)
    static let posLabel = Font.pos(13, .semibold)
    static let posCaption = Font.pos(12, .bold)
    static let posAmountXL = Font.posMono(44, .semibold)
    static let posAmount = Font.posMono(20, .semibold)
    static let posAmountSmall = Font.posMono(15, .medium)
}

/// Main dominante : bandeau de navigation du côté de la main (droitier : à droite) et ticket de l'écran de vente à l'opposé.
enum HandPreference: String, CaseIterable, Identifiable {
    case right, left
    static let storageKey = "hand"
    var id: String { rawValue }
    var label: String {
        switch self {
        case .right: String(localized: "admin.hand_right")
        case .left: String(localized: "admin.hand_left")
        }
    }
}

/// Apparence choisie dans Gestion (clair par défaut, comme le web ; le sombre reste disponible pour la salle).
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
