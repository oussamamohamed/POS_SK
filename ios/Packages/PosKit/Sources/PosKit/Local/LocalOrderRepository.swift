import Foundation

/// Valeurs de l'enum `OrderStatus` du .NET (`Orders.Status`).
enum LocalOrderStatus: Int {
    case open = 0, sentToKitchen = 1, billRequested = 2, paid = 3, cancelled = 4
}

/// Identifiant d'opérateur « inconnu » : le .NET stocke `Guid.Empty` quand la requête n'en porte pas.
let localNoOperator = UUID(uuidString: "00000000-0000-0000-0000-000000000000")!

struct LocalOrderRepository {
    let db: SQLiteDatabase

    /// L'en-tête et les lignes de la commande, sans les infos de table (serveur, couverts) ni les totaux.
    func order(id: UUID) throws -> ActiveOrder? {
        guard let row = try db.query("SELECT * FROM Orders WHERE Id = ?", [.uuid(id)]).first else { return nil }
        let orderLines = try lines(orderId: id)
        return ActiveOrder(
            orderId: id, tableNumber: row.string("TableNumber") ?? "", openedAtUtc: row.date("CreatedAtUtc"), lines: orderLines,
            globalDiscountType: row.int("GlobalDiscountType").flatMap { DiscountType(rawValue: $0) },
            globalDiscountValue: row.decimal("GlobalDiscountValue") ?? 0, globalDiscountReason: row.string("GlobalDiscountReason"),
            destination: OrderDestination(rawValue: row.int("Destination") ?? 0) ?? .takeaway,
            pickupNumber: row.string("PickupNumber"), pickupBuzzer: row.string("PickupBuzzer")
        )
    }

    func lines(orderId: UUID) throws -> [OrderLine] {
        try db.query("SELECT * FROM OrderItems WHERE OrderId = ? ORDER BY OrderedAtUtc, rowid", [.uuid(orderId)]).map(Self.decodeLine)
    }

    func insert(_ o: ActiveOrder, operatorId: UUID, status: LocalOrderStatus) throws {
        try db.run(
            """
            INSERT INTO Orders (Id, TableNumber, OperatorId, Status, Destination, PickupNumber, PickupBuzzer, PickupScheduledAtUtc, CreatedAtUtc,
                GlobalDiscountType, GlobalDiscountValue, GlobalDiscountReason, TipAmount)
            VALUES (?, ?, ?, ?, ?, ?, ?, NULL, ?, ?, ?, ?, 0)
            """,
            [.uuid(o.orderId), .text(o.tableNumber), .uuid(operatorId), .integer(status.rawValue), .integer(o.destination.rawValue),
             .string(o.pickupNumber), .string(o.pickupBuzzer), .date(o.openedAtUtc ?? Date()), .integer(o.globalDiscountType?.rawValue),
             .decimal(o.globalDiscountValue), .string(o.globalDiscountReason)]
        )
    }

    func insert(_ l: OrderLine, orderId: UUID) throws {
        let modifiers = String(decoding: try JSONEncoder().encode(l.modifiersSummary), as: UTF8.self)
        try db.run(
            """
            INSERT INTO OrderItems (Id, OrderId, ProductId, ProductName, Quantity, UnitPrice, TaxRatePercent, TaxRateTakeawayPercent, IsFoodVoucherEligible,
                PreparationStationId, IsDispatched, SelectedModifiers, ModifiersPriceExtra, KitchenComment, Course, DiscountPercent, IsComp, CompReason,
                IsHappyHourApplied, OriginalUnitPrice, AppliedHappyHourScheduleId, OrderedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, 1, ?, ?, ?, ?, NULL, ?, ?, ?, NULL, ?, ?, ?, ?)
            """,
            [.uuid(l.lineId), .uuid(orderId), .uuid(l.productId), .text(l.productName), .integer(l.quantity), .integer(l.unitPrice.cents),
             .decimal(l.taxRatePercent), .decimal(l.taxRateTakeawayPercent), .string(l.preparationStationId), .bool(l.isDispatched), .text(modifiers),
             .integer(l.modifiersPriceExtra.cents), .integer(l.course.rawValue), .decimal(l.discountPercent), .bool(l.isComp),
             .bool(l.isHappyHourApplied), .integer(l.originalUnitPrice?.cents), .uuid(l.appliedHappyHourScheduleId), .date(Date())]
        )
    }

    func setQuantity(lineId: UUID, quantity: Int) throws {
        try db.run("UPDATE OrderItems SET Quantity = ? WHERE Id = ?", [.integer(quantity), .uuid(lineId)])
    }

    /// Marque comme envoyées en cuisine toutes les lignes de la commande.
    func markDispatched(orderId: UUID) throws {
        try db.run("UPDATE OrderItems SET IsDispatched = 1 WHERE OrderId = ? AND IsDispatched = 0", [.uuid(orderId)])
    }

    func setStatus(orderId: UUID, _ status: LocalOrderStatus) throws {
        try db.run("UPDATE Orders SET Status = ? WHERE Id = ?", [.integer(status.rawValue), .uuid(orderId)])
    }

    /// `false` si la commande est inconnue.
    func setDestination(orderId: UUID, _ destination: OrderDestination) throws -> Bool {
        try db.run("UPDATE Orders SET Destination = ? WHERE Id = ?", [.integer(destination.rawValue), .uuid(orderId)]) > 0
    }

    func setTableNumber(orderId: UUID, _ number: String) throws {
        try db.run("UPDATE Orders SET TableNumber = ? WHERE Id = ?", [.text(number), .uuid(orderId)])
    }

    /// `type == nil` retire la remise. `false` si la commande est inconnue.
    func setDiscount(orderId: UUID, type: DiscountType?, value: Decimal, reason: String?) throws -> Bool {
        try db.run(
            "UPDATE Orders SET GlobalDiscountType = ?, GlobalDiscountValue = ?, GlobalDiscountReason = ? WHERE Id = ?",
            [.integer(type?.rawValue), .decimal(value), .string(reason), .uuid(orderId)]
        ) > 0
    }

    /// Passe la ligne en gratuité. `false` si la ligne n'appartient pas à la commande.
    func comp(lineId: UUID, orderId: UUID, reason: String) throws -> Bool {
        try db.run("UPDATE OrderItems SET IsComp = 1, CompReason = ? WHERE Id = ? AND OrderId = ?", [.text(reason), .uuid(lineId), .uuid(orderId)]) > 0
    }

    func moveLines(from source: UUID, to target: UUID) throws {
        try db.run("UPDATE OrderItems SET OrderId = ? WHERE OrderId = ?", [.uuid(target), .uuid(source)])
    }

    func insertTransferLog(source: String, target: String, orderId: UUID, operatorName: String, isMerge: Bool) throws {
        try db.run(
            """
            INSERT INTO TableTransferLogs (Id, SourceTableNumber, TargetTableNumber, OrderId, OperatorId, OperatorName, IsMerge, TimestampUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(UUID()), .text(source), .text(target), .uuid(orderId), .uuid(localNoOperator), .text(operatorName), .bool(isMerge), .date(Date())]
        )
    }

    func insertDiscountAudit(orderId: UUID, itemId: UUID?, type: DiscountType, value: Decimal, saved: Money, reason: String, operatorId: UUID) throws {
        try db.run(
            """
            INSERT INTO OrderDiscountAudits (Id, OrderId, OrderItemId, DiscountType, Value, AmountSaved, Reason, AuthorizedByOperatorId, AppliedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(UUID()), .uuid(orderId), .uuid(itemId), .integer(type.rawValue), .decimal(value), .integer(saved.cents), .text(reason),
             .uuid(operatorId), .date(Date())]
        )
    }

    private static func decodeLine(_ r: SQLRow) throws -> OrderLine {
        let modifiers = try JSONDecoder().decode([String].self, from: Data((r.string("SelectedModifiers") ?? "[]").utf8))
        return OrderLine(
            lineId: r.uuid("Id")!, productId: r.uuid("ProductId")!, productName: r.string("ProductName") ?? "", quantity: r.int("Quantity") ?? 1,
            unitPrice: Money(cents: r.int("UnitPrice") ?? 0), taxRatePercent: r.decimal("TaxRatePercent") ?? 10,
            preparationStationId: r.string("PreparationStationId"), isDispatched: r.bool("IsDispatched"), modifiersSummary: modifiers,
            course: CourseType(rawValue: r.int("Course") ?? 0) ?? .direct, isComp: r.bool("IsComp"),
            discountPercent: r.decimal("DiscountPercent") ?? 0, modifiersPriceExtra: Money(cents: r.int("ModifiersPriceExtra") ?? 0),
            taxRateTakeawayPercent: r.decimal("TaxRateTakeawayPercent"), isHappyHourApplied: r.bool("IsHappyHourApplied"),
            originalUnitPrice: r.int("OriginalUnitPrice").map { Money(cents: $0) }, appliedHappyHourScheduleId: r.uuid("AppliedHappyHourScheduleId")
        )
    }
}
