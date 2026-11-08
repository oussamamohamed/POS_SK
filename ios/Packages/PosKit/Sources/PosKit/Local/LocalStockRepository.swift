import Foundation

struct LocalStockRepository {
    let db: SQLiteDatabase
    static let productOwner = "product"
    static let ingredientOwner = "ingredient"

    private func ingredient(_ row: SQLRow) -> Ingredient? {
        guard let id = row.uuid("Id") else { return nil }
        return Ingredient(id: id, name: row.string("Name") ?? "", unit: row.string("Unit") ?? "g", unitCostMilliCents: row.int64("UnitCostMilliCents") ?? 0,
                          isPreparation: row.bool("IsPreparation"), yieldMilli: row.int64("YieldMilli"), allergenMask: row.int("AllergenMask") ?? 0,
                          lowStockMilli: row.int64("LowStockMilli"), isActive: row.bool("IsActive"))
    }

    func ingredients() throws -> [Ingredient] {
        try db.query("SELECT * FROM Ingredients").compactMap(ingredient)
            .sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    func ingredient(id: UUID) throws -> Ingredient? {
        try db.query("SELECT * FROM Ingredients WHERE Id = ?", [.uuid(id)]).first.flatMap(ingredient)
    }

    func insert(_ i: Ingredient) throws {
        try db.run(
            "INSERT INTO Ingredients (Id, Name, Unit, UnitCostMilliCents, IsPreparation, YieldMilli, AllergenMask, LowStockMilli, IsActive) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [.uuid(i.id), .text(i.name), .text(i.unit), .int(i.unitCostMilliCents), .bool(i.isPreparation), .int64(i.yieldMilli),
             .integer(i.allergenMask), .int64(i.lowStockMilli), .bool(i.isActive)]
        )
    }

    func update(_ i: Ingredient) throws {
        try db.run(
            "UPDATE Ingredients SET Name = ?, Unit = ?, UnitCostMilliCents = ?, IsPreparation = ?, YieldMilli = ?, AllergenMask = ?, LowStockMilli = ?, IsActive = ? WHERE Id = ?",
            [.text(i.name), .text(i.unit), .int(i.unitCostMilliCents), .bool(i.isPreparation), .int64(i.yieldMilli), .integer(i.allergenMask),
             .int64(i.lowStockMilli), .bool(i.isActive), .uuid(i.id)]
        )
    }

    func delete(id: UUID) throws {
        try db.run("DELETE FROM RecipeLines WHERE OwnerKind = ? AND OwnerId = ?", [.text(Self.ingredientOwner), .uuid(id)])
        try db.run("DELETE FROM Ingredients WHERE Id = ?", [.uuid(id)])
    }

    func productExists(_ id: UUID) throws -> Bool {
        !(try db.query("SELECT 1 FROM Products WHERE Id = ?", [.uuid(id)]).isEmpty)
    }

    func product(_ id: UUID) throws -> (overridden: Bool, name: String)? {
        try db.query("SELECT Name, AllergensOverridden FROM Products WHERE Id = ?", [.uuid(id)]).first.map { ($0.bool("AllergensOverridden"), $0.string("Name") ?? "") }
    }

    func lines(kind: String, owner: UUID) throws -> [RecipeLine] {
        try db.query("SELECT IngredientId, QuantityMilli FROM RecipeLines WHERE OwnerKind = ? AND OwnerId = ? ORDER BY rowid", [.text(kind), .uuid(owner)])
            .compactMap { row in row.uuid("IngredientId").map { RecipeLine(ingredientId: $0, quantityMilli: row.int64("QuantityMilli") ?? 0) } }
    }

    func replaceLines(kind: String, owner: UUID, _ lines: [RecipeLine]) throws {
        try db.run("DELETE FROM RecipeLines WHERE OwnerKind = ? AND OwnerId = ?", [.text(kind), .uuid(owner)])
        for l in lines {
            try db.run("INSERT INTO RecipeLines (Id, OwnerKind, OwnerId, IngredientId, QuantityMilli) VALUES (?, ?, ?, ?, ?)",
                       [.uuid(UUID()), .text(kind), .uuid(owner), .uuid(l.ingredientId), .int(l.quantityMilli)])
        }
    }

    func preparationLines() throws -> [UUID: [RecipeLine]] {
        var result: [UUID: [RecipeLine]] = [:]
        for row in try db.query("SELECT OwnerId, IngredientId, QuantityMilli FROM RecipeLines WHERE OwnerKind = ? ORDER BY rowid", [.text(Self.ingredientOwner)]) {
            guard let owner = row.uuid("OwnerId"), let ing = row.uuid("IngredientId") else { continue }
            result[owner, default: []].append(RecipeLine(ingredientId: ing, quantityMilli: row.int64("QuantityMilli") ?? 0))
        }
        return result
    }

    func isUsed(ingredient id: UUID) throws -> Bool {
        !(try db.query("SELECT 1 FROM RecipeLines WHERE IngredientId = ? LIMIT 1", [.uuid(id)]).isEmpty)
    }

    /// Noms des produits et préparations dont la fiche contient cet ingrédient.
    func usageNames(ingredient id: UUID) throws -> [String] {
        let products = try db.query("SELECT p.Name FROM RecipeLines l JOIN Products p ON p.Id = l.OwnerId WHERE l.IngredientId = ? AND l.OwnerKind = ?", [.uuid(id), .text(Self.productOwner)])
        let preps = try db.query("SELECT i.Name FROM RecipeLines l JOIN Ingredients i ON i.Id = l.OwnerId WHERE l.IngredientId = ? AND l.OwnerKind = ?", [.uuid(id), .text(Self.ingredientOwner)])
        return (products + preps).compactMap { $0.string("Name") }.sorted { $0.localizedCompare($1) == .orderedAscending }
    }

    func setProductAllergens(_ id: UUID, mask: Int) throws {
        try db.run("UPDATE Products SET AllergenMask = ?, AllergensDeclared = 1 WHERE Id = ?", [.integer(mask), .uuid(id)])
    }

    /// Coût de revient (centimes) de chaque produit ayant une fiche ; une fiche devenue invalide est ignorée (le plat retombe sur `Cost`).
    func productCostsCents() throws -> [UUID: Int] {
        let owners = try db.query("SELECT DISTINCT OwnerId FROM RecipeLines WHERE OwnerKind = ?", [.text(Self.productOwner)]).compactMap { $0.uuid("OwnerId") }
        guard !owners.isEmpty else { return [:] }
        let all = Dictionary(uniqueKeysWithValues: try ingredients().map { ($0.id, $0) })
        let preps = try preparationLines()
        var result: [UUID: Int] = [:]
        for owner in owners {
            if let cost = try? RecipeCostCalculator.compute(lines: try lines(kind: Self.productOwner, owner: owner), ingredients: all, preparationLines: preps) {
                result[owner] = Int(cost.cents)
            }
        }
        return result
    }
}
