import Foundation
import Testing
@testable import PosKit

@Suite("FEC : écritures (miroir de GenerateFecAsync)")
struct LocalFecWriterTests {
    private struct Row: Equatable {
        let num: String
        let account: String
        let debit: Int
        let credit: Int
        let label: String
    }

    private let noon = SQLDate.parse("2026-09-03T12:00:00.000Z")!

    private func receipt(_ number: String = "POS01-000001", at date: Date? = nil, ttc: Int, ht: Int, tax: String, htJson: String? = nil,
                         void: Bool = false, tenders: [(PaymentMethod, Int)]) -> FecReceipt {
        FecReceipt(
            receiptNumber: number, createdAtUtc: date ?? noon, ttcCents: ttc, htCents: ht, taxBreakdownJson: tax, htBreakdownJson: htJson,
            voidsReceipt: void, tenders: tenders.map { FecTender(method: $0.0.rawValue, amountCents: $0.1) }
        )
    }

    private func export(_ receipts: [FecReceipt]) -> FecExport {
        LocalFecWriter.export(receipts: receipts, from: noon.addingTimeInterval(-86_400), to: noon.addingTimeInterval(86_400), siren: "123456789")
    }

    private func rows(_ result: FecExport) -> [Row] {
        String(decoding: result.data, as: UTF8.self).split(separator: "\n").dropFirst().map { line in
            let c = line.split(separator: "\t", omittingEmptySubsequences: false).map(String.init)
            return Row(num: c[2], account: c[4], debit: cents(c[11]), credit: cents(c[12]), label: c[10])
        }
    }

    private func cents(_ amount: String) -> Int { Int(amount.replacingOccurrences(of: ".", with: "")) ?? -1 }

    private func balancedPerEntry(_ rows: [Row]) -> Bool {
        Dictionary(grouping: rows, by: \.num).values.allSatisfy { e in e.reduce(0) { $0 + $1.debit } == e.reduce(0) { $0 + $1.credit } }
    }

    @Test func aSaleWithTwoTendersBalancesAndEndsWithANewline() {
        let result = export([receipt(ttc: 3850, ht: 3500, tax: "{\"10.0\": 350}", tenders: [(.creditCard, 2000), (.cash, 1850)])])
        let r = rows(result)
        #expect(r.map(\.account) == ["706100", "445710", "512000", "530000"])
        #expect(r[0] == Row(num: "0000001", account: "706100", debit: 0, credit: 3500, label: "Vente POS01-000001 (HT 10%)"))
        #expect(r[1].label == "TVA Collectee 10% POS01-000001")
        #expect(r[2] == Row(num: "0000001", account: "512000", debit: 2000, credit: 0, label: "Reglement CreditCard POS01-000001"))
        #expect(result.totalRecords == 4 && result.totalDebitCents == 3850 && result.totalCreditCents == 3850)
        #expect(result.fileName == "123456789FEC20260904.txt")
        let text = String(decoding: result.data, as: UTF8.self)
        #expect(text.hasPrefix(LocalFecFormat.header + "\n"))
        #expect(text.hasSuffix("\tEUR\n"))
        #expect(!text.contains("\r"))
    }

    @Test func aVoidAtSeveralRatesIsDebitedPerRate() {
        let r = rows(export([receipt("POS01-VOID-000002", ttc: -2300, ht: -2000, tax: "{\"10.0\": -100, \"20.0\": -200}", void: true, tenders: [(.cash, -2300)])]))
        #expect(r.first { $0.account == "706100" }?.debit == 1000)
        #expect(r.first { $0.account == "706200" }?.debit == 1000)
        #expect(r.first { $0.account == "445720" }?.debit == 200)
        #expect(r.first { $0.account == "530000" }?.credit == 2300)
        #expect(balancedPerEntry(r))
    }

    @Test(arguments: [false, true])
    func theTipGoesToTheTipsAccountSoTheEntryBalances(isVoid: Bool) {
        let sign = isVoid ? -1 : 1
        let r = rows(export([receipt(ttc: sign * 1100, ht: sign * 1000, tax: "{\"10.0\": \(sign * 100)}", void: isVoid, tenders: [(.creditCard, sign * 1300)])]))
        let tip = r.first { $0.account == "468600" }
        #expect((isVoid ? tip?.debit : tip?.credit) == 200)
        #expect(tip?.label.hasPrefix("Excedent de reglement / pourboire") == true)
        #expect(balancedPerEntry(r))
    }

    @Test func aNegativeTotalWithoutVoidLinkIsStillAVoid() {
        let r = rows(export([receipt(ttc: -1100, ht: -1000, tax: "{\"10\": -100}", tenders: [(.cash, -1100)])]))
        #expect(r.first { $0.account == "706100" }?.debit == 1000)
        #expect(r.first { $0.account == "530000" }?.credit == 1100)
    }

    @Test(arguments: [false, true])
    func aReceiptWithoutTendersSettlesToCash(isVoid: Bool) {
        let sign = isVoid ? -1 : 1
        let r = rows(export([receipt(ttc: sign * 1100, ht: sign * 1000, tax: "{\"10.0\": \(sign * 100)}", void: isVoid, tenders: [])]))
        let cash = r.first { $0.account == "530000" }
        #expect(cash?.label == "Reglement Caisse POS01-000001")
        #expect((isVoid ? cash?.credit : cash?.debit) == 1100)
        #expect((isVoid ? cash?.debit : cash?.credit) == 0)
        #expect(balancedPerEntry(r))
    }

    @Test func aZeroRateGroupHasNoVatLine() {
        let r = rows(export([receipt(ttc: 1700, ht: 1500, tax: "{\"0\": 0, \"20\": 200}", tenders: [(.creditCard, 1700)])]))
        #expect(r.map(\.account) == ["706000", "706200", "445720", "512000"])
        #expect(r[0].label == "Vente POS01-000001 (HT 0%)")
    }

    @Test func theRateInFreeTextLabelsUsesACommaButAccountNamesKeepTheDot() {
        let result = export([receipt(ttc: 1055, ht: 1000, tax: "{\"5.5\": 55}", tenders: [(.cash, 1055)])])
        let r = rows(result)
        #expect(r[0].label == "Vente POS01-000001 (HT 5,5%)")
        #expect(r[1].label == "TVA Collectee 5,5% POS01-000001")
        #expect(String(decoding: result.data, as: UTF8.self).contains("Ventes Restauration A Emporter 5.5%"))
    }

    @Test func roomChargeSettlesToTheRoomAccount() {
        let r = rows(export([receipt(ttc: 600, ht: 500, tax: "{\"20\":100}", tenders: [(.roomCharge, 600)])]))
        #expect(r.last == Row(num: "0000001", account: "411000", debit: 600, credit: 0, label: "Reglement RoomCharge POS01-000001"))
    }

    @Test func periodBoundsAreInclusiveSwappedWhenReversedAndNumberingRestarts() throws {
        let first = try #require(SQLDate.parse("2026-09-01T20:45:00.000Z"))
        let last = try #require(SQLDate.parse("2026-09-03T09:00:00.000Z"))
        let receipts = [
            receipt("A-1", at: first.addingTimeInterval(-0.001), ttc: 1100, ht: 1000, tax: "{\"10\":100}", tenders: [(.cash, 1100)]),
            receipt("A-2", at: first, ttc: 1100, ht: 1000, tax: "{\"10\":100}", tenders: [(.cash, 1100)]),
            receipt("A-3", at: last, ttc: 1100, ht: 1000, tax: "{\"10\":100}", tenders: [(.cash, 1100)]),
            receipt("A-4", at: last.addingTimeInterval(0.001), ttc: 1100, ht: 1000, tax: "{\"10\":100}", tenders: [(.cash, 1100)]),
        ]
        let result = LocalFecWriter.export(receipts: receipts, from: last, to: first, siren: " 987654321 ")
        let text = String(decoding: result.data, as: UTF8.self)
        #expect(result.fileName == "987654321FEC20260903.txt")
        #expect(Set(rows(result).map(\.num)) == ["0000001", "0000002"])
        #expect(text.contains("\tA-2\t") && text.contains("\tA-3\t"))
        #expect(!text.contains("\tA-1\t") && !text.contains("\tA-4\t"))
    }

    @Test func anEmptyPeriodGivesTheHeaderOnly() {
        let result = LocalFecWriter.export(receipts: [], from: noon, to: noon, siren: "")
        #expect(result.data == Data((LocalFecFormat.header + "\n").utf8))
        #expect(result.fileName == "000000000FEC20260903.txt")
        #expect(result.totalRecords == 0)
    }
}
