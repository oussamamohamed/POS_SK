import Foundation
import Testing
@testable import PosKit

@Suite("Parité de hachage fiscal avec le .NET")
struct FiscalHashingTests {
    struct Reference: Decodable {
        struct Receipt: Decodable {
            let previousHash: String
            let terminalId: String
            let sequence: Int
            let amountCents: Int
            let timestamp: String
            let taxBreakdownJson: String
            let hash: String
        }
        struct Jet: Decodable {
            let previousHash: String
            let chainSequence: Int
            let eventType: String
            let timestamp: String
            let terminalId: String?
            let operatorId: String?
            let payloadJson: String
            let hash: String
        }
        struct Expectation: Decodable {
            let input: String?
            let timestamp: String?
            let expected: String
        }
        struct HashVectors: Decodable {
            let genesisHash: String
            let receipts: [Receipt]
            let jet: [Jet]
            let jsonStrings: [Expectation]
            let dateTimes: [Expectation]
        }
        let hashVectors: HashVectors
    }

    static func reference() throws -> Reference.HashVectors {
        try JSONDecoder().decode(Reference.self, from: ContractDecodingTests.fixture("fiscal_reference")).hashVectors
    }

    /// `2026-10-10T09:00:00.1230000+00:00` → date à la milliseconde.
    static func date(_ text: String) throws -> Date {
        try #require(SQLDate.parse(text.replacingOccurrences(of: "0000+00:00", with: "Z")))
    }

    @Test func genesisHashMatches() throws {
        #expect(FiscalHashing.genesisHash == (try Self.reference()).genesisHash)
    }

    @Test func receiptHashesMatchTheBackend() throws {
        for vector in try Self.reference().receipts {
            let date = try Self.date(vector.timestamp)
            #expect(FiscalHashing.timestampText(date) == vector.timestamp)
            let hash = FiscalHashing.receiptHash(
                previousHash: vector.previousHash, terminalId: vector.terminalId, sequence: vector.sequence,
                amountCents: vector.amountCents, timestamp: FiscalHashing.timestampText(date), taxBreakdownJson: vector.taxBreakdownJson
            )
            #expect(hash == vector.hash)
        }
    }

    @Test func journalHashesMatchTheBackend() throws {
        for vector in try Self.reference().jet {
            let date = try Self.date(vector.timestamp)
            let hash = FiscalHashing.jetHash(
                previousHash: vector.previousHash, chainSequence: vector.chainSequence, eventType: vector.eventType,
                timestamp: FiscalHashing.timestampText(date), terminalId: vector.terminalId,
                operatorId: vector.operatorId.flatMap { UUID(uuidString: $0) }, payloadJson: vector.payloadJson
            )
            #expect(hash == vector.hash)
        }
    }

    @Test func jsonStringsAreEscapedLikeSystemTextJson() throws {
        for vector in try Self.reference().jsonStrings {
            #expect(FiscalJSON.string(try #require(vector.input)) == vector.expected)
        }
    }

    @Test func dateTimesAreWrittenLikeSystemTextJson() throws {
        for vector in try Self.reference().dateTimes {
            #expect(FiscalJSON.dateTime(try Self.date(try #require(vector.timestamp))) == vector.expected)
        }
    }

    @Test func guidsAreLowercaseInSignedJson() {
        let id = UUID(uuidString: "0A1B2C3D-4E5F-6071-8293-A4B5C6D7E8F9")!
        #expect(FiscalHashing.guidText(id) == "0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9")
        #expect(FiscalJSON.object([("Id", FiscalJSON.guid(id)), ("N", "3")]) == "{\"Id\":\"0a1b2c3d-4e5f-6071-8293-a4b5c6d7e8f9\",\"N\":3}")
    }

    @Test func storedDatesRecomputeTheSignedTimestampExactly() throws {
        var dates: [Date] = [
            Date(timeIntervalSince1970: 1_760_087_880.002 - 1e-9), Date(timeIntervalSince1970: 1_760_087_880.123),
            Date(timeIntervalSince1970: 1_760_087_880.999), Date(timeIntervalSince1970: 1_760_087_880),
            Date(timeIntervalSince1970: 1_700_000_000.5), Date(timeIntervalSince1970: 1_500_000_000.0005),
        ]
        var state: UInt64 = 42
        for _ in 0..<5000 {
            state = state &* 6364136223846793005 &+ 1442695040888963407
            dates.append(Date(timeIntervalSince1970: 1_700_000_000 + Double(state >> 33) / Double(1 << 31) * 100_000_000))
        }
        for d in dates {
            let stored = try #require(SQLDate.parse(FiscalHashing.storedText(d)))
            #expect(FiscalHashing.timestampText(stored) == FiscalHashing.timestampText(d))
            let rounded = FiscalHashing.roundedToMilliseconds(d)
            #expect(FiscalHashing.timestampText(rounded) == FiscalHashing.timestampText(d))
            #expect(FiscalHashing.roundedToMilliseconds(rounded) == rounded)
        }
    }

    @Test func periodClosureHashJoinsTheFieldsLikeTheDotNet() {
        let end = SQLDate.parse("2025-01-31T23:00:00.000Z")!
        let hash = FiscalHashing.periodClosureHash(
            previousHash: FiscalHashing.genesisHash, terminalId: "T01", periodType: 1, periodKey: "2025-01",
            totalTtcCents: 10000, totalHtCents: 9091, taxesJson: "{\"10\":909}", tendersJson: "{\"Cash\":10000}",
            perpetualGrandTotalCents: 10000, periodEnd: end
        )
        let raw = "\(FiscalHashing.genesisHash)|T01|1|2025-01|10000|9091|{\"10\":909}|{\"Cash\":10000}|10000|2025-01-31T23:00:00.0000000+00:00"
        #expect(hash == FiscalHashing.sha256(raw, lowercase: true))
        #expect(hash == hash.lowercased() && hash.count == 64)
    }
}
