import Foundation
@testable import PosKit

/// `archive_reference.json` (généré par le .NET, Task 1) : données d'entrée et fichiers d'archive attendus.
struct ArchiveReference: Decodable {
    struct Tender: Decodable {
        var id: UUID
        var method: Int
        var amount: Int
        var tendered: Int
        var changeGiven: Int
    }

    struct Receipt: Decodable {
        var id: UUID
        var terminalId: String
        var receiptNumber: String
        var orderId: UUID
        var sequenceNumber: Int
        var ttcCents: Int
        var htCents: Int
        var taxBreakdownJson: String
        var htBreakdownJson: String?
        var previousSignatureHash: String
        var signatureHash: String
        var createdAtUtc: String
        var voidedReceiptId: UUID?
        var tenders: [Tender]
    }

    struct Daily: Decodable {
        var id: UUID
        var terminalId: String
        var closureSequence: Int
        var periodStartUtc: String
        var periodEndUtc: String
        var ttcCents: Int
        var htCents: Int
        var taxesSummaryJson: String
        var tenderTotalsJson: String
        var perpetualGrandTotalCents: Int
        var previousSignatureHash: String
        var signatureHash: String
        var sealedByUserName: String
        var sealedByUserId: UUID
    }

    struct Period: Decodable {
        var id: UUID
        var terminalId: String
        var periodType: Int
        var periodKey: String
        var closureSequence: Int
        var periodStartUtc: String
        var periodEndUtc: String
        var ttcCents: Int
        var htCents: Int
        var taxesSummaryJson: String
        var tenderTotalsJson: String
        var perpetualGrandTotalCents: Int
        var dailyClosureCount: Int
        var previousSignatureHash: String
        var signatureHash: String
        var sealedByUserId: UUID
        var sealedByUserName: String
        var createdAtUtc: String
    }

    struct Journal: Decodable {
        var id: UUID
        var localSequence: Int
        var terminalId: String?
        var occurredAtUtc: String
        var idempotencyKey: String
        var eventType: String
        var payloadJson: String
        var entryHash: String
        var chainSequence: Int
        var previousHash: String?
        var operatorId: UUID?
    }

    struct Files: Decodable {
        var receipts: String
        var closures: String
        var periodClosures: String
        var journal: String
        var manifest: String?
    }

    struct Archive: Decodable {
        var periodClosureId: UUID
        var fileName: String
        var files: Files
        var sha256: Files
    }

    var receipts: [Receipt]
    var dailyClosures: [Daily]
    var periodClosures: [Period]
    var journalEntries: [Journal]
    var archives: [Archive]

    static func load() throws -> ArchiveReference {
        try JSONDecoder().decode(ArchiveReference.self, from: ContractDecodingTests.fixture("archive_reference"))
    }

    /// Insère toutes les lignes d'entrée telles que le .NET les a reçues (dates déjà au format `storedText`).
    func seed(_ db: SQLiteDatabase) throws {
        for r in receipts {
            try db.run(
                """
                INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                    HtBreakdownJson, PreviousSignatureHash, SignatureHash, CreatedAtUtc, VoidedReceiptId)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(r.id), .text(r.terminalId), .text(r.receiptNumber), .uuid(r.orderId), .integer(r.sequenceNumber), .integer(r.ttcCents),
                 .integer(r.htCents), .text(r.taxBreakdownJson), .string(r.htBreakdownJson), .text(r.previousSignatureHash), .text(r.signatureHash),
                 .text(r.createdAtUtc), .uuid(r.voidedReceiptId)]
            )
            for t in r.tenders {
                try db.run(
                    "INSERT INTO PaymentTenders (Id, FiscalReceiptId, Method, Amount, Tendered, ChangeGiven) VALUES (?, ?, ?, ?, ?, ?)",
                    [.uuid(t.id), .uuid(r.id), .integer(t.method), .integer(t.amount), .integer(t.tendered), .integer(t.changeGiven)]
                )
            }
        }
        for d in dailyClosures {
            try db.run(
                """
                INSERT INTO DailyFiscalClosures (Id, TerminalId, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalSalesTtc, TotalSalesHt,
                    TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, PreviousSignatureHash, SignatureHash, SealedByUserName, SealedByUserId, CreatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(d.id), .text(d.terminalId), .integer(d.closureSequence), .text(d.periodStartUtc), .text(d.periodEndUtc), .integer(d.ttcCents),
                 .integer(d.htCents), .text(d.taxesSummaryJson), .text(d.tenderTotalsJson), .integer(d.perpetualGrandTotalCents),
                 .text(d.previousSignatureHash), .text(d.signatureHash), .text(d.sealedByUserName), .uuid(d.sealedByUserId), .text(d.periodEndUtc)]
            )
        }
        for p in periodClosures {
            try db.run(
                """
                INSERT INTO PeriodClosures (Id, TerminalId, PeriodType, PeriodKey, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalTtcAmount, TotalHtAmount,
                    TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, DailyClosureCount, PreviousSignatureHash, SignatureHash, SealedByUserId,
                    SealedByUserName, CreatedAtUtc)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(p.id), .text(p.terminalId), .integer(p.periodType), .text(p.periodKey), .integer(p.closureSequence), .text(p.periodStartUtc),
                 .text(p.periodEndUtc), .integer(p.ttcCents), .integer(p.htCents), .text(p.taxesSummaryJson), .text(p.tenderTotalsJson),
                 .integer(p.perpetualGrandTotalCents), .integer(p.dailyClosureCount), .text(p.previousSignatureHash), .text(p.signatureHash),
                 .uuid(p.sealedByUserId), .text(p.sealedByUserName), .text(p.createdAtUtc)]
            )
        }
        for j in journalEntries {
            try db.run(
                """
                INSERT INTO JournalEntries (Id, LocalSequence, TerminalId, OccurredAtUtc, IdempotencyKey, EventType, PayloadJson, EntryHash, ChainSequence,
                    PreviousHash, OperatorId)
                VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
                """,
                [.uuid(j.id), .integer(j.localSequence), .string(j.terminalId), .text(j.occurredAtUtc), .text(j.idempotencyKey), .text(j.eventType),
                 .text(j.payloadJson), .text(j.entryHash), .integer(j.chainSequence), .string(j.previousHash), .uuid(j.operatorId)]
            )
        }
    }
}

/// Masque les deux valeurs propres à chaque archive (identifiant, horodatage), comme le générateur .NET.
func maskedManifest(_ text: String) -> String {
    text.replacingOccurrences(of: "\"(archiveId|createdAtUtc)\": \"[^\"]*\"", with: "\"$1\": \"*\"", options: .regularExpression)
}
