import Foundation

/// Types d'événements du JET : mêmes valeurs que `JournalEventTypes.cs`. `BACKUP_*` n'existent que sur l'iPad autonome.
enum LocalJournalEvents {
    static let receiptVoided = "RECEIPT_VOIDED"
    static let duplicatePrinted = "DUPLICATE_PRINTED"
    static let heldOrderVoided = "EVENT_HELD_ORDER_VOIDED"
    static let happyHourActivated = "EVENT_HAPPY_HOUR_OVERRIDE_ACTIVATED"
    static let happyHourStopped = "EVENT_HAPPY_HOUR_OVERRIDE_STOPPED"
    static let zClosure = "Z_CLOSURE"
    static let backupCompleted = "BACKUP_COMPLETED"
    static let backupFailed = "BACKUP_FAILED"
    static let periodClosure = "PERIOD_CLOSURE"
    static let fecExported = "FEC_EXPORTED"
    static let archiveCreated = "ARCHIVE_CREATED"
    static let archiveExported = "ARCHIVE_EXPORTED"
}
