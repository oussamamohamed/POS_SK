import Foundation
import Testing
@testable import PosKit

@Suite struct TicketFormatTests {
    @Test func amountMatchesInvariantTwoDecimals() {
        #expect(TicketFormat.amount(0) == "0.00"); #expect(TicketFormat.amount(5) == "0.05")
        #expect(TicketFormat.amount(1250) == "12.50"); #expect(TicketFormat.amount(-50) == "-0.50"); #expect(TicketFormat.amount(-123456) == "-1234.56")
    }

    @Test func rateTrimsLikeZeroPoundPound() {
        #expect(TicketFormat.rate(5.5) == "5.5"); #expect(TicketFormat.rate(10) == "10"); #expect(TicketFormat.rate(2.1) == "2.1")
        #expect(TicketFormat.rate(0.125) == "0.13"); #expect(TicketFormat.rate(0) == "0")
    }

    @Test func datesUseGivenZoneAndLatinDigits() throws {
        let paris = try #require(TimeZone(identifier: "Europe/Paris"))
        let date = try #require(ISO8601DateFormatter().date(from: "2025-07-15T10:30:45Z"))
        #expect(TicketFormat.utcDateTime(date) == "15/07/2025 10:30:45")
        #expect(TicketFormat.localDateTime(date, in: paris) == "15/07/2025 12:30")
        #expect(TicketFormat.localTime(date, in: paris) == "12:30")
    }

    @Test func rateAmountDecodesStringRate() throws {
        let ok = try JSONDecoder().decode(TicketRateAmount.self, from: Data(#"{"rate":"5.5","cents":123}"#.utf8))
        #expect(ok == TicketRateAmount(rate: 5.5, cents: 123))
        #expect(throws: (any Error).self) {
            try JSONDecoder().decode(TicketRateAmount.self, from: Data(#"{"rate":"abc","cents":1}"#.utf8))
        }
        let back = try JSONEncoder().encode(ok)
        #expect(String(decoding: back, as: UTF8.self).contains(#""rate":"5.5""#))
    }
}
