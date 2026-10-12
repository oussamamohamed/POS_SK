import Foundation

extension LocalMigrator {
    /// Clôtures journalières Z (NF525) : même table et colonnes que `DailyFiscalClosures` dans `AppDbContext`.
    /// Les triggers d'immuabilité garantissent l'inaltérabilité de la chaîne de clôture.
    static let schemaV7 = """
    CREATE TABLE DailyFiscalClosures (
        Id TEXT NOT NULL PRIMARY KEY,
        TerminalId TEXT NOT NULL,
        ClosureSequence INTEGER NOT NULL,
        PeriodStartUtc TEXT NOT NULL,
        PeriodEndUtc TEXT NOT NULL,
        TotalSalesTtc INTEGER NOT NULL,
        TotalSalesHt INTEGER NOT NULL,
        TaxesSummaryJson TEXT NOT NULL,
        TenderTotalsJson TEXT NOT NULL,
        PerpetualGrandTotalCents INTEGER NOT NULL,
        PreviousSignatureHash TEXT NOT NULL,
        SignatureHash TEXT NOT NULL,
        SealedByUserName TEXT NOT NULL,
        SealedByUserId TEXT NOT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE UNIQUE INDEX IX_DailyFiscalClosures_TerminalId_ClosureSequence ON DailyFiscalClosures (TerminalId, ClosureSequence);

    CREATE TRIGGER TR_DailyFiscalClosures_NoUpdate BEFORE UPDATE ON DailyFiscalClosures
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal closures are append-only and immutable.'); END;
    CREATE TRIGGER TR_DailyFiscalClosures_NoDelete BEFORE DELETE ON DailyFiscalClosures
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal closures are append-only and immutable.'); END;
    """
}
