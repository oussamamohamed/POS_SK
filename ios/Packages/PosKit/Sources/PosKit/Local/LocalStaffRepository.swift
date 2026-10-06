import Foundation

extension UserRole {
    /// Valeur de l'enum `UserRole` du .NET (stockée en entier dans `Users.Role`).
    var storageValue: Int {
        switch self {
        case .waiter: 0
        case .cashier: 1
        case .kitchenStaff: 2
        case .floorManager: 3
        case .admin: 4
        }
    }

    init(storageValue: Int) {
        self = UserRole.allCases.first { $0.storageValue == storageValue } ?? .waiter
    }
}

struct LocalStaffRepository {
    let db: SQLiteDatabase

    func members() throws -> [StaffMember] {
        try db.query("SELECT * FROM Users ORDER BY rowid").map(Self.decode)
    }

    func member(id: UUID) throws -> StaffMember? {
        try db.query("SELECT * FROM Users WHERE Id = ?", [.uuid(id)]).first.map(Self.decode)
    }

    func activeMember(pin: String) throws -> StaffMember? {
        let rows = try db.query("SELECT * FROM Users WHERE IsActive = 1")
        return rows.first { Self.matches($0, pin: pin) }.map(Self.decode)
    }

    func isPinInUse(_ pin: String, excluding id: UUID? = nil) throws -> Bool {
        try db.query("SELECT * FROM Users").contains { $0.uuid("Id") != id && Self.matches($0, pin: pin) }
    }

    func insert(_ member: StaffMember, pin: String) throws {
        let salt = PinHasher.makeSalt()
        let now = Date()
        try db.run(
            "INSERT INTO Users (Id, Name, Role, PinHash, PinSalt, IsActive, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?)",
            [.uuid(member.id), .text(member.name), .integer(member.role.storageValue), .text(PinHasher.hash(pin: pin, salt: salt)),
             .text(salt), .bool(member.isActive), .date(now), .date(now)]
        )
    }

    /// `false` si l'identifiant est inconnu. `pin == nil` : code inchangé.
    func update(id: UUID, name: String, role: UserRole, pin: String?, isActive: Bool) throws -> Bool {
        try db.transaction {
            let changed = try db.run(
                "UPDATE Users SET Name = ?, Role = ?, IsActive = ?, UpdatedAtUtc = ? WHERE Id = ?",
                [.text(name), .integer(role.storageValue), .bool(isActive), .date(Date()), .uuid(id)]
            )
            guard changed > 0 else { return false }
            if let pin {
                let salt = PinHasher.makeSalt()
                try db.run("UPDATE Users SET PinHash = ?, PinSalt = ? WHERE Id = ?", [.text(PinHasher.hash(pin: pin, salt: salt)), .text(salt), .uuid(id)])
            }
            return true
        }
    }

    private static func matches(_ row: SQLRow, pin: String) -> Bool {
        PinHasher.hash(pin: pin, salt: row.string("PinSalt") ?? "") == row.string("PinHash")
    }

    private static func decode(_ row: SQLRow) -> StaffMember {
        StaffMember(id: row.uuid("Id")!, name: row.string("Name") ?? "", role: UserRole(storageValue: row.int("Role") ?? 0), isActive: row.bool("IsActive"))
    }
}
