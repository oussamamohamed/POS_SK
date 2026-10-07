import Foundation

extension LocalMigrator {
    /// Commandes de salle, cuisine, journaux de transfert et d'audit des remises. Mêmes tables et colonnes que `AppDbContext`.
    /// Les paiements, mises en attente, chambres et avoirs arrivent avec le plan 1c.
    static let schemaV2 = """
    CREATE TABLE Orders (
        Id TEXT NOT NULL PRIMARY KEY,
        TableNumber TEXT NOT NULL,
        OperatorId TEXT NOT NULL,
        Status INTEGER NOT NULL,
        Destination INTEGER NOT NULL,
        PickupNumber TEXT,
        PickupBuzzer TEXT,
        PickupScheduledAtUtc TEXT,
        CreatedAtUtc TEXT NOT NULL,
        GlobalDiscountType INTEGER,
        GlobalDiscountValue TEXT NOT NULL,
        GlobalDiscountReason TEXT,
        TipAmount INTEGER NOT NULL
    );

    CREATE TABLE OrderItems (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL REFERENCES Orders (Id) ON DELETE CASCADE,
        ProductId TEXT NOT NULL,
        ProductName TEXT NOT NULL,
        Quantity INTEGER NOT NULL,
        UnitPrice INTEGER NOT NULL,
        TaxRatePercent TEXT NOT NULL,
        TaxRateTakeawayPercent TEXT,
        IsFoodVoucherEligible INTEGER NOT NULL,
        PreparationStationId TEXT,
        IsDispatched INTEGER NOT NULL,
        SelectedModifiers TEXT NOT NULL,
        ModifiersPriceExtra INTEGER NOT NULL,
        KitchenComment TEXT,
        Course INTEGER NOT NULL,
        DiscountPercent TEXT NOT NULL,
        IsComp INTEGER NOT NULL,
        CompReason TEXT,
        IsHappyHourApplied INTEGER NOT NULL,
        OriginalUnitPrice INTEGER,
        AppliedHappyHourScheduleId TEXT,
        OrderedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_OrderItems_OrderId ON OrderItems (OrderId);

    CREATE TABLE KitchenTickets (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL,
        TableNumber TEXT NOT NULL,
        ServerName TEXT NOT NULL,
        CoversCount INTEGER NOT NULL,
        StationId TEXT NOT NULL,
        Status INTEGER NOT NULL,
        DispatchedAtUtc TEXT NOT NULL,
        PreparedAtUtc TEXT,
        CompletedAtUtc TEXT
    );

    CREATE TABLE KitchenTicketItems (
        Id TEXT NOT NULL PRIMARY KEY,
        TicketId TEXT NOT NULL REFERENCES KitchenTickets (Id) ON DELETE CASCADE,
        ProductId TEXT NOT NULL,
        ProductName TEXT NOT NULL,
        Quantity INTEGER NOT NULL,
        ModifiersSummary TEXT,
        KitchenComment TEXT,
        Status INTEGER NOT NULL
    );

    CREATE TABLE TableTransferLogs (
        Id TEXT NOT NULL PRIMARY KEY,
        SourceTableNumber TEXT NOT NULL,
        TargetTableNumber TEXT NOT NULL,
        OrderId TEXT NOT NULL,
        OperatorId TEXT NOT NULL,
        OperatorName TEXT NOT NULL,
        IsMerge INTEGER NOT NULL,
        TimestampUtc TEXT NOT NULL
    );

    CREATE TABLE OrderDiscountAudits (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL,
        OrderItemId TEXT,
        DiscountType INTEGER NOT NULL,
        Value TEXT NOT NULL,
        AmountSaved INTEGER NOT NULL,
        Reason TEXT NOT NULL,
        AuthorizedByOperatorId TEXT NOT NULL,
        AppliedAtUtc TEXT NOT NULL
    );
    """
}
