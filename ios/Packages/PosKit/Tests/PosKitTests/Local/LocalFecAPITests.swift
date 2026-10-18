import Foundation
import Testing
@testable import PosKit

@Suite("LocalPosAPI : export FEC")
struct LocalFecAPITests {
    private struct Row {
        let num: String
        let account: String
        let debit: Int
        let credit: Int
    }

    /// API sur fichier pilotée par une horloge de test, avec une seconde connexion pour relire les tables.
    private func makeAPIWithDatabase(pin: String = "1234") async throws -> (LocalPosAPI, SQLiteDatabase, LocalTestClock) {
        let clock = LocalTestClock("2026-10-12T12:00:00Z")
        let path = temporaryDatabasePath()
        let api = try LocalPosAPI(path: path, clock: { clock.now }, calendar: localTestCalendar)
        _ = try await api.login(pin: pin)
        return (api, try SQLiteDatabase(path: path), clock)
    }

    private func rows(_ data: Data) -> [Row] {
        String(decoding: data, as: UTF8.self).split(separator: "\n").dropFirst().map { line in
            let c = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            return Row(num: c[2], account: c[4], debit: Int(c[11].replacingOccurrences(of: ".", with: "")) ?? -1,
                       credit: Int(c[12].replacingOccurrences(of: ".", with: "")) ?? -1)
        }
    }

    @Test func realSalesExportBalancedEntriesAndTheExportIsJournaled() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        let burger = try await localProduct(api, "Burger Gourmet Rossini") // 19,50 €
        try await seatTable(api, "T1", items: [localInput(burger)])
        let first = try #require(try await api.activeOrder(table: "T1"))
        _ = try await api.pay(localPayment(first, table: "T1", tenders: [localTender(.creditCard, 2150)], tip: 200))
        clock.advance(600)
        try await seatTable(api, "T2", items: [localInput(burger)])
        let second = try #require(try await api.activeOrder(table: "T2"))
        _ = try await api.pay(localPayment(second, table: "T2", tenders: [localTender(.cash, 1950)]))
        let secondReceipt = try #require(try db.query("SELECT Id FROM FiscalReceipts WHERE ReceiptNumber = 'T01-000002'").first?.uuid("Id"))
        clock.advance(600)
        _ = try await api.voidReceipt(receiptId: secondReceipt)

        let from = try #require(SQLDate.parse("2026-10-01T00:00:00.000Z"))
        let (fileName, data) = try await api.exportFec(from: from, to: clock.now, siren: "123456789")

        #expect(fileName == "123456789FEC20261012.txt")
        let r = rows(data)
        #expect(Set(r.map(\.num)) == ["0000001", "0000002", "0000003"])
        for num in Set(r.map(\.num)) {
            let entry = r.filter { $0.num == num }
            #expect(entry.reduce(0) { $0 + $1.debit } == entry.reduce(0) { $0 + $1.credit }, "écriture \(num) déséquilibrée")
        }
        #expect(r.contains { $0.num == "0000001" && $0.account == "468600" && $0.credit == 200 })
        #expect(r.contains { $0.num == "0000003" && $0.account == "530000" && $0.credit == 1950 })

        let journal = try db.query("SELECT PayloadJson, TerminalId, OperatorId FROM JournalEntries WHERE EventType = 'FEC_EXPORTED'")
        #expect(journal.count == 1)
        #expect(journal.first?.string("PayloadJson")
            == "{\"StartDateUtc\":\"2026-10-01T00:00:00+00:00\",\"EndDateUtc\":\"2026-10-12T12:20:00+00:00\",\"Siren\":\"123456789\",\"Company\":null,\"FileName\":\"123456789FEC20261012.txt\"}")
        #expect(journal.first?.string("TerminalId") == nil)
        #expect(journal.first?.uuid("OperatorId") != nil)
        #expect(try LocalFiscalRepository(db: db).verifyChains().isEmpty)
    }

    @Test func anEmptyPeriodWithoutSirenGivesTheHeaderOnly() async throws {
        let (api, _, clock) = try await makeAPIWithDatabase()
        let (fileName, data) = try await api.exportFec(from: clock.now, to: clock.now, siren: "")
        #expect(fileName == "000000000FEC20261012.txt")
        #expect(data == Data((LocalFecFormat.header + "\n").utf8))
    }

    @Test func aWaiterCannotExportTheFecAndNothingIsJournaled() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase(pin: "2468")
        await #expect(throws: APIError.self) {
            _ = try await api.exportFec(from: clock.now.addingTimeInterval(-86_400), to: clock.now, siren: "123456789")
        }
        #expect(try db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'FEC_EXPORTED'").isEmpty)
    }

    /// Même littéral que le test .NET `SystemTextJson_DefaultEscaping_MatchesTheIpadFiscalJsonString`.
    private static let escaped = "\"1\\u007F\\uD83D\\uDE00\\u2028\\u00E9\""

    @Test func fiscalJSONStringEscapesLikeSystemTextJson() {
        #expect(FiscalJSON.string("1\u{7F}😀\u{2028}é") == Self.escaped)
    }

    @Test func companyIsEscapedIdenticallyInTheJournalPayload() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        _ = try await api.exportFec(from: clock.now, to: clock.now, siren: "", company: "1\u{7F}😀\u{2028}é")
        let payload = try #require(try db.query("SELECT PayloadJson FROM JournalEntries WHERE EventType = 'FEC_EXPORTED'").first?.string("PayloadJson"))
        #expect(payload.contains("\"Company\":" + Self.escaped))
    }

    @Test func anInvalidSirenIsRefusedBeforeAnythingIsWritten() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        for siren in ["12345678", "1234567890", "../x", "12345678A", "١٢٣٤٥٦٧٨٩", "123 456 789"] {
            await #expect(throws: APIError.server(status: 400, message: L10n.string("fiscal.fec_error_invalid_siren")), Comment(rawValue: siren)) {
                _ = try await api.exportFec(from: clock.now, to: clock.now, siren: siren)
            }
        }
        #expect(try db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'FEC_EXPORTED'").isEmpty)
        let (fileName, _) = try await api.exportFec(from: clock.now, to: clock.now, siren: " 123456789 ")
        #expect(fileName == "123456789FEC20261012.txt")
    }

    @Test func aFailedExportWritesNoJournalEntry() async throws {
        let (api, db, clock) = try await makeAPIWithDatabase()
        try db.run(
            """
            INSERT INTO FiscalReceipts (Id, TerminalId, ReceiptNumber, OrderId, SequenceNumber, TotalTtcAmount, TotalHtAmount, TaxBreakdownJson,
                PreviousSignatureHash, SignatureHash, CreatedAtUtc, IsVoid)
            VALUES (?, 'T01', 'T01-000001', ?, 1, 1100, 1000, '{"10":100}', '', '', 'pas une date', 0)
            """,
            [.uuid(UUID()), .uuid(UUID())]
        )
        await #expect(throws: APIError.self) {
            _ = try await api.exportFec(from: clock.now.addingTimeInterval(-86_400), to: clock.now, siren: "123456789")
        }
        #expect(try db.query("SELECT 1 AS x FROM JournalEntries WHERE EventType = 'FEC_EXPORTED'").isEmpty)
    }

    @Test func theJournalKeepsAnEmptySirenAndTheCompanyAsGiven() async throws {
        let (api, db, _) = try await makeAPIWithDatabase()
        let from = try #require(SQLDate.parse("2026-10-01T00:00:00.000Z"))
        let to = try #require(SQLDate.parse("2026-10-12T12:00:00.000Z"))
        _ = try await api.exportFec(from: from, to: to, siren: "", company: "Chez \"Marie\" & Cie")
        _ = try await api.exportFec(from: from, to: to, siren: "")
        let payloads = try db.query("SELECT PayloadJson FROM JournalEntries WHERE EventType = 'FEC_EXPORTED' ORDER BY ChainSequence")
            .compactMap { $0.string("PayloadJson") }
        #expect(payloads == [
            #"{"StartDateUtc":"2026-10-01T00:00:00+00:00","EndDateUtc":"2026-10-12T12:00:00+00:00","Siren":"","Company":"Chez \u0022Marie\u0022 \u0026 Cie","FileName":"000000000FEC20261012.txt"}"#,
            #"{"StartDateUtc":"2026-10-01T00:00:00+00:00","EndDateUtc":"2026-10-12T12:00:00+00:00","Siren":"","Company":null,"FileName":"000000000FEC20261012.txt"}"#,
        ])
    }

    @Test func sirenInputIsNormalizedToAsciiDigitsWithoutSpaces() {
        #expect(Siren.normalized("١٢٣ ٤٥٦ ٧٨٩") == "123456789")
        #expect(Siren.normalized(" 123 456 789 ") == "123456789")
        #expect(Siren.normalized("12A/..") == "12A/..")
        #expect(Siren.isValid("") && Siren.isValid("  ") && Siren.isValid(" 123456789 "))
        #expect(!Siren.isValid("12345678") && !Siren.isValid("١٢٣٤٥٦٧٨٩") && !Siren.isValid("../x"))
    }
}
