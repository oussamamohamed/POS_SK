import Foundation
import Testing
@testable import PosKit

@Suite("FEC : parité octet pour octet avec la fixture .NET")
struct LocalFecReferenceTests {
    private struct Reference: Decodable {
        var receipts: [Receipt]
        var exports: [Export]
    }

    private struct Receipt: Decodable {
        var id: String
        var receiptNumber: String
        var sequenceNumber: Int
        var createdAtUtc: String
        var ttcCents: Int
        var htCents: Int
        var taxBreakdownJson: String
        var htBreakdownJson: String?
        var voidedReceiptId: String?
        var tenders: [Tender]
    }

    private struct Tender: Decodable {
        var id: String
        var method: Int
        var amountCents: Int
        var tenderedCents: Int
        var changeGivenCents: Int
    }

    private struct Export: Decodable {
        var name: String
        var fromUtc: String
        var toUtc: String
        var siren: String
        var fileName: String
        var totalRecords: Int
        var totalDebitCents: Int
        var totalCreditCents: Int
        var fecText: String
        var fecBase64: String
    }

    private static func reference() throws -> Reference {
        try JSONDecoder().decode(Reference.self, from: ContractDecodingTests.fixture("fec_reference"))
    }

    /// Base migrée (sans amorçage de démonstration) contenant exactement les reçus et règlements de la fixture, dans son ordre.
    private static func seededDatabase(_ reference: Reference) throws -> SQLiteDatabase {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        for r in reference.receipts {
            try db.run(
                """
                INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                    PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid, VoidedReceiptId, HtBreakdownJson)
                VALUES (?, 'T01', ?, ?, ?, ?, ?, ?, '', '', ?, 0, ?, ?)
                """,
                [.text(r.id), .text(r.receiptNumber), .uuid(UUID()), .integer(r.sequenceNumber), .integer(r.ttcCents), .integer(r.htCents),
                 .text(r.taxBreakdownJson), .text(r.createdAtUtc), .string(r.voidedReceiptId), .string(r.htBreakdownJson)]
            )
            for t in r.tenders {
                try db.run(
                    "INSERT INTO PaymentTenders (Id, FiscalReceiptId, Method, Amount, Tendered, ChangeGiven) VALUES (?, ?, ?, ?, ?, ?)",
                    [.text(t.id), .text(r.id), .integer(t.method), .integer(t.amountCents), .integer(t.tenderedCents), .integer(t.changeGivenCents)]
                )
            }
        }
        return db
    }

    @Test func everyExportOfTheDotNetFixtureIsByteIdentical() throws {
        let reference = try Self.reference()
        let exporter = LocalFecExporter(db: try Self.seededDatabase(reference))
        for e in reference.exports {
            let context = Comment(rawValue: e.name)
            let from = try #require(SQLDate.parse(e.fromUtc), context)
            let to = try #require(SQLDate.parse(e.toUtc), context)
            let result = try exporter.export(from: from, to: to, siren: e.siren)
            #expect(result.fileName == e.fileName, context)
            #expect(String(decoding: result.data, as: UTF8.self) == e.fecText, context) // diff lisible en cas d'écart
            #expect(result.data == Data(base64Encoded: e.fecBase64), context)       // preuve octet pour octet
            #expect(result.totalRecords == e.totalRecords, context)
            #expect(result.totalDebitCents == e.totalDebitCents, context)
            #expect(result.totalCreditCents == e.totalCreditCents, context)
        }
    }

    @Test func theFixtureCoversTheReviewFocusCases() throws {
        let reference = try Self.reference()
        #expect(reference.exports.count == 4)
        let year = try #require(reference.exports.last).fecText
        #expect(year.contains("\t468600\t"))                         // pourboire et sa contre-passation
        #expect(year.contains("\t411000\t"))                         // facturation chambre (iPad)
        #expect(year.contains("Reglement Caisse T01-000010"))        // reçu sans règlement
        #expect(year.contains("\t20260902\t706550\tVentes Restauration A Emporter 5.5%\t\t\tT01-000005\t")) // 23 h 30 UTC daté du jour UTC
        #expect(!year.contains("\r"))
        let reversed = reference.exports[1]
        #expect(reversed.fileName == "987654321FEC20260903.txt")
    }

    @Test func anUnreadableReceiptDateStopsTheExportInsteadOfDroppingTheReceipt() throws {
        let db = try SQLiteDatabase(path: ":memory:")
        try LocalMigrator.migrate(db)
        try db.run(
            """
            INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid)
            VALUES (?, 'T01', 'T01-000001', ?, 1, 1100, 1000, '{"10":100}', '', '', 'pas une date', 0)
            """,
            [.uuid(UUID()), .uuid(UUID())]
        )
        #expect(throws: APIError.self) {
            _ = try LocalFecExporter(db: db).export(from: .distantPast, to: .distantFuture, siren: "123456789")
        }
    }
}
