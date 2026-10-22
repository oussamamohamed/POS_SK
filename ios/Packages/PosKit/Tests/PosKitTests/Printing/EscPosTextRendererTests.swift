import Foundation
import Testing
@testable import PosKit

@Suite struct EscPosTextRendererTests {
    @Test func wrapMatchesDotNet() throws {
        for w in try TicketReferenceFixture.load().wrap {
            #expect(EscPosTextRenderer.wrap(w.text, width: w.width).map { String(decoding: $0, as: UTF16.self) } == w.lines, "« \(w.text) »")
        }
    }

    @Test func renderIsByteIdenticalToDotNet() throws {
        for r in try TicketReferenceFixture.load().textRender {
            let doc = try JSONDecoder().decode(TicketDocument.self, from: r.documentData)
            #expect(hex(EscPosTextRenderer.render(doc, paperWidthMm: r.paperWidthMm, openCashDrawer: r.drawer)) == r.expectedHex, "\(r.name)")
        }
    }

    /// Le .NET compte et encode en unités UTF-16 : un emoji vaut deux unités, donc deux `?`.
    @Test func emojiCountsAsTwoUnitsAndPrintsTwoQuestionMarks() {
        let doc = TicketDocument(language: "fr", rightToLeft: false, lines: [.columns(label: "😀", value: "1")])
        let out = EscPosTextRenderer.render(doc, paperWidthMm: 58, openCashDrawer: false)
        #expect(out.filter { $0 == 0x3F }.count == 2)
        #expect(!out.contains(0xF0))
    }

    @Test func columnsForPaperWidth() {
        #expect(EscPosTextRenderer.columns(forPaperWidthMm: 58) == 32)
        #expect(EscPosTextRenderer.columns(forPaperWidthMm: 80) == 48)
    }
}
