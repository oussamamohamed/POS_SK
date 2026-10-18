import Foundation

/// Export FEC du mode autonome : lit les reçus de la période (`FiscalReceipts`, ordre de séquence) et leurs règlements (`PaymentTenders`,
/// ordre d'enregistrement), puis délègue à `LocalFecWriter`. Comme le .NET, aucun filtre de poste (le poste autonome est seul, `T01`).
/// Le filtre de période est fait en SQL sur le texte stocké (`FiscalHashing.storedText`, qui se trie comme les dates) ; `LocalFecWriter`
/// refait le filtre exact sur les dates, la sortie reste identique au .NET octet pour octet (`LocalFecReferenceTests`).
/// ponytail: les reçus de la période sont chargés en mémoire (quelques milliers par mois) ; au-delà, écrire les lignes au fil de la lecture.
struct LocalFecExporter {
    let db: SQLiteDatabase

    /// Forme exacte de `FiscalHashing.storedText` : seule une date ainsi écrite se compare correctement en texte.
    static let storedDatePattern = "[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]T[0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]Z"

    func export(from: Date, to: Date, siren: String) throws -> FecExport {
        // Une date d'une autre forme serait mal classée par le filtre SQL : arrêt plutôt qu'un reçu perdu en silence.
        if let odd = try db.query(
            "SELECT ReceiptNumber FROM FiscalReceipts WHERE CreatedAtUtc IS NULL OR CreatedAtUtc NOT GLOB ? LIMIT 1", [.text(Self.storedDatePattern)]
        ).first {
            throw APIError.server(status: 500, message: "Date illisible pour le reçu \(odd.string("ReceiptNumber") ?? "") : export FEC impossible.")
        }
        // Bornes arrondies à la milliseconde : un reçu (daté à la milliseconde) de [début ; fin] est toujours dans [texte(début) ; texte(fin)].
        let (start, end) = from > to ? (to, from) : (from, to)
        let bounds: [SQLValue] = [.text(FiscalHashing.storedText(start)), .text(FiscalHashing.storedText(end))]

        var tenders: [String: [FecTender]] = [:]
        for row in try db.query(
            """
            SELECT FiscalReceiptId, Method, Amount FROM PaymentTenders
            WHERE upper(FiscalReceiptId) IN (SELECT upper(Id) FROM FiscalReceipts WHERE CreatedAtUtc >= ? AND CreatedAtUtc <= ?)
            ORDER BY rowid
            """,
            bounds
        ) {
            let receiptId = (row.string("FiscalReceiptId") ?? "").uppercased()
            tenders[receiptId, default: []].append(FecTender(method: row.int("Method") ?? 0, amountCents: row.int("Amount") ?? 0))
        }
        let receipts = try db.query(
            """
            SELECT Id, ReceiptNumber, CreatedAtUtc, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson, HtBreakdownJson, VoidedReceiptId
            FROM FiscalReceipts WHERE CreatedAtUtc >= ? AND CreatedAtUtc <= ? ORDER BY SequenceNumber, rowid
            """,
            bounds
        ).map { row throws -> FecReceipt in
            let number = row.string("ReceiptNumber") ?? ""
            guard let createdAt = row.date("CreatedAtUtc") else {
                throw APIError.server(status: 500, message: "Date illisible pour le reçu \(number) : export FEC impossible.")
            }
            return FecReceipt(
                receiptNumber: number, createdAtUtc: createdAt, ttcCents: row.int("TotalTtcAmount") ?? 0, htCents: row.int("TotalHtAmount") ?? 0,
                taxBreakdownJson: row.string("TaxBreakdownJson") ?? "{}", htBreakdownJson: row.string("HtBreakdownJson"),
                voidsReceipt: row.string("VoidedReceiptId") != nil, tenders: tenders[(row.string("Id") ?? "").uppercased()] ?? []
            )
        }
        return LocalFecWriter.export(receipts: receipts, from: from, to: to, siren: siren)
    }
}
