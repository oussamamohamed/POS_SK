import Foundation

/// Migrations versionnées par `PRAGMA user_version`. Ne jamais modifier un script déjà publié : en ajouter un.
enum LocalMigrator {
    static let steps: [String] = [schemaV1, schemaV2, schemaV3]

    static func migrate(_ db: SQLiteDatabase) throws {
        let current = try db.userVersion()
        guard current <= steps.count else {
            throw SQLiteError(code: -1, message: "Base plus récente que l'application (version \(current))")
        }
        try db.transaction {
            for (index, script) in steps.enumerated() where index >= current {
                try db.exec(script)
                try db.exec("PRAGMA user_version = \(index + 1)")
            }
        }
    }

    // Tables nommées d'après les `DbSet` de `AppDbContext` ; Guid = TEXT majuscules, Money = INTEGER (centimes),
    // decimal = TEXT, DateTimeOffset = TEXT ISO 8601 UTC, enum = INTEGER.
    static let schemaV1 = """
    CREATE TABLE Users (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        Role INTEGER NOT NULL,
        PinHash TEXT NOT NULL,
        PinSalt TEXT NOT NULL,
        IsActive INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_Users_IsActive ON Users (IsActive);

    CREATE TABLE Categories (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        IconName TEXT,
        ColorHex TEXT,
        PreparationStationId TEXT,
        DisplayOrder INTEGER NOT NULL,
        IsActive INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_Categories_DisplayOrder ON Categories (DisplayOrder);

    CREATE TABLE Products (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        CategoryId TEXT NOT NULL,
        Description TEXT,
        Price INTEGER NOT NULL,
        TaxRatePercent TEXT NOT NULL,
        TaxRateTakeawayPercent TEXT,
        IsFoodVoucherEligible INTEGER NOT NULL,
        ColorHex TEXT,
        DisplayOrder INTEGER NOT NULL,
        IsAvailable INTEGER NOT NULL,
        IsActive INTEGER NOT NULL,
        IsQuickKey INTEGER NOT NULL,
        PreparationStationId TEXT,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT NOT NULL,
        Modifiers TEXT NOT NULL DEFAULT '[]'
    );

    CREATE TABLE ModifierGroups (
        Id TEXT NOT NULL PRIMARY KEY,
        ProductId TEXT NOT NULL,
        GroupName TEXT NOT NULL,
        MinSelections INTEGER NOT NULL,
        MaxSelections INTEGER NOT NULL,
        DisplayOrder INTEGER NOT NULL
    );

    CREATE TABLE ModifierOptions (
        Id TEXT NOT NULL PRIMARY KEY,
        GroupId TEXT NOT NULL REFERENCES ModifierGroups (Id) ON DELETE CASCADE,
        Name TEXT NOT NULL,
        ExtraPrice INTEGER NOT NULL,
        IsDefault INTEGER NOT NULL,
        DisplayOrder INTEGER NOT NULL
    );

    CREATE TABLE DiningTables (
        TableNumber TEXT NOT NULL PRIMARY KEY,
        Capacity INTEGER NOT NULL,
        Status INTEGER NOT NULL,
        PositionX REAL NOT NULL,
        PositionY REAL NOT NULL,
        AssignedWaiterName TEXT,
        AssignedWaiterId TEXT,
        CoversCount INTEGER NOT NULL,
        ActiveOrderId TEXT,
        OpenedAtUtc TEXT,
        UpdatedAtUtc TEXT NOT NULL
    );

    CREATE TABLE RestaurantSettings (
        Id INTEGER NOT NULL PRIMARY KEY,
        ReceiptLanguage TEXT NOT NULL,
        KitchenTicketLanguage TEXT NOT NULL,
        CompanyName TEXT NOT NULL,
        AddressLines TEXT NOT NULL,
        Siret TEXT NOT NULL,
        VatNumber TEXT NOT NULL,
        CertificateNumber TEXT,
        FiscalYearStartMonth INTEGER NOT NULL,
        FiscalYearStartDay INTEGER NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );

    CREATE TABLE GridLayouts (
        Id TEXT NOT NULL PRIMARY KEY,
        CategoryId TEXT NOT NULL,
        Name TEXT NOT NULL,
        ColumnsCount INTEGER NOT NULL,
        RowsCount INTEGER NOT NULL,
        PageIndex INTEGER NOT NULL,
        Version INTEGER NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );
    CREATE UNIQUE INDEX IX_GridLayouts_CategoryId_PageIndex ON GridLayouts (CategoryId, PageIndex);

    CREATE TABLE GridSlots (
        Id TEXT NOT NULL PRIMARY KEY,
        GridLayoutId TEXT NOT NULL REFERENCES GridLayouts (Id) ON DELETE CASCADE,
        ProductId TEXT REFERENCES Products (Id) ON DELETE SET NULL,
        RowIndex INTEGER NOT NULL,
        ColumnIndex INTEGER NOT NULL,
        SlotIndex INTEGER NOT NULL,
        CustomLabel TEXT,
        CustomColorHex TEXT,
        IsDisabled INTEGER NOT NULL
    );
    CREATE UNIQUE INDEX IX_GridSlots_GridLayoutId_RowIndex_ColumnIndex ON GridSlots (GridLayoutId, RowIndex, ColumnIndex);
    """
}
