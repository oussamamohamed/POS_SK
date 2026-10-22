import Foundation
import Testing
@testable import PosKit

@Suite struct TicketRasterRendererTests {
    private func doc(_ lines: [TicketLine], rtl: Bool = false) -> TicketDocument { TicketDocument(language: rtl ? "ar" : "fr", rightToLeft: rtl, lines: lines) }
    private func ink(_ image: MonoImage, columns: Range<Int>, rows: Range<Int>? = nil) -> Int {
        (rows ?? 0..<image.height).reduce(0) { total, y in
            total + columns.reduce(0) { $0 + (((image.bits[y * image.bytesPerRow + $1 / 8] >> (7 - UInt8($1 % 8))) & 1) == 1 ? 1 : 0) }
        }
    }

    @Test func widthsFollowPaper() {
        #expect(TicketRasterRenderer.dots(forPaperWidthMm: 58) == 384)
        #expect(TicketRasterRenderer.dots(forPaperWidthMm: 80) == 576)
    }

    @Test func textProducesInkAndAWholeNumberOfBytesPerRow() {
        let image = TicketRasterRenderer.render(doc([.text("Bonjour — 12.50 €", align: .start)]), widthDots: 576)[0]
        #expect(image.width == 576); #expect(image.bits.count == image.bytesPerRow * image.height)
        #expect(ink(image, columns: 0..<576) > 100)
    }

    @Test func separatorIsSixteenDotsPlusMargins() {
        let image = TicketRasterRenderer.render(doc([.separator()]), widthDots: 384)[0]
        #expect(image.height == 16 + 16)
        #expect(ink(image, columns: 0..<384) > 0)
        // Trait de 2 points centré sur y + 8 (marge 8 + 8 = 16) : rien hors des lignes 15 à 16, rien dans les marges latérales.
        #expect(ink(image, columns: 0..<384, rows: 0..<15) == 0)
        #expect(ink(image, columns: 0..<384, rows: 17..<32) == 0)
        #expect(ink(image, columns: 0..<8) == 0); #expect(ink(image, columns: 376..<384) == 0)
    }

    @Test func cutSeparatorSplitsSegmentsAndDropsEmptyOnes() {
        let images = TicketRasterRenderer.render(doc([.text("A"), .separator(cut: true), .text("B"), .separator(cut: true)]), widthDots: 384)
        #expect(images.count == 2)
    }

    @Test func rightAlignedTextLeansRightAndRtlStartLeansRight() {
        let end = TicketRasterRenderer.render(doc([.text("Total", align: .end)]), widthDots: 576)[0]
        #expect(ink(end, columns: 288..<576) > ink(end, columns: 0..<288))
        let arabicStart = TicketRasterRenderer.render(doc([.text("مرحبا بكم", align: .start)], rtl: true), widthDots: 576)[0]
        #expect(ink(arabicStart, columns: 288..<576) > ink(arabicStart, columns: 0..<288))
    }

    @Test func columnsPutValueAtTheOppositeEdgeInRtl() {
        let ltr = TicketRasterRenderer.render(doc([.columns(label: "Total", value: "12.50")]), widthDots: 576)[0]
        #expect(ink(ltr, columns: 400..<576) > 0); #expect(ink(ltr, columns: 0..<100) > 0)
        let rtl = TicketRasterRenderer.render(doc([.columns(label: "المجموع", value: "12.50")], rtl: true), widthDots: 576)[0]
        #expect(ink(rtl, columns: 0..<176) > 0); #expect(ink(rtl, columns: 400..<576) > 0)
    }

    /// Une colonne vide à la fois : chaque élément est du bon côté, et le miroir RTL l'inverse.
    @Test func columnsMirrorLabelAndValueSidesInRtl() {
        let half = 0..<288, otherHalf = 288..<576
        for rtl in [false, true] {
            let valueOnly = TicketRasterRenderer.render(doc([.columns(label: " ", value: "12.50")], rtl: rtl), widthDots: 576)[0]
            #expect(ink(valueOnly, columns: rtl ? half : otherHalf) > 0, "valeur, rtl=\(rtl)")
            #expect(ink(valueOnly, columns: rtl ? otherHalf : half) == 0, "valeur, rtl=\(rtl)")
            let labelOnly = TicketRasterRenderer.render(doc([.columns(label: rtl ? "المجموع" : "Total", value: " ")], rtl: rtl), widthDots: 576)[0]
            #expect(ink(labelOnly, columns: rtl ? otherHalf : half) > 0, "étiquette, rtl=\(rtl)")
            #expect(ink(labelOnly, columns: rtl ? half : otherHalf) == 0, "étiquette, rtl=\(rtl)")
        }
    }

    /// Orientation : un « L » a sa barre horizontale en bas (image retournée) et à droite du fût (image en miroir).
    @Test func glyphsAreUprightAndNotMirrored() throws {
        let image = TicketRasterRenderer.render(doc([.text("L", large: true)]), widthDots: 384)[0]
        let inked = (0..<image.height).filter { ink(image, columns: 0..<384, rows: $0..<($0 + 1)) > 0 }
        let top = try #require(inked.first), bottom = try #require(inked.last)
        #expect(ink(image, columns: 0..<384, rows: (bottom - 2)..<(bottom + 1)) > ink(image, columns: 0..<384, rows: top..<(top + 3)))
        let stem = (0..<384).first { ink(image, columns: $0..<($0 + 1), rows: top..<(top + 3)) > 0 } ?? 0
        #expect(ink(image, columns: (stem + 8)..<384, rows: (bottom - 2)..<(bottom + 1)) > 0)
    }

    @Test func renderingIsDeterministic() {
        let d = doc([.text("Déterministe", bold: true, large: true), .columns(label: "a", value: "b")])
        #expect(TicketRasterRenderer.render(d, widthDots: 576) == TicketRasterRenderer.render(d, widthDots: 576))
    }

    @Test func payloadUsesTextOnlyForLtrTextModePrinters() {
        let ltr = doc([.text("Hello")]); let arabic = doc([.text("مرحبا")], rtl: true)
        var printer = Printer(name: "P", ipAddress: "10.0.0.1", textMode: true)
        #expect(TicketPayload.build(printer: printer, document: ltr, openCashDrawer: false).starts(with: [0x1B, 0x40, 0x1B, 0x74, 19]))
        #expect(TicketPayload.build(printer: printer, document: arabic, openCashDrawer: false).starts(with: [0x1B, 0x40, 0x1D, 0x76, 0x30]))
        printer.textMode = false
        #expect(TicketPayload.build(printer: printer, document: ltr, openCashDrawer: false).starts(with: [0x1B, 0x40, 0x1D, 0x76, 0x30]))
    }

    @Test func bundledFontsLoad() {
        #expect(TicketRasterRenderer.fontsAvailable)
    }
}
