import Foundation

/// Valeurs entières identiques à `PrintJobKind` du .NET.
enum PrintJobKind: Int, Sendable { case pickupVoucher = 0, receipt, kitchenTicket, report }

/// Valeurs entières identiques à `PrintJobStatus` du .NET.
enum PrintJobStatus: Int, Sendable { case pending = 0, sent, failed, cancelled }

/// Ligne de `PrintJobs` : un document en attente d'impression, rejoué jusqu'à `deadlineAtUtc`.
struct PrintJobRecord: Equatable, Sendable {
    let id: UUID
    let printerId: UUID
    let kind: PrintJobKind
    let documentJson: String
    let openCashDrawer: Bool
    var status: PrintJobStatus
    var attempts: Int
    var nextAttemptAtUtc: Date
    var deadlineAtUtc: Date
    var lastError: String?
    let createdAtUtc: Date
    var sentAtUtc: Date?
    let duplicateOfDocumentId: UUID?
    let duplicateNumber: Int?
}
