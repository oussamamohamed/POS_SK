import Foundation

/// Données d'amorçage du premier lancement : mêmes comptes, familles, articles et tables que `Program.SeedDatabase`
/// (réutilise `Seed.make()` du backend en mémoire).
enum LocalSeeder {
    static func seedIfEmpty(_ db: SQLiteDatabase) throws {
        guard try db.query("SELECT COUNT(*) AS n FROM Users").first?.int("n") == 0 else { return }
        let seed = Seed.make()
        try db.transaction {
            let staff = LocalStaffRepository(db: db)
            for record in seed.staff { try staff.insert(record.member, pin: record.pin) }
        }
    }
}
