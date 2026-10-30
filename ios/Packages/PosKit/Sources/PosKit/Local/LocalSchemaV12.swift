import Foundation

extension LocalMigrator {
    /// Attente annoncée d'un client en liste d'attente, miroir de `Reservations.QuoteMinutes` du .NET.
    static let schemaV12 = """
    ALTER TABLE Reservations ADD COLUMN QuoteMinutes INTEGER NULL;
    """
}
