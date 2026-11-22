import Foundation

extension LocalMigrator {
    /// Annulation d'une ligne envoyée : mêmes colonnes et table que `Program.cs` (`ApplySchema`) du .NET.
    static let schemaV17 = """
    ALTER TABLE KitchenTicketItems ADD COLUMN OrderItemId TEXT NULL;
    ALTER TABLE KitchenTicketItems ADD COLUMN CancelledQuantity INTEGER NOT NULL DEFAULT 0;
    CREATE TABLE OrderLineVoids (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL,
        OrderItemId TEXT NOT NULL,
        TicketId TEXT NULL,
        ProductId TEXT NOT NULL,
        ProductName TEXT NOT NULL,
        Quantity INTEGER NOT NULL,
        UnitPrice INTEGER NOT NULL,
        ModifiersSummary TEXT NULL,
        KitchenComment TEXT NULL,
        PreparationStationId TEXT NULL,
        Reason TEXT NOT NULL,
        OperatorId TEXT NOT NULL,
        OperatorName TEXT NOT NULL,
        ServerName TEXT NOT NULL,
        TableNumber TEXT NOT NULL,
        VoidedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_OrderLineVoids_OrderId ON OrderLineVoids (OrderId);
    """
}
