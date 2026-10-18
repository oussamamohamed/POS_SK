import Foundation

/// Export FEC du mode autonome : lit tous les reçus (`FiscalReceipts`, ordre de séquence) et leurs règlements (`PaymentTenders`, ordre
/// d'enregistrement), puis délègue à `LocalFecWriter`. Comme le .NET, aucun filtre de poste (le poste autonome est seul, `T01`).
/// ponytail: tous les reçus sont chargés en mémoire, comme `FecExportService` ; quelques milliers par mois tiennent sans peine.
/// Filtrer par date en SQL si une base dépasse quelques centaines de milliers de reçus.
struct LocalFecExporter {
    let db: SQLiteDatabase

    func export(from: Date, to: Date, siren: String) throws -> FecExport {
        var tenders: [String: [FecTender]] = [:]
        for row in try db.query("SELECT FiscalReceiptId, Method, Amount FROM PaymentTenders ORDER BY rowid") {
            let receiptId = (row.string("FiscalReceiptId") ?? "").uppercased()
            tenders[receiptId, default: []].append(FecTender(method: row.int("Method") ?? 0, amountCents: row.int("Amount") ?? 0))
        }
        let receipts = try db.query(
            """
            SELECT Id, ReceiptNumber, CreatedAtUtc, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson, HtBreakdownJson, VoidedReceiptId
            FROM FiscalReceipts ORDER BY SequenceNumber, rowid
            """
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
