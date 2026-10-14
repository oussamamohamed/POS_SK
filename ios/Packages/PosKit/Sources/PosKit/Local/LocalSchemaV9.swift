import Foundation

extension LocalMigrator {
    /// Archives fiscales NF525 : même table et mêmes colonnes que `FiscalArchives` dans `Program.cs` / `AppDbContext`. Chaîne globale (pas de poste).
    static let schemaV9 = """
    CREATE TABLE FiscalArchives (
        Id TEXT NOT NULL PRIMARY KEY,
        PeriodClosureId TEXT NOT NULL,
        PeriodType INTEGER NOT NULL,
        PeriodKey TEXT NOT NULL,
        FileName TEXT NOT NULL,
        FileSha256 TEXT NOT NULL,
        FileSizeBytes INTEGER NOT NULL,
        ArchiveSequence INTEGER NOT NULL,
        PreviousSignatureHash TEXT NOT NULL,
        SignatureHash TEXT NOT NULL,
        CreatedByUserId TEXT NOT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE UNIQUE INDEX IX_FiscalArchives_PeriodClosureId ON FiscalArchives (PeriodClosureId);
    CREATE UNIQUE INDEX IX_FiscalArchives_ArchiveSequence ON FiscalArchives (ArchiveSequence);

    CREATE TRIGGER TR_FiscalArchives_NoUpdate BEFORE UPDATE ON FiscalArchives
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal archives are append-only and immutable.'); END;
    CREATE TRIGGER TR_FiscalArchives_NoDelete BEFORE DELETE ON FiscalArchives
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal archives are append-only and immutable.'); END;
    """
}
