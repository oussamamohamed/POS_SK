import Foundation
import Testing
@testable import PosKit

@Suite struct TicketBuilderReferenceTests {
    @Test func everyScenarioMatchesTheDotNetDocument() throws {
        let f = try TicketReferenceFixture.load()
        let context = TicketContext(timeZone: try #require(TimeZone(identifier: f.timeZone)), softwareVersion: f.softwareVersion)
        #expect(f.documents.count >= 30)
        #expect(Set(f.documents.map(\.kind)) == ["receipt", "receiptFallback", "fiscalReceipt", "pickupCoupon", "kitchenTicket",
                                                  "xReport", "zClosure", "periodClosure", "testPage", "withDuplicateNotice"])
        for entry in f.documents {
            let input = try TicketScenarioInput.decode(entry.inputData)
            let expected = try JSONDecoder().decode(TicketDocument.self, from: entry.expectedData)
            // Un kind inconnu lève une erreur : le test échoue, il ne saute pas le scénario.
            let actual = try TicketScenarioInput.build(entry.kind, entry.language, input, context)
            #expect(actual == expected, "\(entry.name) : \(Self.diff(actual, expected))")
        }
    }

    /// Le JSON Swift de chaque document attendu a la forme du JSON .NET (clés, discriminant, align numérique).
    @Test func serializedExpectedDocumentsMatchTheDotNetJSON() throws {
        let f = try TicketReferenceFixture.load()
        for entry in f.documents {
            let expected = try JSONDecoder().decode(TicketDocument.self, from: entry.expectedData)
            let swiftJSON = try Self.canonicalJSON(Data(try TicketDocumentJSON.serialize(expected).utf8))
            #expect(swiftJSON == (try Self.canonicalJSON(entry.expectedData)), "\(entry.name)")
        }
    }

    /// La comparaison distingue un booléen d'un entier (`NSNumber(true).isEqual(1)` est vrai, pas ce texte).
    @Test func canonicalJSONDistinguishesBoolFromInt() throws {
        let line = #"{"type":"TicketText","text":"A","align":1,"bold":%@,"large":false}"#
        let asBool = try Self.canonicalJSON(Data(String(format: line, "true").utf8))
        #expect(asBool == (try Self.canonicalJSON(Data(#"{"large":false,"bold":true,"align":1,"text":"A","type":"TicketText"}"#.utf8))))
        #expect(asBool != (try Self.canonicalJSON(Data(String(format: line, "1").utf8))))
    }

    /// Texte JSON à clés triées : indépendant de l'ordre des clés, sensible au type des valeurs.
    private static func canonicalJSON(_ data: Data) throws -> String {
        let object = try JSONSerialization.jsonObject(with: data)
        return String(decoding: try JSONSerialization.data(withJSONObject: object, options: [.sortedKeys]), as: UTF8.self)
    }

    private static func diff(_ actual: TicketDocument, _ expected: TicketDocument) -> String {
        var parts: [String] = []
        if actual.language != expected.language { parts.append("language \(actual.language) ≠ \(expected.language)") }
        if actual.rightToLeft != expected.rightToLeft { parts.append("rightToLeft \(actual.rightToLeft) ≠ \(expected.rightToLeft)") }
        if actual.lines.count != expected.lines.count { parts.append("lignes \(actual.lines.count) ≠ \(expected.lines.count)") }
        for index in 0..<max(actual.lines.count, expected.lines.count) {
            let a = index < actual.lines.count ? "\(actual.lines[index])" : "∅"
            let e = index < expected.lines.count ? "\(expected.lines[index])" : "∅"
            if a != e { parts.append("[\(index)] \(a) ≠ \(e)") }
        }
        return parts.joined(separator: "\n")
    }
}
