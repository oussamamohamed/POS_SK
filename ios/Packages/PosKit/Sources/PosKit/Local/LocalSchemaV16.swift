import Foundation

extension LocalMigrator {
    /// Caisse et alertes, mêmes tables, colonnes, déclencheurs et seuils par défaut que `AppDbContext` / `CashSchema` du .NET (spec 024).
    static let schemaV16 = """
    CREATE TABLE CashSessions (
        Id TEXT NOT NULL PRIMARY KEY,
        TerminalId TEXT NOT NULL,
        OpenedAtUtc TEXT NOT NULL,
        OpenedByOperatorId TEXT NOT NULL,
        OpeningFloatCents INTEGER NOT NULL
    );
    CREATE INDEX IX_CashSessions_TerminalId ON CashSessions (TerminalId);
    CREATE TABLE CashMovements (
        Id TEXT NOT NULL PRIMARY KEY,
        SessionId TEXT NOT NULL,
        Kind TEXT NOT NULL,
        AmountCents INTEGER NOT NULL,
        Reason TEXT NOT NULL,
        OperatorId TEXT NOT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_CashMovements_SessionId ON CashMovements (SessionId);
    CREATE TABLE CashCounts (
        Id TEXT NOT NULL PRIMARY KEY,
        SessionId TEXT NOT NULL,
        ClosedAtUtc TEXT NOT NULL,
        ClosedByOperatorId TEXT NOT NULL,
        CountedCents INTEGER NOT NULL,
        ExpectedCents INTEGER NOT NULL,
        VarianceCents INTEGER NOT NULL,
        DenominationsJson TEXT,
        VarianceNote TEXT
    );
    CREATE UNIQUE INDEX IX_CashCounts_SessionId ON CashCounts (SessionId);
    CREATE TRIGGER TR_CashSessions_NoUpdate BEFORE UPDATE ON CashSessions BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;
    CREATE TRIGGER TR_CashSessions_NoDelete BEFORE DELETE ON CashSessions BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;
    CREATE TRIGGER TR_CashMovements_NoUpdate BEFORE UPDATE ON CashMovements BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;
    CREATE TRIGGER TR_CashMovements_NoDelete BEFORE DELETE ON CashMovements BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;
    CREATE TRIGGER TR_CashCounts_NoUpdate BEFORE UPDATE ON CashCounts BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;
    CREATE TRIGGER TR_CashCounts_NoDelete BEFORE DELETE ON CashCounts BEGIN SELECT RAISE(ABORT, 'cash_append_only'); END;
    CREATE TRIGGER TR_CashSessions_OneOpen BEFORE INSERT ON CashSessions WHEN EXISTS (SELECT 1 FROM CashSessions s WHERE s.TerminalId = NEW.TerminalId AND NOT EXISTS (SELECT 1 FROM CashCounts c WHERE c.SessionId = s.Id)) BEGIN SELECT RAISE(ABORT, 'cash_session_already_open'); END;
    CREATE TABLE AlertRules (
        Type TEXT NOT NULL PRIMARY KEY,
        Threshold INTEGER NOT NULL,
        Enabled INTEGER NOT NULL DEFAULT 0,
        UpdatedAtUtc TEXT NOT NULL,
        UpdatedBy TEXT
    );
    INSERT INTO AlertRules (Type, Threshold, Enabled, UpdatedAtUtc) VALUES ('cash_variance', 1000, 0, '2026-01-01T00:00:00.000Z'), ('voids_count', 3, 0, '2026-01-01T00:00:00.000Z'), ('discount_percent', 30, 0, '2026-01-01T00:00:00.000Z'), ('comps_total', 5000, 0, '2026-01-01T00:00:00.000Z');
    CREATE TABLE Alerts (
        Id TEXT NOT NULL PRIMARY KEY,
        RuleType TEXT NOT NULL,
        OperatorId TEXT,
        TerminalId TEXT NOT NULL,
        PeriodKey TEXT NOT NULL,
        DedupeKey TEXT NOT NULL,
        DetailJson TEXT NOT NULL,
        State TEXT NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        ResolvedAtUtc TEXT,
        ResolvedBy TEXT,
        ResolutionNote TEXT
    );
    CREATE UNIQUE INDEX IX_Alerts_DedupeKey ON Alerts (DedupeKey);
    ALTER TABLE RestaurantSettings ADD COLUMN TrendThresholdPercent INTEGER NOT NULL DEFAULT 15;
    ALTER TABLE RestaurantSettings ADD COLUMN CashVarianceNoteRequired INTEGER NOT NULL DEFAULT 1;
    """
}
