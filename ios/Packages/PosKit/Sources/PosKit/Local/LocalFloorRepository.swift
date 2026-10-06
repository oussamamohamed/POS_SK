import Foundation

struct LocalFloorRepository {
    let db: SQLiteDatabase

    func tables() throws -> [DiningTable] {
        try db.query("SELECT * FROM DiningTables ORDER BY rowid").map {
            DiningTable(
                tableNumber: $0.string("TableNumber") ?? "", capacity: $0.int("Capacity") ?? 2, status: TableStatus(rawValue: $0.int("Status") ?? 0) ?? .free,
                positionX: $0.double("PositionX"), positionY: $0.double("PositionY"), assignedWaiterName: $0.string("AssignedWaiterName"),
                coversCount: $0.int("CoversCount") ?? 0, activeOrderId: $0.uuid("ActiveOrderId"), openedAtUtc: $0.date("OpenedAtUtc")
            )
        }
    }

    func tableExists(_ number: String) throws -> Bool {
        try !db.query("SELECT 1 AS x FROM DiningTables WHERE TableNumber = ?", [.text(number)]).isEmpty
    }

    func insert(_ t: DiningTable) throws {
        try db.run(
            """
            INSERT INTO DiningTables (TableNumber, Capacity, Status, PositionX, PositionY, AssignedWaiterName, CoversCount, ActiveOrderId, OpenedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.text(t.tableNumber), .integer(t.capacity), .integer(t.status.rawValue), .real(t.positionX ?? 0), .real(t.positionY ?? 0),
             .string(t.assignedWaiterName), .integer(t.coversCount), .uuid(t.activeOrderId), .date(t.openedAtUtc), .date(Date())]
        )
    }

    /// Réglages initiaux : valeurs par défaut de l'entité `RestaurantSettings` du .NET.
    func insertDefaultSettings() throws {
        try db.run(
            """
            INSERT INTO RestaurantSettings (Id, ReceiptLanguage, KitchenTicketLanguage, CompanyName, AddressLines, Siret, VatNumber,
                CertificateNumber, FiscalYearStartMonth, FiscalYearStartDay, UpdatedAtUtc)
            VALUES (1, 'fr', 'fr', ?, ?, ?, ?, NULL, 1, 1, ?)
            """,
            [.text("RESTAURANT L'ANTIGRAVITE"), .text("12 Rue de la Gastronomie\n75001 Paris"), .text("88877766600012"), .text("FR12888777666"), .date(Date())]
        )
    }

    func settings() throws -> RestaurantSettings {
        guard let row = try db.query("SELECT * FROM RestaurantSettings WHERE Id = 1").first else {
            throw SQLiteError(code: -1, message: "Réglages du restaurant absents")
        }
        return RestaurantSettings(
            receiptLanguage: row.string("ReceiptLanguage") ?? "fr", kitchenTicketLanguage: row.string("KitchenTicketLanguage"),
            companyName: row.string("CompanyName"), addressLines: row.string("AddressLines"), siret: row.string("Siret"), vatNumber: row.string("VatNumber"),
            certificateNumber: row.string("CertificateNumber"), fiscalYearStartMonth: row.int("FiscalYearStartMonth"), fiscalYearStartDay: row.int("FiscalYearStartDay")
        )
    }

    /// Un champ `nil` garde la valeur actuelle (comme le PUT de l'API).
    func save(_ s: RestaurantSettings) throws -> RestaurantSettings {
        let current = try settings()
        try db.run(
            """
            UPDATE RestaurantSettings SET ReceiptLanguage = ?, KitchenTicketLanguage = ?, CompanyName = ?, AddressLines = ?, Siret = ?, VatNumber = ?,
                CertificateNumber = ?, FiscalYearStartMonth = ?, FiscalYearStartDay = ?, UpdatedAtUtc = ?
            WHERE Id = 1
            """,
            [.text(s.receiptLanguage), .text(s.kitchenTicketLanguage), .text(s.companyName ?? current.companyName ?? ""),
             .text(s.addressLines ?? current.addressLines ?? ""), .text(s.siret ?? current.siret ?? ""), .text(s.vatNumber ?? current.vatNumber ?? ""),
             .string(s.certificateNumber ?? current.certificateNumber), .integer(s.fiscalYearStartMonth ?? current.fiscalYearStartMonth ?? 1),
             .integer(s.fiscalYearStartDay ?? current.fiscalYearStartDay ?? 1), .date(Date())]
        )
        return try settings()
    }
}
