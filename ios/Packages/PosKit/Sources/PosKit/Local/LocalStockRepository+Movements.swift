import Foundation

/// Mouvements de stock et consommation à la vente (miroir de `StockConsumption` et `StockService` du .NET).
extension LocalStockRepository {
    private func row(_ r: SQLRow) -> StockMovement? {
        guard let id = r.uuid("Id"), let ingredient = r.uuid("IngredientId"), let date = r.date("CreatedAtUtc") else { return nil }
        return StockMovement(id: id, ingredientId: ingredient, kind: r.string("Kind") ?? "", quantityMilli: r.int64("QuantityMilli") ?? 0,
                             unitCostMilliCents: r.int64("UnitCostMilliCents"), orderId: r.uuid("OrderId"), operatorId: r.uuid("OperatorId"),
                             note: r.string("Note"), createdAtUtc: date)
    }

    func levels() throws -> [UUID: Int64] {
        Dictionary(uniqueKeysWithValues: try db.query("SELECT IngredientId, StockMilli FROM IngredientStock").compactMap { r in
            r.uuid("IngredientId").map { ($0, r.int64("StockMilli") ?? 0) }
        })
    }

    func level(of ingredient: UUID) throws -> Int64 {
        try db.query("SELECT COALESCE(SUM(QuantityMilli), 0) AS S FROM StockMovements WHERE IngredientId = ?", [.uuid(ingredient)]).first?.int64("S") ?? 0
    }

    func insert(_ m: StockMovement) throws {
        try db.run(
            "INSERT INTO StockMovements (Id, IngredientId, Kind, QuantityMilli, UnitCostMilliCents, OrderId, OperatorId, Note, CreatedAtUtc) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            [.uuid(m.id), .uuid(m.ingredientId), .text(m.kind), .int(m.quantityMilli), .int64(m.unitCostMilliCents), .uuid(m.orderId), .uuid(m.operatorId),
             .string(m.note), .date(m.createdAtUtc)]
        )
    }

    func hasMovements(ingredient id: UUID) throws -> Bool {
        !(try db.query("SELECT 1 FROM StockMovements WHERE IngredientId = ? LIMIT 1", [.uuid(id)]).isEmpty)
    }

    func movements(ingredient: UUID?, from: Date?, to: Date?) throws -> [StockMovement] {
        let all = ingredient.map { try? db.query("SELECT * FROM StockMovements WHERE IngredientId = ?", [.uuid($0)]) } ?? (try? db.query("SELECT * FROM StockMovements"))
        return (all ?? []).compactMap(row).filter { (from == nil || $0.createdAtUtc >= from!) && (to == nil || $0.createdAtUtc <= to!) }
            .sorted { $0.createdAtUtc != $1.createdAtUtc ? $0.createdAtUtc > $1.createdAtUtc : $0.id.uuidString > $1.id.uuidString }
    }

    /// Valeur (centimes, demi supérieur en valeur absolue) d'une quantité à un prix en milli-centimes.
    static func valueCents(quantityMilli: Int64, unitCostMilliCents: Int64) -> Int64 {
        let magnitude = (try? U128.zero.adding(UInt64(quantityMilli.magnitude), times: UInt64(unitCostMilliCents.magnitude)).divided(by: 1_000_000)).map(Int64.init) ?? 0
        return (quantityMilli < 0) != (unitCostMilliCents < 0) && magnitude != 0 ? -magnitude : magnitude
    }

    // MARK: Consommation à la vente

    /// Un mouvement `sale` par (commande, ingrédient) à l'encaissement soldé, dans la transaction du règlement. Ne lève jamais :
    /// une panne de calcul laisse la vente intacte et le stock inchangé. Les lignes offertes ou remisées sont consommées.
    func recordSale(order: ActiveOrder, operatorId: UUID?) {
        guard let already = try? db.query("SELECT 1 FROM StockMovements WHERE OrderId = ? AND Kind = ? LIMIT 1", [.uuid(order.orderId), .text(StockMovementKind.sale)]),
              already.isEmpty, let totals = try? saleTotals(order) else { return }
        for (ingredient, quantity) in totals.sorted(by: { $0.key.uuidString < $1.key.uuidString }) where quantity != 0 {
            try? insert(StockMovement(ingredientId: ingredient, kind: StockMovementKind.sale, quantityMilli: -quantity, orderId: order.orderId, operatorId: operatorId))
        }
    }

    private func saleTotals(_ order: ActiveOrder) throws -> [UUID: Int64] {
        let all = Dictionary(uniqueKeysWithValues: try ingredients().map { ($0.id, $0) })
        let preps = try preparationLines()
        let consumptions = try db.query(
            """
            SELECT g.ProductId AS ProductId, o.Name AS OptionName, c.IngredientId AS IngredientId, c.DeltaQuantityMilli AS Delta
            FROM ModifierOptionConsumptions c JOIN ModifierOptions o ON o.Id = c.OptionId JOIN ModifierGroups g ON g.Id = o.GroupId
            """)
        var total: [UUID: Int64] = [:]
        for line in order.lines where line.quantity > 0 {
            let lines = try self.lines(kind: Self.productOwner, owner: line.productId)
            for (id, quantity) in RecipeExpander.toRaw(lines, multiplier: Int64(line.quantity), ingredients: all, preparationLines: preps) { total[id, default: 0] += quantity }
            for name in line.modifiersSummary {
                for c in consumptions where c.uuid("ProductId") == line.productId && c.string("OptionName") == name {
                    if let ingredient = c.uuid("IngredientId") { total[ingredient, default: 0] += (c.int64("Delta") ?? 0) * Int64(line.quantity) }
                }
            }
        }
        return total.filter { all[$0.key] != nil }
    }

    /// Restitue la vente d'une commande annulée par avoir (seulement si le responsable l'a demandé).
    func restoreSale(orderId: UUID, operatorId: UUID?) {
        guard let existing = try? db.query("SELECT * FROM StockMovements WHERE OrderId = ?", [.uuid(orderId)]).compactMap(row),
              !existing.contains(where: { $0.kind == StockMovementKind.saleReversal }) else { return }
        for sale in existing where sale.kind == StockMovementKind.sale {
            try? insert(StockMovement(ingredientId: sale.ingredientId, kind: StockMovementKind.saleReversal, quantityMilli: -sale.quantityMilli, orderId: orderId, operatorId: operatorId))
        }
    }

    // MARK: Alertes, réglages, rupture automatique

    func alerts(now: Date) throws -> StockAlerts {
        let levels = try levels()
        let ingredients = try ingredients().filter { !$0.isPreparation && $0.isActive }
        let low = ingredients.compactMap { i -> LowStockAlert? in
            guard let threshold = i.lowStockMilli, levels[i.id, default: 0] <= threshold else { return nil }
            return LowStockAlert(ingredientId: i.id, name: i.name, unit: i.unit, stockMilli: levels[i.id, default: 0], lowStockMilli: threshold)
        }
        let byId = Dictionary(uniqueKeysWithValues: ingredients.map { ($0.id, $0) })
        let since = now.addingTimeInterval(-7 * 86_400)
        let counts = try movements(ingredient: nil, from: since, to: nil).filter { $0.kind == StockMovementKind.count && byId[$0.ingredientId] != nil }.map { m -> CountVarianceAlert in
            let i = byId[m.ingredientId]!
            return CountVarianceAlert(ingredientId: i.id, name: i.name, unit: i.unit, varianceMilli: m.quantityMilli,
                                      valueCents: Self.valueCents(quantityMilli: m.quantityMilli, unitCostMilliCents: m.unitCostMilliCents ?? 0), createdAtUtc: m.createdAtUtc)
        }
        return StockAlerts(lowStock: low, recentCounts: counts)
    }

    func settings() throws -> StockSettings {
        let r = try db.query("SELECT AutoUnavailableOnStockOut, PrintAllergensOnKitchenTicket, AllergenDisclaimer FROM RestaurantSettings WHERE Id = 1").first
        return StockSettings(autoUnavailableOnStockOut: r?.bool("AutoUnavailableOnStockOut") ?? false, printAllergensOnKitchenTicket: r?.bool("PrintAllergensOnKitchenTicket") ?? false,
                             allergenDisclaimer: r?.string("AllergenDisclaimer"))
    }

    func save(_ s: StockSettings) throws {
        let disclaimer = s.allergenDisclaimer?.trimmingCharacters(in: .whitespacesAndNewlines)
        try db.run("UPDATE RestaurantSettings SET AutoUnavailableOnStockOut = ?, PrintAllergensOnKitchenTicket = ?, AllergenDisclaimer = ? WHERE Id = 1",
                   [.bool(s.autoUnavailableOnStockOut), .bool(s.printAllergensOnKitchenTicket), .string(disclaimer?.isEmpty == true ? nil : disclaimer)])
    }

    /// Applique la rupture automatique (option active) et la lève là où le stock est revenu. Épuisé = ingrédient suivi (au moins un
    /// mouvement) dont le stock est nul ou négatif ; une rupture manuelle n'est jamais levée.
    @discardableResult
    func refreshAutoAvailability() throws -> [(productId: UUID, isAvailable: Bool)] {
        let enabled = try settings().autoUnavailableOnStockOut
        let autoCount = try db.query("SELECT 1 FROM Products WHERE AutoUnavailable = 1 LIMIT 1").count
        guard enabled || autoCount > 0 else { return [] }

        var productLines: [UUID: [RecipeLine]] = [:]
        for r in try db.query("SELECT OwnerId, IngredientId, QuantityMilli FROM RecipeLines WHERE OwnerKind = ? ORDER BY rowid", [.text(Self.productOwner)]) {
            if let owner = r.uuid("OwnerId"), let ing = r.uuid("IngredientId") { productLines[owner, default: []].append(RecipeLine(ingredientId: ing, quantityMilli: r.int64("QuantityMilli") ?? 0)) }
        }
        guard !productLines.isEmpty || autoCount > 0 else { return [] }
        let all = Dictionary(uniqueKeysWithValues: try ingredients().map { ($0.id, $0) })
        let preps = try preparationLines()
        let levels = try levels()
        func soldOut(_ product: UUID) -> Bool {
            guard let lines = productLines[product] else { return false }
            return RecipeExpander.toRaw(lines, multiplier: 1, ingredients: all, preparationLines: preps).keys.contains { (levels[$0] ?? 1) <= 0 }
        }

        var changes: [(UUID, Bool)] = []
        for r in try db.query("SELECT Id, IsAvailable, AutoUnavailable FROM Products WHERE IsAvailable = 1 OR AutoUnavailable = 1") {
            guard let id = r.uuid("Id") else { continue }
            let out = enabled && soldOut(id)
            if r.bool("IsAvailable") && out {
                try db.run("UPDATE Products SET IsAvailable = 0, AutoUnavailable = 1 WHERE Id = ?", [.uuid(id)]); changes.append((id, false))
            } else if !r.bool("IsAvailable") && r.bool("AutoUnavailable") && !out {
                try db.run("UPDATE Products SET IsAvailable = 1, AutoUnavailable = 0 WHERE Id = ?", [.uuid(id)]); changes.append((id, true))
            }
        }
        return changes
    }

    /// Allergènes des plats d'un bon de cuisine, si l'option « allergènes sur le ticket » est active (désactivée par défaut) ; `nil` sinon.
    func kitchenAllergens(productIds: [UUID]) throws -> TicketKitchenAllergens? {
        let s = try settings()
        guard s.printAllergensOnKitchenTicket else { return nil }
        var byProduct: [String: TicketProductAllergens] = [:]
        for id in Set(productIds) {
            if let r = try db.query("SELECT AllergensDeclared, AllergenMask FROM Products WHERE Id = ?", [.uuid(id)]).first {
                byProduct[id.uuidString] = TicketProductAllergens(declared: r.bool("AllergensDeclared"), mask: r.int("AllergenMask") ?? 0)
            }
        }
        return TicketKitchenAllergens(byProduct: byProduct, disclaimer: s.allergenDisclaimer)
    }

    // MARK: Ajustements des options de modificateur

    func optionExists(_ id: UUID) throws -> Bool {
        !(try db.query("SELECT 1 FROM ModifierOptions WHERE Id = ?", [.uuid(id)]).isEmpty)
    }

    func consumption(optionId: UUID) throws -> [ModifierConsumptionItem] {
        try db.query("SELECT IngredientId, DeltaQuantityMilli FROM ModifierOptionConsumptions WHERE OptionId = ? ORDER BY rowid", [.uuid(optionId)])
            .compactMap { r in r.uuid("IngredientId").map { ModifierConsumptionItem(ingredientId: $0, deltaQuantityMilli: r.int64("DeltaQuantityMilli") ?? 0) } }
    }

    func replaceConsumption(optionId: UUID, _ items: [ModifierConsumptionItem]) throws {
        try db.run("DELETE FROM ModifierOptionConsumptions WHERE OptionId = ?", [.uuid(optionId)])
        for i in items {
            try db.run("INSERT INTO ModifierOptionConsumptions (Id, OptionId, IngredientId, DeltaQuantityMilli) VALUES (?, ?, ?, ?)",
                       [.uuid(UUID()), .uuid(optionId), .uuid(i.ingredientId), .int(i.deltaQuantityMilli)])
        }
    }
}
