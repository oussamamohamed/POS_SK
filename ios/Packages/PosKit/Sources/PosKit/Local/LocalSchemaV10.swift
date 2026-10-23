import Foundation

extension LocalMigrator {
    /// File d'impression : `PrintJobs` identique à l'entité `PrintJob` du .NET. Colonnes propres à l'iPad sur `PrinterConfigurations` :
    /// `TransportKind` (`lan`, `star`, `epson`) et `DeviceIdentifier`.
    static let schemaV10 = """
    CREATE TABLE PrintJobs (
        Id TEXT NOT NULL PRIMARY KEY,
        PrinterId TEXT NOT NULL,
        Kind INTEGER NOT NULL,
        DocumentJson TEXT NOT NULL,
        OpenCashDrawer INTEGER NOT NULL,
        Status INTEGER NOT NULL,
        Attempts INTEGER NOT NULL,
        NextAttemptAtUtc TEXT NOT NULL,
        DeadlineAtUtc TEXT NOT NULL,
        LastError TEXT,
        CreatedAtUtc TEXT NOT NULL,
        SentAtUtc TEXT,
        DuplicateOfDocumentId TEXT,
        DuplicateNumber INTEGER
    );
    CREATE INDEX IX_PrintJobs_PrinterId_Status ON PrintJobs (PrinterId, Status);
    CREATE INDEX IX_PrintJobs_Status_CreatedAtUtc ON PrintJobs (Status, CreatedAtUtc);

    ALTER TABLE PrinterConfigurations ADD COLUMN TransportKind TEXT NOT NULL DEFAULT 'lan';
    ALTER TABLE PrinterConfigurations ADD COLUMN DeviceIdentifier TEXT NULL;
    """
}
