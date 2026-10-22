import Foundation

/// Port de `EscPosTextRenderer.cs` : rendu texte ESC/POS (PC858) d'un `TicketDocument` LTR.
/// Les longueurs et coupes sont en unités UTF-16, comme le .NET.
enum EscPosTextRenderer {
    private static let initSeq: [UInt8] = [0x1B, 0x40, 0x1B, 0x74, 0x13]
    private static let drawerKick: [UInt8] = [0x1B, 0x70, 0x00, 0x19, 0xFA]
    private static let feedAndCut: [UInt8] = [0x1D, 0x56, 0x42, 0x00]
    private static let space: UInt16 = 0x20

    static func columns(forPaperWidthMm mm: Int) -> Int { mm == 58 ? 32 : 48 }

    static func render(_ doc: TicketDocument, paperWidthMm: Int, openCashDrawer: Bool) -> [UInt8] {
        let cols = columns(forPaperWidthMm: paperWidthMm)
        var bytes = initSeq
        var lines = doc.lines
        // La coupe finale est toujours ajoutée : une coupe en fin de document ferait une coupe à vide.
        while case .separator(cut: true)? = lines.last { lines.removeLast() }
        for line in lines {
            switch line {
            case let .text(text, align, bold, large):
                bytes += [0x1B, 0x61, UInt8(align.rawValue)]
                if bold { bytes += [0x1B, 0x45, 1] }
                if large { bytes += [0x1D, 0x21, 0x11] }
                for l in wrap(text, width: large ? cols / 2 : cols) { appendLine(&bytes, l) }
                if large { bytes += [0x1D, 0x21, 0x00] }
                if bold { bytes += [0x1B, 0x45, 0] }
            case let .columns(label, value):
                appendColumns(&bytes, Array(label.utf16), Array(value.utf16), cols)
            case .separator(cut: true):
                bytes += feedAndCut
            case .separator:
                bytes += [0x1B, 0x61, 0]
                appendLine(&bytes, [UInt16](repeating: 0x2D, count: cols))
            }
        }
        if openCashDrawer { bytes += drawerKick }
        bytes += feedAndCut
        return bytes
    }

    /// Coupe aux espaces ; un mot plus long que la ligne est coupé net. Toujours au moins une ligne.
    static func wrap(_ text: String, width: Int) -> [[UInt16]] {
        var result: [[UInt16]] = []
        var line: [UInt16] = []
        for word in text.utf16.split(separator: space, omittingEmptySubsequences: true) {
            var rest = Array(word)
            while rest.count > width {
                if !line.isEmpty { result.append(line); line = [] }
                result.append(Array(rest[..<width]))
                rest = Array(rest[width...])
            }
            if rest.isEmpty { continue }
            if !line.isEmpty && line.count + 1 + rest.count > width { result.append(line); line = [] }
            if !line.isEmpty { line.append(space) }
            line += rest
        }
        if !line.isEmpty { result.append(line) }
        if result.isEmpty { result.append([]) }
        return result
    }

    private static func appendColumns(_ bytes: inout [UInt8], _ label: [UInt16], _ value: [UInt16], _ cols: Int) {
        bytes += [0x1B, 0x61, 0]
        if label.count + 1 + value.count <= cols {
            appendLine(&bytes, label + [UInt16](repeating: space, count: cols - label.count - value.count) + value)
            return
        }
        for l in wrap(String(decoding: label, as: UTF16.self), width: cols) { appendLine(&bytes, l) }
        for l in wrap(String(decoding: value, as: UTF16.self), width: cols) {
            appendLine(&bytes, [UInt16](repeating: space, count: max(0, cols - l.count)) + l)
        }
    }

    private static func appendLine(_ bytes: inout [UInt8], _ units: [UInt16]) {
        bytes += Pc858.encode(units)
        bytes.append(0x0A)
    }
}
