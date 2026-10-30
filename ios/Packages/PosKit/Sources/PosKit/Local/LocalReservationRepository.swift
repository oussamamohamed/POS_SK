import Foundation

struct LocalReservationRepository {
    let db: SQLiteDatabase

    func all() throws -> [Reservation] {
        try db.query("SELECT * FROM Reservations ORDER BY StartsAtUtc, CreatedAtUtc, rowid").map(Self.decode)
    }

    func reservation(id: UUID) throws -> Reservation? {
        try db.query("SELECT * FROM Reservations WHERE Id = ?", [.uuid(id)]).first.map(Self.decode)
    }

    func insert(_ r: Reservation, now: Date) throws {
        try db.run(
            """
            INSERT INTO Reservations (Id, GuestName, Covers, StartsAtUtc, DurationMinutes, TableNumber, Note, Phone, Status, IsWaitlist, CreatedAtUtc, UpdatedAtUtc, QuoteMinutes)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(r.id), .text(r.guestName), .integer(r.covers), .date(r.startsAtUtc), .integer(r.durationMinutes), .string(r.tableNumber), .string(r.note),
             .string(r.phone), .integer(r.status.rawValue), .bool(r.isWaitlist), .date(now), .date(now), .integer(r.quoteMinutes)]
        )
    }

    /// `keepPhone` : un non-responsable ne voit pas le téléphone, sa modification ne doit pas l'effacer.
    func update(id: UUID, _ d: ReservationDraft, keepPhone: Bool, now: Date) throws {
        try db.run(
            """
            UPDATE Reservations SET GuestName = ?1, Covers = ?2, StartsAtUtc = ?3, DurationMinutes = ?4, TableNumber = ?5, Note = ?6,
                Phone = CASE WHEN ?7 THEN Phone ELSE ?8 END, IsWaitlist = ?9, UpdatedAtUtc = ?10, QuoteMinutes = ?12
            WHERE Id = ?11
            """,
            [.text(d.guestName), .integer(d.covers), .date(d.startsAt), .integer(d.durationMinutes), .string(d.tableNumber), .string(d.note),
             .bool(keepPhone), .string(d.phone), .bool(d.isWaitlist), .date(now), .uuid(id), .integer(d.quoteMinutes)]
        )
    }

    func setStatus(id: UUID, _ status: ReservationStatus, tableNumber: String? = nil, now: Date) throws {
        try db.run(
            "UPDATE Reservations SET Status = ?1, TableNumber = COALESCE(?2, TableNumber), UpdatedAtUtc = ?3 WHERE Id = ?4",
            [.integer(status.rawValue), .string(tableNumber), .date(now), .uuid(id)]
        )
    }

    /// Une table libérée par un paiement soldé termine la réservation installée dessus (miroir de `ReservationService.CompleteSeatedAsync`).
    func completeSeated(table: String, now: Date) throws {
        try db.run(
            "UPDATE Reservations SET Status = ?, UpdatedAtUtc = ? WHERE TableNumber = ? AND Status = ?",
            [.integer(ReservationStatus.done.rawValue), .date(now), .text(table), .integer(ReservationStatus.seated.rawValue)]
        )
    }

    /// Applique la conservation : téléphone effacé 2 jours après l'heure de la réservation, ligne supprimée après 90 jours.
    func purge(now: Date) throws {
        try db.run("DELETE FROM Reservations WHERE StartsAtUtc < ?", [.date(now.addingTimeInterval(-ReservationRules.rowRetention))])
        try db.run(
            "UPDATE Reservations SET Phone = NULL WHERE Phone IS NOT NULL AND StartsAtUtc < ?",
            [.date(now.addingTimeInterval(-ReservationRules.phoneRetention))]
        )
    }

    private static func decode(_ r: SQLRow) -> Reservation {
        Reservation(
            id: r.uuid("Id")!, guestName: r.string("GuestName") ?? "", covers: r.int("Covers") ?? 0, startsAtUtc: r.date("StartsAtUtc") ?? Date(),
            durationMinutes: r.int("DurationMinutes") ?? 90, tableNumber: r.string("TableNumber"), note: r.string("Note"), phone: r.string("Phone"),
            status: ReservationStatus(rawValue: r.int("Status") ?? 0) ?? .booked, isWaitlist: r.bool("IsWaitlist"), createdAtUtc: r.date("CreatedAtUtc") ?? Date(), quoteMinutes: r.int("QuoteMinutes")
        )
    }
}
