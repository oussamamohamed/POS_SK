namespace RestaurantPos.Domain.Entities;

public static class JournalEventTypes
{
    public const string ServerStarted = "SERVER_STARTED";
    public const string ServerStopped = "SERVER_STOPPED";
    public const string LoginSucceeded = "LOGIN_SUCCEEDED";
    public const string LoginFailed = "LOGIN_FAILED";
    public const string ChainBreakDetected = "CHAIN_BREAK_DETECTED";
    public const string PeriodClosure = "PERIOD_CLOSURE";
    public const string ZClosure = "Z_CLOSURE";
    public const string ArchiveCreated = "ARCHIVE_CREATED";
    public const string ArchiveExported = "ARCHIVE_EXPORTED";
    public const string FecExported = "FEC_EXPORTED";
    public const string TaxRateChanged = "TAX_RATE_CHANGED";
    public const string FiscalSettingsChanged = "FISCAL_SETTINGS_CHANGED";
    public const string ReceiptVoided = "RECEIPT_VOIDED";
    public const string DuplicatePrinted = "DUPLICATE_PRINTED";
    public const string DevicePaired = "DEVICE_PAIRED";
    public const string DeviceRevoked = "DEVICE_REVOKED";

    // Événements existants conservés
    public const string EventHeldOrderVoided = "EVENT_HELD_ORDER_VOIDED";
    public const string EventHappyHourActivated = "EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED";
    public const string EventHappyHourDeactivated = "EVENT_HAPPY_HOUR_OVERRIDE_STOPPED";
}
