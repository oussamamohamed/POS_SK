import Foundation

/// Vérification des chaînes de clôtures de période et d'archives : miroir de `VerifyPeriodClosuresChain` et `VerifyArchivesChain`
/// (`FiscalChainVerificationService.cs`). Les lignes sont lues brutes (sans `periodClosureRecord` / `archiveRecord`, qui complètent
/// les valeurs manquantes) : une colonne vide doit casser la chaîne, pas être remplacée.
extension LocalFiscalRepository {
    func tableExists(_ name: String) throws -> Bool {
        try !db.query("SELECT 1 AS x FROM sqlite_master WHERE type = 'table' AND name = ?", [.text(name)]).isEmpty
    }

    /// Une ligne par poste ; aucun poste : une ligne vide valide sans poste, comme le .NET.
    func periodClosureChainStatuses() throws -> [FiscalChainStatus] {
        // Base antérieure à V8 (ex. sauvegarde ancienne restaurée avant migration) : table absente = chaîne vide, valide.
        guard try tableExists("PeriodClosures") else { return [FiscalChainStatus(chain: "period_closures", terminalId: nil, checkedCount: 0, isValid: true)] }
        let terminals = try db.query("SELECT DISTINCT TerminalId FROM PeriodClosures ORDER BY TerminalId").compactMap { $0.string("TerminalId") }
        guard !terminals.isEmpty else { return [FiscalChainStatus(chain: "period_closures", terminalId: nil, checkedCount: 0, isValid: true)] }
        return try terminals.map { try periodClosureChainStatus(terminal: $0) }
    }

    /// Dans un poste, chaque type (1 mensuelle, 2 annuelle) est une chaîne depuis la genèse. Le .NET parcourt les types dans l'ordre
    /// d'apparition ; ici mensuelle puis annuelle (ne change le verdict que si les deux types sont rompus).
    private func periodClosureChainStatus(terminal: String) throws -> FiscalChainStatus {
        let rows = try db.query("SELECT * FROM PeriodClosures WHERE TerminalId = ? ORDER BY PeriodType, ClosureSequence", [.text(terminal)])
        for type in Set(rows.compactMap { $0.int("PeriodType") }).sorted() {
            var previous = FiscalHashing.genesisHash
            var expected = 1
            for row in rows where row.int("PeriodType") == type {
                let sequence = row.int("ClosureSequence") ?? 0
                let key = row.string("PeriodKey") ?? ""
                let broken = { (kind: String) in
                    FiscalChainStatus(
                        chain: "period_closures", terminalId: terminal, checkedCount: rows.count, isValid: false,
                        break: FiscalChainBreak(sequenceNumber: sequence, reference: "\(type == 1 ? "M" : "A")-\(key)", kind: kind)
                    )
                }
                if sequence != expected { return broken("sequence_gap") }
                if row.string("PreviousSignatureHash") != previous { return broken("previous_hash_mismatch") }
                guard let periodEnd = row.date("PeriodEndUtc") else { return broken("signature_mismatch") }
                let signature = FiscalHashing.periodClosureHash(
                    previousHash: previous, terminalId: terminal, periodType: type, periodKey: key,
                    totalTtcCents: row.int("TotalTtcAmount") ?? 0, totalHtCents: row.int("TotalHtAmount") ?? 0,
                    taxesJson: row.string("TaxesSummaryJson") ?? "", tendersJson: row.string("TenderTotalsJson") ?? "",
                    perpetualGrandTotalCents: row.int("PerpetualGrandTotalCents") ?? 0, periodEnd: periodEnd
                )
                if signature != row.string("SignatureHash") { return broken("signature_mismatch") }
                previous = row.string("SignatureHash") ?? ""
                expected += 1
            }
        }
        return FiscalChainStatus(chain: "period_closures", terminalId: terminal, checkedCount: rows.count, isValid: true)
    }

    /// Chaîne globale des archives (tous postes), par `ArchiveSequence`.
    func archiveChainStatus() throws -> FiscalChainStatus {
        guard try tableExists("FiscalArchives") else { return FiscalChainStatus(chain: "archives", terminalId: nil, checkedCount: 0, isValid: true) } // avant V9
        let rows = try db.query("SELECT * FROM FiscalArchives ORDER BY ArchiveSequence")
        var previous = FiscalHashing.genesisHash
        var expected = 1
        for row in rows {
            let sequence = row.int("ArchiveSequence") ?? 0
            let broken = { (kind: String) in
                FiscalChainStatus(
                    chain: "archives", terminalId: nil, checkedCount: rows.count, isValid: false,
                    break: FiscalChainBreak(sequenceNumber: sequence, reference: "ARC-\(sequence)", kind: kind)
                )
            }
            if sequence != expected { return broken("sequence_gap") }
            if row.string("PreviousSignatureHash") != previous { return broken("previous_hash_mismatch") }
            guard let createdAt = row.date("CreatedAtUtc") else { return broken("signature_mismatch") }
            let signature = FiscalHashing.archiveHash(
                previousHash: previous, periodType: row.int("PeriodType") ?? 0, periodKey: row.string("PeriodKey") ?? "",
                fileSha256: row.string("FileSha256") ?? "", createdAt: createdAt
            )
            if signature != row.string("SignatureHash") { return broken("signature_mismatch") }
            previous = row.string("SignatureHash") ?? ""
            expected += 1
        }
        return FiscalChainStatus(chain: "archives", terminalId: nil, checkedCount: rows.count, isValid: true)
    }
}
