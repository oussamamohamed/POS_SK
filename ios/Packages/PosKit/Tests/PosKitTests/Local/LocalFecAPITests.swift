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
}
