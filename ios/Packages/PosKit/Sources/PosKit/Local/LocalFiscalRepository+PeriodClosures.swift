import Foundation

struct PeriodClosureRecord: Equatable {
    let id: UUID
    let terminalId: String
    let periodType: FiscalPeriodType
    let periodKey: String
    let sequence: Int
    let periodStartUtc: Date
    let periodEndUtc: Date
    let totalTtcCents: Int
    let totalHtCents: Int
    let taxesSummaryJson: String
    let tenderTotalsJson: String
    let perpetualGrandTotalCents: Int
    let dailyClosureCount: Int
    let previousSignatureHash: String
    let signatureHash: String
    let sealedByUserId: UUID
    let sealedByUserName: String
    let createdAtUtc: Date
}

/// Refus métier d'une clôture de période : mêmes causes que `PeriodClosureException` du .NET.
enum PeriodClosureError: Error, Equatable {
    case invalidKey
    case notEnded
    case alreadyClosed
    case missingDailyClosures([String])
    case missingMonthlyClosures([String])
}

extension FiscalPeriodType {
    /// Valeur signée et stockée (`FiscalPeriodType` du .NET : `Monthly = 1`, `Annual = 2`).
    var storedValue: Int { self == .monthly ? 1 : 2 }
}

extension LocalFiscalRepository {
    /// Clôture une période (`ExecutePeriodClosureAsync`). Mensuelle : somme des Z journalières dont la période commence dans le mois.
    /// Annuelle : somme des 12 mensuelles si l'exercice commence le 1er, sinon des Z journalières. Aucune transaction ouverte ici.
    func executePeriodClosure(
        terminalId: String, type: FiscalPeriodType, key: String, managerId: UUID, managerName: String,
        fiscalYearStartMonth: Int, fiscalYearStartDay: Int, calendar deviceCalendar: Calendar, now: Date
    ) throws -> PeriodClosureRecord {
        // Calendrier grégorien imposé : un calendrier d'appareil japonais ou bouddhiste fausserait années, bornes et jours manquants.
        var calendar = Calendar(identifier: .gregorian)
        calendar.timeZone = deviceCalendar.timeZone
        let start = try Self.periodStart(type: type, key: key, month: fiscalYearStartMonth, day: fiscalYearStartDay, calendar: calendar)
        guard let rawEnd = calendar.date(byAdding: type == .monthly ? .month : .year, value: 1, to: start) else { throw PeriodClosureError.invalidKey }
        let end = FiscalHashing.roundedToMilliseconds(rawEnd)
        let timestampNow = FiscalHashing.roundedToMilliseconds(now)
        guard timestampNow >= end else { throw PeriodClosureError.notEnded }
        guard try db.query(
            "SELECT 1 AS x FROM PeriodClosures WHERE TerminalId = ? AND PeriodType = ? AND PeriodKey = ?",
            [.text(terminalId), .integer(type.storedValue), .text(key)]
        ).isEmpty else { throw PeriodClosureError.alreadyClosed }

        var totalTtc = 0, totalHt = 0, dailyCount = 0, perpetual = 0
        var taxes = OrderedTotals(), tenders = OrderedTotals()

        if type == .monthly || fiscalYearStartDay != 1 {
            let all = try dailyClosures(terminalId: terminalId)
            let inPeriod = all.filter { $0.periodStartUtc >= start && $0.periodStartUtc < end }
            try ensureEveryReceiptIsCovered(terminalId: terminalId, start: start, end: end, closures: inPeriod, calendar: calendar)
            totalTtc = inPeriod.reduce(0) { $0 + $1.totalSalesTtcCents }
            totalHt = inPeriod.reduce(0) { $0 + $1.totalSalesHtCents }
            dailyCount = inPeriod.count
            if let last = inPeriod.last {
                perpetual = last.perpetualGrandTotalCents
            } else {
                perpetual = all.filter { $0.periodEndUtc < start }.max { $0.periodEndUtc < $1.periodEndUtc }?.perpetualGrandTotalCents ?? 0
            }
            for z in inPeriod {
                taxes.merge(z.taxesSummaryJson)
                tenders.merge(z.tenderTotalsJson)
            }
        } else {
            let expected = (0..<12).compactMap { calendar.date(byAdding: .month, value: $0, to: start) }.map { Self.monthKey($0, calendar) }
            let monthly = try periodClosures(terminalId: terminalId, type: .monthly)
            let closed = Set(monthly.map(\.periodKey))
            let missing = expected.filter { !closed.contains($0) }
            guard missing.isEmpty else { throw PeriodClosureError.missingMonthlyClosures(missing) }
            let year = monthly.filter { expected.contains($0.periodKey) }.sorted { $0.periodKey < $1.periodKey }
            totalTtc = year.reduce(0) { $0 + $1.totalTtcCents }
            totalHt = year.reduce(0) { $0 + $1.totalHtCents }
            dailyCount = year.reduce(0) { $0 + $1.dailyClosureCount }
            perpetual = year.last?.perpetualGrandTotalCents ?? 0
            for m in year {
                taxes.merge(m.taxesSummaryJson)
                tenders.merge(m.tenderTotalsJson)
            }
        }

        let last = try periodClosures(terminalId: terminalId, type: type).last
        let sequence = (last?.sequence ?? 0) + 1
        let previousHash = last?.signatureHash ?? FiscalHashing.genesisHash
        let taxesJson = taxes.json, tendersJson = tenders.json
        let signature = FiscalHashing.periodClosureHash(
            previousHash: previousHash, terminalId: terminalId, periodType: type.storedValue, periodKey: key,
            totalTtcCents: totalTtc, totalHtCents: totalHt, taxesJson: taxesJson, tendersJson: tendersJson,
            perpetualGrandTotalCents: perpetual, periodEnd: end
        )
        let id = UUID()
        try db.run(
            """
            INSERT INTO PeriodClosures (Id, TerminalId, PeriodType, PeriodKey, ClosureSequence, PeriodStartUtc, PeriodEndUtc, TotalTtcAmount, TotalHtAmount,
                TaxesSummaryJson, TenderTotalsJson, PerpetualGrandTotalCents, DailyClosureCount, PreviousSignatureHash, SignatureHash, SealedByUserId,
                SealedByUserName, CreatedAtUtc)
            VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
            """,
            [
                .uuid(id), .text(terminalId), .integer(type.storedValue), .text(key), .integer(sequence),
                .text(FiscalHashing.storedText(start)), .text(FiscalHashing.storedText(end)), .integer(totalTtc), .integer(totalHt),
                .text(taxesJson), .text(tendersJson), .integer(perpetual), .integer(dailyCount), .text(previousHash), .text(signature),
                .uuid(managerId), .text(managerName), .text(FiscalHashing.storedText(timestampNow)),
            ]
        )
        try appendJournal(
            eventType: LocalJournalEvents.periodClosure,
            payloadJson: FiscalJSON.object([
                ("PeriodClosureId", FiscalJSON.guid(id)),
                ("TerminalId", FiscalJSON.string(terminalId)),
                ("PeriodType", FiscalJSON.string(type.rawValue)),
                ("PeriodKey", FiscalJSON.string(key)),
                ("ClosureSequence", "\(sequence)"),
                ("TotalTtcCents", "\(totalTtc)"),
                ("PerpetualGrandTotalCents", "\(perpetual)"),
                ("SignatureHash", FiscalJSON.string(signature)),
            ]),
            terminalId: terminalId, operatorId: managerId, now: now
        )
        return PeriodClosureRecord(
            id: id, terminalId: terminalId, periodType: type, periodKey: key, sequence: sequence, periodStartUtc: start, periodEndUtc: end,
            totalTtcCents: totalTtc, totalHtCents: totalHt, taxesSummaryJson: taxesJson, tenderTotalsJson: tendersJson, perpetualGrandTotalCents: perpetual,
            dailyClosureCount: dailyCount, previousSignatureHash: previousHash, signatureHash: signature, sealedByUserId: managerId,
            sealedByUserName: managerName, createdAtUtc: timestampNow
        )
    }

    /// Clôtures du poste, de la plus ancienne à la plus récente (par type puis séquence).
    func periodClosures(terminalId: String, type: FiscalPeriodType?) throws -> [PeriodClosureRecord] {
        var sql = "SELECT * FROM PeriodClosures WHERE TerminalId = ?"
        var params: [SQLValue] = [.text(terminalId)]
        if let type {
            sql += " AND PeriodType = ?"
            params.append(.integer(type.storedValue))
        }
        sql += " ORDER BY PeriodType, ClosureSequence"
        return try db.query(sql, params).compactMap(periodClosureRecord)
    }

    func periodClosure(id: UUID) throws -> PeriodClosureRecord? {
        try db.query("SELECT * FROM PeriodClosures WHERE Id = ?", [.uuid(id)]).first.flatMap(periodClosureRecord)
    }

    func dailyClosures(terminalId: String) throws -> [DailyFiscalClosureRecord] {
        try db.query("SELECT * FROM DailyFiscalClosures WHERE TerminalId = ? ORDER BY ClosureSequence", [.text(terminalId)]).compactMap(closureRecord)
    }

    // MARK: Bornes de période

    /// Début de période à minuit local. Refuse une clé mal formée et un jour d'exercice inexistant (30 février).
    private static func periodStart(type: FiscalPeriodType, key: String, month: Int, day: Int, calendar: Calendar) throws -> Date {
        let components: DateComponents
        switch type {
        case .monthly:
            guard key.range(of: #"^(19|2[0-9])[0-9]{2}-(0[1-9]|1[0-2])\z"#, options: .regularExpression) != nil,
                  let y = Int(key.prefix(4)), let m = Int(key.suffix(2))
            else { throw PeriodClosureError.invalidKey }
            components = DateComponents(year: y, month: m, day: 1)
        case .annual:
            guard key.range(of: #"^(19|2[0-9])[0-9]{2}\z"#, options: .regularExpression) != nil, let y = Int(key)
            else { throw PeriodClosureError.invalidKey }
            components = DateComponents(year: y, month: month, day: day)
        }
        guard let start = calendar.date(from: components),
              calendar.component(.day, from: start) == components.day, calendar.component(.month, from: start) == components.month
        else { throw PeriodClosureError.invalidKey }
        return FiscalHashing.roundedToMilliseconds(start)
    }

    private static func monthKey(_ date: Date, _ calendar: Calendar) -> String {
        let c = calendar.dateComponents([.year, .month], from: date)
        return String(format: "%04d-%02d", c.year ?? 0, c.month ?? 0)
    }

    /// Un reçu de la période doit être couvert par une Z journalière dont la fin est postérieure ou égale ; sinon on liste les jours (fuseau local).
    private func ensureEveryReceiptIsCovered(terminalId: String, start: Date, end: Date, closures: [DailyFiscalClosureRecord], calendar: Calendar) throws {
        let rows = try db.query(
            "SELECT CreatedAtUtc FROM FiscalReceipts WHERE TerminalId = ? AND CreatedAtUtc >= ? AND CreatedAtUtc < ?",
            [.text(terminalId), .text(FiscalHashing.storedText(start)), .text(FiscalHashing.storedText(end))]
        )
        var missing = Set<String>()
        for row in rows {
            guard let created = row.date("CreatedAtUtc"), !closures.contains(where: { $0.periodEndUtc >= created }) else { continue }
            let c = calendar.dateComponents([.year, .month, .day], from: created)
            missing.insert(String(format: "%04d-%02d-%02d", c.year ?? 0, c.month ?? 0, c.day ?? 0))
        }
        guard missing.isEmpty else { throw PeriodClosureError.missingDailyClosures(missing.sorted()) }
    }

    private func periodClosureRecord(_ row: SQLRow) -> PeriodClosureRecord? {
        guard let id = row.uuid("Id"), let typeValue = row.int("PeriodType") else { return nil }
        return PeriodClosureRecord(
            id: id, terminalId: row.string("TerminalId") ?? "", periodType: typeValue == 1 ? .monthly : .annual, periodKey: row.string("PeriodKey") ?? "",
            sequence: row.int("ClosureSequence") ?? 0, periodStartUtc: row.date("PeriodStartUtc") ?? Date(timeIntervalSince1970: 0),
            periodEndUtc: row.date("PeriodEndUtc") ?? Date(), totalTtcCents: row.int("TotalTtcAmount") ?? 0, totalHtCents: row.int("TotalHtAmount") ?? 0,
            taxesSummaryJson: row.string("TaxesSummaryJson") ?? "{}", tenderTotalsJson: row.string("TenderTotalsJson") ?? "{}",
            perpetualGrandTotalCents: row.int("PerpetualGrandTotalCents") ?? 0, dailyClosureCount: row.int("DailyClosureCount") ?? 0,
            previousSignatureHash: row.string("PreviousSignatureHash") ?? FiscalHashing.genesisHash, signatureHash: row.string("SignatureHash") ?? "",
            sealedByUserId: row.uuid("SealedByUserId") ?? UUID(), sealedByUserName: row.string("SealedByUserName") ?? "",
            createdAtUtc: row.date("CreatedAtUtc") ?? Date()
        )
    }
}

/// Totaux par clé fusionnés dans l'ordre d'insertion (comme le `Dictionary<string, long>` du .NET), puis sérialisés sans espace.
private struct OrderedTotals {
    private var keys: [String] = []
    private var values: [String: Int] = [:]

    mutating func merge(_ json: String) {
        guard let entries = FiscalJSON.parseIntObject(json) else { return } // JSON illisible d'une donnée ancienne : ignoré comme le .NET
        for (key, value) in entries {
            if values[key] == nil { keys.append(key) }
            values[key, default: 0] += value
        }
    }

    var json: String { FiscalJSON.object(keys.map { ($0, "\(values[$0] ?? 0)") }) }
}
