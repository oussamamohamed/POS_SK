import Foundation
import Testing
@testable import PosKit

@Suite struct EscPosCommandsTests {
    @Test func buildIsByteIdenticalToDotNet() throws {
        for c in try TicketReferenceFixture.load().commands {
            let segments = try c.segments.map { s -> MonoImage in
                let bits = try #require(bytes(fromHex: s.bitsHex), "\(c.name)")
                return MonoImage(width: s.width, height: s.height, bits: bits)
            }
            #expect(hex(EscPosCommands.build(segments, openCashDrawer: c.drawer)) == c.expectedHex, "\(c.name)")
        }
    }

    private func bytes(fromHex s: String) -> [UInt8]? {
        let chars = Array(s)
        guard chars.count % 2 == 0 else { return nil }
        return stride(from: 0, to: chars.count, by: 2).compactMap { UInt8(String(chars[$0...$0 + 1]), radix: 16) }
    }
}
