import Foundation

struct LocalCatalogRepository {
    let db: SQLiteDatabase

    func activeCategories() throws -> [MenuCategory] {
        try db.query("SELECT * FROM Categories WHERE IsActive = 1 ORDER BY DisplayOrder, rowid").map {
            MenuCategory(id: $0.string("Id") ?? "", name: $0.string("Name") ?? "", iconName: $0.string("IconName"), colorHex: $0.string("ColorHex"),
                         displayOrder: $0.int("DisplayOrder"), isActive: $0.bool("IsActive"), preparationStationId: $0.string("PreparationStationId"))
        }
    }

    func insert(_ c: MenuCategory) throws {
        let now = Date()
        try db.run(
            "INSERT INTO Categories (Id, Name, IconName, ColorHex, PreparationStationId, DisplayOrder, IsActive, CreatedAtUtc, UpdatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [.text(c.id), .text(c.name), .string(c.iconName), .string(c.colorHex), .string(c.preparationStationId),
             .integer(c.displayOrder ?? 0), .bool(c.isActive ?? true), .date(now), .date(now)]
        )
    }

    /// `stationId` : `nil` = inchangé, `""` = effacer (même règle que le PUT de l'API). `false` si la famille est inconnue.
    func updateCategory(id: String, name: String, colorHex: String, displayOrder: Int, stationId: String?) throws -> Bool {
        try db.run(
            """
            UPDATE Categories SET Name = ?1, ColorHex = ?2, DisplayOrder = ?3,
                PreparationStationId = CASE WHEN ?4 IS NULL THEN PreparationStationId WHEN ?4 = '' THEN NULL ELSE ?4 END,
                UpdatedAtUtc = ?5
            WHERE Id = ?6
            """,
            [.text(name), .text(colorHex), .integer(displayOrder), .string(stationId), .date(Date()), .text(id)]
        ) > 0
    }

    func activeProducts() throws -> [Product] {
        let groups = try modifierGroupsByProduct()
        return try db.query("SELECT * FROM Products WHERE IsActive = 1 ORDER BY DisplayOrder, rowid").map {
            Product(
                id: $0.uuid("Id")!, name: $0.string("Name") ?? "", categoryId: $0.string("CategoryId") ?? "", description: $0.string("Description"),
                price: Money(cents: $0.int("Price") ?? 0), taxRatePercent: $0.decimal("TaxRatePercent") ?? 10,
                taxRateTakeawayPercent: $0.decimal("TaxRateTakeawayPercent"), colorHex: $0.string("ColorHex"), displayOrder: $0.int("DisplayOrder"),
                isQuickKey: $0.bool("IsQuickKey"), preparationStationId: $0.string("PreparationStationId"),
                isAvailable: $0.bool("IsAvailable"), isActive: $0.bool("IsActive"), modifierGroups: groups[$0.uuid("Id")!] ?? []
            )
        }
    }

    func insert(_ p: Product) throws {
        let now = Date()
        try db.run(
            """
            INSERT INTO Products (Id, Name, CategoryId, Description, Price, TaxRatePercent, TaxRateTakeawayPercent, IsFoodVoucherEligible, ColorHex,
                DisplayOrder, IsAvailable, IsActive, IsQuickKey, PreparationStationId, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(p.id), .text(p.name), .text(p.categoryId), .string(p.description), .integer(p.price.cents), .decimal(p.taxRatePercent),
             .decimal(p.taxRateTakeawayPercent), .string(p.colorHex), .integer(p.displayOrder ?? 0), .bool(p.isAvailable ?? true),
             .bool(p.isActive ?? true), .bool(p.isQuickKey), .string(p.preparationStationId), .date(now), .date(now)]
        )
        // Un groupe appartient à un seul article (`ModifierGroups.ProductId`) : les lignes reçoivent de nouveaux identifiants,
        // car un même `ModifierGroup` du modèle peut être partagé par plusieurs articles (ex. « Cuisson »).
        for (groupIndex, group) in p.modifierGroups.enumerated() {
            let groupId = UUID()
            try db.run(
                "INSERT INTO ModifierGroups (Id, ProductId, GroupName, MinSelections, MaxSelections, DisplayOrder) VALUES (?, ?, ?, ?, ?, ?)",
                [.uuid(groupId), .uuid(p.id), .text(group.groupName), .integer(group.minSelections), .integer(group.maxSelections), .integer(groupIndex)]
            )
            for (optionIndex, option) in group.options.enumerated() {
                try db.run(
                    "INSERT INTO ModifierOptions (Id, GroupId, Name, ExtraPrice, IsDefault, DisplayOrder) VALUES (?, ?, ?, ?, ?, ?)",
                    [.uuid(UUID()), .uuid(groupId), .text(option.name), .integer(option.extraPrice.cents), .bool(option.isDefault), .integer(optionIndex)]
                )
            }
        }
    }

    /// `false` si l'article est inconnu.
    func update(productId: UUID, _ d: ProductDraft) throws -> Bool {
        try db.run(
            """
            UPDATE Products SET Name = ?, CategoryId = ?, Description = ?, Price = ?, TaxRatePercent = ?, ColorHex = ?, DisplayOrder = ?,
                IsQuickKey = ?, PreparationStationId = ?, UpdatedAtUtc = ?
            WHERE Id = ?
            """,
            [.text(d.name), .text(d.categoryId), .string(d.description.isEmpty ? nil : d.description), .integer(d.price.cents), .decimal(d.taxRatePercent),
             .string(d.colorHex), .integer(d.displayOrder), .bool(d.isQuickKey), .string(d.stationId), .date(Date()), .uuid(productId)]
        ) > 0
    }

    /// Archivage logique (l'article reste référencé par les commandes passées). `false` si inconnu.
    func archive(productId: UUID) throws -> Bool {
        try db.run("UPDATE Products SET IsActive = 0, UpdatedAtUtc = ? WHERE Id = ?", [.date(Date()), .uuid(productId)]) > 0
    }

    /// Poste propre de l'article et poste de sa famille (repli du routage cuisine). Inclut les articles archivés.
    func stationFallbacks() throws -> [UUID: (product: String?, category: String?)] {
        let rows = try db.query(
            """
            SELECT p.Id AS Id, p.PreparationStationId AS PStation, c.PreparationStationId AS CStation
            FROM Products p LEFT JOIN Categories c ON c.Id = p.CategoryId
            """
        )
        var result: [UUID: (product: String?, category: String?)] = [:]
        for row in rows {
            if let id = row.uuid("Id") { result[id] = (product: row.string("PStation"), category: row.string("CStation")) }
        }
        return result
    }

    private func modifierGroupsByProduct() throws -> [UUID: [ModifierGroup]] {
        let options = Dictionary(grouping: try db.query("SELECT * FROM ModifierOptions ORDER BY DisplayOrder, rowid"), by: { $0.uuid("GroupId")! })
        var result: [UUID: [ModifierGroup]] = [:]
        for row in try db.query("SELECT * FROM ModifierGroups ORDER BY DisplayOrder, rowid") {
            let id = row.uuid("Id")!
            let minSelections = row.int("MinSelections") ?? 0
            let maxSelections = row.int("MaxSelections") ?? 1
            let group = ModifierGroup(
                id: id, groupName: row.string("GroupName") ?? "", minSelections: minSelections, maxSelections: maxSelections,
                isMandatory: minSelections > 0, isSingleChoice: maxSelections == 1,
                options: (options[id] ?? []).map {
                    ModifierOption(id: $0.uuid("Id")!, name: $0.string("Name") ?? "", extraPrice: Money(cents: $0.int("ExtraPrice") ?? 0), isDefault: $0.bool("IsDefault"))
                }
            )
            result[row.uuid("ProductId")!, default: []].append(group)
        }
        return result
    }
}
