import Foundation

/// Configuration des imprimantes (`PrinterConfigurations`). Les stations affectées sont stockées en tableau JSON, comme EF Core.
struct LocalPrinterRepository {
    let db: SQLiteDatabase

    func all() throws -> [Printer] {
        try db.query("SELECT * FROM PrinterConfigurations ORDER BY Name, rowid").compactMap(Self.decode)
    }

    func printer(id: UUID) throws -> Printer? {
        try db.query("SELECT * FROM PrinterConfigurations WHERE Id = ?", [.uuid(id)]).first.flatMap(Self.decode)
    }

    func insert(_ p: Printer) throws {
        let now = Date()
        try db.run(
            """
            INSERT INTO PrinterConfigurations (Id, Name, IpAddress, Port, PaperWidthMm, OpenCashDrawerOnReceipt, TextMode, AssignedStationIds,
                IsActive, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(p.id), .text(p.name), .text(p.ipAddress), .integer(p.port), .integer(p.paperWidthMm), .bool(p.openCashDrawerOnReceipt),
             .bool(p.textMode), .text(Self.encode(stations: p.assignedStationIds)), .bool(p.isActive), .date(now), .date(now)]
        )
    }

    /// `false` si l'identifiant est inconnu.
    func update(_ p: Printer) throws -> Bool {
        try db.run(
            """
            UPDATE PrinterConfigurations SET Name = ?, IpAddress = ?, Port = ?, PaperWidthMm = ?, OpenCashDrawerOnReceipt = ?, TextMode = ?,
                AssignedStationIds = ?, IsActive = ?, UpdatedAtUtc = ?
            WHERE Id = ?
            """,
            [.text(p.name), .text(p.ipAddress), .integer(p.port), .integer(p.paperWidthMm), .bool(p.openCashDrawerOnReceipt), .bool(p.textMode),
             .text(Self.encode(stations: p.assignedStationIds)), .bool(p.isActive), .date(Date()), .uuid(p.id)]
        ) > 0
    }

    private static func encode(stations: [String]) -> String {
        let encoder = JSONEncoder()
        encoder.outputFormatting = .withoutEscapingSlashes
        return (try? encoder.encode(stations)).flatMap { String(data: $0, encoding: .utf8) } ?? "[]"
    }

    private static func decode(_ row: SQLRow) -> Printer? {
        guard let id = row.uuid("Id") else { return nil }
        let stations = row.string("AssignedStationIds").flatMap { try? JSONDecoder().decode([String].self, from: Data($0.utf8)) } ?? []
        return Printer(
            id: id, name: row.string("Name") ?? "", ipAddress: row.string("IpAddress") ?? "", port: row.int("Port") ?? 9100,
            paperWidthMm: row.int("PaperWidthMm") ?? 80, openCashDrawerOnReceipt: row.bool("OpenCashDrawerOnReceipt"), assignedStationIds: stations,
            isActive: row.bool("IsActive"), textMode: row.bool("TextMode")
        )
    }
}
