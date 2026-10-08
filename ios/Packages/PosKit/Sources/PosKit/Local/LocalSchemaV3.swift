import Foundation

extension LocalMigrator {
    /// Mise en attente, avoirs, chambres d'hôtel (mêmes tables et colonnes que `AppDbContext`) et deux tables propres à l'iPad :
    /// `NonFiscalPayments` (règlements provisoires, remplacés par `FiscalReceipts`/`PaymentTenders` au sous-projet 2) et
    /// `LocalCounters` (numéros de reçu et de retrait, qui sont en mémoire côté .NET).
    static let schemaV3 = """
    CREATE TABLE HeldOrders (
        Id TEXT NOT NULL PRIMARY KEY,
        TerminalId TEXT NOT NULL,
        OrderId TEXT NOT NULL,
        CustomerLabel TEXT,
        Destination INTEGER NOT NULL,
        ItemCount INTEGER NOT NULL,
        TotalTtc INTEGER NOT NULL,
        OrderSnapshotJson TEXT NOT NULL,
        HeldAtUtc TEXT NOT NULL,
        HeldByStaffId TEXT NOT NULL,
        IsRecalled INTEGER NOT NULL,
        RecalledAtUtc TEXT,
        IsVoided INTEGER NOT NULL,
        VoidedAtUtc TEXT,
        VoidReason TEXT,
        VoidedByStaffId TEXT
    );
    CREATE INDEX IX_HeldOrders_TerminalId_IsRecalled_IsVoided ON HeldOrders (TerminalId, IsRecalled, IsVoided);

    CREATE TABLE CustomerCreditVouchers (
        Id TEXT NOT NULL PRIMARY KEY,
        VoucherCode TEXT NOT NULL,
        OriginalOrderId TEXT NOT NULL,
        TerminalId TEXT NOT NULL,
        Amount INTEGER NOT NULL,
        IssuedAtUtc TEXT NOT NULL,
        ExpiresAtUtc TEXT NOT NULL,
        IsRedeemed INTEGER NOT NULL,
        RedeemedAtUtc TEXT,
        RedeemedOrderId TEXT
    );
    CREATE UNIQUE INDEX IX_CustomerCreditVouchers_VoucherCode ON CustomerCreditVouchers (VoucherCode);

    CREATE TABLE HotelRooms (
        Id TEXT NOT NULL PRIMARY KEY,
        RoomNumber TEXT NOT NULL,
        GuestName TEXT NOT NULL,
        CheckInDateUtc TEXT NOT NULL,
        CheckOutDateUtc TEXT NOT NULL,
        IsOccupied INTEGER NOT NULL,
        MaxCreditLimit TEXT NOT NULL,
        CurrentBalance TEXT NOT NULL
    );
    CREATE INDEX IX_HotelRooms_RoomNumber ON HotelRooms (RoomNumber);

    CREATE TABLE RoomFolioCharges (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL,
        RoomNumber TEXT NOT NULL,
        GuestName TEXT NOT NULL,
        Amount INTEGER NOT NULL,
        TipAmount INTEGER NOT NULL,
        SignatureDataUrl TEXT,
        Notes TEXT,
        ChargedAtUtc TEXT NOT NULL
    );

    CREATE TABLE NonFiscalPayments (
        Id TEXT NOT NULL PRIMARY KEY,
        OrderId TEXT NOT NULL REFERENCES Orders (Id),
        TerminalId TEXT NOT NULL,
        ReceiptNumber TEXT NOT NULL,
        Method INTEGER NOT NULL,
        Amount INTEGER NOT NULL,
        Tendered INTEGER NOT NULL,
        ChangeGiven INTEGER NOT NULL,
        CreatedAtUtc TEXT NOT NULL
    );
    CREATE INDEX IX_NonFiscalPayments_OrderId ON NonFiscalPayments (OrderId);

    CREATE TABLE LocalCounters (
        Name TEXT NOT NULL PRIMARY KEY,
        Day TEXT NOT NULL,
        LastSequence INTEGER NOT NULL
    );
    """
}
