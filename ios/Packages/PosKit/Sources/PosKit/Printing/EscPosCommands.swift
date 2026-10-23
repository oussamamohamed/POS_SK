import Foundation

/// Image monochrome : 1 bit par point, `width` multiple de 8, bit de poids fort à gauche.
struct MonoImage: Equatable, Sendable {
    let width: Int
    let height: Int
    let bits: [UInt8]
    var bytesPerRow: Int { width / 8 }

    /// Les octets par ligne doivent tenir dans l'en-tête GS v 0 (deux octets) ; la hauteur est libre (bandes de 256 lignes) ; `bits` couvre exactement l'image.
    init(width: Int, height: Int, bits: [UInt8]) {
        precondition(width > 0 && width % 8 == 0, "Largeur non multiple de 8 : \(width)")
        precondition(width / 8 <= 65535 && height >= 0, "Dimensions hors limites : \(width)×\(height)")
        precondition(bits.count == width / 8 * height, "Taille de bits incohérente : \(bits.count)")
        self.width = width
        self.height = height
        self.bits = bits
    }
}

/// Port de `EscPosCommands.cs` : ESC @, bandes GS v 0, coupe, tiroir.
enum EscPosCommands {
    static let bandHeight = 256
    private static let initSeq: [UInt8] = [0x1B, 0x40]
    private static let drawerKick: [UInt8] = [0x1B, 0x70, 0x00, 0x19, 0xFA]
    private static let feedAndCut: [UInt8] = [0x1D, 0x56, 0x42, 0x00]

    /// ESC @, puis pour chaque segment : bandes GS v 0 de 256 lignes et coupe ; impulsion tiroir avant la dernière coupe.
    static func build(_ segments: [MonoImage], openCashDrawer: Bool) -> [UInt8] {
        var bytes = initSeq
        for (i, img) in segments.enumerated() {
            var top = 0
            while top < img.height {
                let rows = min(bandHeight, img.height - top)
                bytes += [0x1D, 0x76, 0x30, 0x00, UInt8(img.bytesPerRow & 0xFF), UInt8(img.bytesPerRow >> 8), UInt8(rows & 0xFF), UInt8(rows >> 8)]
                bytes += img.bits[(top * img.bytesPerRow)..<((top + rows) * img.bytesPerRow)]
                top += bandHeight
            }
            if openCashDrawer && i == segments.count - 1 { bytes += drawerKick }
            bytes += feedAndCut
        }
        return bytes
    }
}
