import Foundation

/// Règlement d'un reçu : moyen (valeur entière de `PaymentTenders.Method`) et montant réglé (`Amount`, pas le montant remis).
struct FecTender: Equatable {
    let method: Int
    let amountCents: Int
}

/// Reçu tel que le FEC le lit : colonnes de `FiscalReceipts`, règlements dans l'ordre d'enregistrement.
struct FecReceipt: Equatable {
    let receiptNumber: String
    let createdAtUtc: Date
    let ttcCents: Int
    let htCents: Int
    let taxBreakdownJson: String
    let htBreakdownJson: String?
    /// `VoidedReceiptId` renseigné : reçu d'annulation.
    let voidsReceipt: Bool
    let tenders: [FecTender]
}

/// Fichier FEC et totaux, comme `FecExportResult`.
struct FecExport: Equatable {
    let fileName: String
    let data: Data
    let totalRecords: Int
    let totalDebitCents: Int
    let totalCreditCents: Int
}

/// Miroir de `FecExportService.GenerateFecAsync` : une écriture par reçu de la période (bornes incluses, inversées si besoin), dans l'ordre
/// des reçus. Vente : HT par taux puis TVA au crédit, règlements au débit, excédent de règlement (pourboire) au crédit du 468600.
/// Annulation (`VoidedReceiptId` ou TTC négatif) : mêmes lignes en valeur absolue, sens inversé.
/// Fins de ligne `\n` (`AppendLine` du serveur macOS/Linux, dernière ligne comprise), UTF-8 sans BOM.
enum LocalFecWriter {
    static func export(receipts: [FecReceipt], from: Date, to: Date, siren: String) -> FecExport {
        let (start, end) = from > to ? (to, from) : (from, to)
        var lines = [LocalFecFormat.header]
        var totalDebit = 0
        var totalCredit = 0
        let selected = receipts.filter { $0.createdAtUtc >= start && $0.createdAtUtc <= end }
        for (index, receipt) in selected.enumerated() {
            let entry = LocalFecFormat.entryNumber(index + 1)
            let date = LocalFecFormat.date(receipt.createdAtUtc)
            let piece = receipt.receiptNumber
            let isVoid = receipt.voidsReceipt || receipt.ttcCents < 0
            func write(_ account: FecAccount, _ label: String, debit: Int, credit: Int) {
                lines.append(LocalFecFormat.line(entry: entry, date: date, account: account, pieceRef: piece, label: label, debit: debit, credit: credit))
                totalDebit += debit
                totalCredit += credit
            }

            let items = LocalFecBreakdown.items(
                ttcCents: receipt.ttcCents, htCents: receipt.htCents, taxBreakdownJson: receipt.taxBreakdownJson, htBreakdownJson: receipt.htBreakdownJson
            )
            for item in items {
                let ht = abs(item.htCents)
                let vat = abs(item.vatCents)
                if ht <= 0 && vat <= 0 { continue }
                let rate = LocalFecFormat.rateLabel(item.rate).replacingOccurrences(of: ".", with: ",") // FormatRateForLabel : virgule dans les libellés
                write(LocalFecFormat.salesAccount(rate: item.rate), "Vente \(piece) (HT \(rate)%)", debit: isVoid ? ht : 0, credit: isVoid ? 0 : ht)
                if vat > 0 {
                    write(LocalFecFormat.vatAccount(rate: item.rate), "TVA Collectee \(rate)% \(piece)", debit: isVoid ? vat : 0, credit: isVoid ? 0 : vat)
                }
            }

            if receipt.tenders.isEmpty {
                let amount = abs(receipt.ttcCents)
                write(LocalFecFormat.cashFallbackAccount, "Reglement Caisse \(piece)", debit: isVoid ? 0 : amount, credit: isVoid ? amount : 0)
            } else {
                for tender in receipt.tenders {
                    let amount = abs(tender.amountCents)
                    let label = "Reglement \(LocalFecFormat.methodName(tender.method)) \(piece)"
                    write(LocalFecFormat.paymentAccount(method: tender.method), label, debit: isVoid ? 0 : amount, credit: isVoid ? amount : 0)
                }
                // Excédent de règlement (pourboire) : encaissé hors TTC du reçu, dette et non produit ; sans lui l'écriture serait déséquilibrée.
                let tip = receipt.tenders.reduce(0) { $0 + abs($1.amountCents) } - abs(receipt.ttcCents)
                if tip > 0 {
                    write(LocalFecFormat.tipsAccount, "Excedent de reglement / pourboire \(piece)", debit: isVoid ? tip : 0, credit: isVoid ? 0 : tip)
                }
            }
        }
        let text = lines.map { $0 + "\n" }.joined()
        return FecExport(
            fileName: LocalFecFormat.fileName(siren: siren, end: end), data: Data(text.utf8), totalRecords: lines.count - 1,
            totalDebitCents: totalDebit, totalCreditCents: totalCredit
        )
    }
}
