import Foundation
import Testing
@testable import PosKit

@Suite struct TicketTextsTests {
    @Test func everyKeyMatchesDotNetInEveryLanguage() throws {
        let f = try TicketReferenceFixture.load()
        #expect(Set(f.texts.keys) == ["en", "fr", "ar"])
        for (lang, table) in f.texts {
            #expect(!table.isEmpty, "\(lang)")
            for (key, value) in table { #expect(TicketTexts.get(lang, key) == value, "\(lang) \(key)") }
        }
    }

    @Test func substitutesNamedArguments() {
        #expect(TicketTexts.get("en", "report.z_title", ["sequence": "7"]) == "Z CLOSURE #7")
    }

    @Test func missingKeyReturnsTheKey() { #expect(TicketTexts.get("fr", "nope.key") == "nope.key") }

    @Test func unknownLanguageFallsBackToEnglish() {
        #expect(TicketTexts.get("de", "report.z_title", ["sequence": "1"]) == "Z CLOSURE #1")
    }
}
