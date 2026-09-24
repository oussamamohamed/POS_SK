import POSKit
import SwiftUI
import UIKit

extension Color {
    /// Couleur à partir d'un code `#RRGGBB` (catégories et articles configurés en back-office).
    init?(hex: String?) {
        guard let hex else { return nil }
        let cleaned = hex.trimmingCharacters(in: .whitespaces).replacingOccurrences(of: "#", with: "")
        guard cleaned.count == 6, let value = UInt64(cleaned, radix: 16) else { return nil }
        self.init(
            red: Double((value >> 16) & 0xFF) / 255,
            green: Double((value >> 8) & 0xFF) / 255,
            blue: Double(value & 0xFF) / 255
        )
    }

    static let posBackground = Color(uiColor: .systemGroupedBackground)
    static let posSurface = Color(uiColor: .secondarySystemGroupedBackground)
    static let posElevated = Color(uiColor: .tertiarySystemGroupedBackground)
    static let posSeparator = Color(uiColor: .separator)
}

extension TableStatus {
    var color: Color {
        switch self {
        case .free: .green
        case .occupied: .orange
        case .billRequested: .purple
        case .paid: .blue
        }
    }
}

extension TicketStatus {
    var color: Color {
        switch self {
        case .pending: .orange
        case .inPreparation: .blue
        case .ready: .green
        case .served: .gray
        }
    }
}

extension Notice.Style {
    var color: Color {
        switch self {
        case .success: .green
        case .info: .blue
        case .warning: .orange
        case .error: .red
        }
    }

    var systemImage: String {
        switch self {
        case .success: "checkmark.circle.fill"
        case .info: "info.circle.fill"
        case .warning: "exclamationmark.triangle.fill"
        case .error: "xmark.octagon.fill"
        }
    }
}

/// Style des grands boutons d'action (≥ 56 pt de haut, cible tactile confortable en service).
struct ActionButtonStyle: ButtonStyle {
    var tint: Color = .accentColor
    var prominent = true

    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .font(.headline)
            .frame(maxWidth: .infinity, minHeight: 56)
            .padding(.horizontal, 12)
            .foregroundStyle(prominent ? Color.white : tint)
            .background(
                RoundedRectangle(cornerRadius: 14, style: .continuous)
                    .fill(prominent ? tint : tint.opacity(0.14))
            )
            .opacity(configuration.isPressed ? 0.75 : 1)
            .scaleEffect(configuration.isPressed ? 0.98 : 1)
    }
}

/// Effet de secousse (PIN refusé).
struct ShakeEffect: GeometryEffect {
    var animatableData: CGFloat

    func effectValue(size: CGSize) -> ProjectionTransform {
        ProjectionTransform(CGAffineTransform(translationX: 10 * sin(animatableData * .pi * 4), y: 0))
    }
}

extension Date {
    /// « il y a 12 min » / « 1 h 05 ».
    func elapsedLabel(now: Date = Date()) -> String {
        let minutes = max(0, Int(now.timeIntervalSince(self) / 60))
        if minutes < 60 { return "\(minutes) min" }
        return String(format: "%d h %02d", minutes / 60, minutes % 60)
    }

    var shortTime: String {
        formatted(date: .omitted, time: .shortened)
    }
}
