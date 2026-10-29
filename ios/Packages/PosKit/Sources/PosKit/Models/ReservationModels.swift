import Foundation

/// Miroir de `RestaurantPos.Domain.Entities.ReservationStatus` (sérialisé en nombre).
public enum ReservationStatus: Int, Codable, Hashable, Sendable, CaseIterable {
    case booked = 0, seated, done, noShow, cancelled
}

/// Miroir de `ReservationDto` : le téléphone n'est renseigné que pour un responsable (les autres voient `hasPhone`).
public struct Reservation: Codable, Identifiable, Hashable, Sendable {
    public var id: UUID
    public var guestName: String
    public var covers: Int
    public var startsAtUtc: Date
    public var durationMinutes: Int
    public var tableNumber: String?
    public var note: String?
    public var phone: String?
    public var hasPhone: Bool
    public var status: ReservationStatus
    public var isWaitlist: Bool
    public var isLate: Bool
    public var createdAtUtc: Date

    public init(id: UUID = UUID(), guestName: String, covers: Int, startsAtUtc: Date, durationMinutes: Int = 90, tableNumber: String? = nil, note: String? = nil, phone: String? = nil, hasPhone: Bool? = nil, status: ReservationStatus = .booked, isWaitlist: Bool = false, isLate: Bool = false, createdAtUtc: Date = Date()) {
        self.id = id
        self.guestName = guestName
        self.covers = covers
        self.startsAtUtc = startsAtUtc
        self.durationMinutes = durationMinutes
        self.tableNumber = tableNumber
        self.note = note
        self.phone = phone
        self.hasPhone = hasPhone ?? (phone?.isEmpty == false)
        self.status = status
        self.isWaitlist = isWaitlist
        self.isLate = isLate
        self.createdAtUtc = createdAtUtc
    }

    public var endsAtUtc: Date { startsAtUtc.addingTimeInterval(TimeInterval(durationMinutes * 60)) }
}

/// Saisie d'une réservation ou d'une entrée de la liste d'attente (`ReservationRequest` côté API).
public struct ReservationDraft: Hashable, Sendable {
    public var guestName: String
    public var covers: Int
    public var startsAt: Date
    public var durationMinutes: Int
    public var tableNumber: String?
    public var note: String?
    public var phone: String?
    public var isWaitlist: Bool

    public init(guestName: String = "", covers: Int = 2, startsAt: Date = Date(), durationMinutes: Int = 90, tableNumber: String? = nil, note: String? = nil, phone: String? = nil, isWaitlist: Bool = false) {
        self.guestName = guestName
        self.covers = covers
        self.startsAt = startsAt
        self.durationMinutes = durationMinutes
        self.tableNumber = tableNumber
        self.note = note
        self.phone = phone
        self.isWaitlist = isWaitlist
    }

    public init(_ r: Reservation) {
        self.init(guestName: r.guestName, covers: r.covers, startsAt: r.startsAtUtc, durationMinutes: r.durationMinutes, tableNumber: r.tableNumber, note: r.note, phone: r.phone, isWaitlist: r.isWaitlist)
    }
}
