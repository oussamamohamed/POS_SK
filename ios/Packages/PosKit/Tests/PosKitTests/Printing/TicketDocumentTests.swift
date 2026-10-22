import Foundation
import Testing
@testable import PosKit

@Suite struct TicketDocumentTests {
    @Test func roundTripKeepsEveryLineKind() throws {
        let doc = TicketDocument(language: "fr", rightToLeft: false, lines: [
            .text("Bonjour", align: .center, bold: true, large: true),
            .columns(label: "Total", value: "12.50"),
            .separator(cut: false),
            .separator(cut: true)
        ])
        let json = try TicketDocumentJSON.serialize(doc)
        #expect(try TicketDocumentJSON.deserialize(json) == doc)
    }

    @Test func readsDotNetShapeWithDefaults() throws {
        let json = #"{"language":"en","rightToLeft":true,"lines":[{"type":"TicketText","text":"A"},{"type":"TicketColumns","label":"L","value":"V"},{"type":"TicketSeparator"}]}"#
        let doc = try TicketDocumentJSON.deserialize(json)
        #expect(doc == TicketDocument(language: "en", rightToLeft: true, lines: [.text("A"), .columns(label: "L", value: "V"), .separator()]))
    }

    @Test func writesAlignAsNumberAndTypeDiscriminator() throws {
        let json = try TicketDocumentJSON.serialize(TicketDocument(language: "en", rightToLeft: false, lines: [.text("x", align: .end)]))
        #expect(json.contains(#""type":"TicketText""#))
        #expect(json.contains(#""align":2"#))
    }

    @Test func unknownTypeThrows() {
        #expect(throws: (any Error).self) {
            try TicketDocumentJSON.deserialize(#"{"language":"en","rightToLeft":false,"lines":[{"type":"Nope"}]}"#)
        }
    }

    @Test func fixtureDocumentsDecode() throws {
        let fixture = try TicketReferenceFixture.load()
        #expect(!fixture.documents.isEmpty)
        #expect(!fixture.textRender.isEmpty)
        for entry in fixture.documents {
            #expect(throws: Never.self) { _ = try JSONDecoder().decode(TicketDocument.self, from: entry.expectedData) }
        }
        for entry in fixture.textRender {
            #expect(throws: Never.self) { _ = try JSONDecoder().decode(TicketDocument.self, from: entry.documentData) }
        }
    }
}
