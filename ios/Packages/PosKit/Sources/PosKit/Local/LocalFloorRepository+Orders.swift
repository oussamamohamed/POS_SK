import Foundation

extension LocalFloorRepository {
    func table(_ number: String) throws -> DiningTable? {
        try tables().first { $0.tableNumber == number }
    }

    func waiterId(of number: String) throws -> UUID? {
        try db.query("SELECT AssignedWaiterId FROM DiningTables WHERE TableNumber = ?", [.text(number)]).first?.uuid("AssignedWaiterId")
    }

    /// Met à jour l'état d'occupation d'une table. `orderId == nil` et `status == .free` libèrent la table.
    func update(_ number: String, status: TableStatus, covers: Int, waiterName: String?, waiterId: UUID?, activeOrderId: UUID?, openedAt: Date?) throws {
        try db.run(
            """
            UPDATE DiningTables SET Status = ?, CoversCount = ?, AssignedWaiterName = ?, AssignedWaiterId = ?, ActiveOrderId = ?, OpenedAtUtc = ?, UpdatedAtUtc = ?
            WHERE TableNumber = ?
            """,
            [.integer(status.rawValue), .integer(covers), .string(waiterName), .uuid(waiterId), .uuid(activeOrderId), .date(openedAt), .date(Date()), .text(number)]
        )
    }
}
