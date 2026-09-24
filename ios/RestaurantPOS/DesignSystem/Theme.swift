import SwiftUI
import PosKit

/// Palette et métriques de l'app (s'adapte automatiquement au mode clair/sombre).
enum Theme {
    static let accent = Color.accentColor
    static let success = Color.green
    static let warning = Color.orange
    static let danger = Color.red
    static let happyHour = Color(red: 0.96, green: 0.62, blue: 0.04)

    static let cornerRadius: CGFloat = 14
    static let smallRadius: CGFloat = 10
    /// Cible tactile minimale (Apple HIG 44 pt ; on vise plus large pour le service).
    static let touchTarget: CGFloat = 54
    static let sidebarWidth: CGFloat = 92
    static let ticketWidth: CGFloat = 400

    static func color(for status: TableStatus) -> Color {
        switch status {
        case .free: .green
        case .occupied: .orange
        case .billRequested: .purple
        case .paid: .blue
        }
    }

    static func color(for status: TicketStatus) -> Color {
        switch status {
        case .pending: .orange
        case .inPreparation: .blue
        case .ready: .green
        case .served: .gray
        }
    }

    static func color(for course: CourseType) -> Color {
        switch course {
        case .direct: .blue
        case .suite: .purple
        case .dessert: .pink
        case .onDemand: .teal
        }
    }

    static func color(for kind: Toast.Kind) -> Color {
        switch kind {
        case .success: .green
        case .info: .blue
        case .warning: .orange
        case .error: .red
        }
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
