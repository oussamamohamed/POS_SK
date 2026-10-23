import CoreGraphics
import CoreText
import Foundation

/// Port de `EscPosRasterRenderer.cs` : dessine un `TicketDocument` en images 1 bit (mise en forme bidi et liaison arabe par CoreText).
/// Mêmes constantes de mise en page que le .NET ; le rendu n'est pas identique octet pour octet (polices et lissage).
enum TicketRasterRenderer {
    private static let margin: CGFloat = 8
    private static let normalSize: CGFloat = 24
    private static let largeSize: CGFloat = 40

    static func dots(forPaperWidthMm mm: Int) -> Int { mm == 58 ? 384 : 576 }

    /// Vrai si les quatre polices Noto embarquées se chargent.
    static var fontsAvailable: Bool { EmbeddedFonts.shared.complete }

    static func render(_ doc: TicketDocument, widthDots: Int) -> [MonoImage] {
        precondition(widthDots > 16 && widthDots % 8 == 0, "Largeur de rendu invalide : \(widthDots) points (multiple de 8 supérieur à 16 requis)")
        var segments: [[TicketLine]] = [[]]
        for line in doc.lines {
            if case .separator(cut: true) = line { segments.append([]) } else { segments[segments.count - 1].append(line) }
        }
        return segments.filter { !$0.isEmpty }.map { renderSegment($0, rtl: doc.rightToLeft, width: widthDots) }
    }

    // MARK: - Segment

    /// `draw(context, yHaut, hauteurImage)` : `y` croît vers le bas, converti en coordonnées CoreGraphics (origine en bas) au dessin.
    private struct Block {
        let height: CGFloat
        let draw: (CGContext, CGFloat, CGFloat) -> Void
    }

    private static func renderSegment(_ lines: [TicketLine], rtl: Bool, width: Int) -> MonoImage {
        let content = CGFloat(width) - 2 * margin
        let blocks = lines.map { layout($0, rtl: rtl, content: content) }
        let total: CGFloat = blocks.reduce(0) { $0 + $1.height }
        let height = Int(total.rounded(.up)) + 2 * Int(margin)
        guard let context = CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width,
                                      space: CGColorSpaceCreateDeviceGray(), bitmapInfo: CGImageAlphaInfo.none.rawValue)
        else { return MonoImage(width: width, height: height, bits: [UInt8](repeating: 0, count: width / 8 * height)) }
        context.setFillColor(gray: 1, alpha: 1)
        context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        context.setFillColor(gray: 0, alpha: 1)
        context.setShouldAntialias(true)
        context.textMatrix = .identity
        var y = margin
        for block in blocks {
            block.draw(context, y, CGFloat(height))
            y += block.height
        }
        return threshold(context, width: width, height: height)
    }

    private static func layout(_ line: TicketLine, rtl: Bool, content: CGFloat) -> Block {
        switch line {
        case let .text(text, align, bold, large):
            let frame = Frame(text, bold: bold, large: large, alignment: alignment(align, rtl: rtl), rtl: rtl, maxWidth: content)
            return Block(height: frame.height) { ctx, y, h in frame.draw(ctx, x: margin, y: y, imageHeight: h) }
        case let .columns(label, value):
            return columns(label: label, value: value, rtl: rtl, content: content)
        case .separator:
            // Ligne pointillée de 2 points d'épaisseur centrée sur y + 8, motif 6 pleins / 4 vides.
            return Block(height: 16) { ctx, y, h in
                for x in stride(from: margin, to: margin + content, by: 10) {
                    ctx.fill(CGRect(x: x, y: h - (y + 9), width: min(6, margin + content - x), height: 2))
                }
            }
        }
    }

    private static func columns(label: String, value: String, rtl: Bool, content: CGFloat) -> Block {
        let valueFrame = Frame(value, bold: false, large: false, alignment: rtl ? .left : .right, rtl: rtl, maxWidth: content)
        let labelWidth = max(content / 3, content - valueFrame.measuredWidth - 16)
        let labelFrame = Frame(label, bold: false, large: false, alignment: rtl ? .right : .left, rtl: rtl, maxWidth: labelWidth)
        let labelX = rtl ? margin + content - labelWidth : margin
        return Block(height: max(labelFrame.height, valueFrame.height)) { ctx, y, h in
            labelFrame.draw(ctx, x: labelX, y: y, imageHeight: h)
            valueFrame.draw(ctx, x: margin, y: y, imageHeight: h)
        }
    }

    /// Début = gauche en LTR, droite en RTL ; fin = l'inverse.
    private static func alignment(_ align: TicketAlign, rtl: Bool) -> CTTextAlignment {
        switch align {
        case .center: .center
        case .end: rtl ? .left : .right
        case .start: rtl ? .right : .left
        }
    }

    // MARK: - Texte

    /// Bloc de texte mis en forme par `CTFramesetter` dans une largeur maximale.
    private struct Frame {
        let framesetter: CTFramesetter
        let maxWidth: CGFloat
        let height: CGFloat
        let measuredWidth: CGFloat

        init(_ text: String, bold: Bool, large: Bool, alignment: CTTextAlignment, rtl: Bool, maxWidth: CGFloat) {
            let string = TicketRasterRenderer.attributed(text.isEmpty ? " " : text, bold: bold || large,
                                                         size: large ? largeSize : normalSize, alignment: alignment, rtl: rtl)
            framesetter = CTFramesetterCreateWithAttributedString(string)
            self.maxWidth = maxWidth
            let size = CTFramesetterSuggestFrameSizeWithConstraints(framesetter, CFRange(location: 0, length: 0), nil,
                                                                    CGSize(width: maxWidth, height: .greatestFiniteMagnitude), nil)
            height = size.height
            // Largeur de la ligne la plus longue, indépendante de l'alignement (MeasuredWidth de RichTextKit).
            let frame = Self.frame(framesetter, CGRect(x: 0, y: 0, width: maxWidth, height: height.rounded(.up) + 1))
            let ctLines: [CTLine] = (CTFrameGetLines(frame) as? [CTLine]) ?? []
            let lineWidths: [CGFloat] = ctLines.map { CGFloat(CTLineGetTypographicBounds($0, nil, nil, nil)) - CGFloat(CTLineGetTrailingWhitespaceWidth($0)) }
            measuredWidth = min(maxWidth, lineWidths.max() ?? 0)
        }

        func draw(_ context: CGContext, x: CGFloat, y: CGFloat, imageHeight: CGFloat) {
            // Le cadre CoreText commence en haut de son rectangle : bord haut à `y` (vers le bas), soit `imageHeight - y` en CoreGraphics.
            let boxHeight = height.rounded(.up) + 1
            CTFrameDraw(Self.frame(framesetter, CGRect(x: x, y: imageHeight - y - boxHeight, width: maxWidth, height: boxHeight)), context)
        }

        private static func frame(_ framesetter: CTFramesetter, _ rect: CGRect) -> CTFrame {
            CTFramesetterCreateFrame(framesetter, CFRange(location: 0, length: 0), CGPath(rect: rect, transform: nil), nil)
        }
    }

    private static func attributed(_ text: String, bold: Bool, size: CGFloat, alignment: CTTextAlignment, rtl: Bool) -> CFAttributedString {
        let result = NSMutableAttributedString()
        let black = CGColor(gray: 0, alpha: 1)
        for (run, arabic) in scriptRuns(text) {
            result.append(NSAttributedString(string: run, attributes: [
                NSAttributedString.Key(kCTFontAttributeName as String): EmbeddedFonts.shared.font(arabic: arabic, bold: bold, size: size),
                NSAttributedString.Key(kCTForegroundColorAttributeName as String): black
            ]))
        }
        var align = alignment
        var direction: CTWritingDirection = rtl ? .rightToLeft : .leftToRight
        let style = withUnsafePointer(to: &align) { alignPointer in
            withUnsafePointer(to: &direction) { directionPointer in
                let settings = [
                    CTParagraphStyleSetting(spec: .alignment, valueSize: MemoryLayout<CTTextAlignment>.size, value: alignPointer),
                    CTParagraphStyleSetting(spec: .baseWritingDirection, valueSize: MemoryLayout<CTWritingDirection>.size, value: directionPointer)
                ]
                return CTParagraphStyleCreate(settings, settings.count)
            }
        }
        result.addAttribute(NSAttributedString.Key(kCTParagraphStyleAttributeName as String), value: style, range: NSRange(location: 0, length: result.length))
        return result
    }

    /// Noto Sans n'a pas l'arabe et Noto Sans Arabic pas le latin : passages par écriture (unités UTF-16, comme le .NET),
    /// les espaces suivent le passage courant.
    static func scriptRuns(_ text: String) -> [(String, Bool)] {
        let units = Array(text.utf16)
        guard let first = units.first else { return [] }
        var runs: [(String, Bool)] = []
        var start = 0
        var current = isArabic(first)
        for i in units.indices.dropFirst() {
            if isWhiteSpace(units[i]) || isArabic(units[i]) == current { continue }
            runs.append((String(decoding: units[start..<i], as: UTF16.self), current))
            start = i
            current.toggle()
        }
        runs.append((String(decoding: units[start...], as: UTF16.self), current))
        return runs
    }

    private static func isArabic(_ u: UInt16) -> Bool {
        (0x0600...0x06FF).contains(u) || (0x0750...0x077F).contains(u) || (0x08A0...0x08FF).contains(u)
            || (0xFB50...0xFDFF).contains(u) || (0xFE70...0xFEFF).contains(u)
    }

    private static func isWhiteSpace(_ u: UInt16) -> Bool {
        Unicode.Scalar(u).map { $0.properties.isWhitespace } ?? false
    }

    // MARK: - Seuil

    /// Luminance < 128 → point noir, bit de poids fort à gauche. La première ligne du tampon est le haut de l'image.
    private static func threshold(_ context: CGContext, width: Int, height: Int) -> MonoImage {
        let bytesPerRow = width / 8
        var bits = [UInt8](repeating: 0, count: bytesPerRow * height)
        if let data = context.data {
            let stride = context.bytesPerRow
            let pixels = data.bindMemory(to: UInt8.self, capacity: stride * height)
            for y in 0..<height {
                for x in 0..<width where pixels[y * stride + x] < 128 {
                    bits[y * bytesPerRow + x / 8] |= UInt8(0x80 >> (x % 8))
                }
            }
        }
        return MonoImage(width: width, height: height, bits: bits)
    }
}

// MARK: - Polices

/// Descripteurs des polices Noto créés depuis les données du bundle, sans enregistrement global.
/// `@unchecked Sendable` : les descripteurs CoreText sont immuables et sûrs entre fils ; le singleton est initialisé
/// paresseusement une seule fois (`static let`, garanti atomique par Swift).
private final class EmbeddedFonts: @unchecked Sendable {
    static let shared = EmbeddedFonts()

    private let latin: CTFontDescriptor?
    private let latinBold: CTFontDescriptor?
    private let arabic: CTFontDescriptor?
    private let arabicBold: CTFontDescriptor?

    var complete: Bool { latin != nil && latinBold != nil && arabic != nil && arabicBold != nil }

    private init() {
        latin = Self.load("NotoSans-Regular")
        latinBold = Self.load("NotoSans-Bold")
        arabic = Self.load("NotoSansArabic-Regular")
        arabicBold = Self.load("NotoSansArabic-Bold")
    }

    /// Repli sur Helvetica si une police manque : le ticket reste imprimable (CoreText complète les glyphes absents).
    func font(arabic isArabic: Bool, bold: Bool, size: CGFloat) -> CTFont {
        let descriptor = isArabic ? (bold ? arabicBold : arabic) : (bold ? latinBold : latin)
        if let descriptor { return CTFontCreateWithFontDescriptor(descriptor, size, nil) }
        return CTFontCreateWithName((bold ? "Helvetica-Bold" : "Helvetica") as CFString, size, nil)
    }

    private static func load(_ name: String) -> CTFontDescriptor? {
        guard let url = Bundle.module.url(forResource: name, withExtension: "ttf"),
              let data = try? Data(contentsOf: url) else { return nil }
        return CTFontManagerCreateFontDescriptorFromData(data as CFData)
    }
}

// MARK: - Charge utile

/// Miroir de `PrinterTransport.BuildPayload` : texte si l'imprimante est en mode texte et le document LTR, sinon image
/// (l'arabe exige la mise en forme).
enum TicketPayload {
    static func build(printer: Printer, document: TicketDocument, openCashDrawer: Bool) -> [UInt8] {
        printer.textMode && !document.rightToLeft
            ? EscPosTextRenderer.render(document, paperWidthMm: printer.paperWidthMm, openCashDrawer: openCashDrawer)
            : EscPosCommands.build(TicketRasterRenderer.render(document, widthDots: TicketRasterRenderer.dots(forPaperWidthMm: printer.paperWidthMm)),
                                   openCashDrawer: openCashDrawer)
    }
}
