import Foundation
import Testing
@testable import PosKit

@Suite struct Pc858Tests {
    @Test func tableMatchesDotNetForEveryByte() throws {
        let f = try TicketReferenceFixture.load()
        #expect(f.pc858.count == 256)
        #expect(Pc858.table.map { Int($0) } == f.pc858)
        for byte in 0..<256 {
            let scalar = try #require(UnicodeScalar(UInt32(f.pc858[byte])))
            let encoded = Pc858.encode(Array(String(scalar).utf16))
            // Si deux octets décodent vers le même caractère, l'inverse prend le premier.
            #expect(encoded.count == 1, "octet \(byte)")
            #expect(encoded.first == UInt8(f.pc858.firstIndex(of: f.pc858[byte])!))
        }
    }

    @Test func encodeSamplesMatchDotNet() throws {
        let samples = try TicketReferenceFixture.load().encode
        #expect(!samples.isEmpty)
        for sample in samples {
            #expect(hex(Pc858.encode(Array(sample.text.utf16))) == sample.hex, "« \(sample.text) »")
        }
    }
}
