import Foundation

/// Plannings Happy Hour, règles tarifaires et dérogations de responsable (tables de `AppDbContext`).
struct LocalHappyHourRepository {
    let db: SQLiteDatabase

    // MARK: Plannings

    func schedules() throws -> [HappyHourSchedule] {
        try db.query("SELECT * FROM HappyHourSchedules ORDER BY rowid").map(hydrate)
    }

    func schedule(id: UUID) throws -> HappyHourSchedule? {
        try db.query("SELECT * FROM HappyHourSchedules WHERE Id = ?", [.uuid(id)]).first.map(hydrate)
    }

    /// Insère le planning et ses règles. À appeler dans une transaction.
    @discardableResult
    func insert(_ s: HappyHourSchedule) throws -> UUID {
        guard let start = LocalTimeOfDay.seconds(from: s.startTime), let end = LocalTimeOfDay.seconds(from: s.endTime) else {
            throw SQLiteError(code: -1, message: "Heure de planning invalide : \(s.startTime) - \(s.endTime)")
        }
        let id = s.id ?? UUID()
        try db.run(
            """
            INSERT INTO HappyHourSchedules (Id, Name, DaysOfWeek, StartTime, EndTime, IsActive, AppliesToTakeaway, Priority, CreatedAtUtc, UpdatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, NULL)
            """,
            [.uuid(id), .text(s.name), .text(s.daysOfWeek.map(String.init).joined(separator: ",")), .text(LocalTimeOfDay.stored(start)),
             .text(LocalTimeOfDay.stored(end)), .bool(s.isActive), .bool(s.appliesToTakeaway), .integer(s.priority), .date(Date())]
        )
        for rule in s.priceRules { try insertRule(rule, scheduleId: id) }
        return id
    }

    /// `false` si le planning est inconnu. Les règles sont supprimées en cascade.
    func delete(id: UUID) throws -> Bool {
        try db.run("DELETE FROM HappyHourSchedules WHERE Id = ?", [.uuid(id)]) > 0
    }

    // MARK: Règles

    func insertRule(_ r: HappyHourRule, scheduleId: UUID) throws {
        try db.run(
            """
            INSERT INTO HappyHourPriceRules (Id, ScheduleId, TargetType, TargetId, TargetName, PricingMode, FixedPrice, DiscountPercent, CreatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(r.id ?? UUID()), .uuid(scheduleId), .integer(r.targetType.rawValue), .text(r.targetId), .text(r.targetName),
             .integer(r.pricingMode.rawValue), .integer(r.fixedPrice?.cents), .decimal(r.discountPercent), .date(Date())]
        )
    }

    /// Remplace la règle de même cible (identifiant comparé sans tenir compte de la casse), sinon en crée une. À appeler dans une transaction.
    func upsertRule(scheduleId: UUID, targetType: HappyHourTargetType, targetId: String, targetName: String, mode: HappyHourPricingMode,
                    fixedPrice: Money?, discountPercent: Decimal?) throws {
        let existing = try db.query(
            "SELECT Id FROM HappyHourPriceRules WHERE ScheduleId = ? AND TargetType = ? AND lower(TargetId) = lower(?)",
            [.uuid(scheduleId), .integer(targetType.rawValue), .text(targetId)]
        ).first?.uuid("Id")
        if let existing {
            try db.run(
                "UPDATE HappyHourPriceRules SET TargetName = ?, PricingMode = ?, FixedPrice = ?, DiscountPercent = ? WHERE Id = ?",
                [.text(targetName), .integer(mode.rawValue), .integer(fixedPrice?.cents), .decimal(discountPercent), .uuid(existing)]
            )
        } else {
            try insertRule(
                HappyHourRule(targetType: targetType, targetId: targetId, targetName: targetName, pricingMode: mode, fixedPrice: fixedPrice, discountPercent: discountPercent),
                scheduleId: scheduleId
            )
        }
    }

    /// Nombre de règles supprimées ; les identifiants inconnus sont ignorés.
    func deleteRules(scheduleId: UUID, ids: [UUID]) throws -> Int {
        guard !ids.isEmpty else { return 0 }
        let marks = Array(repeating: "?", count: ids.count).joined(separator: ",")
        return try db.run("DELETE FROM HappyHourPriceRules WHERE ScheduleId = ? AND Id IN (\(marks))", [.uuid(scheduleId)] + ids.map { SQLValue.uuid($0) })
    }

    /// Nom affiché d'un produit (identifiant UUID) ou d'une catégorie ; `nil` si la cible est inconnue.
    func targetName(type: HappyHourTargetType, id: String) throws -> String? {
        switch type {
        case .product:
            guard let uuid = UUID(uuidString: id) else { return nil }
            return try db.query("SELECT Name FROM Products WHERE Id = ?", [.uuid(uuid)]).first?.string("Name")
        case .category:
            return try db.query("SELECT Name FROM Categories WHERE Id = ?", [.text(id)]).first?.string("Name")
        }
    }

    // MARK: Dérogations

    /// Dérogation active et non expirée du terminal (la plus longue si plusieurs).
    func activeOverride(terminalId: String, now: Date) throws -> (startsAt: Date, expiresAt: Date)? {
        try db.query("SELECT StartsAtUtc, ExpiresAtUtc FROM HappyHourOverrideSessions WHERE TerminalId = ? AND IsActive = 1", [.text(terminalId)])
            .compactMap { row -> (startsAt: Date, expiresAt: Date)? in
                guard let starts = row.date("StartsAtUtc"), let expires = row.date("ExpiresAtUtc"), expires > now else { return nil }
                return (startsAt: starts, expiresAt: expires)
            }
            .max { $0.expiresAt < $1.expiresAt }
    }

    func deactivateOverrides(terminalId: String) throws {
        try db.run("UPDATE HappyHourOverrideSessions SET IsActive = 0 WHERE TerminalId = ? AND IsActive = 1", [.text(terminalId)])
    }

    func insertOverride(terminalId: String, operatorId: UUID, operatorName: String, startsAt: Date, expiresAt: Date, reason: String) throws {
        try db.run(
            """
            INSERT INTO HappyHourOverrideSessions (Id, TerminalId, OperatorId, OperatorName, OverrideType, StartsAtUtc, ExpiresAtUtc, Reason, IsActive, CreatedAtUtc)
            VALUES (?, ?, ?, ?, 0, ?, ?, ?, 1, ?)
            """,
            [.uuid(UUID()), .text(terminalId), .uuid(operatorId), .text(operatorName), .date(startsAt), .date(expiresAt), .text(reason), .date(Date())]
        )
    }

    // MARK: Lecture

    private func hydrate(_ row: SQLRow) throws -> HappyHourSchedule {
        guard let id = row.uuid("Id") else { throw SQLiteError(code: -1, message: "Planning Happy Hour sans identifiant") }
        let rules = try db.query("SELECT * FROM HappyHourPriceRules WHERE ScheduleId = ? ORDER BY rowid", [.uuid(id)]).map { r in
            HappyHourRule(
                id: r.uuid("Id"), targetType: HappyHourTargetType(rawValue: r.int("TargetType") ?? 0) ?? .fallback, targetId: r.string("TargetId") ?? "",
                targetName: r.string("TargetName") ?? "", pricingMode: HappyHourPricingMode(rawValue: r.int("PricingMode") ?? 0) ?? .fallback,
                fixedPrice: r.int("FixedPrice").map { Money(cents: $0) }, discountPercent: r.decimal("DiscountPercent")
            )
        }
        return HappyHourSchedule(
            id: id, name: row.string("Name") ?? "", daysOfWeek: (row.string("DaysOfWeek") ?? "").split(separator: ",").compactMap { Int($0) },
            startTime: LocalTimeOfDay.display(LocalTimeOfDay.seconds(from: row.string("StartTime") ?? "") ?? 0),
            endTime: LocalTimeOfDay.display(LocalTimeOfDay.seconds(from: row.string("EndTime") ?? "") ?? 0),
            isActive: row.bool("IsActive"), appliesToTakeaway: row.bool("AppliesToTakeaway"), priority: row.int("Priority") ?? 1, priceRules: rules
        )
    }
}
