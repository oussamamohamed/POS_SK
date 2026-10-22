import Foundation
import Testing
@testable import PosKit

@Suite struct TicketBuilderReferenceTests {
    @Test func everyScenarioMatchesTheDotNetDocument() throws {
        let f = try TicketReferenceFixture.load()
        let context = TicketContext(timeZone: try #require(TimeZone(identifier: f.timeZone)), softwareVersion: f.softwareVersion)
        #expect(f.documents.count >= 30)
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
            let swiftJSON = try JSONSerialization.jsonObject(with: Data(try TicketDocumentJSON.serialize(expected).utf8))
            let dotNetJSON = try JSONSerialization.jsonObject(with: entry.expectedData)
            #expect((swiftJSON as? NSDictionary)?.isEqual(dotNetJSON) == true, "\(entry.name)")
        }
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
