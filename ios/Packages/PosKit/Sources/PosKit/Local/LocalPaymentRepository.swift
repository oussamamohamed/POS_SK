import Foundation

struct LocalPaymentRepository {
    let db: SQLiteDatabase

    /// `#A-01` … `#A-99` puis retour à `#A-01`, remis à zéro chaque jour UTC (comme `TakeawayCounterService`).
    func nextPickupNumber(terminalId: String, now: Date) throws -> String {
        let prefix = Self.pickupPrefix(for: terminalId)
        let day = String(SQLDate.format(now).prefix(10))
        let sequence = try advanceCounter(name: "pickup:\(prefix)", day: day, wrapAt: 99)
        return "#\(prefix)-\(String(format: "%02d", sequence))"
    }

    /// Lettre dérivée du terminal (`TakeawayCounterService.DeriveTerminalPrefix`).
    static func pickupPrefix(for terminalId: String) -> String {
        let clean = terminalId.trimmingCharacters(in: .whitespacesAndNewlines).uppercased()
        if clean.isEmpty { return "A" }
        if clean.hasSuffix("B") || clean.contains("POS-B") || clean.contains("POS02") || clean.contains("2") { return "B" }
        if clean.hasSuffix("C") || clean.contains("POS-C") || clean.contains("POS03") || clean.contains("3") { return "C" }
        if clean.hasSuffix("D") || clean.contains("POS-D") || clean.contains("POS04") || clean.contains("4") { return "D" }
        return "A"
    }

    func insertCreditVoucher(code: String, orderId: UUID, terminalId: String, amount: Money, expiresAt: Date) throws {
        try db.run(
            """
            INSERT INTO CustomerCreditVouchers (Id, VoucherCode, OriginalOrderId, TerminalId, Amount, IssuedAtUtc, ExpiresAtUtc, IsRedeemed, RedeemedAtUtc, RedeemedOrderId)
            VALUES (?, ?, ?, ?, ?, ?, ?, 0, NULL, NULL)
            """,
            [.uuid(UUID()), .text(code), .uuid(orderId), .text(terminalId), .integer(amount.cents), .date(Date()), .date(expiresAt)]
        )
    }

    /// Incrémente un compteur persistant. `day` change → repart de zéro ; `wrapAt` atteint → repart à 1.
    private func advanceCounter(name: String, day: String, wrapAt: Int?) throws -> Int {
        let row = try db.query("SELECT Day, LastSequence FROM LocalCounters WHERE Name = ?", [.text(name)]).first
        let current = row?.string("Day") == day ? (row?.int("LastSequence") ?? 0) : 0
        var next = current + 1
        if let wrapAt, current >= wrapAt { next = 1 }
        try db.run(
            "INSERT INTO LocalCounters (Name, Day, LastSequence) VALUES (?, ?, ?) ON CONFLICT(Name) DO UPDATE SET Day = excluded.Day, LastSequence = excluded.LastSequence",
            [.text(name), .text(day), .integer(next)]
        )
        return next
    }
}
