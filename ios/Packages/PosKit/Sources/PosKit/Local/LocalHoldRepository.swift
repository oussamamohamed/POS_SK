import Foundation

struct LocalHoldRepository {
    let db: SQLiteDatabase

    @discardableResult
    func insert(orderId: UUID, terminalId: String, label: String?, destination: OrderDestination, itemCount: Int, total: Money, snapshot: String, staffId: UUID) throws -> UUID {
        let id = UUID()
        try db.run(
            """
            INSERT INTO HeldOrders (Id, TerminalId, OrderId, CustomerLabel, Destination, ItemCount, TotalTtc, OrderSnapshotJson, HeldAtUtc, HeldByStaffId,
                IsRecalled, RecalledAtUtc, IsVoided, VoidedAtUtc, VoidReason, VoidedByStaffId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, NULL, 0, NULL, NULL, NULL)
            """,
            [.uuid(id), .text(terminalId), .uuid(orderId), .string(label), .integer(destination.rawValue), .integer(itemCount), .integer(total.cents),
             .text(snapshot), .date(Date()), .uuid(staffId)]
        )
        return id
    }

    /// Mises en attente non rappelées ni annulées, la plus récente d'abord. `terminalId` vide = tous les terminaux.
    func active(terminalId: String) throws -> [HeldOrder] {
        let rows: [SQLRow]
        if terminalId.isEmpty {
            rows = try db.query("SELECT * FROM HeldOrders WHERE IsRecalled = 0 AND IsVoided = 0 ORDER BY HeldAtUtc DESC, rowid DESC")
        } else {
            rows = try db.query("SELECT * FROM HeldOrders WHERE IsRecalled = 0 AND IsVoided = 0 AND TerminalId = ? ORDER BY HeldAtUtc DESC, rowid DESC", [.text(terminalId)])
        }
        return rows.map {
            HeldOrder(
                holdId: $0.uuid("Id")!, terminalId: $0.string("TerminalId"), orderId: $0.uuid("OrderId")!, customerLabel: $0.string("CustomerLabel"),
                destination: OrderDestination(rawValue: $0.int("Destination") ?? 0) ?? .takeaway, itemCount: $0.int("ItemCount") ?? 0,
                totalTtc: Money(cents: $0.int("TotalTtc") ?? 0), heldAtUtc: $0.date("HeldAtUtc") ?? Date()
            )
        }
    }

    func hasActiveHold(orderId: UUID) throws -> Bool {
        try !db.query("SELECT 1 AS x FROM HeldOrders WHERE OrderId = ? AND IsRecalled = 0 AND IsVoided = 0", [.uuid(orderId)]).isEmpty
    }

    /// Commande d'une mise en attente encore active, sinon `nil`.
    func activeOrderId(holdId: UUID) throws -> UUID? {
        try db.query("SELECT OrderId FROM HeldOrders WHERE Id = ? AND IsRecalled = 0 AND IsVoided = 0", [.uuid(holdId)]).first?.uuid("OrderId")
    }

    func markRecalled(id: UUID) throws -> Bool {
        try db.run("UPDATE HeldOrders SET IsRecalled = 1, RecalledAtUtc = ? WHERE Id = ? AND IsRecalled = 0 AND IsVoided = 0", [.date(Date()), .uuid(id)]) > 0
    }

    func markVoided(id: UUID, staffId: UUID, reason: String) throws -> Bool {
        try db.run(
            "UPDATE HeldOrders SET IsVoided = 1, VoidedAtUtc = ?, VoidedByStaffId = ?, VoidReason = ? WHERE Id = ? AND IsRecalled = 0 AND IsVoided = 0",
            [.date(Date()), .uuid(staffId), .text(reason), .uuid(id)]
        ) > 0
    }
}
