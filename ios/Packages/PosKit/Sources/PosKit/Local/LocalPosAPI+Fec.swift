import Foundation

extension LocalPosAPI {
    /// Export FEC (`GET /api/fiscal/fec`) : fichier identique au .NET pour les mêmes reçus, entrée JET `FEC_EXPORTED` dans la même
    /// transaction. Réservé aux responsables. Charge utile du JET comme l'endpoint : dates telles que reçues (avant inversion), SIREN tel
    /// que saisi, `Company` nul (pas de paramètre société côté iPad, comme un appel HTTP sans `company`).
    public func exportFec(from: Date, to: Date, siren: String) async throws -> (fileName: String, data: Data) {
        let manager = try requireManager()
        let fiscal = fiscalRepository, exporter = LocalFecExporter(db: db)
        let now = clock()
        return try db.transaction { () throws -> (fileName: String, data: Data) in
            let result = try exporter.export(from: from, to: to, siren: siren)
            try fiscal.appendJournal(
                eventType: LocalJournalEvents.fecExported,
                payloadJson: FiscalJSON.object([
                    ("StartDateUtc", FiscalJSON.dateTime(from)),
                    ("EndDateUtc", FiscalJSON.dateTime(to)),
                    ("Siren", FiscalJSON.string(siren)),
                    ("Company", "null"),
                    ("FileName", FiscalJSON.string(result.fileName)),
                ]),
                terminalId: nil, operatorId: manager.id, now: now
            )
            return (result.fileName, result.data)
        }
    }
}
