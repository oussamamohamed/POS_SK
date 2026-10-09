import Foundation

/// Contenu d'une base neuve.
public enum LocalSeedMode: Sendable {
    /// Comptes (1234, 2468, 5678, 9999), catalogue, tables, chambres, imprimantes et planning de démonstration : tests et démonstration.
    case demo
    /// Aucune donnée : `completeFirstRun` crée le premier responsable et l'identité de l'établissement. Mode des installations réelles.
    case blank
}

/// Données d'amorçage du premier lancement : mêmes comptes, familles, articles et tables que `Program.SeedDatabase`
/// (réutilise `Seed.make()` du backend en mémoire).
enum LocalSeeder {
    static func seedIfEmpty(_ db: SQLiteDatabase, mode: LocalSeedMode) throws {
        guard mode == .demo, try db.query("SELECT COUNT(*) AS n FROM Users").first?.int("n") == 0 else { return }
        let seed = Seed.make()
        try db.transaction {
            let staff = LocalStaffRepository(db: db)
            for record in seed.staff { try staff.insert(record.member, pin: record.pin) }
            let catalog = LocalCatalogRepository(db: db)
            for category in seed.categories { try catalog.insert(category) }
            for product in seed.products { try catalog.insert(product) }
            let floor = LocalFloorRepository(db: db)
            for table in seed.tables { try floor.insert(table) }
            try floor.insertDefaultSettings()
            // Chambres de démonstration (comme `Program.SeedDatabase`) : à remplacer par l'accueil de l'établissement avant toute mise en service.
            let hotel = LocalHotelRepository(db: db)
            let now = Date()
            for room in seed.rooms { try hotel.insert(room, checkIn: now.addingTimeInterval(-86_400), checkOut: now.addingTimeInterval(3 * 86_400)) }
            // Imprimantes et planning Happy Hour de démonstration (comme `Program.SeedDatabase`).
            let printers = LocalPrinterRepository(db: db)
            for printer in seed.printers { try printers.insert(printer) }
            let happyHour = LocalHappyHourRepository(db: db)
            for schedule in seed.schedules { try happyHour.insert(schedule) }
        }
    }
}
