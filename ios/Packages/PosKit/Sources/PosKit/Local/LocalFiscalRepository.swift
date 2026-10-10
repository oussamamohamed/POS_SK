import Foundation

struct FiscalTenderInput {
    let method: PaymentMethod
    let amount: Int
    let tendered: Int
    let change: Int
}

struct FiscalReceiptRecord: Equatable {
    let id: UUID
    let orderId: UUID
    let terminalId: String
    let receiptNumber: String
    let sequence: Int
    let ttcCents: Int
    let htCents: Int
    let taxBreakdownJson: String
    let signature: String
}

/// Écrit la chaîne fiscale NF525 (reçus par terminal, journal global). **Aucune méthode n'ouvre de transaction** : l'appelant (`LocalPosAPI`)
/// enveloppe la vente, le reçu et le journal dans un seul `db.transaction`, ce qui sérialise aussi la lecture de fin de chaîne.
struct LocalFiscalRepository {
    let db: SQLiteDatabase

    // MARK: Règlements

    /// Réglé sur la commande : somme nette des règlements (les annulations sont négatives).
    func paidCents(orderId: UUID) throws -> Int {
        try db.query(
            "SELECT COALESCE(SUM(t.Amount), 0) AS total FROM PaymentTenders t JOIN FiscalReceipts r ON r.Id = t.FiscalReceiptId WHERE r.OrderId = ?",
            [.uuid(orderId)]
        ).first?.int("total") ?? 0
    }

    /// TTC des reçus de la commande, hors annulations et hors reçus annulés (`priorFiscalTtcCents` du .NET).
    func priorFiscalTtcCents(orderId: UUID) throws -> Int {
        try db.query(
            """
            SELECT COALESCE(SUM(TotalTtcAmount), 0) AS total FROM FiscalReceipts
            WHERE OrderId = ? AND VoidedReceiptId IS NULL AND Id NOT IN (SELECT VoidedReceiptId FROM FiscalReceipts WHERE VoidedReceiptId IS NOT NULL)
            """,
            [.uuid(orderId)]
        ).first?.int("total") ?? 0
    }

    // MARK: Reçus

    @discardableResult
    func appendReceipt(orderId: UUID, terminalId: String, amounts: FiscalReceiptAmounts, tenders: [FiscalTenderInput], now: Date) throws -> FiscalReceiptRecord {
        try insertReceipt(orderId: orderId, terminalId: terminalId, amounts: amounts, tenders: tenders, voidedReceiptId: nil, now: now)
    }

    /// Contre-passation. Renvoie `nil` sans rien écrire si le reçu est inconnu, d'un autre terminal, lui-même une annulation ou déjà annulé.
    func voidReceipt(originalId: UUID, terminalId: String, operatorId: UUID, now: Date) throws -> FiscalReceiptRecord? {
        guard let original = try receipt(id: originalId), original.terminalId == terminalId,
              try db.query("SELECT VoidedReceiptId FROM FiscalReceipts WHERE Id = ?", [.uuid(originalId)]).first?.uuid("VoidedReceiptId") == nil,
              try db.query("SELECT 1 AS x FROM FiscalReceipts WHERE VoidedReceiptId = ?", [.uuid(originalId)]).isEmpty
        else { return nil }
        let tenders = try db.query("SELECT * FROM PaymentTenders WHERE FiscalReceiptId = ? ORDER BY rowid", [.uuid(originalId)]).map {
            FiscalTenderInput(
                method: PaymentMethod(rawValue: $0.int("Method") ?? 0) ?? .cash,
                amount: -($0.int("Amount") ?? 0), tendered: -($0.int("Tendered") ?? 0), change: -($0.int("ChangeGiven") ?? 0)
            )
        }
        let amounts = FiscalReceiptAmounts(
            ttcCents: -original.ttcCents, htCents: -original.htCents, taxBreakdownJson: FiscalReceiptMath.negatedTaxJson(original.taxBreakdownJson)
        )
        let voided = try insertReceipt(orderId: original.orderId, terminalId: terminalId, amounts: amounts, tenders: tenders, voidedReceiptId: originalId, now: now)
        try appendJournal(
            eventType: LocalJournalEvents.receiptVoided,
            payloadJson: FiscalJSON.object([
                ("OriginalReceiptId", FiscalJSON.guid(original.id)),
                ("OriginalReceiptNumber", FiscalJSON.string(original.receiptNumber)),
                ("VoidReceiptId", FiscalJSON.guid(voided.id)),
                ("VoidReceiptNumber", FiscalJSON.string(voided.receiptNumber)),
                ("TotalTtcCents", "\(original.ttcCents)"),
            ]),
            terminalId: terminalId, operatorId: operatorId, now: now
        )
        return voided
    }

    func receipt(id: UUID) throws -> FiscalReceiptRecord? {
        try db.query("SELECT * FROM FiscalReceipts WHERE Id = ?", [.uuid(id)]).first.flatMap(record)
    }

    /// Identifiant (GUID) ou numéro de reçu (`T01-000001`), comme `POST /api/checkout/receipts/{receiptId}/reprint`.
    func receipt(identifier: String) throws -> FiscalReceiptRecord? {
        if let id = UUID(uuidString: identifier), let found = try receipt(id: id) { return found }
        return try db.query("SELECT * FROM FiscalReceipts WHERE ReceiptNumber = ?", [.text(identifier)]).first.flatMap(record)
    }

    private func insertReceipt(orderId: UUID, terminalId: String, amounts: FiscalReceiptAmounts, tenders: [FiscalTenderInput], voidedReceiptId: UUID?, now: Date) throws -> FiscalReceiptRecord {
        let tail = try db.query(
            "SELECT SequenceNumber, SignatureHash FROM FiscalReceipts WHERE TerminalId = ? ORDER BY SequenceNumber DESC LIMIT 1", [.text(terminalId)]
        ).first
        let sequence = (tail?.int("SequenceNumber") ?? 0) + 1
        let previousHash = tail?.string("SignatureHash") ?? FiscalHashing.genesisHash
        let timestamp = FiscalHashing.roundedToMilliseconds(now)
        let signature = FiscalHashing.receiptHash(
            previousHash: previousHash, terminalId: terminalId, sequence: sequence, amountCents: amounts.ttcCents,
            timestamp: FiscalHashing.timestampText(timestamp), taxBreakdownJson: amounts.taxBreakdownJson
        )
        let number = (voidedReceiptId == nil ? "\(terminalId)-" : "\(terminalId)-VOID-") + String(format: "%06d", sequence)
        let id = UUID()
        try db.run(
            """
            INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid, VoidedReceiptId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?)
            """,
            [.uuid(id), .text(terminalId), .text(number), .uuid(orderId), .integer(sequence), .integer(amounts.ttcCents), .integer(amounts.htCents),
             .text(amounts.taxBreakdownJson), .text(previousHash), .text(signature), .text(FiscalHashing.storedText(timestamp)), .uuid(voidedReceiptId)]
        )
        for tender in tenders {
            try db.run(
                "INSERT INTO PaymentTenders (Id, FiscalReceiptId, Method, Amount, Tendered, ChangeGiven) VALUES (?, ?, ?, ?, ?, ?)",
                [.uuid(UUID()), .uuid(id), .integer(tender.method.rawValue), .integer(tender.amount), .integer(tender.tendered), .integer(tender.change)]
            )
        }
        return FiscalReceiptRecord(
            id: id, orderId: orderId, terminalId: terminalId, receiptNumber: number, sequence: sequence, ttcCents: amounts.ttcCents,
            htCents: amounts.htCents, taxBreakdownJson: amounts.taxBreakdownJson, signature: signature
        )
    }

    private func record(_ row: SQLRow) -> FiscalReceiptRecord? {
        guard let id = row.uuid("Id"), let orderId = row.uuid("OrderId") else { return nil }
        return FiscalReceiptRecord(
            id: id, orderId: orderId, terminalId: row.string("TerminalId") ?? "", receiptNumber: row.string("ReceiptNumber") ?? "",
            sequence: row.int("SequenceNumber") ?? 0, ttcCents: row.int("TotalTtcAmount") ?? 0, htCents: row.int("TotalHtAmount") ?? 0,
            taxBreakdownJson: row.string("TaxBreakdownJson") ?? "{}", signature: row.string("SignatureHash") ?? ""
        )
    }

    // MARK: Journal technique (JET)

    func appendJournal(eventType: String, payloadJson: String, terminalId: String?, operatorId: UUID?, now: Date) throws {
        let tail = try db.query("SELECT ChainSequence, EntryHash FROM JournalEntries WHERE ChainSequence IS NOT NULL ORDER BY ChainSequence DESC LIMIT 1").first
        let sequence = (tail?.int("ChainSequence") ?? 0) + 1
        let previousHash = tail?.string("EntryHash") ?? FiscalHashing.genesisHash
        let timestamp = FiscalHashing.roundedToMilliseconds(now)
        let hash = FiscalHashing.jetHash(
            previousHash: previousHash, chainSequence: sequence, eventType: eventType, timestamp: FiscalHashing.timestampText(timestamp),
            terminalId: terminalId, operatorId: operatorId, payloadJson: payloadJson
        )
        try db.run(
            """
            INSERT INTO JournalEntries (Id, LocalSequence, TerminalId, OccurredAtUtc, IdempotencyKey, EventType, PayloadJson, EntryHash, ChainSequence, PreviousHash, OperatorId)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [.uuid(UUID()), .integer(sequence), .string(terminalId), .text(FiscalHashing.storedText(timestamp)), .text(UUID().uuidString.lowercased()), .text(eventType),
             .text(payloadJson), .text(hash), .integer(sequence), .text(previousHash), .uuid(operatorId)]
        )
    }

    func lastJournalDate(eventType: String) throws -> Date? {
        try db.query("SELECT OccurredAtUtc FROM JournalEntries WHERE EventType = ? ORDER BY ChainSequence DESC LIMIT 1", [.text(eventType)]).first?.date("OccurredAtUtc")
    }

    // MARK: Duplicata

    /// Prochain n° de duplicata du document : le JET fait foi (`DuplicateNumbering.NextAsync` du .NET).
    func nextDuplicateNumber(documentId: UUID) throws -> Int {
        let needle = "%" + FiscalHashing.guidText(documentId) + "%"
        let count = try db.query(
            "SELECT COUNT(*) AS n FROM JournalEntries WHERE EventType = ? AND PayloadJson LIKE ?", [.text(LocalJournalEvents.duplicatePrinted), .text(needle)]
        ).first?.int("n") ?? 0
        return count + 1
    }

    /// Consigne un duplicata de reçu (sans impression : `printQueued` faux) et renvoie son numéro.
    @discardableResult
    func recordDuplicate(of receipt: FiscalReceiptRecord, operatorId: UUID?, now: Date) throws -> Int {
        let number = try nextDuplicateNumber(documentId: receipt.id)
        try appendJournal(
            eventType: LocalJournalEvents.duplicatePrinted,
            payloadJson: FiscalJSON.object([
                ("documentId", FiscalJSON.guid(receipt.id)),
                ("documentType", FiscalJSON.string("receipt")),
                ("receiptNumber", FiscalJSON.string(receipt.receiptNumber)),
                ("duplicateNumber", "\(number)"),
                ("printQueued", "false"),
                ("terminalId", FiscalJSON.string(receipt.terminalId)),
            ]),
            terminalId: receipt.terminalId, operatorId: operatorId, now: now
        )
        return number
    }

    // MARK: Vérification

    /// Recalcule les chaînes (reçus par terminal, JET). Renvoie les anomalies (`sequence_gap`, `previous_hash_mismatch`, `signature_mismatch`).
    func verifyChains() throws -> [String] {
        var issues: [String] = []
        let terminals = try db.query("SELECT DISTINCT TerminalId FROM FiscalReceipts ORDER BY TerminalId").compactMap { $0.string("TerminalId") }
        for terminal in terminals {
            var previous = FiscalHashing.genesisHash
            var expected = 1
            for row in try db.query("SELECT * FROM FiscalReceipts WHERE TerminalId = ? ORDER BY SequenceNumber", [.text(terminal)]) {
                let sequence = row.int("SequenceNumber") ?? 0
                let label = "receipt \(terminal)#\(sequence)"
                if sequence != expected { issues.append("\(label): sequence_gap (attendu \(expected))"); expected = sequence }
                if row.string("PreviousSignatureHash") != previous { issues.append("\(label): previous_hash_mismatch") }
                let timestamp = row.date("CreatedAtUtc").map(FiscalHashing.timestampText) ?? ""
                let signature = FiscalHashing.receiptHash(
                    previousHash: row.string("PreviousSignatureHash") ?? "", terminalId: terminal, sequence: sequence,
                    amountCents: row.int("TotalTtcAmount") ?? 0, timestamp: timestamp, taxBreakdownJson: row.string("TaxBreakdownJson") ?? ""
                )
                if signature != row.string("SignatureHash") { issues.append("\(label): signature_mismatch") }
                previous = row.string("SignatureHash") ?? ""
                expected += 1
            }
        }
        var previous = FiscalHashing.genesisHash
        var expected = 1
        for row in try db.query("SELECT * FROM JournalEntries WHERE ChainSequence IS NOT NULL ORDER BY ChainSequence") {
            let sequence = row.int("ChainSequence") ?? 0
            let label = "jet#\(sequence)"
            if sequence != expected { issues.append("\(label): sequence_gap (attendu \(expected))"); expected = sequence }
            if row.string("PreviousHash") != previous { issues.append("\(label): previous_hash_mismatch") }
            let hash = FiscalHashing.jetHash(
                previousHash: row.string("PreviousHash") ?? "", chainSequence: sequence, eventType: row.string("EventType") ?? "",
                timestamp: row.date("OccurredAtUtc").map(FiscalHashing.timestampText) ?? "", terminalId: row.string("TerminalId"),
                operatorId: row.uuid("OperatorId"), payloadJson: row.string("PayloadJson") ?? ""
            )
            if hash != row.string("EntryHash") { issues.append("\(label): signature_mismatch") }
            previous = row.string("EntryHash") ?? ""
            expected += 1
        }
        return issues
    }
}
