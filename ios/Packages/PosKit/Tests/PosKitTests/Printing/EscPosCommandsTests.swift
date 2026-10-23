import Foundation
import Testing
@testable import PosKit

@Suite struct EscPosCommandsTests {
    @Test func buildIsByteIdenticalToDotNet() throws {
        let commands = try TicketReferenceFixture.load().commands
        #expect(!commands.isEmpty)
        for c in commands {
            let segments = try c.segments.map { s -> MonoImage in
                let bits = try #require(bytes(fromHex: s.bitsHex), "\(c.name)")
                return MonoImage(width: s.width, height: s.height, bits: bits)
            }
            #expect(hex(EscPosCommands.build(segments, openCashDrawer: c.drawer)) == c.expectedHex, "\(c.name)")
        }
    }

    /// Plus de 65535 lignes : l'image est découpée en bandes de 256 lignes, sans plafond de hauteur.
    @Test func veryTallImageIsSplitIntoBands() {
        let height = 70_000
        let image = MonoImage(width: 8, height: height, bits: [UInt8](repeating: 0xFF, count: height))
        let out = EscPosCommands.build([image], openCashDrawer: false)
        // Parcours structuré : ESC @ (2), puis par bande 8 octets d'en-tête + 1 octet par ligne, puis la coupe (4).
        var headers = 0
        var i = 2
        while i + 8 <= out.count - 4, Array(out[i..<i + 4]) == [0x1D, 0x76, 0x30, 0x00] {
            headers += 1
            let rows = Int(out[i + 6]) | Int(out[i + 7]) << 8
            i += 8 + rows * Int(out[i + 4])
        }
        #expect(headers == (height + 255) / 256)
        #expect(i == out.count - 4)
    }
}
