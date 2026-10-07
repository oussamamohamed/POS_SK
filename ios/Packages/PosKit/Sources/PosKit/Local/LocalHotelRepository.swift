import Foundation

struct LocalHotelRepository {
    let db: SQLiteDatabase

    func insert(_ room: HotelRoom, checkIn: Date, checkOut: Date) throws {
        try db.run(
            """
            INSERT INTO HotelRooms (Id, RoomNumber, GuestName, CheckInDateUtc, CheckOutDateUtc, IsOccupied, MaxCreditLimit, CurrentBalance)
            VALUES (?, ?, ?, ?, ?, 1, ?, ?)
            """,
            [.uuid(UUID()), .text(room.roomNumber), .text(room.guestName), .date(checkIn), .date(checkOut),
             .decimal(room.maxCreditLimit.euros), .decimal(room.currentBalance.euros)]
        )
    }

    func occupiedRooms() throws -> [HotelRoom] {
        try db.query("SELECT * FROM HotelRooms WHERE IsOccupied = 1 ORDER BY RoomNumber").map(Self.decode)
    }

    func occupiedRoom(_ number: String) throws -> HotelRoom? {
        let trimmed = number.trimmingCharacters(in: .whitespacesAndNewlines)
        return try db.query("SELECT * FROM HotelRooms WHERE RoomNumber = ? AND IsOccupied = 1", [.text(trimmed)]).first.map(Self.decode)
    }

    func addToBalance(roomNumber: String, _ amount: Money) throws {
        let trimmed = roomNumber.trimmingCharacters(in: .whitespacesAndNewlines)
        guard let row = try db.query("SELECT CurrentBalance FROM HotelRooms WHERE RoomNumber = ? AND IsOccupied = 1", [.text(trimmed)]).first else {
            throw SQLiteError(code: -1, message: "Chambre \(trimmed) introuvable")
        }
        let balance = (row.decimal("CurrentBalance") ?? 0) + amount.euros
        try db.run("UPDATE HotelRooms SET CurrentBalance = ? WHERE RoomNumber = ? AND IsOccupied = 1", [.decimal(balance), .text(trimmed)])
    }

    func insertCharge(orderId: UUID, roomNumber: String, guestName: String, amount: Money, tip: Money, signature: String?, notes: String?) throws {
        try db.run(
            """
            INSERT INTO RoomFolioCharges (Id, OrderId, RoomNumber, GuestName, Amount, TipAmount, SignatureDataUrl, Notes, ChargedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(UUID()), .uuid(orderId), .text(roomNumber), .text(guestName), .integer(amount.cents), .integer(tip.cents),
             .string(signature), .string(notes), .date(Date())]
        )
    }

    private static func decode(_ row: SQLRow) -> HotelRoom {
        HotelRoom(
            roomNumber: row.string("RoomNumber") ?? "", guestName: row.string("GuestName") ?? "",
            maxCreditLimit: Money(euros: row.decimal("MaxCreditLimit") ?? 0), currentBalance: Money(euros: row.decimal("CurrentBalance") ?? 0)
        )
    }
}
