import Foundation

extension LocalMigrator {
    /// Imprimantes (configuration), Happy Hour (plannings, règles, dérogations) : mêmes tables et colonnes que `AppDbContext`
    /// (`TimeOnly` stocké `HH:mm:ss`, jours de la semaine en entiers séparés par des virgules, 0 = dimanche, prix fixe en centimes).
    /// Deux tables propres à l'iPad : `LocalPinFailures` et `LocalPinLockout` (verrouillage des PIN, en mémoire côté .NET).
    static let schemaV4 = """
    CREATE TABLE PrinterConfigurations (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        IpAddress TEXT NOT NULL,
        Port INTEGER NOT NULL,
        PaperWidthMm INTEGER NOT NULL,
        OpenCashDrawerOnReceipt INTEGER NOT NULL,
        TextMode INTEGER NOT NULL,
        AssignedStationIds TEXT NOT NULL,
        IsActive INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT NOT NULL
    );

    CREATE TABLE HappyHourSchedules (
        Id TEXT NOT NULL PRIMARY KEY,
        Name TEXT NOT NULL,
        DaysOfWeek TEXT NOT NULL,
        StartTime TEXT NOT NULL,
        EndTime TEXT NOT NULL,
        IsActive INTEGER NOT NULL,
        AppliesToTakeaway INTEGER NOT NULL,
        Priority INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        UpdatedAtUtc TEXT
    );

    CREATE TABLE HappyHourPriceRules (
        Id TEXT NOT NULL PRIMARY KEY,
        ScheduleId TEXT NOT NULL REFERENCES HappyHourSchedules (Id) ON DELETE CASCADE,
        TargetType INTEGER NOT NULL,
        TargetId TEXT NOT NULL,
        TargetName TEXT NOT NULL,
        PricingMode INTEGER NOT NULL,
        FixedPrice INTEGER,
        DiscountPercent TEXT,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_HappyHourPriceRules_ScheduleId ON HappyHourPriceRules (ScheduleId);

    CREATE TABLE HappyHourOverrideSessions (
        Id TEXT NOT NULL PRIMARY KEY,
        TerminalId TEXT NOT NULL,
        OperatorId TEXT NOT NULL,
        OperatorName TEXT NOT NULL,
        OverrideType INTEGER NOT NULL,
        StartsAtUtc TEXT NOT NULL,
        ExpiresAtUtc TEXT NOT NULL,
        Reason TEXT NOT NULL,
        IsActive INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_HappyHourOverrideSessions_TerminalId_IsActive ON HappyHourOverrideSessions (TerminalId, IsActive);

    CREATE TABLE LocalPinFailures (
        Id INTEGER PRIMARY KEY AUTOINCREMENT,
        AttemptedAtUtc TEXT NOT NULL
    );

    CREATE TABLE LocalPinLockout (
        Id INTEGER NOT NULL PRIMARY KEY CHECK (Id = 1),
        LockedUntilUtc TEXT NOT NULL
    );
    """
}
