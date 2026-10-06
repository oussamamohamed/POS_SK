import Foundation

struct LocalGridRepository {
    let db: SQLiteDatabase

    func layout(categoryId: String, page: Int) throws -> TouchGridLayout? {
        guard let row = try db.query("SELECT * FROM GridLayouts WHERE CategoryId = ? AND PageIndex = ?", [.text(categoryId), .integer(page)]).first else { return nil }
        return try hydrate(row)
    }

    func layout(id: UUID) throws -> TouchGridLayout? {
        guard let row = try db.query("SELECT * FROM GridLayouts WHERE Id = ?", [.uuid(id)]).first else { return nil }
        return try hydrate(row)
    }

    func layouts() throws -> [TouchGridLayout] {
        try db.query("SELECT * FROM GridLayouts ORDER BY rowid").map(hydrate)
    }

    /// Insère ou met à jour la grille puis remplace tous ses emplacements. À appeler dans une transaction.
    func store(_ layout: TouchGridLayout) throws {
        try db.run(
            """
            INSERT INTO GridLayouts (Id, CategoryId, Name, ColumnsCount, RowsCount, PageIndex, Version, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            ON CONFLICT(Id) DO UPDATE SET Name = excluded.Name, ColumnsCount = excluded.ColumnsCount, RowsCount = excluded.RowsCount,
                Version = excluded.Version, UpdatedAtUtc = excluded.UpdatedAtUtc
            """,
            [.uuid(layout.id), .text(layout.categoryId), .text(layout.name ?? "Défaut"), .integer(layout.columnsCount), .integer(layout.rowsCount),
             .integer(layout.pageIndex), .integer(layout.version ?? 1), .date(Date())]
        )
        try db.run("DELETE FROM GridSlots WHERE GridLayoutId = ?", [.uuid(layout.id)])
        for slot in layout.slots {
            try db.run(
                """
                INSERT INTO GridSlots (Id, GridLayoutId, ProductId, RowIndex, ColumnIndex, SlotIndex, CustomLabel, CustomColorHex, IsDisabled)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(slot.id ?? UUID()), .uuid(layout.id), .uuid(slot.productId), .integer(slot.rowIndex), .integer(slot.columnIndex),
                 .integer(slot.rowIndex * layout.columnsCount + slot.columnIndex), .string(slot.customLabel), .string(slot.customColorHex), .bool(slot.isDisabled ?? false)]
            )
        }
    }

    private func hydrate(_ row: SQLRow) throws -> TouchGridLayout {
        let id = row.uuid("Id")!
        let categoryId = row.string("CategoryId") ?? ""
        let maxPage = try db.query("SELECT MAX(PageIndex) AS m FROM GridLayouts WHERE CategoryId = ?", [.text(categoryId)]).first?.int("m") ?? 0
        let slots = try db.query(
            """
            SELECT s.*, p.Name AS PName, p.Price AS PPrice, p.PreparationStationId AS PStation, p.ColorHex AS PColor
            FROM GridSlots s LEFT JOIN Products p ON p.Id = s.ProductId
            WHERE s.GridLayoutId = ? ORDER BY s.SlotIndex
            """,
            [.uuid(id)]
        ).map { s -> GridSlot in
            let productId = s.uuid("ProductId")
            let summary = productId.map { ProductSummary(id: $0, name: s.string("PName") ?? "", price: Money(cents: s.int("PPrice") ?? 0), preparationStationId: s.string("PStation"), colorHex: s.string("PColor")) }
            return GridSlot(id: s.uuid("Id"), gridLayoutId: id, productId: productId, rowIndex: s.int("RowIndex") ?? 0, columnIndex: s.int("ColumnIndex") ?? 0,
                            slotIndex: s.int("SlotIndex"), customLabel: s.string("CustomLabel"), customColorHex: s.string("CustomColorHex"),
                            isDisabled: s.bool("IsDisabled"), product: summary)
        }
        return TouchGridLayout(id: id, categoryId: categoryId, name: row.string("Name"), columnsCount: row.int("ColumnsCount") ?? 4, rowsCount: row.int("RowsCount") ?? 4,
                               pageIndex: row.int("PageIndex") ?? 0, totalPages: max(1, maxPage + 1), version: row.int("Version"), slots: slots)
    }
}
