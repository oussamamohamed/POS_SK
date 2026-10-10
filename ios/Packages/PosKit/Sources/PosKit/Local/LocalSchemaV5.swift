import Foundation

extension LocalMigrator {
    /// Chaîne fiscale NF525 (remplace `NonFiscalPayments`) : mêmes tables et colonnes que `AppDbContext` (`FiscalReceipts`, `PaymentTenders`, `JournalEntries` = JET).
    /// Les triggers reprennent `FiscalImmutabilityInterceptor` (mêmes messages) : aucune ligne fiscale ne peut être modifiée ni supprimée.
    /// `IsVoid` est conservée pour la parité du schéma et n'est jamais écrite ; une annulation est un reçu lié par `VoidedReceiptId`.
    /// Les clôtures Z, de période et les archives viennent au sous-projet 3.
    static let schemaV5 = """
    DROP TABLE NonFiscalPayments;

    CREATE TABLE FiscalReceipts (
        Id TEXT NOT NULL PRIMARY KEY,
        TerminalId TEXT NOT NULL,
        ReceiptNumber TEXT NOT NULL,
        OrderId TEXT NOT NULL,
        SequenceNumber INTEGER NOT NULL,
        TotalTtcAmount INTEGER NOT NULL,
        TotalHtAmount INTEGER NOT NULL,
        TaxBreakdownJson TEXT NOT NULL,
        PreviousSignatureHash TEXT NOT NULL,
        SignatureHash TEXT NOT NULL,
        CreatedAtUtc TEXT NOT NULL,
        IsVoid INTEGER NOT NULL DEFAULT 0,
        VoidedReceiptId TEXT
    );
    CREATE UNIQUE INDEX IX_FiscalReceipts_TerminalId_SequenceNumber ON FiscalReceipts (TerminalId, SequenceNumber);
    CREATE INDEX IX_FiscalReceipts_VoidedReceiptId ON FiscalReceipts (VoidedReceiptId);
    CREATE INDEX IX_FiscalReceipts_OrderId ON FiscalReceipts (OrderId);

    CREATE TABLE PaymentTenders (
        Id TEXT NOT NULL PRIMARY KEY,
        FiscalReceiptId TEXT NOT NULL REFERENCES FiscalReceipts (Id),
        Method INTEGER NOT NULL,
        Amount INTEGER NOT NULL,
        Tendered INTEGER NOT NULL,
        ChangeGiven INTEGER NOT NULL
    );
    CREATE INDEX IX_PaymentTenders_FiscalReceiptId ON PaymentTenders (FiscalReceiptId);

    CREATE TABLE JournalEntries (
        Id TEXT NOT NULL PRIMARY KEY,
        LocalSequence INTEGER NOT NULL,
        TerminalId TEXT,
        OccurredAtUtc TEXT NOT NULL,
        IdempotencyKey TEXT NOT NULL,
        EventType TEXT NOT NULL,
        PayloadJson TEXT NOT NULL,
        EntryHash TEXT NOT NULL,
        ChainSequence INTEGER,
        PreviousHash TEXT,
        OperatorId TEXT
    );
    CREATE UNIQUE INDEX IX_JournalEntries_ChainSequence ON JournalEntries (ChainSequence) WHERE ChainSequence IS NOT NULL;

    CREATE TRIGGER TR_FiscalReceipts_NoUpdate BEFORE UPDATE ON FiscalReceipts
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal receipts are append-only and immutable.'); END;
    CREATE TRIGGER TR_FiscalReceipts_NoDelete BEFORE DELETE ON FiscalReceipts
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Fiscal receipts are append-only and immutable.'); END;
    CREATE TRIGGER TR_PaymentTenders_NoUpdate BEFORE UPDATE ON PaymentTenders
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Payment tenders are immutable.'); END;
    CREATE TRIGGER TR_PaymentTenders_NoDelete BEFORE DELETE ON PaymentTenders
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Payment tenders are immutable.'); END;
    CREATE TRIGGER TR_JournalEntries_NoUpdate BEFORE UPDATE ON JournalEntries WHEN OLD.ChainSequence IS NOT NULL
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Chained journal entries are immutable.'); END;
    CREATE TRIGGER TR_JournalEntries_NoDelete BEFORE DELETE ON JournalEntries WHEN OLD.ChainSequence IS NOT NULL
    BEGIN SELECT RAISE(ABORT, 'NF525 INV-3: Chained journal entries are immutable.'); END;
    """
}
