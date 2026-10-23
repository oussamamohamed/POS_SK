import Foundation
import Testing

/// Octets en hexadécimal minuscule, comme la fixture.
func hex(_ bytes: [UInt8]) -> String { bytes.map { String(format: "%02x", $0) }.joined() }

/// Décodage hexadécimal strict : longueur impaire ou paire invalide = échec du test (rien n'est ignoré en silence).
func bytes(fromHex s: String) -> [UInt8]? {
    let chars = Array(s)
    guard chars.count % 2 == 0 else {
        Issue.record("Hexadécimal de longueur impaire : \(chars.count)")
        return nil
    }
    var out: [UInt8] = []
    out.reserveCapacity(chars.count / 2)
    for i in stride(from: 0, to: chars.count, by: 2) {
        guard let b = UInt8(String(chars[i...i + 1]), radix: 16) else {
            Issue.record("Paire hexadécimale invalide : \(String(chars[i...i + 1]))")
            return nil
        }
        out.append(b)
    }
    return out
}

/// Décodage JSON des entrées de la fixture : dates ISO 8601 UTC avec millisecondes (`2025-07-15T10:30:45.000Z`).
enum TicketJSON {
    static func decoder() -> JSONDecoder {
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .custom { decoder in
            let text = try decoder.singleValueContainer().decode(String.self)
            guard let date = try? Date.ISO8601FormatStyle(includingFractionalSeconds: true).parse(text) else {
                throw DecodingError.dataCorrupted(.init(codingPath: decoder.codingPath, debugDescription: "Date invalide : \(text)"))
            }
            return date
        }
        return decoder
    }
}
