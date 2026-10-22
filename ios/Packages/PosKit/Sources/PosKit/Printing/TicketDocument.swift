import Foundation

enum TicketAlign: Int, Codable, Sendable { case start = 0, center = 1, end = 2 }

enum TicketLine: Equatable, Sendable {
    case text(String, align: TicketAlign = .start, bold: Bool = false, large: Bool = false)
    case columns(label: String, value: String)
    case separator(cut: Bool = false)
}

struct TicketDocument: Equatable, Sendable {
    var language: String
    var rightToLeft: Bool
    var lines: [TicketLine]
}

extension TicketLine: Codable {
    private enum Keys: String, CodingKey { case type, text, align, bold, large, label, value, cut }

    init(from decoder: Decoder) throws {
        let c = try decoder.container(keyedBy: Keys.self)
        switch try c.decode(String.self, forKey: .type) {
        case "TicketText":
            self = .text(try c.decode(String.self, forKey: .text), align: try c.decodeIfPresent(TicketAlign.self, forKey: .align) ?? .start,
                         bold: try c.decodeIfPresent(Bool.self, forKey: .bold) ?? false, large: try c.decodeIfPresent(Bool.self, forKey: .large) ?? false)
        case "TicketColumns":
            self = .columns(label: try c.decode(String.self, forKey: .label), value: try c.decode(String.self, forKey: .value))
        case "TicketSeparator":
            self = .separator(cut: try c.decodeIfPresent(Bool.self, forKey: .cut) ?? false)
        case let other:
            throw DecodingError.dataCorruptedError(forKey: .type, in: c, debugDescription: "Type de ligne inconnu : \(other)")
        }
    }

    func encode(to encoder: Encoder) throws {
        var c = encoder.container(keyedBy: Keys.self)
        switch self {
        case let .text(text, align, bold, large):
            try c.encode("TicketText", forKey: .type); try c.encode(text, forKey: .text); try c.encode(align, forKey: .align)
            try c.encode(bold, forKey: .bold); try c.encode(large, forKey: .large)
        case let .columns(label, value):
            try c.encode("TicketColumns", forKey: .type); try c.encode(label, forKey: .label); try c.encode(value, forKey: .value)
        case let .separator(cut):
            try c.encode("TicketSeparator", forKey: .type); try c.encode(cut, forKey: .cut)
        }
    }
}

extension TicketDocument: Codable {}

/// Même forme que `TicketDocumentJson` du .NET (clés, discriminant `type`, `align` numérique), mais PAS les mêmes octets :
/// `JSONEncoder` échappe `/` et laisse l'unicode brut, alors que le .NET « Web » échappe le non-ASCII en \uXXXX.
/// Le lot 4b ne doit donc pas comparer les JSON stockés octet pour octet.
enum TicketDocumentJSON {
    static func serialize(_ document: TicketDocument) throws -> String {
        String(decoding: try JSONEncoder().encode(document), as: UTF8.self)
    }

    static func deserialize(_ json: String) throws -> TicketDocument {
        try JSONDecoder().decode(TicketDocument.self, from: Data(json.utf8))
    }
}
