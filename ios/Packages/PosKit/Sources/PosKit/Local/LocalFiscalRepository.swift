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
    let htBreakdownJson: String?
    let signature: String
}

struct DailyFiscalClosureRecord: Equatable {
    let id: UUID
    let terminalId: String
    let sequence: Int
    let periodStartUtc: Date
    let periodEndUtc: Date
    let totalSalesTtcCents: Int
    let totalSalesHtCents: Int
    let taxesSummaryJson: String
    let tenderTotalsJson: String
    let perpetualGrandTotalCents: Int
    let previousSignatureHash: String
    let signatureHash: String
    let sealedByUserName: String
    let sealedByUserId: UUID
    let createdAtUtc: Date
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
            ttcCents: -original.ttcCents, htCents: -original.htCents, taxBreakdownJson: FiscalReceiptMath.negatedTaxJson(original.taxBreakdownJson),
            htBreakdownJson: original.htBreakdownJson.map(FiscalReceiptMath.negatedTaxJson)
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

    /// Le reçu appartient-il à une période déjà clôturée (Z) de son poste ? Miroir de `IsInClosedPeriodAsync` : seule la Z du poste émetteur compte.
    func isInClosedPeriod(receiptId: UUID) throws -> Bool {
        try !db.query(
            """
            SELECT 1 AS x FROM FiscalReceipts r
            WHERE r.Id = ? AND EXISTS (SELECT 1 FROM DailyFiscalClosures c WHERE c.TerminalId = r.TerminalId AND r.CreatedAtUtc <= c.PeriodEndUtc)
            """, [.uuid(receiptId)]
        ).isEmpty
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
                PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid, VoidedReceiptId, HtBreakdownJson)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, 0, ?, ?)
            """,
            [.uuid(id), .text(terminalId), .text(number), .uuid(orderId), .integer(sequence), .integer(amounts.ttcCents), .integer(amounts.htCents),
             .text(amounts.taxBreakdownJson), .text(previousHash), .text(signature), .text(FiscalHashing.storedText(timestamp)), .uuid(voidedReceiptId),
             amounts.htBreakdownJson.map { .text($0) } ?? .null]
        )
        for tender in tenders {
            try db.run(
                "INSERT INTO PaymentTenders (Id, FiscalReceiptId, Method, Amount, Tendered, ChangeGiven) VALUES (?, ?, ?, ?, ?, ?)",
                [.uuid(UUID()), .uuid(id), .integer(tender.method.rawValue), .integer(tender.amount), .integer(tender.tendered), .integer(tender.change)]
            )
        }
        return FiscalReceiptRecord(
            id: id, orderId: orderId, terminalId: terminalId, receiptNumber: number, sequence: sequence, ttcCents: amounts.ttcCents,
            htCents: amounts.htCents, taxBreakdownJson: amounts.taxBreakdownJson, htBreakdownJson: amounts.htBreakdownJson, signature: signature
        )
    }

    private func record(_ row: SQLRow) -> FiscalReceiptRecord? {
        guard let id = row.uuid("Id"), let orderId = row.uuid("OrderId") else { return nil }
        return FiscalReceiptRecord(
            id: id, orderId: orderId, terminalId: row.string("TerminalId") ?? "", receiptNumber: row.string("ReceiptNumber") ?? "",
            sequence: row.int("SequenceNumber") ?? 0, ttcCents: row.int("TotalTtcAmount") ?? 0, htCents: row.int("TotalHtAmount") ?? 0,
            taxBreakdownJson: row.string("TaxBreakdownJson") ?? "{}", htBreakdownJson: row.string("HtBreakdownJson"),
            signature: row.string("SignatureHash") ?? ""
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

    /// Consigne un duplicata de clôture Z et renvoie son numéro.
    @discardableResult
    func recordClosureDuplicate(of closure: DailyFiscalClosureRecord, operatorId: UUID?, now: Date) throws -> Int {
        let number = try nextDuplicateNumber(documentId: closure.id)
        try appendJournal(
            eventType: LocalJournalEvents.duplicatePrinted,
            payloadJson: FiscalJSON.object([
                ("documentId", FiscalJSON.guid(closure.id)),
                ("documentType", FiscalJSON.string("z_closure")),
                ("closureSequence", "\(closure.sequence)"),
                ("duplicateNumber", "\(number)"),
                ("printQueued", "false"),
                ("terminalId", FiscalJSON.string(closure.terminalId)),
            ]),
            terminalId: closure.terminalId, operatorId: operatorId, now: now
        )
        return number
    }

    // MARK: Clôtures Z & Rapport X

    struct FiscalSummary {
        let periodStartUtc: Date
        let periodEndUtc: Date
        let totalSalesTtcCents: Int
        let totalSalesHtCents: Int
        let receiptCount: Int
        let vatBreakdown: [String: Money]
        let paymentTotals: [String: Money]
        let perpetualGrandTotalCents: Int
        let taxesSummaryJson: String
        let tenderTotalsJson: String
    }

    func latestClosure(terminalId: String) throws -> DailyFiscalClosureRecord? {
        try db.query(
            "SELECT * FROM DailyFiscalClosures WHERE TerminalId = ? ORDER BY ClosureSequence DESC LIMIT 1",
            [.text(terminalId)]
        ).first.flatMap(closureRecord)
    }

    func receiptCountForClosure(terminalId: String, sequence: Int, periodStartUtc: Date, periodEndUtc: Date) throws -> Int {
        if sequence == 1 {
            return try db.query(
                "SELECT COUNT(*) AS n FROM FiscalReceipts WHERE TerminalId = ? AND CreatedAtUtc <= ?",
                [.text(terminalId), .text(FiscalHashing.storedText(periodEndUtc))]
            ).first?.int("n") ?? 0
        } else {
            return try db.query(
                "SELECT COUNT(*) AS n FROM FiscalReceipts WHERE TerminalId = ? AND CreatedAtUtc > ? AND CreatedAtUtc <= ?",
                [.text(terminalId), .text(FiscalHashing.storedText(periodStartUtc)), .text(FiscalHashing.storedText(periodEndUtc))]
            ).first?.int("n") ?? 0
        }
    }

    func summary(terminalId: String, now: Date) throws -> FiscalSummary {
        let lastClosure = try latestClosure(terminalId: terminalId)
        let periodEnd = now
        // Début de période : fin du dernier Z de tout type (même vide), sinon premier reçu du poste, sinon maintenant (parité .NET).
        let periodStart: Date
        if let lastClosure {
            periodStart = lastClosure.periodEndUtc
        } else if let first = try db.query(
            "SELECT MIN(CreatedAtUtc) AS first FROM FiscalReceipts WHERE TerminalId = ?",
            [.text(terminalId)]
        ).first?.date("first") {
            periodStart = first
        } else {
            periodStart = periodEnd
        }

        let receiptsQuery: String
        let receiptsParams: [SQLValue]
        if lastClosure != nil {
            receiptsQuery = "SELECT * FROM FiscalReceipts WHERE TerminalId = ? AND CreatedAtUtc > ? ORDER BY SequenceNumber"
            receiptsParams = [.text(terminalId), .text(FiscalHashing.storedText(periodStart))]
        } else {
            receiptsQuery = "SELECT * FROM FiscalReceipts WHERE TerminalId = ? ORDER BY SequenceNumber"
            receiptsParams = [.text(terminalId)]
        }
        let receiptRows = try db.query(receiptsQuery, receiptsParams)
        let receipts = receiptRows.compactMap(record)

        let totalTtc = receipts.reduce(0) { $0 + $1.ttcCents }
        let totalHt = receipts.reduce(0) { $0 + $1.htCents }

        var vatCents: [String: Int] = [:]
        for r in receipts {
            let data = Data(r.taxBreakdownJson.utf8)
            if let dict = try? JSONSerialization.jsonObject(with: data) as? [String: Any] {
                for (rate, val) in dict {
                    let cents: Int = (val as? Int) ?? Int((val as? Double) ?? 0)
                    vatCents[rate, default: 0] += cents
                }
            }
        }
        let vatMoney = vatCents.mapValues { Money(cents: $0) }

        var paymentCents: [String: Int] = [:]
        var paymentMoney: [String: Money] = [:]
        let tenderQuery: String
        let tenderParams: [SQLValue]
        if lastClosure != nil {
            tenderQuery = """
            SELECT t.Method, SUM(t.Amount) AS Total
            FROM PaymentTenders t
            JOIN FiscalReceipts r ON r.Id = t.FiscalReceiptId
            WHERE r.TerminalId = ? AND r.CreatedAtUtc > ?
            GROUP BY t.Method
            """
            tenderParams = [.text(terminalId), .text(FiscalHashing.storedText(periodStart))]
        } else {
            tenderQuery = """
            SELECT t.Method, SUM(t.Amount) AS Total
            FROM PaymentTenders t
            JOIN FiscalReceipts r ON r.Id = t.FiscalReceiptId
            WHERE r.TerminalId = ?
            GROUP BY t.Method
            """
            tenderParams = [.text(terminalId)]
        }
        for row in try db.query(tenderQuery, tenderParams) {
            let rawMethod = row.int("Method") ?? 0
            let method = PaymentMethod(rawValue: rawMethod) ?? .cash
            let serverName: String
            switch method {
            case .cash: serverName = "Cash"
            case .creditCard: serverName = "CreditCard"
            case .mealVoucher: serverName = "MealVoucher"
            case .giftCard: serverName = "GiftCard"
            case .roomCharge: serverName = "RoomCharge"
            }
            let amt = row.int("Total") ?? 0
            paymentCents[serverName, default: 0] += amt
            paymentMoney[serverName, default: .zero] += Money(cents: amt)
        }

        let perpetual = (lastClosure?.perpetualGrandTotalCents ?? 0) + totalTtc

        let sortedVat = vatCents.sorted { $0.key < $1.key }
        let taxesJson = FiscalJSON.object(sortedVat.map { ($0.key, "\($0.value)") })

        let sortedTenders = paymentCents.sorted { $0.key < $1.key }
        let tendersJson = FiscalJSON.object(sortedTenders.map { ($0.key, "\($0.value)") })

        return FiscalSummary(
            periodStartUtc: periodStart,
            periodEndUtc: periodEnd,
            totalSalesTtcCents: totalTtc,
            totalSalesHtCents: totalHt,
            receiptCount: receipts.count,
            vatBreakdown: vatMoney,
            paymentTotals: paymentMoney,
            perpetualGrandTotalCents: perpetual,
            taxesSummaryJson: taxesJson,
            tenderTotalsJson: tendersJson
        )
    }

    func insertClosure(
        terminalId: String,
        managerId: UUID,
        managerName: String,
        now: Date
    ) throws -> DailyFiscalClosureRecord {
        let lastClosure = try latestClosure(terminalId: terminalId)
        let sum = try summary(terminalId: terminalId, now: now)

        let sequence = (lastClosure?.sequence ?? 0) + 1
        let previousHash = lastClosure?.signatureHash ?? FiscalHashing.genesisHash
        let timestamp = FiscalHashing.roundedToMilliseconds(now)
        let signature = FiscalHashing.receiptHash(
            previousHash: previousHash,
            terminalId: terminalId,
            sequence: sequence,
            amountCents: sum.totalSalesTtcCents,
            timestamp: FiscalHashing.timestampText(timestamp),
            taxBreakdownJson: sum.taxesSummaryJson
        )
        let id = UUID()
        try db.run(
            """
            INSERT INTO DailyFiscalClosures (
                Id, TerminalId, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalSalesTtc, TotalSalesHt,
                TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, PreviousSignatureHash, SignatureHash,
                SealedByUserName, SealedByUserId, CreatedAtUtc
            ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                .uuid(id), .text(terminalId), .integer(sequence),
                .text(FiscalHashing.storedText(sum.periodStartUtc)), .text(FiscalHashing.storedText(timestamp)),
                .integer(sum.totalSalesTtcCents), .integer(sum.totalSalesHtCents),
                .text(sum.taxesSummaryJson), .text(sum.tenderTotalsJson),
                .integer(sum.perpetualGrandTotalCents), .text(previousHash), .text(signature),
                .text(managerName), .uuid(managerId), .text(FiscalHashing.storedText(timestamp))
            ]
        )

        try appendJournal(
            eventType: LocalJournalEvents.zClosure,
            payloadJson: FiscalJSON.object([
                ("ClosureId", FiscalJSON.guid(id)),
                ("TerminalId", FiscalJSON.string(terminalId)),
                ("ClosureSequence", "\(sequence)"),
                ("TotalSalesTtcCents", "\(sum.totalSalesTtcCents)"),
                ("PerpetualGrandTotalCents", "\(sum.perpetualGrandTotalCents)"),
                ("SignatureHash", FiscalJSON.string(signature)),
            ]),
            terminalId: terminalId, operatorId: managerId, now: now
        )

        return DailyFiscalClosureRecord(
            id: id,
            terminalId: terminalId,
            sequence: sequence,
            periodStartUtc: sum.periodStartUtc,
            periodEndUtc: timestamp,
            totalSalesTtcCents: sum.totalSalesTtcCents,
            totalSalesHtCents: sum.totalSalesHtCents,
            taxesSummaryJson: sum.taxesSummaryJson,
            tenderTotalsJson: sum.tenderTotalsJson,
            perpetualGrandTotalCents: sum.perpetualGrandTotalCents,
            previousSignatureHash: previousHash,
            signatureHash: signature,
            sealedByUserName: managerName,
            sealedByUserId: managerId,
            createdAtUtc: timestamp
        )
    }

    func closureRecord(_ row: SQLRow) -> DailyFiscalClosureRecord? {
        guard let id = row.uuid("Id") else { return nil }
        return DailyFiscalClosureRecord(
            id: id,
            terminalId: row.string("TerminalId") ?? "",
            sequence: row.int("ClosureSequence") ?? 0,
            periodStartUtc: row.date("PeriodStartUtc") ?? Date(timeIntervalSince1970: 0),
            periodEndUtc: row.date("PeriodEndUtc") ?? Date(),
            totalSalesTtcCents: row.int("TotalSalesTtc") ?? 0,
            totalSalesHtCents: row.int("TotalSalesHt") ?? 0,
            taxesSummaryJson: row.string("TaxesSummaryJson") ?? "{}",
            tenderTotalsJson: row.string("TenderTotalsJson") ?? "{}",
            perpetualGrandTotalCents: row.int("PerpetualGrandTotalCents") ?? 0,
            previousSignatureHash: row.string("PreviousSignatureHash") ?? FiscalHashing.genesisHash,
            signatureHash: row.string("SignatureHash") ?? "",
            sealedByUserName: row.string("SealedByUserName") ?? "",
            sealedByUserId: row.uuid("SealedByUserId") ?? UUID(),
            createdAtUtc: row.date("CreatedAtUtc") ?? Date()
        )
    }

    // MARK: Vérification

    /// Recalcule les chaînes (reçus par terminal, clôtures Z, JET, clôtures de période, archives). Renvoie les anomalies
    /// (`sequence_gap`, `previous_hash_mismatch`, `signature_mismatch`).
    func verifyChains() throws -> [String] {
        var issues: [String] = []
        let receiptTerminals = try db.query("SELECT DISTINCT TerminalId FROM FiscalReceipts ORDER BY TerminalId").compactMap { $0.string("TerminalId") }
        for terminal in receiptTerminals {
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
        let closureTerminals = try db.query("SELECT DISTINCT TerminalId FROM DailyFiscalClosures ORDER BY TerminalId").compactMap { $0.string("TerminalId") }
        for terminal in closureTerminals {
            var previous = FiscalHashing.genesisHash
            var expected = 1
            for row in try db.query("SELECT * FROM DailyFiscalClosures WHERE TerminalId = ? ORDER BY ClosureSequence", [.text(terminal)]) {
                let sequence = row.int("ClosureSequence") ?? 0
                let label = "closure \(terminal)#\(sequence)"
                if sequence != expected { issues.append("\(label): sequence_gap (attendu \(expected))"); expected = sequence }
                if row.string("PreviousSignatureHash") != previous { issues.append("\(label): previous_hash_mismatch") }
                let timestamp = row.date("CreatedAtUtc").map(FiscalHashing.timestampText) ?? ""
                let signature = FiscalHashing.receiptHash(
                    previousHash: row.string("PreviousSignatureHash") ?? "", terminalId: terminal, sequence: sequence,
                    amountCents: row.int("TotalSalesTtc") ?? 0, timestamp: timestamp, taxBreakdownJson: row.string("TaxesSummaryJson") ?? "{}"
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
        // Clôtures de période et archives : même contrôle que `verifyAllChains` (la restauration d'une sauvegarde les couvre ainsi).
        for status in try periodClosureChainStatuses() + [archiveChainStatus()] {
            guard let broken = status.break else { continue }
            issues.append("\(status.chain) \(status.terminalId ?? "-") \(broken.reference)#\(broken.sequenceNumber): \(broken.kind)")
        }
        return issues
    }

    /// Vérification complète structurée, dans l'ordre du .NET : reçus, Z, clôtures de période, JET, archives.
    func verifyAllChains(now: Date) throws -> FiscalVerificationResult {
        var chainStatuses: [FiscalChainStatus] = []

        // 1. Chaîne des reçus par terminal
        let receiptTerminals = try db.query("SELECT DISTINCT TerminalId FROM FiscalReceipts ORDER BY TerminalId").compactMap { $0.string("TerminalId") }
        if receiptTerminals.isEmpty {
            chainStatuses.append(FiscalChainStatus(chain: "receipts", terminalId: LocalPosAPI.standaloneTerminalId, checkedCount: 0, isValid: true))
        } else {
            for terminal in receiptTerminals {
                var previous = FiscalHashing.genesisHash
                var expected = 1
                var isValid = true
                var chainBreak: FiscalChainBreak?
                let rows = try db.query("SELECT * FROM FiscalReceipts WHERE TerminalId = ? ORDER BY SequenceNumber", [.text(terminal)])
                for row in rows {
                    let sequence = row.int("SequenceNumber") ?? 0
                    let ref = row.string("ReceiptNumber") ?? "\(terminal)-\(sequence)"
                    if sequence != expected {
                        isValid = false
                        chainBreak = FiscalChainBreak(sequenceNumber: sequence, reference: ref, kind: "sequence_gap")
                        break
                    }
                    if row.string("PreviousSignatureHash") != previous {
                        isValid = false
                        chainBreak = FiscalChainBreak(sequenceNumber: sequence, reference: ref, kind: "previous_hash_mismatch")
                        break
                    }
                    let timestamp = row.date("CreatedAtUtc").map(FiscalHashing.timestampText) ?? ""
                    let signature = FiscalHashing.receiptHash(
                        previousHash: row.string("PreviousSignatureHash") ?? "", terminalId: terminal, sequence: sequence,
                        amountCents: row.int("TotalTtcAmount") ?? 0, timestamp: timestamp, taxBreakdownJson: row.string("TaxBreakdownJson") ?? ""
                    )
                    if signature != row.string("SignatureHash") {
                        isValid = false
                        chainBreak = FiscalChainBreak(sequenceNumber: sequence, reference: ref, kind: "signature_mismatch")
                        break
                    }
                    previous = row.string("SignatureHash") ?? ""
                    expected += 1
                }
                chainStatuses.append(FiscalChainStatus(chain: "receipts", terminalId: terminal, checkedCount: rows.count, isValid: isValid, break: chainBreak))
            }
        }

        // 2. Chaîne des clôtures journalières Z par terminal
        let closureTerminals = try db.query("SELECT DISTINCT TerminalId FROM DailyFiscalClosures ORDER BY TerminalId").compactMap { $0.string("TerminalId") }
        if closureTerminals.isEmpty {
            chainStatuses.append(FiscalChainStatus(chain: "z_closures", terminalId: LocalPosAPI.standaloneTerminalId, checkedCount: 0, isValid: true))
        } else {
            for terminal in closureTerminals {
                var previous = FiscalHashing.genesisHash
                var expected = 1
                var isValid = true
                var chainBreak: FiscalChainBreak?
                let rows = try db.query("SELECT * FROM DailyFiscalClosures WHERE TerminalId = ? ORDER BY ClosureSequence", [.text(terminal)])
                for row in rows {
                    let sequence = row.int("ClosureSequence") ?? 0
                    let ref = "\(terminal)-Z-\(String(format: "%06d", sequence))"
                    if sequence != expected {
                        isValid = false
                        chainBreak = FiscalChainBreak(sequenceNumber: sequence, reference: ref, kind: "sequence_gap")
                        break
                    }
                    if row.string("PreviousSignatureHash") != previous {
                        isValid = false
                        chainBreak = FiscalChainBreak(sequenceNumber: sequence, reference: ref, kind: "previous_hash_mismatch")
                        break
                    }
                    let timestamp = row.date("CreatedAtUtc").map(FiscalHashing.timestampText) ?? ""
                    let signature = FiscalHashing.receiptHash(
                        previousHash: row.string("PreviousSignatureHash") ?? "", terminalId: terminal, sequence: sequence,
                        amountCents: row.int("TotalSalesTtc") ?? 0, timestamp: timestamp, taxBreakdownJson: row.string("TaxesSummaryJson") ?? "{}"
                    )
                    if signature != row.string("SignatureHash") {
                        isValid = false
                        chainBreak = FiscalChainBreak(sequenceNumber: sequence, reference: ref, kind: "signature_mismatch")
                        break
                    }
                    previous = row.string("SignatureHash") ?? ""
                    expected += 1
                }
                chainStatuses.append(FiscalChainStatus(chain: "z_closures", terminalId: terminal, checkedCount: rows.count, isValid: isValid, break: chainBreak))
            }
        }

        // 3. Clôtures de période (par poste, chaque type depuis la genèse)
        chainStatuses += try periodClosureChainStatuses()

        // 4. Journal des événements techniques (JET)
        var previous = FiscalHashing.genesisHash
        var expected = 1
        var jetValid = true
        var jetBreak: FiscalChainBreak?
        let jetRows = try db.query("SELECT * FROM JournalEntries WHERE ChainSequence IS NOT NULL ORDER BY ChainSequence")
        for row in jetRows {
            let sequence = row.int("ChainSequence") ?? 0
            let ref = "JET-\(sequence)"
            if sequence != expected {
                jetValid = false
                jetBreak = FiscalChainBreak(sequenceNumber: sequence, reference: ref, kind: "sequence_gap")
                break
            }
            if row.string("PreviousHash") != previous {
                jetValid = false
                jetBreak = FiscalChainBreak(sequenceNumber: sequence, reference: ref, kind: "previous_hash_mismatch")
                break
            }
            let hash = FiscalHashing.jetHash(
                previousHash: row.string("PreviousHash") ?? "", chainSequence: sequence, eventType: row.string("EventType") ?? "",
                timestamp: row.date("OccurredAtUtc").map(FiscalHashing.timestampText) ?? "", terminalId: row.string("TerminalId"),
                operatorId: row.uuid("OperatorId"), payloadJson: row.string("PayloadJson") ?? ""
            )
            if hash != row.string("EntryHash") {
                jetValid = false
                jetBreak = FiscalChainBreak(sequenceNumber: sequence, reference: ref, kind: "signature_mismatch")
                break
            }
            previous = row.string("EntryHash") ?? ""
            expected += 1
        }
        chainStatuses.append(FiscalChainStatus(chain: "journal", terminalId: nil, checkedCount: jetRows.count, isValid: jetValid, break: jetBreak))

        // 5. Archives (chaîne globale)
        chainStatuses.append(try archiveChainStatus())

        let overallValid = chainStatuses.allSatisfy(\.isValid)
        return FiscalVerificationResult(isValid: overallValid, checkedAtUtc: now, chains: chainStatuses)
    }
}

