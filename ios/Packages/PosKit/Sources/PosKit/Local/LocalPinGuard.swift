import Foundation

/// Même règle que `PinRateLimiterService` : 5 échecs en 1 min verrouillent 30 s. L'état vit dans la base (`LocalPinFailures`,
/// `LocalPinLockout`) : relancer l'application ne le remet pas à zéro. Les méthodes ne doivent pas être appelées dans une transaction.
struct LocalPinGuard {
    let db: SQLiteDatabase

    static let maxFailures = 5
    static let window: TimeInterval = 60
    static let lockout: TimeInterval = 30

    func ensureAllowed(now: Date) throws {
        guard let until = try db.query("SELECT LockedUntilUtc FROM LocalPinLockout WHERE Id = 1").first?.date("LockedUntilUtc"), until > now else { return }
        throw APIError.rateLimited(nil)
    }

    func recordFailure(now: Date) throws {
        try db.transaction {
            try db.run("DELETE FROM LocalPinFailures WHERE AttemptedAtUtc <= ?", [.date(now.addingTimeInterval(-Self.window))])
            try db.run("INSERT INTO LocalPinFailures (AttemptedAtUtc) VALUES (?)", [.date(now)])
            let count = try db.query("SELECT COUNT(*) AS n FROM LocalPinFailures").first?.int("n") ?? 0
            guard count >= Self.maxFailures else { return }
            try db.run(
                "INSERT INTO LocalPinLockout (Id, LockedUntilUtc) VALUES (1, ?) ON CONFLICT(Id) DO UPDATE SET LockedUntilUtc = excluded.LockedUntilUtc",
                [.date(now.addingTimeInterval(Self.lockout))]
            )
            try db.run("DELETE FROM LocalPinFailures")
        }
    }

    func reset() throws {
        try db.transaction {
            try db.run("DELETE FROM LocalPinFailures")
            try db.run("DELETE FROM LocalPinLockout")
        }
    }
}
