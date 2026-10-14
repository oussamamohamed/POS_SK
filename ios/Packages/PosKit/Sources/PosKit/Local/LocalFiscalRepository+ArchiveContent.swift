import Foundation

/// Les cinq fichiers d'une archive fiscale, en UTF-8 sans BOM.
struct ArchiveContent {
    let receipts: Data
    let closures: Data
    let periodClosures: Data
    let journal: Data
    let manifest: Data

    /// Entrées du ZIP, dans l'ordre du .NET.
    var entries: [(name: String, data: Data)] {
        [("receipts.json", receipts), ("closures.json", closures), ("period-closures.json", periodClosures),
         ("journal.json", journal), ("manifest.json", manifest)]
    }
}

extension LocalFiscalRepository {
    /// Contenu de l'archive d'une clôture de période (`FiscalArchiveService.CreateArchiveAsync`) : reçus du poste créés dans `[début ; fin]`
    /// (bornes incluses) avec leurs règlements, Z dont la période commence dans `[début ; fin]`, la clôture elle-même (plus ses mensuelles
    /// pour une annuelle), entrées du JET de **tous** les postes dans `[début ; fin]`. Propriétés et ordre des entités .NET.
    /// ponytail: tout en mémoire (quelques milliers de reçus par mois) ; passer à une écriture en flux au-delà.
    func archiveContent(for closure: PeriodClosureRecord, archiveId: UUID, createdAt: Date) throws -> ArchiveContent {
        let start = SQLValue.text(FiscalHashing.storedText(closure.periodStartUtc))
        let end = SQLValue.text(FiscalHashing.storedText(closure.periodEndUtc))
        let terminal = SQLValue.text(closure.terminalId)

        let receiptRows = try db.query(
            "SELECT * FROM FiscalReceipts WHERE TerminalId = ? AND CreatedAtUtc >= ? AND CreatedAtUtc <= ? ORDER BY SequenceNumber",
            [terminal, start, end]
        )
        let receipts = try receiptRows.map { (row: SQLRow) throws -> IndentedJSON in
            // Ordre d'écriture des règlements (le .NET ne trie pas l'Include).
            let tenders = try db.query("SELECT * FROM PaymentTenders WHERE FiscalReceiptId = ? ORDER BY rowid", [row["Id"]])
            return Self.receiptJSON(row, tenders: tenders.map(Self.tenderJSON))
        }
        let closures = try db.query(
            "SELECT * FROM DailyFiscalClosures WHERE TerminalId = ? AND PeriodStartUtc >= ? AND PeriodStartUtc <= ? ORDER BY ClosureSequence",
            [terminal, start, end]
        ).map(Self.dailyClosureJSON)
        let periods = try db.query(
            """
            SELECT * FROM PeriodClosures WHERE TerminalId = ?
              AND (Id = ? OR (? = 2 AND PeriodType = 1 AND PeriodStartUtc >= ? AND PeriodEndUtc <= ?))
            ORDER BY PeriodType, ClosureSequence
            """,
            [terminal, .uuid(closure.id), .integer(closure.periodType.storedValue), start, end]
        ).map(Self.periodClosureJSON)
        let journal = try db.query(
            "SELECT * FROM JournalEntries WHERE OccurredAtUtc >= ? AND OccurredAtUtc <= ? ORDER BY COALESCE(ChainSequence, 0), Id",
            [start, end]
        ).map(Self.journalJSON)

        let receiptsData = Data(IndentedJSON.array(receipts).text.utf8)
        let closuresData = Data(IndentedJSON.array(closures).text.utf8)
        let periodsData = Data(IndentedJSON.array(periods).text.utf8)
        let journalData = Data(IndentedJSON.array(journal).text.utf8)

        let counts: [(String, IndentedJSON)] = [
            ("receipts", .int(receipts.count)), ("closures", .int(closures.count)),
            ("periodClosures", .int(periods.count)), ("journalEntries", .int(journal.count)),
        ]
        let files: [(String, IndentedJSON)] = [
            ("receipts.json", .string(FiscalHashing.sha256Hex(receiptsData))),
            ("closures.json", .string(FiscalHashing.sha256Hex(closuresData))),
            ("period-closures.json", .string(FiscalHashing.sha256Hex(periodsData))),
            ("journal.json", .string(FiscalHashing.sha256Hex(journalData))),
        ]
        let manifest: [(String, IndentedJSON)] = [
            ("archiveId", .guid(archiveId)),
            ("periodClosureId", .guid(closure.id)),
            ("terminalId", .string(closure.terminalId)),
            ("periodType", .int(closure.periodType.storedValue)),
            ("periodKey", .string(closure.periodKey)),
            ("periodStartUtc", .date(closure.periodStartUtc)),
            ("periodEndUtc", .date(closure.periodEndUtc)),
            ("createdAtUtc", .date(createdAt)),
            ("counts", .object(counts)),
            ("files", .object(files)),
        ]
        return ArchiveContent(
            receipts: receiptsData, closures: closuresData, periodClosures: periodsData, journal: journalData,
            manifest: Data(IndentedJSON.object(manifest).text.utf8)
        )
    }

    /// `archive-{poste}-{M|A}-{clé}.zip`, caractères hors `[A-Za-z0-9_.-]` remplacés par `_` (`SafeFileNamePart` du .NET, unité UTF-16).
    static func archiveFileName(_ closure: PeriodClosureRecord) -> String {
        func safe(_ part: String) -> String {
            part.replacingOccurrences(of: "[^A-Za-z0-9_.-]", with: "_", options: .regularExpression)
        }
        return "archive-\(safe(closure.terminalId))-\(closure.periodType == .monthly ? "M" : "A")-\(safe(closure.periodKey)).zip"
    }

    // MARK: Entités .NET (ordre de déclaration des propriétés)

    /// `FiscalReceipt` (avec `IsVoid`, colonne historique toujours sérialisée par le .NET).
    private static func receiptJSON(_ row: SQLRow, tenders: [IndentedJSON]) -> IndentedJSON {
        let fields: [(String, IndentedJSON)] = [
            ("Id", .guid(row.uuid("Id"))),
            ("TerminalId", .string(row.string("TerminalId"))),
            ("ReceiptNumber", .string(row.string("ReceiptNumber"))),
            ("OrderId", .guid(row.uuid("OrderId"))),
            ("SequenceNumber", .int(row.int("SequenceNumber"))),
            ("TotalTtcAmount", .money(row.int("TotalTtcAmount"))),
            ("TotalHtAmount", .money(row.int("TotalHtAmount"))),
            ("TaxBreakdownJson", .string(row.string("TaxBreakdownJson"))),
            ("HtBreakdownJson", .string(row.string("HtBreakdownJson"))),
            ("PreviousSignatureHash", .string(row.string("PreviousSignatureHash"))),
            ("SignatureHash", .string(row.string("SignatureHash"))),
            ("CreatedAtUtc", .date(row.date("CreatedAtUtc"))),
            ("IsVoid", .bool(row.bool("IsVoid"))),
            ("VoidedReceiptId", .guid(row.uuid("VoidedReceiptId"))),
            ("Tenders", .array(tenders)),
        ]
        return .object(fields)
    }

    /// `PaymentTender`.
    private static func tenderJSON(_ row: SQLRow) -> IndentedJSON {
        let fields: [(String, IndentedJSON)] = [
            ("Id", .guid(row.uuid("Id"))),
            ("FiscalReceiptId", .guid(row.uuid("FiscalReceiptId"))),
            ("Method", .int(row.int("Method"))),
            ("Amount", .money(row.int("Amount"))),
            ("Tendered", .money(row.int("Tendered"))),
            ("ChangeGiven", .money(row.int("ChangeGiven"))),
        ]
        return .object(fields)
    }

    /// `DailyFiscalClosure` (sans `CreatedAtUtc`, colonne propre à l'iPad que l'entité .NET n'a pas).
    private static func dailyClosureJSON(_ row: SQLRow) -> IndentedJSON {
        let fields: [(String, IndentedJSON)] = [
            ("Id", .guid(row.uuid("Id"))),
            ("TerminalId", .string(row.string("TerminalId"))),
            ("ClosureSequence", .int(row.int("ClosureSequence"))),
            ("PeriodStartUtc", .date(row.date("PeriodStartUtc"))),
            ("PeriodEndUtc", .date(row.date("PeriodEndUtc"))),
            ("TotalSalesTtc", .money(row.int("TotalSalesTtc"))),
            ("TotalSalesHt", .money(row.int("TotalSalesHt"))),
            ("TaxesSummaryJson", .string(row.string("TaxesSummaryJson"))),
            ("TenderTotalsJson", .string(row.string("TenderTotalsJson"))),
            ("PerpetualGrandTotalCents", .int(row.int("PerpetualGrandTotalCents"))),
            ("PreviousSignatureHash", .string(row.string("PreviousSignatureHash"))),
            ("SignatureHash", .string(row.string("SignatureHash"))),
            ("SealedByUserName", .string(row.string("SealedByUserName"))),
            ("SealedByUserId", .guid(row.uuid("SealedByUserId"))),
        ]
        return .object(fields)
    }

    /// `FiscalPeriodClosure`.
    private static func periodClosureJSON(_ row: SQLRow) -> IndentedJSON {
        let fields: [(String, IndentedJSON)] = [
            ("Id", .guid(row.uuid("Id"))),
            ("TerminalId", .string(row.string("TerminalId"))),
            ("PeriodType", .int(row.int("PeriodType"))),
            ("PeriodKey", .string(row.string("PeriodKey"))),
            ("ClosureSequence", .int(row.int("ClosureSequence"))),
            ("PeriodStartUtc", .date(row.date("PeriodStartUtc"))),
            ("PeriodEndUtc", .date(row.date("PeriodEndUtc"))),
            ("TotalTtcAmount", .money(row.int("TotalTtcAmount"))),
            ("TotalHtAmount", .money(row.int("TotalHtAmount"))),
            ("TaxesSummaryJson", .string(row.string("TaxesSummaryJson"))),
            ("TenderTotalsJson", .string(row.string("TenderTotalsJson"))),
            ("PerpetualGrandTotalCents", .int(row.int("PerpetualGrandTotalCents"))),
            ("DailyClosureCount", .int(row.int("DailyClosureCount"))),
            ("PreviousSignatureHash", .string(row.string("PreviousSignatureHash"))),
            ("SignatureHash", .string(row.string("SignatureHash"))),
            ("SealedByUserId", .guid(row.uuid("SealedByUserId"))),
            ("SealedByUserName", .string(row.string("SealedByUserName"))),
            ("CreatedAtUtc", .date(row.date("CreatedAtUtc"))),
        ]
        return .object(fields)
    }

    /// `TransactionJournalEntry`.
    private static func journalJSON(_ row: SQLRow) -> IndentedJSON {
        let fields: [(String, IndentedJSON)] = [
            ("Id", .guid(row.uuid("Id"))),
            ("LocalSequence", .int(row.int("LocalSequence"))),
            ("TerminalId", .string(row.string("TerminalId"))),
            ("OccurredAtUtc", .date(row.date("OccurredAtUtc"))),
            ("IdempotencyKey", .string(row.string("IdempotencyKey"))),
            ("EventType", .string(row.string("EventType"))),
            ("PayloadJson", .string(row.string("PayloadJson"))),
            ("EntryHash", .string(row.string("EntryHash"))),
            ("ChainSequence", .int(row.int("ChainSequence"))),
            ("PreviousHash", .string(row.string("PreviousHash"))),
            ("OperatorId", .guid(row.uuid("OperatorId"))),
        ]
        return .object(fields)
    }
}
