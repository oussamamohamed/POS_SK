import Foundation
import Testing

/// Fixture `ticket_document_reference.json`, générée par le .NET (voir CLAUDE.md).
struct TicketReferenceFixture: Decodable {
    struct Encode: Decodable { let text: String; let hex: String }
    struct Wrap: Decodable { let text: String; let width: Int; let lines: [String] }
    struct Segment: Decodable { let width: Int; let height: Int; let bitsHex: String }
    struct Command: Decodable { let name: String; let drawer: Bool; let segments: [Segment]; let expectedHex: String }

    struct TextRender: Decodable {
        let name: String
        let paperWidthMm: Int
        let drawer: Bool
        let expectedHex: String
        /// Octets JSON de l'objet `document`.
        let documentData: Data

        private enum Keys: String, CodingKey { case name, paperWidthMm, drawer, document, expectedHex }
        init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: Keys.self)
            name = try c.decode(String.self, forKey: .name)
            paperWidthMm = try c.decode(Int.self, forKey: .paperWidthMm)
            drawer = try c.decode(Bool.self, forKey: .drawer)
            expectedHex = try c.decode(String.self, forKey: .expectedHex)
            documentData = try rawJSON(c, .document)
        }
    }

    struct Document: Decodable {
        let name: String
        let kind: String
        let language: String
        /// Octets JSON de `input` et `expected`.
        let inputData: Data
        let expectedData: Data

        private enum Keys: String, CodingKey { case name, kind, language, input, expected }
        init(from decoder: Decoder) throws {
            let c = try decoder.container(keyedBy: Keys.self)
            name = try c.decode(String.self, forKey: .name)
            kind = try c.decode(String.self, forKey: .kind)
            language = try c.decode(String.self, forKey: .language)
            inputData = try rawJSON(c, .input)
            expectedData = try rawJSON(c, .expected)
        }
    }

    let timeZone: String
    let softwareVersion: String
    let texts: [String: [String: String]]
    let pc858: [Int]
    let encode: [Encode]
    let wrap: [Wrap]
    let commands: [Command]
    let textRender: [TextRender]
    let documents: [Document]

    static func load() throws -> TicketReferenceFixture {
        let url = try #require(Bundle.module.url(forResource: "ticket_document_reference", withExtension: "json", subdirectory: "Fixtures"))
        return try JSONDecoder().decode(TicketReferenceFixture.self, from: Data(contentsOf: url))
    }
}

/// Ré-encode une valeur JSON imbriquée en octets bruts.
private func rawJSON<K: CodingKey>(_ c: KeyedDecodingContainer<K>, _ key: K) throws -> Data {
    let any = try c.decode(AnyJSON.self, forKey: key)
    return try JSONSerialization.data(withJSONObject: any.value, options: [.fragmentsAllowed])
}

private struct AnyJSON: Decodable {
    let value: Any
    init(from decoder: Decoder) throws {
        let c = try decoder.singleValueContainer()
        if c.decodeNil() { value = NSNull() }
        else if let b = try? c.decode(Bool.self) { value = b }
        else if let i = try? c.decode(Int.self) { value = i }
        else if let d = try? c.decode(Double.self) { value = d }
        else if let s = try? c.decode(String.self) { value = s }
        else if let a = try? c.decode([AnyJSON].self) { value = a.map(\.value) }
        else { value = try c.decode([String: AnyJSON].self).mapValues(\.value) }
    }
}
