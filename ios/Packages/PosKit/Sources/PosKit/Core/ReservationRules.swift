import Foundation

/// Refus métier des réservations (miroir de `ReservationError` du .NET). Les messages sont ceux du .NET en français.
public enum ReservationError: Error, Equatable, Sendable {
    case notFound, invalidName, invalidCovers, invalidDuration, invalidPhone
    case unknownTable, tableRequired, tableCapacity, tableConflict, tableOccupied, invalidState

    public var apiError: APIError {
        switch self {
        case .notFound: .notFound("Réservation introuvable.")
        case .invalidName: .server(status: 400, message: "Le nom du client est obligatoire (100 caractères au plus).")
        case .invalidCovers: .server(status: 400, message: "Le nombre de couverts doit être compris entre 1 et 30.")
        case .invalidDuration: .server(status: 400, message: "La durée doit être comprise entre 30 et 240 minutes.")
        case .invalidPhone: .server(status: 400, message: "Numéro de téléphone invalide.")
        case .unknownTable: .server(status: 400, message: "Table inconnue.")
        case .tableRequired: .server(status: 400, message: "Choisissez une table pour installer ce client.")
        case .tableCapacity: .server(status: 400, message: "Cette table n'a pas assez de places pour ce nombre de couverts.")
        case .tableConflict: .server(status: 409, message: "Cette table est déjà réservée sur ce créneau.")
        case .tableOccupied: .server(status: 409, message: "Cette table est occupée.")
        case .invalidState: .server(status: 409, message: "Cette réservation ne peut plus être modifiée de cette façon.")
        }
    }
}

/// Règles des réservations partagées par `InMemoryPosAPI` et `LocalPosAPI` ; miroir de `ReservationService` (.NET).
public enum ReservationRules {
    /// Une réservation attendue est « en retard » 15 minutes après l'heure prévue.
    public static let lateGrace: TimeInterval = 15 * 60
    /// Le téléphone est effacé 2 jours après l'heure de la réservation.
    public static let phoneRetention: TimeInterval = 2 * 86_400
    /// La ligne entière est supprimée après 90 jours.
    public static let rowRetention: TimeInterval = 90 * 86_400

    /// Nettoie la saisie (espaces, table ignorée en liste d'attente) ou lève le refus correspondant.
    public static func validate(_ draft: ReservationDraft) throws -> ReservationDraft {
        var d = draft
        d.guestName = draft.guestName.trimmingCharacters(in: .whitespacesAndNewlines)
        guard (1...100).contains(d.guestName.count) else { throw ReservationError.invalidName }
        guard (1...30).contains(d.covers) else { throw ReservationError.invalidCovers }
        guard (30...240).contains(d.durationMinutes) else { throw ReservationError.invalidDuration }
        let phone = draft.phone?.trimmingCharacters(in: .whitespacesAndNewlines)
        d.phone = phone?.isEmpty == false ? phone : nil
        if let p = d.phone, !isValidPhone(p) { throw ReservationError.invalidPhone }
        let note = draft.note?.trimmingCharacters(in: .whitespacesAndNewlines)
        d.note = note?.isEmpty == false ? note : nil
        let table = draft.tableNumber?.trimmingCharacters(in: .whitespacesAndNewlines)
        d.tableNumber = draft.isWaitlist || table?.isEmpty != false ? nil : table
        return d
    }

    /// `^[+0-9 ().-]{6,20}$`
    public static func isValidPhone(_ value: String) -> Bool {
        (6...20).contains(value.count) && value.allSatisfy { "+0123456789 ().-".contains($0) }
    }

    /// Deux créneaux se recoupent si chacun commence avant la fin de l'autre (bout à bout = pas de conflit).
    public static func overlaps(start: Date, minutes: Int, otherStart: Date, otherMinutes: Int) -> Bool {
        otherStart < start.addingTimeInterval(TimeInterval(minutes * 60)) && start < otherStart.addingTimeInterval(TimeInterval(otherMinutes * 60))
    }

    public static func isLate(status: ReservationStatus, isWaitlist: Bool, startsAt: Date, now: Date) -> Bool {
        status == .booked && !isWaitlist && now > startsAt.addingTimeInterval(lateGrace)
    }

    /// `Booked` → `Cancelled` / `NoShow` ; `Seated` → `Done` (l'installation passe par `seat`).
    public static func transitionAllowed(from: ReservationStatus, to: ReservationStatus) -> Bool {
        switch (from, to) {
        case (.booked, .cancelled), (.booked, .noShow), (.seated, .done): true
        default: false
        }
    }
}
