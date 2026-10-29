import Foundation

extension LocalMigrator {
    /// Analyse du menu et réservations, miroir de `Product.Cost` et de la table `Reservations` du .NET
    /// (`Program.cs` : `ALTER TABLE Products ADD COLUMN Cost`, `CREATE TABLE Reservations`).
    static let schemaV11 = """
    ALTER TABLE Products ADD COLUMN Cost INTEGER NULL;

    CREATE TABLE Reservations (
        Id TEXT NOT NULL PRIMARY KEY,
        GuestName TEXT NOT NULL,
        Covers INTEGER NOT NULL,
        StartsAtUtc TEXT NOT NULL,
        DurationMinutes INTEGER NOT NULL,
        TableNumber TEXT NULL,
        Note TEXT NULL,
        Phone TEXT NULL,
        Status INTEGER NOT NULL DEFAULT 0,
        IsWaitlist INTEGER NOT NULL DEFAULT 0,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_Reservations_StartsAtUtc ON Reservations (StartsAtUtc);
    """
}
