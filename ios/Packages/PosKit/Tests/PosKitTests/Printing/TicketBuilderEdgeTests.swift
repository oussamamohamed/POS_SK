import Foundation
import Testing
@testable import PosKit

@Suite struct TicketBuilderEdgeTests {
    private let paris = TicketContext(timeZone: TimeZone(identifier: "Europe/Paris")!, softwareVersion: "1.0.0")
    private let order = TicketOrder(destination: .takeaway, tableNumber: "", pickupNumber: nil, pickupBuzzer: nil, items: [])
    private let printer = Printer(name: "Bar", ipAddress: "10.0.0.7", port: 9100, paperWidthMm: 58)

    private func date(_ iso: String) -> Date { ISO8601DateFormatter().date(from: iso)! }

    private func period(taxes: String, tenders: String) -> TicketPeriodClosure {
        TicketPeriodClosure(terminalId: "T01", isMonthly: true, periodKey: "2025-06", closureSequence: 1, periodStartUtc: nil,
                            periodEndUtc: date("2025-06-30T21:59:59Z"), totalTtcCents: 100, totalHtCents: 90,
                            taxesSummaryJson: taxes, tenderTotalsJson: tenders, perpetualGrandTotalCents: 100,
                            dailyClosureCount: 1, signatureHash: "h", sealedByUserName: "Jean")
    }

    private func columns(_ doc: TicketDocument) -> [String: String] {
        Dictionary(doc.lines.compactMap { if case let .columns(l, v) = $0 { (l, v) } else { nil } }, uniquingKeysWith: { a, _ in a })
    }

    @Test func unknownLanguageFallsBackToEnglish() {
        let doc = TicketBuilder.testPage(printer: printer, language: "de", now: Date())
        #expect(doc.language == "en")
        #expect(!doc.rightToLeft)
    }

    @Test func onlyArabicIsRightToLeft() {
        for lang in TicketTexts.supportedLanguages {
            #expect(TicketBuilder.testPage(printer: printer, language: lang, now: Date()).rightToLeft == (lang == "ar"))
        }
    }

    @Test func duplicateNoticeAppearsOnlyOnce() {
        let doc = TicketBuilder.pickupCoupon(order: order, pickupNumber: "A1", buzzer: nil, language: "fr", now: Date())
        let twice = TicketBuilder.withDuplicateNotice(TicketBuilder.withDuplicateNotice(doc, duplicateNumber: 1), duplicateNumber: 2)
        let notices = twice.lines.filter { if case let .text(t, _, _, _) = $0 { t.contains("DUPLICATA") } else { false } }
        #expect(notices == [.text("DUPLICATA n\u{00B0}2", align: .center, bold: true, large: true)])
        #expect(twice.lines.first == notices.first)
        #expect(twice.lines.count == doc.lines.count + 1)
    }

    @Test func invalidPeriodJSONStillProducesADocument() {
        for (taxes, tenders) in [("not json{", ""), ("{\"10\":1.5}", "[1]"), ("null", "{\"Cash\":null}"), ("{\"abc\":5}", "{\" 1 \":5}")] {
            let doc = TicketBuilder.periodClosure(period(taxes: taxes, tenders: tenders), data: .empty, language: "fr", settings: nil, context: paris)
            #expect(!doc.lines.isEmpty)
            #expect(columns(doc)["Du"] == "-")
            #expect(columns(doc)["10 %"] == nil)
        }
        // Clé numérique avec espaces : acceptée comme par `Enum.TryParse`.
        let spaced = TicketBuilder.periodClosure(period(taxes: "", tenders: "{\" 1 \":5}"), data: .empty, language: "fr", settings: nil, context: paris)
        #expect(columns(spaced)["Carte Bancaire (CB)"] == "0.05")
    }

    @Test func localTimesFollowParisSummerAndWinterOffsets() {
        let summary = TicketReportSummary(terminalId: "T01", periodStartUtc: nil, periodEndUtc: date("2025-07-15T10:00:00Z"),
                                          totalSalesTtcCents: 0, totalSalesHtCents: 0, receiptCount: 0,
                                          vatBreakdownCents: [], paymentTotalsCents: [], perpetualGrandTotalCents: 0)
        let summer = TicketBuilder.xReport(summary, data: .empty, language: "fr", now: date("2025-07-15T10:00:00Z"), settings: nil, context: paris)
        let winter = TicketBuilder.xReport(summary, data: .empty, language: "fr", now: date("2025-01-15T10:00:00Z"), settings: nil, context: paris)
        #expect(columns(summer)["Imprimé le"] == "15/07/2025 12:00")
        #expect(columns(winter)["Imprimé le"] == "15/01/2025 11:00")
    }

    @Test func headerAddressSplitsOnLineFeedAndTrimsCarriageReturns() {
        let receipt = TicketReceipt(receiptNumber: "T01-1", createdAtUtc: Date(), terminalId: "T01", totalTtcCents: 0, totalHtCents: 0,
                                    taxBreakdownJson: "{}", signatureHash: "h")
        let settings = TicketSettings(companyName: "Chez Léa", addressLines: " 5 Rue des Lilas\r\n\n\t69002 Lyon \r\n", siret: "1",
                                      vatNumber: "FR1", certificateNumber: nil)
        let doc = TicketBuilder.receiptFallback(receipt, language: "fr", settings: settings, duplicateNumber: nil, context: paris)
        #expect(Array(doc.lines[2...3]) == [.text("5 Rue des Lilas", align: .center), .text("69002 Lyon", align: .center)])
        #expect(doc.lines[4] == .text("SIRET: 1", align: .center))
    }
}
