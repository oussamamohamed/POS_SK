import Foundation

extension LocalMigrator {
    /// Clôtures de période (mensuelle, annuelle) NF525 : même table et mêmes colonnes que `PeriodClosures` dans `AppDbContext` (nom du `DbSet`).
    static let schemaV8 = """
    CREATE TABLE PeriodClosures (
        Id TEXT NOT NULL PRIMARY KEY,
        TerminalId TEXT NOT NULL,
        PeriodType INTEGER NOT NULL,
        PeriodKey TEXT NOT NULL,
        ClosureSequence INTEGER NOT NULL,
        PeriodStartUtc TEXT NOT NULL,
        PeriodEndUtc TEXT NOT NULL,
        TotalTtcAmount INTEGER NOT NULL,
        TotalHtAmount INTEGER NOT NULL,
        TaxesSummaryJson TEXT NOT NULL,
        TenderTotalsJson TEXT NOT NULL,
        PerpetualGrandTotalCents INTEGER NOT NULL,
        DailyClosureCount INTEGER NOT NULL,
        PreviousSignatureHash TEXT NOT NULL,
        SignatureHash TEXT NOT NULL,
        SealedByUserId TEXT NOT NULL,
        SealedByUserName TEXT NOT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE UNIQUE INDEX IX_PeriodClosures_Terminal_Type_Key ON PeriodClosures (TerminalId, PeriodType, PeriodKey);
    CREATE UNIQUE INDEX IX_PeriodClosures_Terminal_Type_Sequence ON PeriodClosures (TerminalId, PeriodType, ClosureSequence);

    CREATE TRIGGER TR_PeriodClosures_NoUpdate BEFORE UPDATE ON PeriodClosures
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal closures are append-only and immutable.'); END;
    CREATE TRIGGER TR_PeriodClosures_NoDelete BEFORE DELETE ON PeriodClosures
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal closures are append-only and immutable.'); END;
    """
}
