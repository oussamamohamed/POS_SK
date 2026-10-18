import Foundation

extension LocalPosAPI {
    /// Export FEC (`GET /api/fiscal/fec`) : fichier identique au .NET pour les mêmes reçus, entrée JET `FEC_EXPORTED` dans la même
    /// transaction (un export en échec n'écrit rien). Réservé aux responsables. SIREN : 9 chiffres ou vide, contrôle propre à l'iPad (le nom
    /// du fichier en dérive ; le .NET n'en fait aucun). Charge utile du JET comme l'endpoint : dates telles que reçues (avant inversion, UTC
    /// `+00:00`), SIREN tel que reçu (`""` si vide : le client HTTP envoie toujours `siren=`, alors que le .NET écrit `null` si le paramètre
    /// est absent), `Company` = texte reçu ou `null`.
    public func exportFec(from: Date, to: Date, siren: String, company: String?) async throws -> (fileName: String, data: Data) {
        let manager = try requireManager()
        guard Siren.isValid(siren) else { throw APIError.server(status: 400, message: L10n.string("fiscal.fec_error_invalid_siren")) }
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
                    ("Company", company.map(FiscalJSON.string) ?? "null"),
                    ("FileName", FiscalJSON.string(result.fileName)),
                ]),
                terminalId: nil, operatorId: manager.id, now: now
            )
            return (result.fileName, result.data)
        }
    }
}
